using Aimmy2.Class;
using Aimmy2.External;
using Aimmy2.Theme;
using Class;
using OpenCvSharp;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Window = System.Windows.Window;

namespace Visuality
{
    public partial class VideoPreviewWindow : Window
    {
        private VideoCaptureManager? _videoCaptureManager;
        private CancellationTokenSource? _renderCts;
        private bool _isRendering = false;
        private int _fovSize = 640;
        private Color _fovColor = Colors.Green;
        private Color _espColor = Colors.Red;

        public VideoPreviewWindow()
        {
            InitializeComponent();
            
            ThemeManager.ExcludeWindowFromBackground(this);
            
            // Subscribe to property changes
            PropertyChanger.ReceiveColor = UpdateFOVColor;
            PropertyChanger.ReceiveFOVSize = UpdateFOVSize;
            PropertyChanger.ReceiveDPColor = UpdateESPColor;
            
            this.Closing += (s, e) =>
            {
                StopRendering();
                e.Cancel = true;
                this.Hide();
            };
        }

        /// <summary>
        /// Initialise la fenêtre de preview avec le gestionnaire de capture vidéo
        /// </summary>
        public void Initialize(VideoCaptureManager videoCaptureManager)
        {
            _videoCaptureManager = videoCaptureManager;
        }

        /// <summary>
        /// Démarre le rendu asynchrone du flux vidéo
        /// </summary>
        public void StartRendering()
        {
            if (_isRendering || _videoCaptureManager == null)
                return;

            _isRendering = true;
            _renderCts = new CancellationTokenSource();
            
            Task.Run(() => RenderLoop(_renderCts.Token), _renderCts.Token);
        }

        /// <summary>
        /// Arrête le rendu
        /// </summary>
        public void StopRendering()
        {
            _isRendering = false;
            _renderCts?.Cancel();
        }

