using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Aimmy2.UILibrary
{
    /// <summary>
    /// CalibrationPanel.xaml code-behind
    /// Handles UI events for calibrating Dual-PC movement merger settings
    /// </summary>
    public partial class CalibrationPanel : UserControl
    {
        private Aimmy2.External.MovementMerger? _merger;
        private Aimmy2.External.ScalingCalculator? _scaler;
        private Action? _onTestCircleStart;
        private Action? _onTestCircleStop;

        // Resolution presets: display name -> (width, height)
        private static readonly Dictionary<string, (int w, int h)> ResolutionPresets = new()
        {
            { "1920 x 1080 (FHD)", (1920, 1080) },
            { "1280 x 720 (HD)", (1280, 720) },
            { "1024 x 768 (XGA)", (1024, 768) },
            { "1440 x 1440 (Square)", (1440, 1440) },
            { "1600 x 900 (HD+)", (1600, 900) },
            { "2560 x 1440 (QHD)", (2560, 1440) },
            { "3840 x 2160 (4K)", (3840, 2160) }
        };

        public CalibrationPanel()
        {
            InitializeComponent();
            WireUpEventHandlers();
        }

        /// <summary>
        /// Set the MovementMerger instance to control
        /// </summary>
        public void SetMerger(Aimmy2.External.MovementMerger merger)
        {
            _merger = merger;
            if (_merger != null)
            {
                // Sync UI with current merger settings
                var (inputGain, aiSensitivity, useSmoothing) = _merger.GetSettings();
                InputGainSlider.Value = inputGain;
                AISensitivitySlider.Value = aiSensitivity;
                SmoothingCheckBox.IsChecked = useSmoothing;
                UpdateLabels();
            }
        }

        /// <summary>
        /// Set the ScalingCalculator instance for resolution management
        /// </summary>
        public void SetScalingCalculator(Aimmy2.External.ScalingCalculator scaler)
        {
            _scaler = scaler;
            if (_scaler != null)
            {
                // Load resolutions from Dictionary and apply to UI
                LoadResolutionsFromDictionary();
                UpdateResolutionDisplay();
            }
        }

        /// <summary>
        /// Load resolution settings from global Dictionary
        /// </summary>
        private void LoadResolutionsFromDictionary()
        {
            try
            {
                var captureW = (int)Aimmy2.Class.Dictionary.sliderSettings["Capture Card Width"];
                var captureH = (int)Aimmy2.Class.Dictionary.sliderSettings["Capture Card Height"];
                var gameW = (int)Aimmy2.Class.Dictionary.sliderSettings["Game Resolution Width"];
                var gameH = (int)Aimmy2.Class.Dictionary.sliderSettings["Game Resolution Height"];
                var nativeW = (int)Aimmy2.Class.Dictionary.sliderSettings["Native Screen Width"];
                var nativeH = (int)Aimmy2.Class.Dictionary.sliderSettings["Native Screen Height"];

                if (_scaler != null)
                {
                    _scaler.SetResolutions(captureW, captureH, gameW, gameH, nativeW, nativeH);
                }

                // Update UI to reflect loaded values
                SetComboBoxValue(GameResolutionCombo, gameW, gameH);
                SetComboBoxValue(NativeResolutionCombo, nativeW, nativeH);
                UpdateResolutionDisplay();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading resolutions: {ex.Message}");
            }
        }

        /// <summary>
        /// Save resolution settings to global Dictionary
        /// </summary>
        private void SaveResolutionsToDictionary()
        {
            try
            {
                if (_scaler != null)
                {
                    var (captureW, captureH, gameW, gameH, nativeW, nativeH) = _scaler.GetResolutions();

                    Aimmy2.Class.Dictionary.sliderSettings["Capture Card Width"] = captureW;
                    Aimmy2.Class.Dictionary.sliderSettings["Capture Card Height"] = captureH;
                    Aimmy2.Class.Dictionary.sliderSettings["Game Resolution Width"] = gameW;
                    Aimmy2.Class.Dictionary.sliderSettings["Game Resolution Height"] = gameH;
                    Aimmy2.Class.Dictionary.sliderSettings["Native Screen Width"] = nativeW;
                    Aimmy2.Class.Dictionary.sliderSettings["Native Screen Height"] = nativeH;
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error saving resolutions: {ex.Message}");
            }
        }

        /// <summary>
        /// Set callbacks for test circle button
        /// </summary>
        public void SetTestCircleCallbacks(Action onStart, Action onStop)
        {
            _onTestCircleStart = onStart;
            _onTestCircleStop = onStop;
        }

        /// <summary>
        /// Wire up event handlers for UI controls
        /// </summary>
        private void WireUpEventHandlers()
        {
            InputGainSlider.ValueChanged += (s, e) =>
            {
                if (_merger != null)
                {
                    _merger.SetInputGain(InputGainSlider.Value);
                    UpdateLabels();
                }
            };

            AISensitivitySlider.ValueChanged += (s, e) =>
            {
                if (_merger != null)
                {
                    _merger.SetAISensitivityMultiplier(AISensitivitySlider.Value);
                    UpdateLabels();
                }
            };

            SmoothingCheckBox.Checked += (s, e) =>
            {
                if (_merger != null)
                {
                    _merger.SetEmaSmoothing(true, 0.7);
                    UpdateStatus("Smoothing enabled");
                }
            };

            SmoothingCheckBox.Unchecked += (s, e) =>
            {
                if (_merger != null)
                {
                    _merger.SetEmaSmoothing(false);
                    UpdateStatus("Smoothing disabled");
                }
            };

            TestCircleButton.Click += (s, e) =>
            {
                UpdateStatus("Running test circle...");
                _onTestCircleStart?.Invoke();
            };

            ResetButton.Click += (s, e) =>
            {
                ResetToDefaults();
            };

            AutoDetectButton.Click += (s, e) =>
            {
                OnAutoDetectClick();
            };

            ApplyResolutionsButton.Click += (s, e) =>
            {
                OnApplyResolutionsClick();
            };
        }

        /// <summary>
        /// Update the displayed values on the labels
        /// </summary>
        private void UpdateLabels()
        {
            InputGainValue.Text = $"{InputGainSlider.Value:F1}x";
            AISensitivityValue.Text = $"{AISensitivitySlider.Value:F1}x";
        }

        /// <summary>
        /// Update resolution display from scaler
        /// </summary>
        private void UpdateResolutionDisplay()
        {
            if (_scaler == null)
                return;

            var (captureW, captureH, gameW, gameH, nativeW, nativeH) = _scaler.GetResolutions();
            CaptureResolutionDisplay.Text = $"{captureW} x {captureH}";

            // Set combo boxes to current values
            SetComboBoxValue(GameResolutionCombo, gameW, gameH);
            SetComboBoxValue(NativeResolutionCombo, nativeW, nativeH);
        }

        /// <summary>
        /// Set ComboBox to match resolution or add custom item
        /// </summary>
        private void SetComboBoxValue(ComboBox comboBox, int width, int height)
        {
            foreach (ComboBoxItem item in comboBox.Items)
            {
                string? content = item.Content?.ToString();
                if (content != null && ExtractResolution(content) == (width, height))
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }

            // If not found, add as custom item
            string customText = $"{width} x {height} (Custom)";
            ComboBoxItem customItem = new() { Content = customText };
            comboBox.Items.Add(customItem);
            comboBox.SelectedItem = customItem;
        }

        /// <summary>
        /// Extract (width, height) from combo box item text
        /// </summary>
        private (int w, int h) ExtractResolution(string text)
        {
            // Format: "1920 x 1080 (FHD)" or "1920 x 1080"
            string[] parts = text.Split('x');
            if (parts.Length >= 2)
            {
                if (int.TryParse(parts[0].Trim(), out int w) &&
                    int.TryParse(parts[1].Trim().Split(' ')[0], out int h))
                {
                    return (w, h);
                }
            }
            return (1920, 1080); // Fallback
        }

        /// <summary>
        /// Handle auto-detect button click
        /// </summary>
        private void OnAutoDetectClick()
        {
            if (_scaler == null)
            {
                UpdateStatus("Error: ScalingCalculator not set");
                return;
            }

            var captured = _scaler.TryAutoDetectCaptureResolution();
            if (captured.HasValue)
            {
                UpdateResolutionDisplay();
                SaveResolutionsToDictionary();
                UpdateStatus($"✅ Capture detected: {captured.Value.width}x{captured.Value.height} - Settings saved!");
            }
            else
            {
                UpdateStatus("❌ Failed to auto-detect capture resolution. Is capture card connected and powered on?");
            }
        }

        /// <summary>
        /// Handle apply resolutions button click
        /// </summary>
        private void OnApplyResolutionsClick()
        {
            if (_scaler == null)
            {
                UpdateStatus("Error: ScalingCalculator not set");
                return;
            }

            var gameRes = ExtractResolution((string?)GameResolutionCombo.SelectedItem ?? "1920 x 1080");
            var nativeRes = ExtractResolution((string?)NativeResolutionCombo.SelectedItem ?? "1920 x 1080");

            var (captureW, captureH, _, _, _, _) = _scaler.GetResolutions();

            _scaler.SetResolutions(
                captureW, captureH,  // Capture resolution (already detected or set)
                gameRes.w, gameRes.h,  // Game resolution
                nativeRes.w, nativeRes.h  // Native resolution
            );

            // Save to Dictionary for persistence
            SaveResolutionsToDictionary();

            UpdateResolutionDisplay();
            UpdateStatus($"✅ Applied: Capture {captureW}x{captureH}, Game {gameRes.w}x{gameRes.h}, Native {nativeRes.w}x{nativeRes.h}");
        }

        /// <summary>
        /// Reset all settings to defaults
        /// </summary>
        private void ResetToDefaults()
        {
            InputGainSlider.Value = 1.0;
            AISensitivitySlider.Value = 1.0;
            SmoothingCheckBox.IsChecked = false;

            if (_merger != null)
            {
                _merger.SetInputGain(1.0);
                _merger.SetAISensitivityMultiplier(1.0);
                _merger.SetEmaSmoothing(false);
                _merger.Reset();
            }

            if (_scaler != null)
            {
                // Reset to common default: 1920x1080 for all
                _scaler.SetResolutions(1280, 720, 1920, 1080, 1920, 1080);
                SaveResolutionsToDictionary();
                UpdateResolutionDisplay();
            }

            UpdateLabels();
            UpdateStatus("✅ Reset to defaults and saved!");
        }

        /// <summary>
        /// Update the status text display
        /// </summary>
        public void UpdateStatus(string message)
        {
            StatusText.Text = message;
        }

        /// <summary>
        /// Run a test circle pattern to verify calibration
        /// Sends delta commands in a circular pattern and waits for user confirmation
        /// </summary>
        public void RunTestCircle()
        {
            if (_merger == null)
            {
                UpdateStatus("Error: MovementMerger not set");
                return;
            }

            if (_scaler == null)
            {
                UpdateStatus("Error: ScalingCalculator not set");
                return;
            }

            // Run test circle async
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    UpdateStatus("Drawing test circle... Watch your cursor!");
                    await System.Threading.Tasks.Task.Delay(500); // Wait before starting

                    // Draw a circle with radius ~50 pixels, with 36 steps (10 degrees each)
                    const int radius = 50;
                    const int steps = 36;
                    const int stepDelayMs = 30; // Milliseconds between each point

                    // Get native screen width for boundary checking
                    var (_, _, _, _, nativeW, nativeH) = _scaler.GetResolutions();
                    int screenCenterX = nativeW / 2;
                    int screenCenterY = nativeH / 2;

                    for (int i = 0; i < steps; i++)
                    {
                        double angle = (i / (double)steps) * 2 * Math.PI;
                        int x = (int)(radius * Math.Cos(angle));
                        int y = (int)(radius * Math.Sin(angle));

                        // Get delta from previous point
                        int prevI = (i - 1 + steps) % steps;
                        double prevAngle = (prevI / (double)steps) * 2 * Math.PI;
                        int prevX = (int)(radius * Math.Cos(prevAngle));
                        int prevY = (int)(radius * Math.Sin(prevAngle));

                        int deltaX = x - prevX;
                        int deltaY = y - prevY;

                        // Use the merger to process the delta
                        // This simulates user mouse movement with no AI target
                        // Parameters: userDeltaX, userDeltaY, aiTargetX, aiTargetY, currentScreenX, currentScreenY, imageSize
                        _merger.MergeMovement(deltaX, deltaY, 0, 0, screenCenterX, screenCenterY, 640);

                        await System.Threading.Tasks.Task.Delay(stepDelayMs);
                    }

                    UpdateStatus("Test circle complete! Did your cursor draw a circle?");
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Test circle error: {ex.Message}");
                }
            });
        }
    }
}
