using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using Other;

namespace Aimmy2.External
{
    /// <summary>
    /// MODIFICATION DUAL-PC: Classe pour capture vidéo via Capture Card/Webcam avec OpenCV
    /// Architecture queue-based pour background processing non-bloquant
    /// </summary>
    public class VideoCaptureManager : IDisposable
    {
        private VideoCapture? _videoCapture;
        private Mat? _currentFrame;
        private readonly object _frameLock = new object();
        private Thread? _captureThread;
        private volatile bool _isCapturing = false;
        private int _currentDeviceIndex = -1;
        
        // Queue-based frame delivery (producer-consumer pattern)
        private readonly ConcurrentQueue<Mat> _frameQueue = new ConcurrentQueue<Mat>();
        private const int MAX_QUEUE_SIZE = 3; // Garde seulement les 3 dernières frames
        private int _droppedFrames = 0;

        public event Action<Mat>? OnFrameCaptured;
        public event Action<string>? OnCaptureError;

        public int CurrentDeviceIndex => _currentDeviceIndex;
        public Mat? CurrentFrame
        {
            get
            {
                lock (_frameLock)
                {
                    return _currentFrame?.Clone();
                }
            }
        }

        public int DroppedFrames => _droppedFrames;
        public int QueuedFrames => _frameQueue.Count;

        /// <summary>
        /// Énumère tous les périphériques vidéo disponibles (DirectShow via DirectShow Enumerator)
        /// </summary>
        public static List<(int index, string name)> EnumerateVideoDevices()
        {
            try
            {
                var allDevices = CaptureDeviceEnumerator.EnumerateCaptureDevices();
                var result = allDevices.Select(d => (d.index, d.name)).ToList();
                
                LogManager.Log(LogManager.LogLevel.Info, $"Énumération vidéo: {result.Count} périphérique(s) trouvé(s)", true, 1500);
                return result;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, $"Erreur énumération périphériques: {ex.Message}", true);
                return new List<(int, string)>();
            }
        }

