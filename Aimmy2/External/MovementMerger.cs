using System;

namespace Aimmy2.External
{
    /// <summary>
    /// DUAL-PC v2: Fuse user mouse input with AI target deltas
    /// Architecture: 
    /// - User mouse deltas sent IMMEDIATELY to Arduino (via MouseHooker → ArduinoSerial)
    /// - AI corrections calculated and sent ASYNCHRONOUSLY when ready
    /// Formula: Total_Delta = (Mouse_User_Delta * Input_Gain) + (AI_Target_Delta * AI_Sensitivity)
    /// 
    /// Thread-safe design to prevent race conditions between mouse hook thread and AI thread.
    /// </summary>
    public class MovementMerger
    {
        private readonly ScalingCalculator _scaler;
        private ArduinoSerial? _arduinoSerial;
        private object _settingsLock = new object();

        // Sensitivity multipliers from UI sliders
        private double _inputGain = 1.0;              // PC B mouse sensitivity multiplier (0.5x - 3.0x)
        private double _aiSensitivityMultiplier = 1.0; // AI target sensitivity (0.1x - 2.0x)

        // Movement smoothing (optional EMA)
        private bool _useEmaSmoothing = false;
        private double _smoothingFactor = 0.7;  // 0.0 = no smoothing, 1.0 = full smoothing

        // Last movement tracking for smoothing
        private int _lastFinalX = 0;
        private int _lastFinalY = 0;

        public MovementMerger(ScalingCalculator scaler)
        {
            _scaler = scaler ?? throw new ArgumentNullException(nameof(scaler));
        }

        /// <summary>
        /// DUAL-PC v2: Configure l'Arduino pour envoi des corrections IA
        /// </summary>
        public void SetArduinoSerial(ArduinoSerial? arduinoSerial)
        {
            _arduinoSerial = arduinoSerial;
        }

        /// <summary>
        /// Update the input gain multiplier from UI slider
        /// Range: 0.5 to 3.0 (default 1.0)
        /// </summary>
        public void SetInputGain(double gain)
        {
            lock (_settingsLock)
            {
                _inputGain = Math.Clamp(gain, 0.5, 3.0);
            }
        }

        /// <summary>
        /// Update the AI sensitivity multiplier from UI slider
        /// Range: 0.1 to 2.0 (default 1.0)
        /// </summary>
        public void SetAISensitivityMultiplier(double multiplier)
        {
            lock (_settingsLock)
            {
                _aiSensitivityMultiplier = Math.Clamp(multiplier, 0.1, 2.0);
            }
        }

        /// <summary>
        /// Enable/disable EMA smoothing for final movement
        /// </summary>
        public void SetEmaSmoothing(bool enabled, double smoothingFactor = 0.7)
        {
            lock (_settingsLock)
            {
                _useEmaSmoothing = enabled;
                _smoothingFactor = Math.Clamp(smoothingFactor, 0.0, 1.0);
            }
        }

