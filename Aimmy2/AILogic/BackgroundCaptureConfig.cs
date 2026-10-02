using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aimmy2.External;
using InputLogic;
using Other;
using static Other.LogManager;

namespace Aimmy2.AILogic
{
    /// <summary>
    /// Configuration et gestion du mode Background-Only:
    /// - Capture vidéo directe (pas de window capture)
    /// - Traitement en queue (non-bloquant)
    /// - Envoi UNIQUEMENT des corrections AI à l'Arduino (pas d'input utilisateur)
    /// - Pas de visualisation UI
    /// </summary>
    public class BackgroundCaptureConfig
    {
        public VideoCaptureManager? VideoCaptureManager { get; set; }
        public int SelectedDeviceIndex { get; set; } = -1;
        public bool IsBackgroundMode { get; set; } = true;

        // Options de capture
        public int TargetWidth { get; set; } = 1280;
        public int TargetHeight { get; set; } = 720;
        public int TargetFPS { get; set; } = 60;

        /// <summary>
        /// Initialise le mode background avec une source vidéo spécifique
        /// </summary>
        public async Task<bool> InitializeAsync(int deviceIndex)
        {
            try
            {
                if (VideoCaptureManager != null)
                {
                    VideoCaptureManager.Close();
                }

                VideoCaptureManager = new VideoCaptureManager();
                SelectedDeviceIndex = deviceIndex;

                bool opened = await VideoCaptureManager.OpenAsync(deviceIndex);
                if (!opened)
                {
                    Log(LogLevel.Error, $"Impossible d'ouvrir le périphérique {deviceIndex}", true);
                    VideoCaptureManager?.Dispose();
                    VideoCaptureManager = null;
                    return false;
                }

                Log(LogLevel.Info, "Mode Background initialisé avec succès", true, 1500);
                return true;
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Erreur initialisation background: {ex.Message}", true);
                VideoCaptureManager?.Dispose();
                VideoCaptureManager = null;
                return false;
            }
        }

        /// <summary>
        /// Arrête le mode background et nettoie les ressources
        /// </summary>
        public void Shutdown()
        {
            VideoCaptureManager?.Close();
            VideoCaptureManager?.Dispose();
            VideoCaptureManager = null;
            SelectedDeviceIndex = -1;
            Log(LogLevel.Info, "Mode Background arrêté", true, 1000);
        }

        /// <summary>
        /// Récupère les info de capture actuelle
        /// </summary>
        public string GetStatus()
        {
            if (VideoCaptureManager == null)
                return "Mode Background: Inactif";

            return VideoCaptureManager.GetCaptureInfo();
        }
    }

    /// <summary>
    /// Gère l'envoi UNIQUEMENT des corrections AI (sans input utilisateur)
    /// </summary>
    public class BackgroundAIOutputHandler
    {
        private readonly ArduinoSerial? _arduino;

        public BackgroundAIOutputHandler(ArduinoSerial? arduino)
        {
            _arduino = arduino;
        }

        /// <summary>
        /// Envoie UNIQUEMENT les corrections AI calculées (pas les mouvements utilisateur)
        /// </summary>
        public void SendAICorrection(int deltaX, int deltaY)
        {
            if (_arduino == null || !_arduino.IsConnected)
                return;

            if (deltaX == 0 && deltaY == 0)
                return;

            _arduino.SendAICorrection(deltaX, deltaY);
        }

        /// <summary>
        /// Envoie un clic (optionnel - selon configuration)
        /// </summary>
        public void SendMouseClick(string button, bool isPressed)
        {
            if (_arduino == null || !_arduino.IsConnected)
                return;

            _arduino.SendMouseClick(button, isPressed);
        }

        /// <summary>
        /// Envoie scroll wheel (optionnel - selon configuration)
        /// </summary>
        public void SendScrollWheel(int direction)
        {
            if (_arduino == null || !_arduino.IsConnected)
                return;

            _arduino.SendScrollWheel(direction);
        }
    }
}
