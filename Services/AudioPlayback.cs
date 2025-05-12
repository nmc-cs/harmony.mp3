using System;
using System.Windows.Media;
using Harmony.Models;
using System.Windows.Threading;

namespace Harmony.Services
{
    public class AudioPlaybackService
    {
        private MediaPlayer _mediaPlayer = null!;
        private DispatcherTimer _positionTimer = null!;
        private AudioFile? _currentFile;
        private PlaybackState _playbackState = PlaybackState.Stopped;

        // Initialize all events to avoid non-nullable warnings
        public event EventHandler<TimeSpan> PositionChanged = delegate { };
        public event EventHandler PlaybackStopped = delegate { };
        public event EventHandler PlaybackStarted = delegate { };
        public event EventHandler PlaybackPaused = delegate { };
        public event EventHandler MediaEnded = delegate { };
        public event EventHandler<PlaybackState> PlaybackStateChanged = delegate { };

        public bool IsPlaying => _playbackState == PlaybackState.Playing;

        public double Volume
        {
            get => _mediaPlayer?.Volume ?? 0;
            set
            {
                if (_mediaPlayer != null)
                    _mediaPlayer.Volume = value;
            }
        }

        public TimeSpan Position
        {
            get => _mediaPlayer?.Position ?? TimeSpan.Zero;
            set
            {
                if (_mediaPlayer != null && _mediaPlayer.NaturalDuration.HasTimeSpan)
                    _mediaPlayer.Position = value;
            }
        }

        public TimeSpan Duration => _mediaPlayer?.NaturalDuration.HasTimeSpan == true
            ? _mediaPlayer.NaturalDuration.TimeSpan
            : TimeSpan.Zero;

        public AudioFile? CurrentFile => _currentFile;

        public PlaybackState PlaybackState
        {
            get => _playbackState;
            private set
            {
                if (_playbackState != value)
                {
                    _playbackState = value;
                    PlaybackStateChanged?.Invoke(this, _playbackState);
                }
            }
        }

        public AudioPlaybackService()
        {
            _mediaPlayer = new MediaPlayer();
            _mediaPlayer.MediaEnded += OnMediaEnded;
            _mediaPlayer.MediaOpened += OnMediaOpened;
            _mediaPlayer.MediaFailed += OnMediaFailed;

            _positionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _positionTimer.Tick += (s, e) => PositionChanged?.Invoke(this, Position);
        }

        private void OnMediaOpened(object sender, EventArgs e)
        {
            // Update duration when media is loaded
            PositionChanged?.Invoke(this, TimeSpan.Zero);
        }

        private void OnMediaFailed(object sender, ExceptionEventArgs e)
        {
            PlaybackState = PlaybackState.Stopped;
            System.Diagnostics.Debug.WriteLine($"Media failed to load: {e.ErrorException.Message}");
        }

        private void OnMediaEnded(object sender, EventArgs e)
        {
            PlaybackState = PlaybackState.Stopped;
            _positionTimer.Stop();
            MediaEnded?.Invoke(this, EventArgs.Empty);
        }

        public void Play(AudioFile file)
        {
            if (file == null) return;

            _currentFile = file;
            _mediaPlayer.Open(new Uri(file.FilePath));
            _mediaPlayer.Play();
            _positionTimer.Start();

            PlaybackState = PlaybackState.Playing;
            PlaybackStarted?.Invoke(this, EventArgs.Empty);
        }

        public void Play()
        {
            if (_mediaPlayer.Source == null) return;

            _mediaPlayer.Play();
            _positionTimer.Start();

            PlaybackState = PlaybackState.Playing;
            PlaybackStarted?.Invoke(this, EventArgs.Empty);
        }

        public void Pause()
        {
            _mediaPlayer.Pause();
            _positionTimer.Stop();

            PlaybackState = PlaybackState.Paused;
            PlaybackPaused?.Invoke(this, EventArgs.Empty);
        }

        public void Stop()
        {
            _mediaPlayer.Stop();
            _positionTimer.Stop();

            PlaybackState = PlaybackState.Stopped;
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }

        public void Seek(TimeSpan position)
        {
            if (_mediaPlayer.Source != null)
            {
                if (_mediaPlayer.NaturalDuration.HasTimeSpan)
                {
                    // Ensure position is within valid range
                    TimeSpan duration = _mediaPlayer.NaturalDuration.TimeSpan;
                    if (position < TimeSpan.Zero)
                        position = TimeSpan.Zero;
                    if (position > duration)
                        position = duration;

                    _mediaPlayer.Position = position;
                    PositionChanged?.Invoke(this, position);
                }
            }
        }
    }
}