        /// <summary>
        /// DUAL-PC v2: Calcule et envoie la correction IA à l'Arduino (dès disponibilité)
        /// 
        /// Cette méthode est appelée par le pipeline IA QUAND les coordonnées cibles sont calculées.
        /// Elle envoie SEULEMENT la correction IA, pas le mouvement utilisateur
        /// (le mouvement utilisateur a déjà été envoyé immédiatement par MouseHooker).
        /// 
        /// Formula:
        ///   aiCorrectionX = aiDeltaX * aiSensitivity
        ///   aiCorrectionY = aiDeltaY * aiSensitivity
        /// 
        /// IMPORTANT: Cette méthode est NON-BLOQUANTE (ajoute à queue Arduino)
        /// </summary>
        /// <param name="aiTargetX">AI detection center X coordinate (in IMAGE_SIZE space, typically 640)</param>
        /// <param name="aiTargetY">AI detection center Y coordinate (in IMAGE_SIZE space, typically 640)</param>
        /// <param name="imageSize">YOLO model image size (default 640)</param>
        public void SendAICorrection(int aiTargetX, int aiTargetY, int imageSize = 640)
        {
            if (_arduinoSerial == null || !_arduinoSerial.IsConnected)
                return;

            double aiSensitivity;
            lock (_settingsLock)
            {
                aiSensitivity = _aiSensitivityMultiplier;
            }

            // Step 1: Convert AI target from IMAGE_SIZE to screen space
            var (aiDeltaX, aiDeltaY) = _scaler.CalculateMovement(aiTargetX, aiTargetY, imageSize);

            // Step 2: Apply AI sensitivity multiplier
            double correctionX = aiDeltaX * aiSensitivity;
            double correctionY = aiDeltaY * aiSensitivity;

            // Step 3: Apply optional smoothing
            if (_useEmaSmoothing)
            {
                correctionX = ApplyEMASmoothing(correctionX, _lastFinalX);
                correctionY = ApplyEMASmoothing(correctionY, _lastFinalY);
            }

            // Step 4: Convert to integers for serial transmission
            int finalCorrectionX = (int)Math.Round(correctionX);
            int finalCorrectionY = (int)Math.Round(correctionY);

            // Cache for smoothing
            _lastFinalX = finalCorrectionX;
            _lastFinalY = finalCorrectionY;

            // Step 5: Envoyer à Arduino (non-bloquant, sera envoyé dès que prêt)
            _arduinoSerial.SendAICorrection(finalCorrectionX, finalCorrectionY);
        }

        /// <summary>
        /// DEPRECATED: Ancienne méthode de fusion synchrone
        /// Remplacée par SendAICorrection() pour latence minimale
        /// Gardée pour compatibilité - combine et retourne au lieu d'envoyer
        /// </summary>
        public (int finalDeltaX, int finalDeltaY) MergeMovement(
            int userDeltaX,
            int userDeltaY,
            int aiTargetX,
            int aiTargetY,
            int currentScreenX,
            int currentScreenY,
            int imageSize = 640)
        {
            double inputGain, aiSensitivity;
            lock (_settingsLock)
            {
                inputGain = _inputGain;
                aiSensitivity = _aiSensitivityMultiplier;
            }

            // Step 1: Use ScalingCalculator to convert AI target from IMAGE_SIZE to screen space
            // The AI detector returns coordinates in 640x640 space
            var (aiDeltaX, aiDeltaY) = _scaler.CalculateMovement(aiTargetX, aiTargetY, imageSize);

            // Step 2: Apply multipliers to each component
            double userContributionX = userDeltaX * inputGain;
            double userContributionY = userDeltaY * inputGain;

            double aiContributionX = aiDeltaX * aiSensitivity;
            double aiContributionY = aiDeltaY * aiSensitivity;

            // Step 3: Merge components
            double totalX = userContributionX + aiContributionX;
            double totalY = userContributionY + aiContributionY;

            // Step 4: Apply optional smoothing
            if (_useEmaSmoothing)
            {
                totalX = ApplyEMASmoothing(totalX, _lastFinalX);
                totalY = ApplyEMASmoothing(totalY, _lastFinalY);
            }

            // Step 5: Convert to integers for serial transmission
            int finalX = (int)Math.Round(totalX);
            int finalY = (int)Math.Round(totalY);

            // Cache for smoothing
            _lastFinalX = finalX;
            _lastFinalY = finalY;

            return (finalX, finalY);
        }

        /// <summary>
        /// Apply Exponential Moving Average smoothing to reduce jitter
        /// </summary>
        private double ApplyEMASmoothing(double current, double previous)
        {
            return (current * _smoothingFactor) + (previous * (1.0 - _smoothingFactor));
        }

        /// <summary>
        /// Reset movement state (call when AI detection resets or user wants fresh start)
        /// </summary>
        public void Reset()
        {
            lock (_settingsLock)
            {
                _lastFinalX = 0;
                _lastFinalY = 0;
            }
        }

        /// <summary>
        /// Get current settings (for UI display)
        /// </summary>
        public (double InputGain, double AISensitivity, bool UseSmoothing) GetSettings()
        {
            lock (_settingsLock)
            {
                return (_inputGain, _aiSensitivityMultiplier, _useEmaSmoothing);
            }
        }
    }
}
