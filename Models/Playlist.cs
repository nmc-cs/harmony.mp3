using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Harmony.Models
{
    public enum RepeatMode
    {
        None,       // No repeat
        One,        // Repeat current track
        All         // Repeat playlist
    }

    public class Playlist
    {
        public string Name { get; set; }
        public ObservableCollection<AudioFile> Files { get; set; }
        public int CurrentIndex { get; set; }
        public bool IsShuffleEnabled { get; set; }
        public RepeatMode RepeatMode { get; set; }

        private List<int>? _shuffleOrder;
        private int _shuffleIndex;
        private Random _random;

        public Playlist(string name)
        {
            Name = name;
            Files = new ObservableCollection<AudioFile>();
            CurrentIndex = -1;
            IsShuffleEnabled = false;
            RepeatMode = RepeatMode.None;
            _random = new Random();
            _shuffleOrder = null;
        }

        public AudioFile? CurrentTrack => (CurrentIndex >= 0 && CurrentIndex < Files.Count)
            ? Files[CurrentIndex]
            : null;

        public bool HasNext
        {
            get
            {
                if (RepeatMode != RepeatMode.None || IsShuffleEnabled)
                    return Files.Count > 0;

                return CurrentIndex < Files.Count - 1;
            }
        }

        public bool HasPrevious
        {
            get
            {
                if (RepeatMode != RepeatMode.None || IsShuffleEnabled)
                    return Files.Count > 0;

                return CurrentIndex > 0;
            }
        }

        public void GenerateShuffleOrder()
        {
            _shuffleOrder = Enumerable.Range(0, Files.Count).ToList();

            // Fisher-Yates shuffle
            for (int i = _shuffleOrder.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                int temp = _shuffleOrder[i];
                _shuffleOrder[i] = _shuffleOrder[j];
                _shuffleOrder[j] = temp;
            }

            // Find current track in shuffle order
            _shuffleIndex = _shuffleOrder.IndexOf(CurrentIndex);
            if (_shuffleIndex < 0) _shuffleIndex = 0;
        }

        public AudioFile? GetNextTrack()
        {
            if (Files.Count == 0) return null;

            if (RepeatMode == RepeatMode.One && CurrentTrack != null)
            {
                // For repeat one, just return the current track again
                return CurrentTrack;
            }

            if (IsShuffleEnabled)
            {
                if (_shuffleOrder == null || _shuffleOrder.Count != Files.Count)
                {
                    GenerateShuffleOrder();
                }

                if (_shuffleOrder == null) return null;

                _shuffleIndex++;

                // If we reached the end of shuffle order
                if (_shuffleIndex >= _shuffleOrder.Count)
                {
                    if (RepeatMode == RepeatMode.All)
                    {
                        // In repeat all mode, regenerate shuffle and start from beginning
                        GenerateShuffleOrder();
                        _shuffleIndex = 0;
                    }
                    else
                    {
                        // Otherwise, stop at the end
                        _shuffleIndex = _shuffleOrder.Count - 1;
                        return null;
                    }
                }

                CurrentIndex = _shuffleOrder[_shuffleIndex];
                return Files[CurrentIndex];
            }
            else
            {
                // Normal sequential playback
                CurrentIndex++;

                // If we reached the end of the playlist
                if (CurrentIndex >= Files.Count)
                {
                    if (RepeatMode == RepeatMode.All)
                    {
                        // In repeat all mode, go back to the first track
                        CurrentIndex = 0;
                    }
                    else
                    {
                        // Otherwise, stop at the end
                        CurrentIndex = Files.Count - 1;
                        return null;
                    }
                }

                return CurrentIndex >= 0 && CurrentIndex < Files.Count ? Files[CurrentIndex] : null;
            }
        }

        public AudioFile? GetPreviousTrack()
        {
            if (Files.Count == 0) return null;

            if (RepeatMode == RepeatMode.One && CurrentTrack != null)
            {
                // For repeat one, just return the current track again
                return CurrentTrack;
            }

            if (IsShuffleEnabled)
            {
                if (_shuffleOrder == null || _shuffleOrder.Count != Files.Count)
                {
                    GenerateShuffleOrder();
                }

                if (_shuffleOrder == null) return null;

                _shuffleIndex--;

                // If we reached the beginning of shuffle order
                if (_shuffleIndex < 0)
                {
                    if (RepeatMode == RepeatMode.All)
                    {
                        // In repeat all mode, go to the end
                        _shuffleIndex = _shuffleOrder.Count - 1;
                    }
                    else
                    {
                        // Otherwise, stop at the beginning
                        _shuffleIndex = 0;
                        return null;
                    }
                }

                CurrentIndex = _shuffleOrder[_shuffleIndex];
                return Files[CurrentIndex];
            }
            else
            {
                // Normal sequential playback
                CurrentIndex--;

                // If we reached the beginning of the playlist
                if (CurrentIndex < 0)
                {
                    if (RepeatMode == RepeatMode.All)
                    {
                        // In repeat all mode, go to the last track
                        CurrentIndex = Files.Count - 1;
                    }
                    else
                    {
                        // Otherwise, stop at the beginning
                        CurrentIndex = 0;
                        return null;
                    }
                }

                return CurrentIndex >= 0 && CurrentIndex < Files.Count ? Files[CurrentIndex] : null;
            }
        }

        public void ToggleShuffle()
        {
            IsShuffleEnabled = !IsShuffleEnabled;
            if (IsShuffleEnabled)
            {
                GenerateShuffleOrder();
            }
        }

        public void CycleRepeatMode()
        {
            switch (RepeatMode)
            {
                case RepeatMode.None:
                    RepeatMode = RepeatMode.One;
                    break;
                case RepeatMode.One:
                    RepeatMode = RepeatMode.All;
                    break;
                case RepeatMode.All:
                    RepeatMode = RepeatMode.None;
                    break;
            }
        }
    }
}