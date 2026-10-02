/*
 * DUAL-PC AIM ASSIST - Arduino Leonardo R3 Firmware
 * 
 * System Architecture:
 * - PC B: Capture + Mouse Hook + AI Detection
 * - Arduino Leonardo R3: HID Mouse device + Movement fusion
 * - PC A: Gaming PC (receives mouse movements as HID device)
 * 
 * Serial Protocol (500k baud):
 * - "U,{X},{Y}\n" = User movement (IMMEDIATE - from MouseHooker)
 * - "A,{X},{Y}\n" = AI correction (ASYNCHRONOUS - when AI is ready)
 * - "C,{L|R|M|3|4},{1|0}\n" = Mouse click (1=press, 0=release)
 * - "W,{-1|0|1}\n" = Scroll wheel (1=up, -1=down)
 * 
 * Movement Fusion:
 * Total_Delta = User_Delta + AI_Correction (both applied immediately as received)
 * 
 * Latency Profile:
 * - User movement: ~5-13ms (direct serial + HID)
 * - AI correction: +10-50ms in parallel (doesn't delay user movement)
 * 
 * Leonardo is a USB device presenting itself as a HID mouse to PC A
 */

#include <Mouse.h>

// Serial communication constants
const int BAUD_RATE = 500000;
const int MAX_COMMAND_LENGTH = 32;
const char USER_CMD = 'U';
const char AI_CMD = 'A';
const char CLICK_CMD = 'C';
const char WHEEL_CMD = 'W';
const char DELIMITER = ',';
const char TERMINATOR = '\n';

// Buffer for serial input
char serialBuffer[MAX_COMMAND_LENGTH];
int bufferIndex = 0;

// Timing for non-blocking serial reads
unsigned long lastCommandTime = 0;
const unsigned long COMMAND_TIMEOUT_MS = 100; // If no terminator received in 100ms, reset buffer

// Movement accumulator (to combine user + AI corrections)
volatile int accumulatedX = 0;
volatile int accumulatedY = 0;
volatile boolean pendingMovement = false;

// Debug LED (optional, remove if not available)
const int DEBUG_LED = LED_BUILTIN; // Pin 13 on Leonardo

void setup() {
  // Initialize serial communication (PC B to Arduino)
  Serial.begin(BAUD_RATE);
  while (!Serial) {
    // Wait for serial connection
    delay(100);
  }
  
  // Initialize HID mouse (Arduino to PC A)
  Mouse.begin();
  
  // Debug LED
  pinMode(DEBUG_LED, OUTPUT);
  digitalWrite(DEBUG_LED, LOW);
  
  // Send startup message to PC B
  Serial.println("READY");
}

void loop() {
  // Non-blocking serial read
  readSerialCommand();
  
  // Process accumulated movement if pending
  if (pendingMovement && accumulatedX != 0 && accumulatedY != 0) {
    sendMouseMovement(accumulatedX, accumulatedY);
    accumulatedX = 0;
    accumulatedY = 0;
    pendingMovement = false;
  }
  
  // Small delay to prevent spinning
  delay(1);
}

/**
 * Non-blocking serial reader
 * Accumulates characters until TERMINATOR or timeout
 * Then parses the command (U or A)
 */
void readSerialCommand() {
  while (Serial.available() > 0) {
    char c = Serial.read();
    
    // Check for command terminator
    if (c == TERMINATOR) {
      // Process the complete command
      if (bufferIndex > 0) {
        parseAndExecuteCommand(serialBuffer, bufferIndex);
      }
      bufferIndex = 0;
      lastCommandTime = millis();
      continue;
    }
    
    // Add character to buffer if there's space
    if (bufferIndex < MAX_COMMAND_LENGTH - 1) {
      serialBuffer[bufferIndex] = c;
      bufferIndex++;
    } else {
      // Buffer overflow, reset
      bufferIndex = 0;
      sendError("OVERFLOW");
    }
  }
  
  // Reset buffer if timeout
  if (bufferIndex > 0 && (millis() - lastCommandTime > COMMAND_TIMEOUT_MS)) {
    bufferIndex = 0;
  }
}

/**
 * Parse and execute the command
 * Formats:
 * - "U,X,Y" or "A,X,Y" - Mouse movement
 * - "C,L/R/M,1/0" - Mouse clicks (1=press, 0=release)
 * - "W,1/-1/0" - Scroll wheel (1=up, -1=down)
 */
