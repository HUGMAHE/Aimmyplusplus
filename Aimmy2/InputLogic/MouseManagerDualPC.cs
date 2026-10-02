using Aimmy2.Class;
using Aimmy2.External;
using Class;
using System;
using System.Drawing;
using System.Threading.Tasks;
using Other;
using static Other.LogManager;

namespace InputLogic
{
    /// <summary>
    /// MODIFICATION DUAL-PC: MouseManager adapté pour KMBox Net uniquement.
    /// - Plus d'Arduino
    /// - Plus de forward souris utilisateur
    /// - Envoi UNIQUEMENT des corrections IA via KMNet
    /// - Scaling + mitigations comportementales conservés
    /// </summary>
    internal class MouseManagerDualPC
    {
        private static KMNetClient? _kmnet;
        private static ScalingCalculator? _scaler;
        private static MovementMerger? _merger;
        private static DateTime LastClickTime = DateTime.MinValue;
        private static bool isSpraying = false;
        private static double previousX = 0;
        private static double previousY = 0;
        private static int _currentScreenX = 0;
        private static int _currentScreenY = 0;
        private static object _positionLock = new object();
        public static double smoothingFactor = 0.5;
        public static bool IsEMASmoothingEnabled = false;
        private static Random MouseRandom = new();

        // Public accessors for settings panels
        public static MovementMerger? Merger => _merger;
        public static ScalingCalculator? Scaler => _scaler;

        // Behavioral Mitigations - Hit Distribution Variance (60/30/10)
        private static int _varianceFrameCounter = 0;
        private static readonly int[] _variancePattern = new[] { 0, 0, 0, 0, 0, 0, 1, 1, 1, 2 }; // 60% (0), 30% (1), 10% (2)

        // Behavioral Mitigations - Reaction Delays
        private static int _reactionDelayMs = 0;
        private static bool _reactionDelayActive = false;
        private static int _pendingDeltaX = 0;
        private static int _pendingDeltaY = 0;

        public static void Initialize(KMNetClient? kmnet, ScalingCalculator scaler)
        {
            _kmnet = kmnet;
            _scaler = scaler;

            // Initialize merger for movement fusion
            if (_scaler != null)
            {
                _merger = new MovementMerger(_scaler);
            }

            if (_kmnet != null)
            {
                Log(LogLevel.Info, "MouseManagerDualPC initialisé en mode KMBox Net", true, 1500);
            }
            else
            {
                Log(LogLevel.Warning, "MouseManagerDualPC initialisé sans backend actif", true, 1500);
            }
        }

        /// <summary>
        /// Stop and cleanup
        /// </summary>
        public static void Shutdown()
        {
            _merger = null;
            _scaler = null;
            _kmnet = null;
        }

        private static double EmaSmoothing(double previousValue, double currentValue, double smoothingFactor)
            => (currentValue * smoothingFactor) + (previousValue * (1 - smoothingFactor));

        /// <summary>
        /// Apply 60/30/10 hit distribution variance to reduce detection
        /// 60% exact aim, 30% reduced accuracy, 10% random offset
        /// </summary>
        private static (int, int) ApplyVariance(int deltaX, int deltaY)
        {
            if (!Dictionary.toggleState["Enable Hit Distribution Variance"])
            {
                return (deltaX, deltaY);
            }

            int varianceType = _variancePattern[_varianceFrameCounter];
            _varianceFrameCounter = (_varianceFrameCounter + 1) % _variancePattern.Length;

            return varianceType switch
            {
                0 => (deltaX, deltaY), // 60%: Exact aim
                1 => ((int)(deltaX * 0.8), (int)(deltaY * 0.8)), // 30%: Reduce by 20%
                2 => (deltaX + MouseRandom.Next(-5, 6), deltaY + MouseRandom.Next(-5, 6)), // 10%: Random ±5px
                _ => (deltaX, deltaY)
            };
        }

