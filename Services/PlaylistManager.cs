using System;
using System.Collections.ObjectModel;
using System.Linq;
using Harmony.Models;
using System.Collections.Generic;
using Microsoft.Win32;
using System.Windows;
using System.IO;

namespace Harmony.Services
{
    public class PlaylistManager
    {
        private readonly AudioPlaybackService _audioService;
        public ObservableCollection<Playlist> Playlists { get; private set; }
        private Playlist _currentPlaylist;

        public Playlist CurrentPlaylist
        {
            get => _currentPlaylist;
            set
            {
                if (_currentPlaylist != value)
                {
                    _currentPlaylist = value;
                    CurrentPlaylistChanged?.Invoke(this, value);
                }
            }
        }

        public event EventHandler<AudioFile> CurrentTrackChanged = delegate { };
        public event EventHandler<Playlist> CurrentPlaylistChanged = delegate { };
        public event EventHandler PlaylistsChanged = delegate { };

        public PlaylistManager(AudioPlaybackService audioService)
        {
            _audioService = audioService;
            Playlists = new ObservableCollection<Playlist>();

            // Create a default playlist
            CurrentPlaylist = new Playlist("Now Playing");
            Playlists.Add(CurrentPlaylist);

            // Listen for end of track to automatically advance
            _audioService.MediaEnded += (s, e) => PlayNext();
        }

        public void LoadFiles()
        {
            var openFileDialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Audio Files|*.mp3;*.wav;*.flac;*.m4a|All Files|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                foreach (var filePath in openFileDialog.FileNames)
                {
                    AddFile(filePath);
                }

                // If we have files and nothing is playing, start playing the first file
                if (CurrentPlaylist.Files.Count > 0 && !_audioService.IsPlaying)
                {
                    PlayTrack(0);
                }
            }
        }

        public void AddFile(string filePath)
        {
            var audioFile = new AudioFile(filePath);
            CurrentPlaylist.Files.Add(audioFile);
        }

        public void CreatePlaylist(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                name = $"Playlist {Playlists.Count + 1}";

            var newPlaylist = new Playlist(name);
            Playlists.Add(newPlaylist);
            PlaylistsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void DeletePlaylist(Playlist playlist)
        {
            if (Playlists.Count <= 1)
            {
                MessageBox.Show("Cannot delete the last playlist.", "Operation Failed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (playlist == CurrentPlaylist)
            {
                // If we're deleting the current playlist, switch to another one first
                CurrentPlaylist = Playlists.First(p => p != playlist);
            }

            Playlists.Remove(playlist);
            PlaylistsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void RenamePlaylist(Playlist playlist, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
                return;

            playlist.Name = newName;
            PlaylistsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void AddFilesToPlaylist(Playlist targetPlaylist, IEnumerable<string> filePaths)
        {
            foreach (var filePath in filePaths)
            {
                try
                {
                    var audioFile = new AudioFile(filePath);
                    targetPlaylist.Files.Add(audioFile);
                }
                catch (Exception ex)
                {
                    // Log or handle error
                    Console.WriteLine($"Error adding file: {ex.Message}");
                }
            }
        }

        public void AddSelectedTracksToPlaylist(Playlist targetPlaylist, IEnumerable<AudioFile> tracks)
        {
            foreach (var track in tracks)
            {
                targetPlaylist.Files.Add(track);
            }
        }

        public void PlayTrack(int index)
        {
            if (index >= 0 && index < CurrentPlaylist.Files.Count)
            {
                CurrentPlaylist.CurrentIndex = index;
                var track = CurrentPlaylist.Files[index];
                _audioService.Play(track);
                CurrentTrackChanged?.Invoke(this, track);
            }
        }

        public void PlayNext()
        {
            var nextTrack = CurrentPlaylist.GetNextTrack();
            if (nextTrack != null)
            {
                _audioService.Play(nextTrack);
                CurrentTrackChanged?.Invoke(this, nextTrack);
            }
        }

        public void PlayPrevious()
        {
            var prevTrack = CurrentPlaylist.GetPreviousTrack();
            if (prevTrack != null)
            {
                _audioService.Play(prevTrack);
                CurrentTrackChanged?.Invoke(this, prevTrack);
            }
        }

        public void ToggleShuffle()
        {
            CurrentPlaylist.ToggleShuffle();
        }

        public void CycleRepeatMode()
        {
            CurrentPlaylist.CycleRepeatMode();
        }

        public void ImportPlaylist(string filePath)
        {
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                var newPlaylist = new Playlist(fileName);

                // Simple M3U parser
                var lines = File.ReadAllLines(filePath);
                foreach (var line in lines)
                {
                    if (!line.StartsWith("#") && !string.IsNullOrWhiteSpace(line))
                    {
                        string fullPath = line;
                        if (!Path.IsPathRooted(line))
                        {
                            // If the path is relative, make it absolute relative to the playlist file
                            var playlistDirectory = Path.GetDirectoryName(filePath);
                            fullPath = Path.Combine(playlistDirectory, line);
                        }

                        if (File.Exists(fullPath))
                        {
                            var audioFile = new AudioFile(fullPath);
                            newPlaylist.Files.Add(audioFile);
                        }
                    }
                }

                if (newPlaylist.Files.Count > 0)
                {
                    Playlists.Add(newPlaylist);
                    PlaylistsChanged?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    MessageBox.Show("No valid audio files found in the playlist.", "Import Failed",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to import playlist: {ex.Message}", "Import Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ExportPlaylist(Playlist playlist, string filePath)
        {
            try
            {
                using (var writer = new StreamWriter(filePath))
                {
                    writer.WriteLine("#EXTM3U");
                    foreach (var file in playlist.Files)
                    {
                        writer.WriteLine($"#EXTINF:-1,{file.Artist} - {file.Title}");
                        writer.WriteLine(file.FilePath);
                    }
                }

                MessageBox.Show($"Playlist '{playlist.Name}' was exported successfully.",
                    "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to export playlist: {ex.Message}", "Export Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}