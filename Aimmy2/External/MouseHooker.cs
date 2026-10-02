using System;
using System.Runtime.InteropServices;

namespace Aimmy2.External
{
    /// <summary>
    /// Low-level mouse hook to capture relative mouse movement (Delta X/Y)
    /// without blocking or modifying the user's actual mouse input.
    /// Uses Windows SetWindowsHookEx with WH_MOUSE_LL for system-wide hooking.
    /// 
    /// DUAL-PC v2: Envoie directement à Arduino via ArduinoSerial pour latence minimale
    /// </summary>
    public class MouseHooker : IDisposable
    {
        // Windows hook constants
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_MOUSEHWHEEL = 0x020E;
        private const int WM_XBUTTONDOWN = 0x020B;
        private const int WM_XBUTTONUP = 0x020C;
        private const int XBUTTON1 = 0x0001;  // Side button 3
        private const int XBUTTON2 = 0x0002;  // Side button 4

        // P/Invoke delegates and handles
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        private static LowLevelMouseProc? _mouseDelegate;
        private IntPtr _mouseHookHandle = IntPtr.Zero;

        // Mouse position tracking for delta calculation
        private int _lastMouseX = 0;
        private int _lastMouseY = 0;
        private object _positionLock = new object();

         // Event for mouse movement detection (peut être utilisé par d'autres)
        public event Action<int, int>? OnMouseDeltaChanged;

        // Events for mouse buttons
        public event Action<bool>? OnLeftClickChanged;       // true=press, false=release
        public event Action<bool>? OnRightClickChanged;      // true=press, false=release
        public event Action<bool>? OnMiddleClickChanged;     // true=press, false=release
        public event Action<bool>? OnXButton1Changed;        // Side button 3 (true=press, false=release)
        public event Action<bool>? OnXButton2Changed;        // Side button 4 (true=press, false=release)
        
        // Event for scroll wheel
        public event Action<int>? OnScrollWheelChanged;      // 1=up, -1=down

        // DUAL-PC v2: Référence à ArduinoSerial pour envoi immédiat
        private ArduinoSerial? _arduinoSerial;

        // Status tracking
        private bool _isActive = false;
        private bool _disposed = false;

        /// <summary>
        /// Gets whether the mouse hook is currently active
        /// </summary>
        public bool IsActive => _isActive && _mouseHookHandle != IntPtr.Zero;

        /// <summary>
        /// DUAL-PC v2: Configure l'Arduino pour envoi direct des mouvements
        /// </summary>
        public void SetArduinoSerial(ArduinoSerial? arduinoSerial)
        {
            _arduinoSerial = arduinoSerial;
        }

        /// <summary>
        /// Start the mouse hook
        /// </summary>
        public void Start()
        {
            if (_isActive || _disposed)
                return;

            _mouseDelegate = MouseHookCallback;
            _mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _mouseDelegate, GetModuleHandle(null), 0);

            if (_mouseHookHandle == IntPtr.Zero)
            {
                throw new Exception("Failed to install mouse hook. Check permissions.");
            }

            _isActive = true;
        }

        /// <summary>
        /// Stop the mouse hook
        /// </summary>
        public void Stop()
        {
            if (!_isActive || _mouseHookHandle == IntPtr.Zero)
                return;

            UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero;
            _isActive = false;
        }