        /// <summary>
        /// Generate and manage reaction delay (asynchronous, non-blocking)
        /// Generates random delay on new target, decrements each frame
        /// </summary>
        private static void GenerateReactionDelay()
        {
            if (!Dictionary.toggleState["Enable Reaction Delay"])
            {
                return;
            }

            int minDelay = (int)Dictionary.sliderSettings["Reaction Delay Min (ms)"];
            int maxDelay = (int)Dictionary.sliderSettings["Reaction Delay Max (ms)"];
            int jitter = MouseRandom.Next(-20, 21); // ±20ms jitter

            _reactionDelayMs = MouseRandom.Next(minDelay, maxDelay + 1) + jitter;
            _reactionDelayMs = Math.Clamp(_reactionDelayMs, minDelay, maxDelay + 40); // Allow up to 40ms extra jitter
            _reactionDelayActive = true;
        }

        /// <summary>
        /// Decrement reaction delay counter (called each frame)
        /// Returns true if delay is still active, false when ready to send
        /// </summary>
        private static bool DecrementReactionDelay(int frameTimeMs = 16) // ~60fps = 16.67ms per frame
        {
            if (!_reactionDelayActive)
            {
                return false;
            }

            _reactionDelayMs -= frameTimeMs;
            if (_reactionDelayMs <= 0)
            {
                _reactionDelayActive = false;
                return false;
            }

            return true;
        }

        public static async Task DoTriggerClick(RectangleF? detectionBox = null)
        {
            if (_kmnet == null || !_kmnet.IsConnected)
            {
                return;
            }

            if (!(InputBindingManager.IsHoldingBinding("Aim Keybind") || InputBindingManager.IsHoldingBinding("Second Aim Keybind")))
            {
                ResetSprayState();
                return;
            }

            if (Dictionary.toggleState["Spray Mode"])
            {
                if (Dictionary.toggleState["Cursor Check"])
                {
                    Point mousePos = WinAPICaller.GetCursorPosition();
                    if (detectionBox.HasValue && !detectionBox.Value.Contains(mousePos.X, mousePos.Y))
                    {
                        if (isSpraying) ReleaseMouseButton();
                        return;
                    }
                }

                if (!isSpraying) HoldMouseButton();
                return;
            }

            int timeSinceLastClick = (int)(DateTime.UtcNow - LastClickTime).TotalMilliseconds;
            int triggerDelayMilliseconds = (int)(Dictionary.sliderSettings["Auto Trigger Delay"] * 1000);
            const int clickDelayMilliseconds = 20;

            if (timeSinceLastClick < triggerDelayMilliseconds && LastClickTime != DateTime.MinValue)
                return;

            await Task.Delay(clickDelayMilliseconds);
            LastClickTime = DateTime.UtcNow;
        }

        public static void HoldMouseButton()
        {
            if (isSpraying) return;
            isSpraying = true;
        }

        public static void ReleaseMouseButton()
        {
            if (!isSpraying) return;
            isSpraying = false;
        }

        public static void ResetSprayState()
        {
            if (isSpraying)
                ReleaseMouseButton();
        }

