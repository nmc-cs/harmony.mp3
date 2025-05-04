using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Harmony.Models;
using Harmony.Services;

namespace Harmony.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly AudioPlaybackService _audioService;
        private readonly PlaylistManager _playlistManager;

        private bool _isPlaying;
        private double _volume = 1.0;
        private TimeSpan _currentPosition;
        private TimeSpan _duration;
        private AudioFile _currentTrack;
        private string _currentTrackText;

        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (_isPlaying != value)
                {
                    _isPlaying = value;
                    OnPropertyChanged();
                }
            }
        }

        public double Volume
        {
            get => _volume;
            set
            {
                if (_volume != value)
                {
                    _volume = value;
                    _audioService.Volume = value;
                    OnPropertyChanged();
                }
            }
        }

        public TimeSpan CurrentPosition
        {
            get => _currentPosition;
            set
            {
                if (_currentPosition != value)
                {
                    _currentPosition = value;
                    OnPropertyChanged();
                }
            }
        }

        public TimeSpan Duration
        {
            get => _duration;
            set
            {
                if (_duration != value)
                {
                    _duration = value;
                    OnPropertyChanged();
                }
            }
        }

        public AudioFile CurrentTrack
        {
            get => _currentTrack;
            set
            {
                if (_currentTrack != value)
                {
                    _currentTrack = value;
                    CurrentTrackText = value?.Title ?? "No track selected";
                    OnPropertyChanged();
                }
            }
        }

        public string CurrentTrackText
        {
            get => _currentTrackText;
            set
            {
                if (_currentTrackText != value)
                {
                    _currentTrackText = value;
                    OnPropertyChanged();
                }
            }
        }

        public Playlist CurrentPlaylist => _playlistManager.CurrentPlaylist;

        // Commands
        public ICommand PlayCommand { get; private set; }
        public ICommand PauseCommand { get; private set; }
        public ICommand StopCommand { get; private set; }
        public ICommand NextCommand { get; private set; }
        public ICommand PreviousCommand { get; private set; }
        public ICommand LoadFilesCommand { get; private set; }
        public ICommand SeekCommand { get; private set; }
        public ICommand PlaySelectedTrackCommand { get; private set; }

        public MainViewModel()
        {
            _audioService = new AudioPlaybackService();
            _playlistManager = new PlaylistManager(_audioService);

            // Subscribe to events
            _audioService.PositionChanged += (s, position) =>
            {
                CurrentPosition = position;
                if (_audioService.Duration.TotalSeconds > 0)
                {
                    Duration = _audioService.Duration;
                }
            };

            _audioService.PlaybackStarted += (s, e) => IsPlaying = true;
            _audioService.PlaybackPaused += (s, e) => IsPlaying = false;
            _audioService.PlaybackStopped += (s, e) => IsPlaying = false;

            _playlistManager.CurrentTrackChanged += (s, track) => CurrentTrack = track;

            // Initialize commands
            PlayCommand = new RelayCommand(_ => Play(), _ => CanPlay());
            PauseCommand = new RelayCommand(_ => Pause(), _ => CanPause());
            StopCommand = new RelayCommand(_ => Stop(), _ => CanStop());
            NextCommand = new RelayCommand(_ => Next(), _ => CanNext());
            PreviousCommand = new RelayCommand(_ => Previous(), _ => CanPrevious());
            LoadFilesCommand = new RelayCommand(_ => LoadFiles());
            SeekCommand = new RelayCommand(position => Seek((double)position));
            PlaySelectedTrackCommand = new RelayCommand(index => PlaySelectedTrack((int)index), _ => CurrentPlaylist?.Files.Count > 0);

            // Set default values
            CurrentTrackText = "No track selected";
            Volume = 0.7; // 70% volume by default
        }

        private bool CanPlay() => _audioService.CurrentFile != null;
        private bool CanPause() => IsPlaying;
        private bool CanStop() => IsPlaying;
        private bool CanNext() => CurrentPlaylist?.HasNext ?? false;
        private bool CanPrevious() => CurrentPlaylist?.HasPrevious ?? false;

        private void Play() => _audioService.Play();
        private void Pause() => _audioService.Pause();
        private void Stop() => _audioService.Stop();
        private void Next() => _playlistManager.PlayNext();
        private void Previous() => _playlistManager.PlayPrevious();
        private void LoadFiles() => _playlistManager.LoadFiles();

        private void Seek(double position)
        {
            if (Duration.TotalSeconds > 0)
            {
                var seekPosition = TimeSpan.FromSeconds(position * Duration.TotalSeconds);
                _audioService.Seek(seekPosition);
            }
        }

        private void PlaySelectedTrack(int index) => _playlistManager.PlayTrack(index);

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Simple implementation of ICommand
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object parameter) => _execute(parameter);
        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}