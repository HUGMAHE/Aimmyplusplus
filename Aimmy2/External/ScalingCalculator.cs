using System;
using System.Drawing;
using Other;
using static Other.LogManager;

namespace Aimmy2.External
{
    /// <summary>
    /// MODIFICATION DUAL-PC: Système de scaling Triple-Résolution
    /// Gère la conversion de coordonnées entre:
    /// - Capture Resolution: Résolution du flux reçu (Capture Card)
    /// - Game Resolution: Résolution du jeu sur PC A (menu dans le jeu)
    /// - Native Resolution: Résolution physique de l'écran PC A
    /// </summary>
    public class ScalingCalculator
    {
        private int _captureWidth = 1920;
        private int _captureHeight = 1080;
        private int _gameWidth = 1920;
        private int _gameHeight = 1080;
        private int _nativeWidth = 1440;
        private int _nativeHeight = 1440;
        private float _sensitivityMultiplier = 1.0f;

        public void SetResolutions(int captureW, int captureH, int gameW, int gameH, int nativeW, int nativeH)
        {
            _captureWidth = Math.Max(captureW, 1);
            _captureHeight = Math.Max(captureH, 1);
            _gameWidth = Math.Max(gameW, 1);
            _gameHeight = Math.Max(gameH, 1);
            _nativeWidth = Math.Max(nativeW, 1);
            _nativeHeight = Math.Max(nativeH, 1);

            Log(LogLevel.Info, 
                $"Résolutions configurées - Capture: {_captureWidth}x{_captureHeight}, " +
                $"Jeu: {_gameWidth}x{_gameHeight}, Native: {_nativeWidth}x{_nativeHeight}", true, 2000);
        }

        public void SetSensitivityMultiplier(float multiplier)
        {
            _sensitivityMultiplier = Math.Max(multiplier, 0.1f);
        }

        public (int deltaX, int deltaY) CalculateMovement(double targetX, double targetY, int modelImageSize)
        {
            float scaleXFromModel = _captureWidth / (float)modelImageSize;
            float scaleYFromModel = _captureHeight / (float)modelImageSize;

            float targetXInCapture = (float)(targetX * scaleXFromModel);
            float targetYInCapture = (float)(targetY * scaleYFromModel);

            float centerCaptureX = _captureWidth / 2.0f;
            float centerCaptureY = _captureHeight / 2.0f;

            float deltaXInCapture = targetXInCapture - centerCaptureX;
            float deltaYInCapture = targetYInCapture - centerCaptureY;

            float normalizedDeltaX = deltaXInCapture / _captureWidth;
            float normalizedDeltaY = deltaYInCapture / _captureHeight;

            float gameSpaceDeltaX = normalizedDeltaX * _gameWidth;
            float gameSpaceDeltaY = normalizedDeltaY * _gameHeight;

            float stretchFactorX = _nativeWidth / (float)_gameWidth;
            float stretchFactorY = _nativeHeight / (float)_gameHeight;

            float stretchedDeltaX = gameSpaceDeltaX * stretchFactorX;
            float stretchedDeltaY = gameSpaceDeltaY * stretchFactorY;

            float finalDeltaX = stretchedDeltaX * _sensitivityMultiplier;
            float finalDeltaY = stretchedDeltaY * _sensitivityMultiplier;

            int roundedDeltaX = (int)Math.Round(finalDeltaX);
            int roundedDeltaY = (int)Math.Round(finalDeltaY);

            const int MAX_MOVEMENT = 500;
            roundedDeltaX = Math.Clamp(roundedDeltaX, -MAX_MOVEMENT, MAX_MOVEMENT);
            roundedDeltaY = Math.Clamp(roundedDeltaY, -MAX_MOVEMENT, MAX_MOVEMENT);

            return (roundedDeltaX, roundedDeltaY);
        }

        public (float normalizedX, float normalizedY) CalculateNormalizedMovement(double targetX, double targetY, int modelImageSize)
        {
            var (deltaX, deltaY) = CalculateMovement(targetX, targetY, modelImageSize);
            float normalizedX = deltaX / (float)_nativeWidth;
            float normalizedY = deltaY / (float)_nativeHeight;
            return (normalizedX, normalizedY);
        }

        public (int CaptureW, int CaptureH, int GameW, int GameH, int NativeW, int NativeH) GetResolutions()
        {
            return (_captureWidth, _captureHeight, _gameWidth, _gameHeight, _nativeWidth, _nativeHeight);
        }

        public (int width, int height)? TryAutoDetectCaptureResolution()
        {
            try
            {
                Log(LogLevel.Info, "Auto-détection de capture resolution...", false);
                return null;
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Erreur auto-détection: {ex.Message}", false);
                return null;
            }
        }

        public void LogDiagnostics()
        {
            Log(LogLevel.Info, "=== SCALING DIAGNOSTICS ===", false);
            Log(LogLevel.Info, $"Capture Resolution: {_captureWidth}x{_captureHeight}", false);
            Log(LogLevel.Info, $"Game Resolution: {_gameWidth}x{_gameHeight}", false);
            Log(LogLevel.Info, $"Native Resolution: {_nativeWidth}x{_nativeHeight}", false);
            Log(LogLevel.Info, $"Sensitivity Multiplier: {_sensitivityMultiplier}x", false);
            
            float stretchX = _nativeWidth / (float)_gameWidth;
            float stretchY = _nativeHeight / (float)_gameHeight;
            Log(LogLevel.Info, $"Stretch Factors: X={stretchX:F2}, Y={stretchY:F2}", false);
            Log(LogLevel.Info, "==========================", false);
        }
    }
}
