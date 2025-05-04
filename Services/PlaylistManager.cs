using System.Collections.ObjectModel;
using System.Linq;
using Harmony.Models;
using System.Collections.Generic;
using Microsoft.Win32;
using System;

namespace Harmony.Services
{
    public class PlaylistManager
    {
        private readonly AudioPlaybackService _audioService;
        public ObservableCollection<Playlist> Playlists { get; private set; }
        public Playlist CurrentPlaylist { get; private set; }

        public event EventHandler<AudioFile> CurrentTrackChanged = delegate { };

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
    }
}