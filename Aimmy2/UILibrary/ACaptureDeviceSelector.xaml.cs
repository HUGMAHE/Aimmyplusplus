using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Aimmy2.AILogic;
using Aimmy2.External;
using Other;
using static Other.LogManager;

namespace Aimmy2.UILibrary
{
    public partial class ACaptureDeviceSelector : UserControl
    {
        private BackgroundCaptureConfig? _config;

        /// <summary>
        /// Fired when device selection completes successfully
        /// </summary>
        public event Action<VideoCaptureManager?>? OnDeviceSelected;

        public ACaptureDeviceSelector()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initialise le sélecteur avec une configuration
        /// </summary>
        public void Initialize(BackgroundCaptureConfig config)
        {
            _config = config;
            RefreshDeviceList();
        }

        /// <summary>
        /// Actualise la liste des périphériques
        /// </summary>
        private void RefreshDeviceList()
        {
            try
            {
                DeviceComboBox.Items.Clear();
                
                // Énumère les périphériques
                var devices = VideoCaptureManager.EnumerateVideoDevices();
                
                foreach (var (index, name) in devices)
                {
                    DeviceComboBox.Items.Add(new ComboBoxItem 
                    { 
                        Content = $"[{index}] {name}",
                        Tag = index
                    });
                }

                StatusText.Text = $"{devices.Count} périphérique(s) trouvé(s)";
                OpenButton.IsEnabled = DeviceComboBox.Items.Count > 0;

                Log(LogLevel.Info, $"Énumération UI: {devices.Count} device(s)", false);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Erreur: {ex.Message}";
                Log(LogLevel.Error, $"Erreur énumération UI: {ex.Message}", false);
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshDeviceList();
        }

        private void DeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            OpenButton.IsEnabled = DeviceComboBox.SelectedItem != null;
        }

        private async void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            if (DeviceComboBox.SelectedItem is not ComboBoxItem item)
                return;

            if (_config == null)
            {
                StatusText.Text = "Configuration non initialisée";
                return;
            }

            if (item.Tag is not int deviceIndex)
                return;

            try
            {
                OpenButton.IsEnabled = false;
                StatusText.Text = "Ouverture...";

                bool success = await _config.InitializeAsync(deviceIndex);

                if (success)
                {
                    StatusText.Text = $"✓ Connecté: Device {deviceIndex}";
                    StatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 250, 123));
                    Log(LogLevel.Info, $"Capture device {deviceIndex} ouvert avec succès", true, 1500);
                    
                    // Fire the event so MainWindow can update AIManager
                    OnDeviceSelected?.Invoke(_config.VideoCaptureManager);
                }
                else
                {
                    StatusText.Text = "Erreur d'ouverture";
                    StatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 85, 85));
                    OpenButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Exception: {ex.Message}";
                OpenButton.IsEnabled = true;
                Log(LogLevel.Error, $"Exception ouverture device: {ex.Message}", false);
            }
        }
    }
}
