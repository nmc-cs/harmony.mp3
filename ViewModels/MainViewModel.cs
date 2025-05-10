using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Harmony.Models;
using Harmony.Services;
using Harmony.Views.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Harmony.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly AudioPlaybackService _audioService;
        private readonly PlaylistManager _playlistManager;
        private readonly DispatcherTimer _commandUpdateTimer;

        private bool _isPlaying;
        private double _volume = 1.0;
        private TimeSpan _currentPosition;
        private double _currentPositionSeconds; // Property for slider binding
        private TimeSpan _duration;
        private AudioFile? _currentTrack;
        private string _currentTrackText = "No track selected"; // Initialize with default value
        private PlaybackState _playbackState = PlaybackState.Stopped;
        private Playlist _selectedPlaylist;
        private ObservableCollection<AudioFile> _selectedTracks;

        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (_isPlaying != value)
                {
                    _isPlaying = value;

                    // Update playback state based on IsPlaying
                    PlaybackState = value ? PlaybackState.Playing :
                        (_audioService.Position.TotalSeconds > 0 ? PlaybackState.Paused : PlaybackState.Stopped);

                    OnPropertyChanged();
                    // Update commands when playback state changes
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public PlaybackState PlaybackState
        {
            get => _playbackState;
            set
            {
                if (_playbackState != value)
                {
                    _playbackState = value;
                    OnPropertyChanged();
                    CommandManager.InvalidateRequerySuggested();
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
                    _currentPositionSeconds = value.TotalSeconds; // Update seconds when TimeSpan changes
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentPositionSeconds));
                }
            }
        }

        // Property for binding to the slider
        public double CurrentPositionSeconds
        {
            get => _currentPositionSeconds;
            set
            {
                if (_currentPositionSeconds != value)
                {
                    _currentPositionSeconds = value;
                    OnPropertyChanged();

                    // Don't update CurrentPosition here to avoid circular updates
                    // The seek operation will be handled by the SeekCommand
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

        public AudioFile? CurrentTrack
        {
            get => _currentTrack;
            set
            {
                if (_currentTrack != value)
                {
                    _currentTrack = value;
                    UpdateCurrentTrackText();
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

        public ObservableCollection<Playlist> Playlists => _playlistManager.Playlists;

        public Playlist SelectedPlaylist
        {
            get => _selectedPlaylist;
            set
            {
                if (_selectedPlaylist != value)
                {
                    _selectedPlaylist = value;
                    if (value != null)
                    {
                        _playlistManager.CurrentPlaylist = value;
                    }
                    OnPropertyChanged();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public ObservableCollection<AudioFile> SelectedTracks
        {
            get => _selectedTracks;
            set
            {
                if (_selectedTracks != value)
                {
                    _selectedTracks = value;
                    OnPropertyChanged();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsShuffleEnabled
        {
            get => CurrentPlaylist?.IsShuffleEnabled ?? false;
            set
            {
                if (CurrentPlaylist != null && CurrentPlaylist.IsShuffleEnabled != value)
                {
                    CurrentPlaylist.IsShuffleEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        public RepeatMode RepeatMode
        {
            get => CurrentPlaylist?.RepeatMode ?? RepeatMode.None;
            set
            {
                if (CurrentPlaylist != null && CurrentPlaylist.RepeatMode != value)
                {
                    CurrentPlaylist.RepeatMode = value;
                    OnPropertyChanged();
                }
            }
        }

        // Commands
        public ICommand PlayCommand { get; private set; }
        public ICommand PauseCommand { get; private set; }
        public ICommand StopCommand { get; private set; }
        public ICommand NextCommand { get; private set; }
        public ICommand PreviousCommand { get; private set; }
        public ICommand LoadFilesCommand { get; private set; }
        public ICommand SeekCommand { get; private set; }
        public ICommand PlaySelectedTrackCommand { get; private set; }
        public ICommand PlaybackControlCommand { get; private set; }
        public ICommand CreatePlaylistCommand { get; private set; }
        public ICommand DeletePlaylistCommand { get; private set; }
        public ICommand RenamePlaylistCommand { get; private set; }
        public ICommand AddToPlaylistCommand { get; private set; }
        public ICommand ImportPlaylistCommand { get; private set; }
        public ICommand ExportPlaylistCommand { get; private set; }
        public ICommand ToggleShuffleCommand { get; private set; }
        public ICommand CycleRepeatModeCommand { get; private set; }

        public MainViewModel()
        {
            _audioService = new AudioPlaybackService();
            _playlistManager = new PlaylistManager(_audioService);
            _selectedTracks = new ObservableCollection<AudioFile>();

            // Initialize with the default playlist
            _selectedPlaylist = _playlistManager.CurrentPlaylist;

            // Subscribe to events
            _audioService.PositionChanged += (s, position) =>
            {
                CurrentPosition = position;
                if (_audioService.Duration.TotalSeconds > 0)
                {
                    Duration = _audioService.Duration;
                }
            };

            _audioService.PlaybackStarted += (s, e) =>
            {
                IsPlaying = true;
                PlaybackState = PlaybackState.Playing;
            };

            _audioService.PlaybackPaused += (s, e) =>
            {
                IsPlaying = false;
                PlaybackState = PlaybackState.Paused;
            };

            _audioService.PlaybackStopped += (s, e) =>
            {
                IsPlaying = false;
                PlaybackState = PlaybackState.Stopped;
            };

            _audioService.MediaEnded += (s, e) =>
            {
                if (RepeatMode == RepeatMode.One)
                {
                    // If repeat one is enabled, restart the current track
                    if (CurrentTrack != null)
                    {
                        _audioService.Play(CurrentTrack);
                    }
                }
                else
                {
                    IsPlaying = false;
                    PlaybackState = PlaybackState.Stopped;
                }
            };

            _playlistManager.CurrentTrackChanged += (s, track) =>
            {
                CurrentTrack = track;
                OnPropertyChanged(nameof(IsShuffleEnabled));
                OnPropertyChanged(nameof(RepeatMode));
            };

            _playlistManager.CurrentPlaylistChanged += (s, playlist) =>
            {
                // Refresh bindings when current playlist changes
                OnPropertyChanged(nameof(CurrentPlaylist));
                OnPropertyChanged(nameof(IsShuffleEnabled));
                OnPropertyChanged(nameof(RepeatMode));
            };

            _playlistManager.PlaylistsChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(Playlists));
            };

            // Initialize commands
            PlayCommand = new RelayCommand(_ => Play(), _ => CanPlay());
            PauseCommand = new RelayCommand(_ => Pause(), _ => CanPause());
            StopCommand = new RelayCommand(_ => Stop(), _ => CanStop());
            NextCommand = new RelayCommand(_ => Next(), _ => CanNext());
            PreviousCommand = new RelayCommand(_ => Previous(), _ => CanPrevious());
            LoadFilesCommand = new RelayCommand(_ => LoadFiles());
            SeekCommand = new RelayCommand(position => Seek((double)position));
            PlaySelectedTrackCommand = new RelayCommand(index => PlaySelectedTrack((int)index), _ => CurrentPlaylist?.Files.Count > 0);
            PlaybackControlCommand = new RelayCommand(_ => TogglePlayback());

            // Playlist management commands
            CreatePlaylistCommand = new RelayCommand(_ => CreatePlaylist());
            DeletePlaylistCommand = new RelayCommand(_ => DeletePlaylist(), _ => CanDeletePlaylist());
            RenamePlaylistCommand = new RelayCommand(_ => RenamePlaylist(), _ => CanRenamePlaylist());
            AddToPlaylistCommand = new RelayCommand(playlist => AddToPlaylist((Playlist)playlist), _ => CanAddToPlaylist());
            ImportPlaylistCommand = new RelayCommand(_ => ImportPlaylist());
            ExportPlaylistCommand = new RelayCommand(_ => ExportPlaylist(), _ => CanExportPlaylist());

            // Playback mode commands
            ToggleShuffleCommand = new RelayCommand(_ => ToggleShuffle());
            CycleRepeatModeCommand = new RelayCommand(_ => CycleRepeatMode());

            // Set default values
            CurrentTrackText = "No track selected";
            Volume = 0.7; // 70% volume by default

            // Create a timer to periodically update command states
            _commandUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _commandUpdateTimer.Tick += (s, e) => CommandManager.InvalidateRequerySuggested();
            _commandUpdateTimer.Start();
        }

        private bool CanPlay() => _audioService.CurrentFile != null && !IsPlaying;
        private bool CanPause() => IsPlaying;
        private bool CanStop() => IsPlaying || PlaybackState == PlaybackState.Paused;
        private bool CanNext() => CurrentPlaylist?.HasNext ?? false;
        private bool CanPrevious() => CurrentPlaylist?.HasPrevious ?? false;
        private bool CanDeletePlaylist() => SelectedPlaylist != null && Playlists.Count > 1;
        private bool CanRenamePlaylist() => SelectedPlaylist != null;
        private bool CanAddToPlaylist() => SelectedTracks != null && SelectedTracks.Count > 0;
        private bool CanExportPlaylist() => SelectedPlaylist != null && SelectedPlaylist.Files.Count > 0;

        private void Play()
        {
            if (_audioService.CurrentFile == null && CurrentPlaylist?.Files.Count > 0)
            {
                // If nothing is loaded yet but we have files, play the first one
                PlaySelectedTrack(0);
            }
            else
            {
                _audioService.Play();
            }
        }

        private void Pause() => _audioService.Pause();
        private void Stop() => _audioService.Stop();
        private void Next() => _playlistManager.PlayNext();
        private void Previous() => _playlistManager.PlayPrevious();
        private void LoadFiles() => _playlistManager.LoadFiles();

        // Method to handle the combined button functionality
        private void TogglePlayback()
        {
            switch (PlaybackState)
            {
                case PlaybackState.Playing:
                    Pause();
                    break;
                case PlaybackState.Paused:
                    Play();
                    break;
                case PlaybackState.Stopped:
                    Play();
                    break;
            }
        }

        private void Seek(double position)
        {
            if (Duration.TotalSeconds > 0)
            {
                // Make sure we're seeking to a valid position
                if (position < 0)
                    position = 0;
                if (position > Duration.TotalSeconds)
                    position = Duration.TotalSeconds;

                var seekPosition = TimeSpan.FromSeconds(position);
                _audioService.Seek(seekPosition);

                // Update our position properties to reflect the new position immediately
                CurrentPosition = seekPosition;

                // If we're stopped or paused, start playback
                if (PlaybackState == PlaybackState.Stopped || PlaybackState == PlaybackState.Paused)
                {
                    Play();
                }
            }
        }

        private void PlaySelectedTrack(int index) => _playlistManager.PlayTrack(index);

        private void CreatePlaylist()
        {
            var dialog = new PlaylistCreationDialog
            {
                Owner = Application.Current.MainWindow
            };

            if (dialog.ShowDialog() == true)
            {
                _playlistManager.CreatePlaylist(dialog.PlaylistName);

                // Select the newly created playlist
                SelectedPlaylist = _playlistManager.Playlists[_playlistManager.Playlists.Count - 1];
            }
        }

        private void DeletePlaylist()
        {
            if (SelectedPlaylist == null) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete the playlist '{SelectedPlaylist.Name}'?",
                "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _playlistManager.DeletePlaylist(SelectedPlaylist);
            }
        }

        private void RenamePlaylist()
        {
            if (SelectedPlaylist == null) return;

            var dialog = new PlaylistCreationDialog
            {
                Owner = Application.Current.MainWindow,
                Title = "Rename Playlist"
            };

            if (dialog.ShowDialog() == true)
            {
                _playlistManager.RenamePlaylist(SelectedPlaylist, dialog.PlaylistName);
            }
        }

        private void AddToPlaylist(Playlist targetPlaylist)
        {
            if (targetPlaylist == null || SelectedTracks == null || SelectedTracks.Count == 0) return;

            _playlistManager.AddSelectedTracksToPlaylist(targetPlaylist, new List<AudioFile>(SelectedTracks));

            MessageBox.Show(
                $"{SelectedTracks.Count} track(s) added to '{targetPlaylist.Name}'.",
                "Tracks Added", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ImportPlaylist()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Playlist Files|*.m3u;*.m3u8|All Files|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                _playlistManager.ImportPlaylist(openFileDialog.FileName);
            }
        }

        private void ExportPlaylist()
        {
            if (SelectedPlaylist == null) return;

            var saveFileDialog = new SaveFileDialog
            {
                FileName = $"{SelectedPlaylist.Name}.m3u",
                Filter = "M3U Playlist|*.m3u|All Files|*.*"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                _playlistManager.ExportPlaylist(SelectedPlaylist, saveFileDialog.FileName);
            }
        }

        private void ToggleShuffle()
        {
            if (CurrentPlaylist != null)
            {
                _playlistManager.ToggleShuffle();
                OnPropertyChanged(nameof(IsShuffleEnabled));
            }
        }

        private void CycleRepeatMode()
        {
            if (CurrentPlaylist != null)
            {
                _playlistManager.CycleRepeatMode();
                OnPropertyChanged(nameof(RepeatMode));
            }
        }

        private void UpdateCurrentTrackText()
        {
            if (CurrentTrack != null)
            {
                // Format display text depending on available metadata
                string artist = string.IsNullOrEmpty(CurrentTrack.Artist) || CurrentTrack.Artist == "Unknown Artist"
                    ? ""
                    : CurrentTrack.Artist;

                string album = string.IsNullOrEmpty(CurrentTrack.Album) || CurrentTrack.Album == "Unknown Album"
                    ? ""
                    : $" - {CurrentTrack.Album}";

                if (!string.IsNullOrEmpty(artist))
                {
                    CurrentTrackText = $"{CurrentTrack.Title} - {artist}{album}";
                }
                else
                {
                    CurrentTrackText = CurrentTrack.Title;
                }
            }
            else
            {
                CurrentTrackText = "No track selected";
            }
        }

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Simple implementation of ICommand
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object>? _canExecute;

        public RelayCommand(Action<object> execute, Predicate<object>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter ?? new object()) ?? true;
        public void Execute(object? parameter) => _execute(parameter ?? new object());
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}