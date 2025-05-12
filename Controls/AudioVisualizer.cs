using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Harmony.Models;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using System.Linq;

namespace Harmony.Controls
{
    /// <summary>
    /// A high-performance audio visualizer that displays a dynamic equalizer in response to music
    /// </summary>
    public class AudioVisualizer : Canvas, IDisposable
    {
        #region Fields & Constants

        // Configuration
        private const int DEFAULT_BAR_COUNT = 32;
        private const int FFT_SIZE = 8192; // Higher value for better frequency resolution
        private const int SAMPLE_RATE = 44100;
        private const float SMOOTHING_FACTOR = 0.3f; // Lower for more responsive, higher for smoother
        private const float BASS_BOOST = 2.5f; // Emphasize bass frequencies
        private const float MID_BOOST = 1.5f; // Emphasize mid frequencies
        private const float SCALE_FACTOR = 4.0f; // Amplify overall visualization

        // State and resources
        private readonly List<Rectangle> _bars = new();
        private readonly DispatcherTimer _animationTimer;
        private readonly MMDeviceEnumerator _deviceEnumerator;
        private readonly WasapiCapture _audioCapture;
        private readonly NAudio.Dsp.Complex[] _fftBuffer;
        private readonly float[] _fftResults;
        private readonly float[] _prevResults;
        private readonly float[] _windowFunction;
        private readonly float[] _audioData;

        // Display properties
        private double _minBarHeight = 2;
        private double _maxBarHeight => ActualHeight - _minBarHeight;
        private readonly Random _random = new Random();
        private bool _isDisposed;
        private bool _isActivated;
        private readonly object _lockObject = new object();

        #endregion

        #region Dependency Properties

        public static readonly DependencyProperty PlaybackStateProperty =
            DependencyProperty.Register(
                nameof(PlaybackState),
                typeof(Harmony.Models.PlaybackState),
                typeof(AudioVisualizer),
                new PropertyMetadata(Harmony.Models.PlaybackState.Stopped, OnPlaybackStateChanged));

        public Harmony.Models.PlaybackState PlaybackState
        {
            get => (Harmony.Models.PlaybackState)GetValue(PlaybackStateProperty);
            set => SetValue(PlaybackStateProperty, value);
        }

        public static readonly DependencyProperty VisualizationColorProperty =
            DependencyProperty.Register(
                nameof(VisualizationColor),
                typeof(Color),
                typeof(AudioVisualizer),
                new PropertyMetadata(Colors.Purple));

        public Color VisualizationColor
        {
            get => (Color)GetValue(VisualizationColorProperty);
            set => SetValue(VisualizationColorProperty, value);
        }

        #endregion

        #region Constructor

        public AudioVisualizer() : this(DEFAULT_BAR_COUNT) { }

        public AudioVisualizer(int barCount)
        {
            // Initialize FFT buffers
            _fftBuffer = new NAudio.Dsp.Complex[FFT_SIZE];
            _fftResults = new float[FFT_SIZE / 2];
            _prevResults = new float[FFT_SIZE / 2];
            _audioData = new float[FFT_SIZE];

            // Pre-compute window function (Hamming window for better frequency resolution)
            _windowFunction = new float[FFT_SIZE];
            for (int i = 0; i < FFT_SIZE; i++)
            {
                _windowFunction[i] = 0.54f - 0.46f * (float)Math.Cos(2.0f * Math.PI * i / (FFT_SIZE - 1));
            }

            // Initialize UI update timer
            _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(33) // ~30fps for smooth animation
            };
            _animationTimer.Tick += AnimationTimerTick;

