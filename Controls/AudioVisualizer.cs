using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Harmony.Models;
using Harmony.Services;

namespace Harmony.Controls
{
    public class AudioVisualizer : Canvas, IDisposable
    {
        private const int DefaultBarCount = 32;

        private readonly List<Rectangle> _bars = new List<Rectangle>();
        private readonly Dispatcher _uiDispatcher;
        private readonly AudioAnalyzerService _analyzer;
        private readonly int _barCount;
        private readonly double _minBarHeight = 2;

        // computed property — no readonly modifier
        private double _maxBarHeight => ActualHeight - _minBarHeight;

        private bool _isPlaying;
        private bool _isDisposed;

        public static readonly DependencyProperty PlaybackStateProperty =
            DependencyProperty.Register(
                nameof(PlaybackState),
                typeof(PlaybackState),
                typeof(AudioVisualizer),
                new PropertyMetadata(PlaybackState.Stopped, OnPlaybackStateChanged));

        public PlaybackState PlaybackState
        {
            get => (PlaybackState)GetValue(PlaybackStateProperty);
            set => SetValue(PlaybackStateProperty, value);
        }

        // Parameterless ctor for XAML
        public AudioVisualizer() : this(DefaultBarCount)
        {
        }

        // Actual initializer
        public AudioVisualizer(int barCount)
        {
            _barCount = barCount;
            _uiDispatcher = Dispatcher.CurrentDispatcher;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;

            // match FFT size to bar count if you like (barCount*64 is an example)
            _analyzer = new AudioAnalyzerService(fftLength: barCount * 64);
            _analyzer.SpectrumDataAvailable += OnSpectrumData;
        }

        private static void OnPlaybackStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AudioVisualizer viz)
                viz.UpdateState((PlaybackState)e.NewValue);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            CreateBars();
            UpdateState(PlaybackState);
            CompositionTarget.Rendering += OnRendering;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            CompositionTarget.Rendering -= OnRendering;
            Dispose();
        }

        private void UpdateState(PlaybackState state)
        {
            _isPlaying = (state == PlaybackState.Playing);
            if (_isPlaying) Start();
            else Stop();
        }

        private void Start()
        {
            try { _analyzer.StartAnalyzing(); }
            catch { /* log if you want */ }
        }

        private void Stop()
        {
            _analyzer.StopAnalyzing();
            ResetBars();
        }

        private float[] _latestSpectrum;
        private void OnSpectrumData(object _, float[] spectrum)
        {
            // cache for the UI thread
            _latestSpectrum = spectrum;
        }

        private void OnRendering(object _, EventArgs __)
        {
            if (!_isPlaying || _latestSpectrum == null || _bars.Count == 0)
                return;

            int n = _bars.Count;
            for (int i = 0; i < n; i++)
            {
                double val = _latestSpectrum[i * _latestSpectrum.Length / n];
                double targetHeight = _minBarHeight + _maxBarHeight * val;
                AnimateBarHeight(_bars[i], targetHeight);
                AnimateBarColor(_bars[i], val);
            }
        }

        private void AnimateBarHeight(Rectangle bar, double toHeight)
        {
            var anim = new DoubleAnimation
            {
                To = toHeight,
                Duration = TimeSpan.FromMilliseconds(50),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            bar.BeginAnimation(HeightProperty, anim, HandoffBehavior.SnapshotAndReplace);
        }

        private void AnimateBarColor(Rectangle bar, double intensity)
        {
            byte r = (byte)Math.Min(255, 120 + intensity * 135);
            byte g = (byte)Math.Min(255, intensity * 200);
            byte b = (byte)Math.Min(255, 180 + intensity * 75);
            var target = Color.FromRgb(r, g, b);

            var colorAnim = new ColorAnimation
            {
                To = target,
                Duration = TimeSpan.FromMilliseconds(100)
            };
            ((SolidColorBrush)bar.Fill).BeginAnimation(
                SolidColorBrush.ColorProperty,
                colorAnim);
        }

        private void ResetBars()
        {
            foreach (var bar in _bars)
            {
                bar.Height = _minBarHeight;
                ((SolidColorBrush)bar.Fill).Color = Colors.Purple;
            }
        }

        private void CreateBars()
        {
            Children.Clear();
            _bars.Clear();

            double barWidth = ActualWidth / (_barCount * 1.2);
            double spacing = barWidth / 6;
            double totalWidth = barWidth * _barCount + spacing * (_barCount - 1);
            double startX = (ActualWidth - totalWidth) / 2;

            for (int i = 0; i < _barCount; i++)
            {
                var bar = new Rectangle
                {
                    Width = barWidth,
                    Height = _minBarHeight,
                    Fill = new SolidColorBrush(Colors.Purple),
                    RadiusX = barWidth * 0.2,
                    RadiusY = barWidth * 0.2
                };

                SetLeft(bar, startX + i * (barWidth + spacing));
                SetBottom(bar, 0);

                _bars.Add(bar);
                Children.Add(bar);
            }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            if (sizeInfo.NewSize.Width > 0 && sizeInfo.NewSize.Height > 0)
                CreateBars();
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _analyzer.SpectrumDataAvailable -= OnSpectrumData;
            _analyzer.Dispose();
        }
    }
}