void parseAndExecuteCommand(const char* cmd, int length) {
  char commandType = cmd[0];
  
  // ===== MOUSE MOVEMENT (U or A) =====
  if (commandType == USER_CMD || commandType == AI_CMD) {
    int x = 0, y = 0;
    int parsed = 0;
    
    if (length >= 5 && cmd[1] == DELIMITER) {
      parsed = sscanf(&cmd[2], "%d,%d", &x, &y);
      if (parsed != 2) {
        sendError("PARSE_MOVE");
        return;
      }
    } else {
      sendError("FORMAT_MOVE");
      return;
    }
    
    if (commandType == USER_CMD) {
      // User movement: send immediately
      sendMouseMovement(x, y);
      digitalWrite(DEBUG_LED, HIGH);
      delay(10);
      digitalWrite(DEBUG_LED, LOW);
    } 
    else if (commandType == AI_CMD) {
      // AI correction: send immediately (will be combined on PC A side)
      sendMouseMovement(x, y);
    }
  }
  
   // ===== MOUSE CLICK (C) =====
   else if (commandType == CLICK_CMD) {
     // Format: "C,L/R/M/3/4,1/0"
     // Example: "C,L,1" = left click press, "C,3,0" = button 3 release
     if (length >= 5 && cmd[1] == DELIMITER && cmd[3] == DELIMITER) {
       char button = cmd[2];  // L, R, M, 3, or 4
       int pressValue = 0;
       int parsed = sscanf(&cmd[4], "%d", &pressValue);
       
       if (parsed != 1) {
         sendError("PARSE_CLICK");
         return;
       }
       
       // Validate button is one of the recognized values
       if (button == 'L' || button == 'R' || button == 'M' || button == '3' || button == '4') {
         handleMouseClick(button, pressValue == 1);
       } else {
         sendError("INVALID_BTN");
       }
     } else {
       sendError("FORMAT_CLICK");
     }
   }
  
  // ===== SCROLL WHEEL (W) =====
  else if (commandType == WHEEL_CMD) {
    // Format: "W,1/-1" (1=up, -1=down)
    int direction = 0;
    int parsed = sscanf(&cmd[2], "%d", &direction);
    
    if (parsed != 1 || direction < -1 || direction > 1) {
      sendError("PARSE_WHEEL");
      return;
    }
    
    if (direction != 0) {
      handleScrollWheel(direction);
    }
  }
  
  else {
    sendError("UNKNOWN_CMD");
  }
}

/**
 * Send mouse movement to PC A via HID
 * CRITICAL: Must be non-blocking to maintain latency
 */
void sendMouseMovement(int deltaX, int deltaY) {
  // Clamp values to valid range for HID
  deltaX = constrain(deltaX, -127, 127);
  deltaY = constrain(deltaY, -127, 127);
  
  // Send movement to PC A
  Mouse.move(deltaX, deltaY, 0);  // 0 = no scroll wheel
  
  // Optional: echo back to verify
  // Serial.print("ACK:");
  // Serial.print(deltaX);
  // Serial.print(",");
  // Serial.println(deltaY);
}

/**
 * Handle mouse button clicks (left, right, middle, button 3, button 4)
 * button: 'L', 'R', 'M', '3', or '4'
 * isPressed: true for press, false for release
 */
void handleMouseClick(char button, boolean isPressed) {
  if (isPressed) {
    // Button press
    switch (button) {
      case 'L':
        Mouse.press(MOUSE_LEFT);
        break;
      case 'R':
        Mouse.press(MOUSE_RIGHT);
        break;
      case 'M':
        Mouse.press(MOUSE_MIDDLE);
        break;
      case '3':
        // Button 3 (X Button 1) - side button forward
        Mouse.press(0x08);  // Extended button code
        break;
      case '4':
        // Button 4 (X Button 2) - side button back
        Mouse.press(0x10);  // Extended button code
        break;
    }
  } else {
    // Button release
    switch (button) {
      case 'L':
        Mouse.release(MOUSE_LEFT);
        break;
      case 'R':
        Mouse.release(MOUSE_RIGHT);
        break;
      case 'M':
        Mouse.release(MOUSE_MIDDLE);
        break;
      case '3':
        // Button 3 (X Button 1) - side button forward
        Mouse.release(0x08);  // Extended button code
        break;
      case '4':
        // Button 4 (X Button 2) - side button back
        Mouse.release(0x10);  // Extended button code
        break;
    }
  }
}

/**
 * Handle scroll wheel events
 * direction: 1 for scroll up, -1 for scroll down
 */
void handleScrollWheel(int direction) {
  // Mouse.scroll() parameter: positive=scroll up, negative=scroll down
  // Each scroll increment is typically 1 line
  Mouse.scroll(direction);
}

/**
 * Send error message back to PC B for debugging
 */
void sendError(const char* errorCode) {
  Serial.print("ERR:");
  Serial.println(errorCode);
}