        /// <summary>
        /// Énumère UNIQUEMENT les cartes de capture USB (exclut webcams intégrées)
        /// </summary>
        public static List<(int index, string name)> EnumerateUSBCaptureCards()
        {
            try
            {
                var usbDevices = CaptureDeviceEnumerator.EnumerateUSBCaptureCards();
                var result = usbDevices.Select(d => (d.index, d.name)).ToList();
                
                LogManager.Log(LogManager.LogLevel.Info, $"Cartes USB trouvées: {result.Count}", true, 1500);
                return result;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, $"Erreur énumération cartes USB: {ex.Message}", true);
                return new List<(int, string)>();
            }
        }

        /// <summary>
        /// Ouvre une source vidéo (capture card ou webcam) avec traitement en arrière-plan
        /// </summary>
        public async Task<bool> OpenAsync(int deviceIndex)
        {
            return await Task.Run(() =>
            {
                try
                {
                    Close(); // Ferme toute capture existante

                    _videoCapture = new VideoCapture(deviceIndex, VideoCaptureAPIs.DSHOW);

                    if (!_videoCapture.IsOpened())
                    {
                        LogManager.Log(LogManager.LogLevel.Error, $"Impossible d'ouvrir le périphérique {deviceIndex}", true);
                        _videoCapture?.Dispose();
                        _videoCapture = null;
                        return false;
                    }

                    // Configure la résolution et le FPS
                    _videoCapture.Set(VideoCaptureProperties.FrameWidth, 1280);
                    _videoCapture.Set(VideoCaptureProperties.FrameHeight, 720);
                    _videoCapture.Set(VideoCaptureProperties.Fps, 60);
                    _videoCapture.Set(VideoCaptureProperties.BufferSize, 1); // Minimise latence de buffer

                    _currentDeviceIndex = deviceIndex;
                    _isCapturing = true;
                    _droppedFrames = 0;

                    _captureThread = new Thread(CaptureWorker)
                    {
                        IsBackground = true,
                        Name = $"VideoCaptureThread-Device{deviceIndex}",
                        Priority = ThreadPriority.AboveNormal
                    };
                    _captureThread.Start();

                    LogManager.Log(LogManager.LogLevel.Info, $"Capture vidéo ouverte - Device {deviceIndex} (1280x720@60FPS)", true, 1500);
                    return true;
                }
                catch (Exception ex)
                {
                    _isCapturing = false;
                    string error = $"Erreur ouverture capture: {ex.Message}";
                    LogManager.Log(LogManager.LogLevel.Error, error, true);
                    OnCaptureError?.Invoke(error);
                    return false;
                }
            });
        }

        /// <summary>
        /// Worker thread qui capture continuellement les frames et les place dans la queue
        /// Pattern producer-consumer: capture non-bloquante avec queue de distribution
        /// </summary>
        private void CaptureWorker()
        {
            Mat frame = new Mat();
            int consecutiveErrors = 0;
            const int MAX_CONSECUTIVE_ERRORS = 10;

            try
            {
                while (_isCapturing && _videoCapture != null)
                {
                    try
                    {
                        // Capture le frame
                        _videoCapture.Read(frame);

                        if (frame.Empty())
                        {
                            consecutiveErrors++;
                            if (consecutiveErrors > MAX_CONSECUTIVE_ERRORS)
                            {
                                LogManager.Log(LogManager.LogLevel.Error, "Trop d'erreurs de capture successives, arrêt", true);
                                break;
                            }
                            Thread.Sleep(5);
                            continue;
                        }

                        consecutiveErrors = 0;

                        // Mets à jour le frame courant
                        lock (_frameLock)
                        {
                            _currentFrame?.Dispose();
                            _currentFrame = frame.Clone();
                        }

                        // Ajoute le frame à la queue (produit)
                        _frameQueue.Enqueue(frame.Clone());

                        // Contrôle la taille de la queue (garde seulement les frames récents)
                        while (_frameQueue.Count > MAX_QUEUE_SIZE)
                        {
                            if (_frameQueue.TryDequeue(out var oldFrame))
                            {
                                oldFrame?.Dispose();
                                _droppedFrames++;
                            }
                        }

                        // Notifie les consommateurs
                        OnFrameCaptured?.Invoke(frame.Clone());
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Warning, $"Erreur lecture frame: {ex.Message}", false);
                        consecutiveErrors++;
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, $"Erreur thread capture: {ex.Message}", false);
                OnCaptureError?.Invoke(ex.Message);
            }
            finally
            {
                frame?.Dispose();
                LogManager.Log(LogManager.LogLevel.Info, $"Thread capture arrêté (frames perdus: {_droppedFrames})", false);
            }
        }

        /// <summary>
        /// Récupère le frame suivant de la queue (consommateur)
        /// </summary>
        public bool TryGetNextFrame(out Mat? frame)
        {
            return _frameQueue.TryDequeue(out frame);
        }

        /// <summary>
        /// Récupère la résolution actuelle de la capture
        /// </summary>
        public (int width, int height)? GetCaptureResolution()
        {
            try
            {
                if (_videoCapture == null || !_videoCapture.IsOpened())
                    return null;

                int width = (int)_videoCapture.Get(VideoCaptureProperties.FrameWidth);
                int height = (int)_videoCapture.Get(VideoCaptureProperties.FrameHeight);
                return (width, height);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Retourne les propriétés de la capture en cours
        /// </summary>
        public string GetCaptureInfo()
        {
            if (_videoCapture == null || !_videoCapture.IsOpened())
                return "Aucune capture active";

            try
            {
                int width = (int)_videoCapture.Get(VideoCaptureProperties.FrameWidth);
                int height = (int)_videoCapture.Get(VideoCaptureProperties.FrameHeight);
                double fps = _videoCapture.Get(VideoCaptureProperties.Fps);
                return $"Device {_currentDeviceIndex}: {width}x{height}@{fps}FPS (Queue: {_frameQueue.Count}, Dropped: {_droppedFrames})";
            }
            catch
            {
                return "Erreur lecture propriétés";
            }
        }

        public void Close()
        {
            _isCapturing = false;

            if (_captureThread != null)
            {
                _captureThread.Join(2000);
            }

            // Nettoie la queue
            while (_frameQueue.TryDequeue(out var frame))
            {
                frame?.Dispose();
            }

            lock (_frameLock)
            {
                _currentFrame?.Dispose();
                _currentFrame = null;
            }

            _videoCapture?.Dispose();
            _videoCapture = null;
            _currentDeviceIndex = -1;

            LogManager.Log(LogManager.LogLevel.Info, "Capture vidéo fermée", true, 1500);
        }

        public void Dispose()
        {
            Close();
        }
    }
}
