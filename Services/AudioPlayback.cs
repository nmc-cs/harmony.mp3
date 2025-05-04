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

        // Initialize all events to avoid non-nullable warnings
        public event EventHandler<TimeSpan> PositionChanged = delegate { };
        public event EventHandler PlaybackStopped = delegate { };
        public event EventHandler PlaybackStarted = delegate { };
        public event EventHandler PlaybackPaused = delegate { };
        public event EventHandler MediaEnded = delegate { };

        public bool IsPlaying => _mediaPlayer?.Source != null && _mediaPlayer.Position < _mediaPlayer.NaturalDuration.TimeSpan;
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

        public AudioPlaybackService()
        {
            _mediaPlayer = new MediaPlayer();
            _mediaPlayer.MediaEnded += (s, e) => MediaEnded?.Invoke(this, EventArgs.Empty);

            _positionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _positionTimer.Tick += (s, e) => PositionChanged?.Invoke(this, Position);
        }

        public void Play(AudioFile file)
        {
            if (file == null) return;

            _currentFile = file;
            _mediaPlayer.Open(new Uri(file.FilePath));
            _mediaPlayer.Play();
            _positionTimer.Start();

            PlaybackStarted?.Invoke(this, EventArgs.Empty);
        }

        public void Play()
        {
            if (_mediaPlayer.Source == null) return;

            _mediaPlayer.Play();
            _positionTimer.Start();

            PlaybackStarted?.Invoke(this, EventArgs.Empty);
        }

        public void Pause()
        {
            _mediaPlayer.Pause();
            _positionTimer.Stop();

            PlaybackPaused?.Invoke(this, EventArgs.Empty);
        }

        public void Stop()
        {
            _mediaPlayer.Stop();
            _positionTimer.Stop();

            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }

        public void Seek(TimeSpan position)
        {
            if (_mediaPlayer.NaturalDuration.HasTimeSpan && position <= _mediaPlayer.NaturalDuration.TimeSpan)
            {
                _mediaPlayer.Position = position;
                PositionChanged?.Invoke(this, position);
            }
        }
    }
}