            // Initialize audio capture
            try
            {
                _deviceEnumerator = new MMDeviceEnumerator();
                var captureDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

                _audioCapture = new WasapiLoopbackCapture(captureDevice)
                {
                    WaveFormat = new WaveFormat(SAMPLE_RATE, 16, 2)
                };

                _audioCapture.DataAvailable += OnAudioDataAvailable;
                _audioCapture.RecordingStopped += (s, e) =>
                {
                    // Re-activate capture if we're still supposed to be visualizing
                    if (_isActivated && !_isDisposed)
                    {
                        try { _audioCapture.StartRecording(); }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to restart capture: {ex.Message}"); }
                    }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing audio capture: {ex.Message}");
                // Create a fallback for when system audio capture isn't available
                _animationTimer.Tick -= AnimationTimerTick;
                _animationTimer.Tick += FallbackAnimationTick;
            }

            // Set up event handlers
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            SizeChanged += OnSizeChanged;
        }

        #endregion

        #region Event Handlers

        private static void OnPlaybackStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AudioVisualizer visualizer)
            {
                visualizer.UpdatePlaybackState((Harmony.Models.PlaybackState)e.NewValue);
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            CreateBars();
            UpdatePlaybackState(PlaybackState);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Stop();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
            {
                CreateBars();
            }
        }

        private void UpdatePlaybackState(Harmony.Models.PlaybackState state)
        {
            switch (state)
            {
                case Harmony.Models.PlaybackState.Playing:
                    Start();
                    break;
                case Harmony.Models.PlaybackState.Paused:
                case Harmony.Models.PlaybackState.Stopped:
                    Stop();
                    break;
            }
        }

        private void OnAudioDataAvailable(object sender, WaveInEventArgs e)
        {
            if (!_isActivated || e.BytesRecorded == 0) return;

            // Convert byte array to float array
            int bytesPerSample = _audioCapture.WaveFormat.BitsPerSample / 8;
            int samplesRecorded = e.BytesRecorded / bytesPerSample;

            // Copy and process the latest audio data for FFT
            lock (_lockObject)
            {
                // Shift the buffer to make room for new data
                Array.Copy(_audioData, samplesRecorded, _audioData, 0, _audioData.Length - samplesRecorded);

                // Convert the incoming audio bytes to float values and apply the window function
                for (int i = 0; i < samplesRecorded && i < _audioData.Length; i++)
                {
                    if (bytesPerSample == 2) // 16-bit audio
                    {
                        // Convert the 16-bit sample to a float and scale to [-1.0, 1.0]
                        short sample = (short)(e.Buffer[i * 2] | (e.Buffer[i * 2 + 1] << 8));
                        _audioData[_audioData.Length - samplesRecorded + i] = sample / 32768f * _windowFunction[i % _windowFunction.Length];
                    }
                    else // Handle other bit depths if needed
                    {
                        // Default fallback
                        _audioData[_audioData.Length - samplesRecorded + i] = 0;
                    }
                }

                // Perform FFT on the audio data
                PerformFFT();
            }
        }

        private void AnimationTimerTick(object sender, EventArgs e)
        {
            UpdateVisualization();
        }

        private void FallbackAnimationTick(object sender, EventArgs e)
        {
            // Generate random visualizer movement for when we can't capture system audio
            if (PlaybackState == Harmony.Models.PlaybackState.Playing)
            {
                float[] randomData = new float[32];
                for (int i = 0; i < randomData.Length; i++)
                {
                    // Frequencies typically decrease as we go higher
                    // Bass frequencies (low indexes) are usually higher
                    float intensity = (float)(_random.NextDouble() * 0.7 + 0.3) * (1.0f - (i / (float)randomData.Length) * 0.6f);

                    // Add some "beats" that are synchronized
                    if (i < 5 && _random.NextDouble() > 0.7)
                    {
                        intensity = (float)(_random.NextDouble() * 0.5 + 0.5);
                    }

                    randomData[i] = intensity;
                }

                lock (_lockObject)
                {
                    // Apply smoothing between frames
                    for (int i = 0; i < _fftResults.Length && i < randomData.Length; i++)
                    {
                        _fftResults[i] = _prevResults[i] * SMOOTHING_FACTOR + randomData[i] * (1 - SMOOTHING_FACTOR);
                        _prevResults[i] = _fftResults[i];
                    }
                }

                UpdateVisualization();
            }
            else
            {
                // Reset visualization to zero when not playing
                ResetBars();
            }
        }

        #endregion

        #region Audio Processing & Visualization