        /// <summary>
        /// Boucle de rendu asynchrone en arrière-plan
        /// </summary>
        private async Task RenderLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && _isRendering)
                {
                    try
                    {
                        // Récupère la frame actuelle
                        var frame = _videoCaptureManager?.CurrentFrame;
                        if (frame == null || frame.Empty())
                        {
                            await Task.Delay(16, cancellationToken); // ~60 FPS
                            continue;
                        }

                        // Convertit Mat en BitmapSource
                        var bitmap = ConvertMatToBitmapSource(frame);
                        if (bitmap == null)
                        {
                            await Task.Delay(16, cancellationToken);
                            continue;
                        }

                        // Met à jour le UI thread
                        Dispatcher.Invoke(() =>
                        {
                        try
                        {
                            VideoImage.Source = bitmap;
                            
                            // Mets à jour les dimensions du canvas
                            OverlayCanvas.Width = VideoImage.ActualWidth;
                            OverlayCanvas.Height = VideoImage.ActualHeight;

                            // Redessine les overlays
                            DrawOverlays(frame);
                        }
                            catch (Exception ex)
                            {
                                // Silencieusement ignorer les erreurs de rendering
                            }
                        });

                        frame.Dispose();
                        await Task.Delay(16, cancellationToken); // ~60 FPS
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        await Task.Delay(100, cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                // Silencieusement ignorer les erreurs de rendu
            }
        }

        /// <summary>
        /// Convertit une Mat OpenCV en BitmapSource WPF
        /// </summary>
        private BitmapSource? ConvertMatToBitmapSource(Mat mat)
        {
            try
            {
                if (mat.Empty())
                    return null;

                // Convertir BGR (OpenCV) en RGB (WPF)
                var rgbMat = new Mat();
                Cv2.CvtColor(mat, rgbMat, ColorConversionCodes.BGR2RGB);

                // Créer une copie des données
                byte[] pixelData = new byte[rgbMat.Total() * rgbMat.Channels()];
                System.Runtime.InteropServices.Marshal.Copy(rgbMat.Data, pixelData, 0, pixelData.Length);

                // Créer BitmapSource
                var bitmap = BitmapSource.Create(
                    rgbMat.Cols,
                    rgbMat.Rows,
                    96,
                    96,
                    PixelFormats.Rgb24,
                    null,
                    pixelData,
                    rgbMat.Cols * rgbMat.Channels());

                bitmap.Freeze();
                rgbMat.Dispose();
                return bitmap;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        /// <summary>
        /// Dessine les overlays (FOV + ESP) sur le canvas
        /// </summary>
        private void DrawOverlays(Mat frame)
        {
            try
            {
                // Efface les overlays précédents
                OverlayCanvas.Children.Clear();

                // Calcule la scale entre la frame et l'affichage
                double scaleX = OverlayCanvas.ActualWidth / frame.Cols;
                double scaleY = OverlayCanvas.ActualHeight / frame.Rows;

                // Dessine le FOV
                DrawFOVCircle(scaleX, scaleY);

                // Dessine les boîtes ESP (détections)
                DrawESPBoxes(frame, scaleX, scaleY);
            }
            catch (Exception ex)
            {
                // Silencieusement ignorer
            }
        }

        /// <summary>
        /// Dessine le cercle FOV (juste le contour)
        /// </summary>
        private void DrawFOVCircle(double scaleX, double scaleY)
        {
            try
            {
                // Récupère les settings FOV
                if (!Dictionary.sliderSettings.TryGetValue("FOV Size", out var fovSizeObj))
                    fovSizeObj = 640;

                _fovSize = (int)(double)fovSizeObj;

                // Centre du canvas
                double centerX = OverlayCanvas.ActualWidth / 2.0;
                double centerY = OverlayCanvas.ActualHeight / 2.0;

                // Rayon du cercle (FOV size / 2, en pixels de la preview)
                double radius = (_fovSize / 2.0) * scaleX;

                // Crée le cercle (ellipse)
                var circle = new Ellipse()
                {
                    Width = radius * 2,
                    Height = radius * 2,
                    Stroke = new SolidColorBrush(_fovColor),
                    StrokeThickness = 2,
                    Fill = null,
                    IsHitTestVisible = false
                };

                // Positionne le cercle au centre
                Canvas.SetLeft(circle, centerX - radius);
                Canvas.SetTop(circle, centerY - radius);

                OverlayCanvas.Children.Add(circle);
            }
            catch (Exception ex)
            {
                // Silencieusement ignorer
            }
        }

        /// <summary>
        /// Dessine les boîtes ESP pour les détections
        /// </summary>
        private void DrawESPBoxes(Mat frame, double scaleX, double scaleY)
        {
            try
            {
                // Note: Cette méthode nécessiterait d'accéder aux détections depuis AIManager
                // Pour maintenant, on crée juste le structure; les vraies détections seront ajoutées
                // via une méthode publique UpdateDetections() appelée depuis AIManager

                // Cette partie sera complétée une fois qu'on aura accès aux détections réelles
            }
            catch (Exception ex)
            {
                // Silencieusement ignorer
            }
        }

        /// <summary>
        /// Met à jour les détections ESP à afficher
        /// Appelée depuis AIManager lors de nouvelles détections
        /// </summary>
        public void UpdateDetections(System.Collections.Generic.List<(double x_min, double y_min, double x_max, double y_max, double confidence)> detections)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => UpdateDetections(detections));
                return;
            }

            try
            {
                // Récupère le canvas pour avoir les dimensions actuelles
                if (VideoImage.Source is BitmapSource bitmapSource)
                {
                    double frameWidth = bitmapSource.PixelWidth;
                    double frameHeight = bitmapSource.PixelHeight;
                    double scaleX = OverlayCanvas.ActualWidth / frameWidth;
                    double scaleY = OverlayCanvas.ActualHeight / frameHeight;

                    // Ajoute les boîtes de détection
                    foreach (var detection in detections)
                    {
                        // Convertit les coordonnées de frame à canvas
                        double x = detection.x_min * scaleX;
                        double y = detection.y_min * scaleY;
                        double w = (detection.x_max - detection.x_min) * scaleX;
                        double h = (detection.y_max - detection.y_min) * scaleY;

                        // Crée le rectangle de détection
                        var rect = new Rectangle()
                        {
                            Width = w,
                            Height = h,
                            Stroke = new SolidColorBrush(_espColor),
                            StrokeThickness = 2,
                            Fill = null,
                            IsHitTestVisible = false
                        };

                        Canvas.SetLeft(rect, x);
                        Canvas.SetTop(rect, y);
                        OverlayCanvas.Children.Add(rect);

                        // Ajoute le texte de confiance
                        var textBlock = new TextBlock()
                        {
                            Text = $"{(detection.confidence * 100):F0}%",
                            Foreground = new SolidColorBrush(_espColor),
                            FontSize = 12,
                            FontWeight = FontWeights.Bold,
                            IsHitTestVisible = false
                        };

                        Canvas.SetLeft(textBlock, x + 5);
                        Canvas.SetTop(textBlock, y + 5);
                        OverlayCanvas.Children.Add(textBlock);
                    }
                }
            }
            catch (Exception ex)
            {
                // Silencieusement ignorer
            }
        }

        /// <summary>
        /// Met à jour la couleur du FOV
        /// </summary>
        private void UpdateFOVColor(Color newColor)
        {
            _fovColor = newColor;
        }

        /// <summary>
        /// Met à jour la taille du FOV
        /// </summary>
        private void UpdateFOVSize(double newSize)
        {
            _fovSize = (int)newSize;
        }

        /// <summary>
        /// Met à jour la couleur ESP
        /// </summary>
        private void UpdateESPColor(Color newColor)
        {
            _espColor = newColor;
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            
            // Démarre le rendu une fois que la fenêtre est rendue
            StartRendering();
        }

        protected override void OnClosed(EventArgs e)
        {
            StopRendering();
            _renderCts?.Dispose();
            base.OnClosed(e);
        }
    }
}