          /// <summary>
           /// Internal callback for mouse events from Windows hook
           /// DUAL-PC v2: Envoie directement à Arduino (non-bloquant, immédiat)
           /// Handles: movement, left/right/middle clicks, scroll wheel
           /// </summary>
           private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
           {
               if (nCode >= 0)
               {
                   try
                   {
                       var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                       int msgType = (int)wParam;

                       // ===== MOUSE MOVEMENT =====
                       if (msgType == WM_MOUSEMOVE)
                       {
                           lock (_positionLock)
                           {
                               int deltaX = hookStruct.pt.x - _lastMouseX;
                               int deltaY = hookStruct.pt.y - _lastMouseY;

                               if (deltaX != 0 || deltaY != 0)
                               {
                                   // DUAL-PC v2: Send immediately to Arduino (non-blocking)
                                   if (_arduinoSerial != null && _arduinoSerial.IsConnected)
                                   {
                                       _arduinoSerial.SendRawUserMovement(deltaX, deltaY);
                                   }

                                   OnMouseDeltaChanged?.Invoke(deltaX, deltaY);
                                   
                                   // Restore cursor to prevent double-movement
                                   SetCursorPos(_lastMouseX, _lastMouseY);
                               }
                               
                               _lastMouseX = hookStruct.pt.x;
                               _lastMouseY = hookStruct.pt.y;
                           }
                       }

                       // ===== LEFT CLICK =====
                       else if (msgType == WM_LBUTTONDOWN)
                       {
                           if (_arduinoSerial != null && _arduinoSerial.IsConnected)
                           {
                               _arduinoSerial.SendMouseClick("L", true);
                           }
                           OnLeftClickChanged?.Invoke(true);
                       }
                       else if (msgType == WM_LBUTTONUP)
                       {
                           if (_arduinoSerial != null && _arduinoSerial.IsConnected)
                           {
                               _arduinoSerial.SendMouseClick("L", false);
                           }
                           OnLeftClickChanged?.Invoke(false);
                       }

                       // ===== RIGHT CLICK =====
                       else if (msgType == WM_RBUTTONDOWN)
                       {
                           if (_arduinoSerial != null && _arduinoSerial.IsConnected)
                           {
                               _arduinoSerial.SendMouseClick("R", true);
                           }
                           OnRightClickChanged?.Invoke(true);
                       }
                       else if (msgType == WM_RBUTTONUP)
                       {
                           if (_arduinoSerial != null && _arduinoSerial.IsConnected)
                           {
                               _arduinoSerial.SendMouseClick("R", false);
                           }
                           OnRightClickChanged?.Invoke(false);
                       }

                       // ===== MIDDLE CLICK =====
                       else if (msgType == WM_MBUTTONDOWN)
                       {
                           if (_arduinoSerial != null && _arduinoSerial.IsConnected)
                           {
                               _arduinoSerial.SendMouseClick("M", true);
                           }
                           OnMiddleClickChanged?.Invoke(true);
                       }
                       else if (msgType == WM_MBUTTONUP)
                       {
                           if (_arduinoSerial != null && _arduinoSerial.IsConnected)
                           {
                               _arduinoSerial.SendMouseClick("M", false);
                           }
                           OnMiddleClickChanged?.Invoke(false);
                       }

                        // ===== SCROLL WHEEL =====
                        else if (msgType == WM_MOUSEWHEEL)
                        {
                            // Extract wheel rotation from high word of mouseData
                            short wheelDelta = (short)(hookStruct.mouseData >> 16);
                            int scrollDirection = wheelDelta > 0 ? 1 : (wheelDelta < 0 ? -1 : 0);
                            
                            if (scrollDirection != 0)
                            {
                                if (_arduinoSerial != null && _arduinoSerial.IsConnected)
                                {
                                    _arduinoSerial.SendScrollWheel(scrollDirection);
                                }
                                OnScrollWheelChanged?.Invoke(scrollDirection);
                            }
                        }

                        // ===== X-BUTTON 1 (Side button 3) =====
                        else if (msgType == WM_XBUTTONDOWN)
                        {
                            // Extract which X button from high word of mouseData
                            int xButton = (int)(hookStruct.mouseData >> 16) & 0xFFFF;
                            
                            if (xButton == XBUTTON1)
                            {
                                if (_arduinoSerial != null && _arduinoSerial.IsConnected)
                                {
                                    _arduinoSerial.SendMouseClick("3", true);
                                }
                                OnXButton1Changed?.Invoke(true);
                            }
                            else if (xButton == XBUTTON2)
                            {
                                if (_arduinoSerial != null && _arduinoSerial.IsConnected)
                                {
                                    _arduinoSerial.SendMouseClick("4", true);
                                }
                                OnXButton2Changed?.Invoke(true);
                            }
                        }
                        else if (msgType == WM_XBUTTONUP)
                        {
                            // Extract which X button from high word of mouseData
                            int xButton = (int)(hookStruct.mouseData >> 16) & 0xFFFF;
                            
                            if (xButton == XBUTTON1)
                            {
                                if (_arduinoSerial != null && _arduinoSerial.IsConnected)
                                {
                                    _arduinoSerial.SendMouseClick("3", false);
                                }
                                OnXButton1Changed?.Invoke(false);
                            }
                            else if (xButton == XBUTTON2)
                            {
                                if (_arduinoSerial != null && _arduinoSerial.IsConnected)
                                {
                                    _arduinoSerial.SendMouseClick("4", false);
                                }
                                OnXButton2Changed?.Invoke(false);
                            }
                        }
                    }
                   catch (Exception ex)
                   {
                       System.Diagnostics.Debug.WriteLine($"MouseHooker error: {ex.Message}");
                   }
               }

               // Pass to next hook in chain
               return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
           }

        #region Windows P/Invoke

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetCursorPos(int x, int y);

        #endregion

        /// <summary>
        /// Dispose and cleanup resources
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            Stop();
            _mouseDelegate = null;
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Finalizer to ensure cleanup
        /// </summary>
        ~MouseHooker()
        {
            Dispose();
        }
    }
}