        private void PerformFFT()
        {
            // Copy audio data to FFT buffer
            for (int i = 0; i < FFT_SIZE; i++)
            {
                _fftBuffer[i].X = _audioData[i];
                _fftBuffer[i].Y = 0;
            }

            // Perform Fast Fourier Transform
            FastFourierTransform.FFT(false, (int)Math.Log2(FFT_SIZE), _fftBuffer);

            // Calculate magnitude of each frequency component
            for (int i = 0; i < FFT_SIZE / 2; i++)
            {
                // Calculate magnitude and apply scaling
                float magnitude = (float)Math.Sqrt(_fftBuffer[i].X * _fftBuffer[i].X +
                                                  _fftBuffer[i].Y * _fftBuffer[i].Y);

                // Scale the magnitude based on frequency - boost bass and mid frequencies
                float frequencyFactor = 1.0f;

                // Apply frequency-dependent scaling (bass boost)
                if (i < FFT_SIZE / 16)        // Bass frequencies
                    frequencyFactor = BASS_BOOST;
                else if (i < FFT_SIZE / 8)    // Mid frequencies
                    frequencyFactor = MID_BOOST;

                // Apply overall scaling and frequency-specific scaling
                magnitude *= SCALE_FACTOR * frequencyFactor;

                // Clamp the value to a reasonable range
                magnitude = Math.Min(1.0f, magnitude);

                // Apply smoothing between frames for more natural movement
                _fftResults[i] = _prevResults[i] * SMOOTHING_FACTOR + magnitude * (1 - SMOOTHING_FACTOR);
                _prevResults[i] = _fftResults[i];
            }
        }

        private void UpdateVisualization()
        {
            if (_bars.Count == 0) return;

            lock (_lockObject)
            {
                // Process FFT results into bar heights
                float[] barValues = ProcessFFTResults(_bars.Count);

                // Update each bar
                for (int i = 0; i < _bars.Count; i++)
                {
                    if (i < barValues.Length)
                    {
                        double targetHeight = _minBarHeight + _maxBarHeight * barValues[i];
                        AnimateBar(_bars[i], targetHeight, barValues[i]);
                    }
                }
            }
        }

        private float[] ProcessFFTResults(int barCount)
        {
            // Map FFT frequency bins to visualizer bars using logarithmic scaling
            // This gives more space to lower frequencies which is more pleasing visually
            float[] barValues = new float[barCount];

            for (int i = 0; i < barCount; i++)
            {
                // Use logarithmic mapping to emphasize bass and mid frequencies
                double ratio = Math.Log(1 + (i / (double)barCount) * 8) / Math.Log(9);
                int fftIndex = (int)(ratio * (_fftResults.Length / 3)); // Use only first third of spectrum for better visualization

                if (fftIndex < _fftResults.Length)
                {
                    // Use a small window of frequencies for each bar for smoother representation
                    int windowSize = Math.Max(1, _fftResults.Length / (barCount * 4));
                    float sum = 0;

                    for (int j = 0; j < windowSize && fftIndex + j < _fftResults.Length; j++)
                    {
                        sum += _fftResults[fftIndex + j];
                    }

                    barValues[i] = sum / windowSize;
                }
            }

            return barValues;
        }

        private void AnimateBar(Rectangle bar, double targetHeight, float intensity)
        {
            // Create animation for height
            var heightAnimation = new DoubleAnimation
            {
                To = targetHeight,
                Duration = TimeSpan.FromMilliseconds(50),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            // Apply height animation
            bar.BeginAnimation(HeightProperty, heightAnimation);

            // Animate the color based on intensity
            if (bar.Fill is SolidColorBrush brush)
            {
                // Base color is the visualization color property
                byte r = (byte)Math.Min(255, VisualizationColor.R + (255 - VisualizationColor.R) * intensity * 0.7);
                byte g = (byte)Math.Min(255, VisualizationColor.G + (255 - VisualizationColor.G) * intensity * 0.7);
                byte b = (byte)Math.Min(255, VisualizationColor.B + (255 - VisualizationColor.B) * intensity * 0.7);

                var targetColor = Color.FromRgb(r, g, b);

                // Create and apply color animation
                var colorAnimation = new ColorAnimation
                {
                    To = targetColor,
                    Duration = TimeSpan.FromMilliseconds(50)
                };

                brush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnimation);
            }
        }