        public static void MoveCrosshair(int detectedX, int detectedY, int modelImageSize = 640)
        {
            bool kmnetActive = _kmnet != null && _kmnet.IsConnected;

            if ((_kmnet == null) || _scaler == null || _merger == null)
            {
                return;
            }

            try
            {
                // detectedX, detectedY are in SCREEN SPACE (already scaled from AIManager.CalculateCoordinates)
                // Convert back to MODEL_SIZE space for MovementMerger
                int nativeScreenWidth = WinAPICaller.ScreenWidth;
                int nativeScreenHeight = WinAPICaller.ScreenHeight;

                int modelSpaceX = (int)((detectedX / (float)nativeScreenWidth) * modelImageSize);
                int modelSpaceY = (int)((detectedY / (float)nativeScreenHeight) * modelImageSize);

                // Check if we need to generate a new reaction delay
                if (!_reactionDelayActive && Dictionary.toggleState["Enable Reaction Delay"])
                {
                    GenerateReactionDelay();
                }

                // Get current screen position
                int currentScreenX, currentScreenY;
                lock (_positionLock)
                {
                    currentScreenX = _currentScreenX;
                    currentScreenY = _currentScreenY;
                }

                // Use merger to fuse user input with AI target
                var (fusedDeltaX, fusedDeltaY) = _merger.MergeMovement(
                    userDeltaX: 0,
                    userDeltaY: 0,
                    aiTargetX: modelSpaceX,
                    aiTargetY: modelSpaceY,
                    currentScreenX: currentScreenX,
                    currentScreenY: currentScreenY,
                    imageSize: modelImageSize
                );

                // Apply optional smoothing
                if (IsEMASmoothingEnabled)
                {
                    fusedDeltaX = (int)EmaSmoothing(previousX, fusedDeltaX, smoothingFactor);
                    fusedDeltaY = (int)EmaSmoothing(previousY, fusedDeltaY, smoothingFactor);
                }

                // Apply Mouse Sensitivity scaling
                double mouseSensitivity = Dictionary.sliderSettings["Mouse Sensitivity (+/-)"];
                double sensitivityFactor = 1.0 - mouseSensitivity;
                if (sensitivityFactor < 0.01) sensitivityFactor = 0.01;
                fusedDeltaX = (int)(fusedDeltaX * sensitivityFactor);
                fusedDeltaY = (int)(fusedDeltaY * sensitivityFactor);

                // Apply jitter
                int MouseJitter = (int)Dictionary.sliderSettings["Mouse Jitter"];
                int jitterX = MouseRandom.Next(-MouseJitter, MouseJitter);
                int jitterY = MouseRandom.Next(-MouseJitter, MouseJitter);

                fusedDeltaX += jitterX;
                fusedDeltaY += jitterY;

                // Apply hit distribution variance (60/30/10)
                (fusedDeltaX, fusedDeltaY) = ApplyVariance(fusedDeltaX, fusedDeltaY);

                // Clamp to safe range
                fusedDeltaX = Math.Clamp(fusedDeltaX, -500, 500);
                fusedDeltaY = Math.Clamp(fusedDeltaY, -500, 500);

                // Handle reaction delay (asynchronous, non-blocking)
                if (DecrementReactionDelay())
                {
                    _pendingDeltaX = fusedDeltaX;
                    _pendingDeltaY = fusedDeltaY;
                }
                else if (_reactionDelayActive == false && (_pendingDeltaX != 0 || _pendingDeltaY != 0))
                {
                    // Delay expired, send accumulated movement
                    if (_kmnet != null && _kmnet.IsConnected)
                    {
                        _ = _kmnet.SendMovementAsync(_pendingDeltaX, _pendingDeltaY);
                    }

                    lock (_positionLock)
                    {
                        _currentScreenX += _pendingDeltaX;
                        _currentScreenY += _pendingDeltaY;
                    }

                    previousX = _pendingDeltaX;
                    previousY = _pendingDeltaY;

                    _pendingDeltaX = 0;
                    _pendingDeltaY = 0;
                }
                else if (!_reactionDelayActive)
                {
                    // Normal send (no reaction delay active)
                    if (fusedDeltaX != 0 || fusedDeltaY != 0)
                    {
                        if (_kmnet != null && _kmnet.IsConnected)
                        {
                            _ = _kmnet.SendMovementAsync(fusedDeltaX, fusedDeltaY);
                        }
                    }

                    lock (_positionLock)
                    {
                        _currentScreenX += fusedDeltaX;
                        _currentScreenY += fusedDeltaY;
                    }

                    previousX = fusedDeltaX;
                    previousY = fusedDeltaY;
                }

                if (!Dictionary.toggleState["Auto Trigger"])
                {
                    ResetSprayState();
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Erreur mouvement KMBox: {ex.Message}", false);
            }
        }
    }
}