        #endregion

        #region UI Management

        private void CreateBars()
        {
            Children.Clear();
            _bars.Clear();

            if (ActualWidth <= 0 || ActualHeight <= 0) return;

            // Create the visualizer bars with optimal spacing
            int barCount = DEFAULT_BAR_COUNT;
            double barWidth = Math.Max(3, ActualWidth / (barCount * 1.2));
            double spacing = barWidth / 4;
            double totalWidth = barWidth * barCount + spacing * (barCount - 1);
            double startX = (ActualWidth - totalWidth) / 2;

            for (int i = 0; i < barCount; i++)
            {
                var bar = new Rectangle
                {
                    Width = barWidth,
                    Height = _minBarHeight,
                    Fill = new SolidColorBrush(VisualizationColor),
                    RadiusX = barWidth * 0.3, // Rounded corners
                    RadiusY = barWidth * 0.3,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = Colors.Black,
                        Direction = 270,
                        ShadowDepth = 2,
                        BlurRadius = 4,
                        Opacity = 0.5
                    }
                };

                // Position from bottom of container
                SetLeft(bar, startX + i * (barWidth + spacing));
                SetBottom(bar, 0);

                _bars.Add(bar);
                Children.Add(bar);
            }
        }

        private void ResetBars()
        {
            if (_bars.Count == 0) return;

            // Animate all bars to their minimum height
            foreach (var bar in _bars)
            {
                // Stop any running animations
                bar.BeginAnimation(HeightProperty, null);

                // Create animation for smooth transition to minimum height
                var heightAnimation = new DoubleAnimation
                {
                    To = _minBarHeight,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };

                bar.BeginAnimation(HeightProperty, heightAnimation);

                // Reset color
                if (bar.Fill is SolidColorBrush brush)
                {
                    brush.BeginAnimation(SolidColorBrush.ColorProperty, null);

                    var colorAnimation = new ColorAnimation
                    {
                        To = VisualizationColor,
                        Duration = TimeSpan.FromMilliseconds(300)
                    };

                    brush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnimation);
                }
            }

            // Also reset the FFT results to avoid sudden jumps if playback starts again
            lock (_lockObject)
            {
                Array.Clear(_fftResults, 0, _fftResults.Length);
                Array.Clear(_prevResults, 0, _prevResults.Length);
            }
        }

        private void Start()
        {
            if (_isDisposed || _isActivated) return;

            _isActivated = true;

            try
            {
                if (_audioCapture != null && !_audioCapture.CaptureState.HasFlag(CaptureState.Capturing))
                {
                    _audioCapture.StartRecording();
                }

                _animationTimer.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting visualizer: {ex.Message}");
                // If we fail to start the capture, fall back to the simulation mode
                _animationTimer.Tick -= AnimationTimerTick;
                _animationTimer.Tick += FallbackAnimationTick;
                _animationTimer.Start();
            }
        }

        private void Stop()
        {
            _isActivated = false;

            try
            {
                _animationTimer.Stop();

                if (_audioCapture != null && _audioCapture.CaptureState.HasFlag(CaptureState.Capturing))
                {
                    _audioCapture.StopRecording();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping visualizer: {ex.Message}");
            }

            ResetBars();
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            if (_isDisposed) return;

            _isDisposed = true;
            Stop();

            _animationTimer.Tick -= AnimationTimerTick;
            _animationTimer.Tick -= FallbackAnimationTick;

            try
            {
                if (_audioCapture != null)
                {
                    _audioCapture.DataAvailable -= OnAudioDataAvailable;
                    _audioCapture.Dispose();
                }

                if (_deviceEnumerator != null)
                {
                    _deviceEnumerator.Dispose();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing visualizer: {ex.Message}");
            }
        }

        #endregion
    }
}