using System.Collections.ObjectModel;

namespace Harmony.Models
{
    public class Playlist
    {
        public string Name { get; set; }
        public ObservableCollection<AudioFile> Files { get; set; }
        public int CurrentIndex { get; set; }

        public Playlist(string name)
        {
            Name = name;
            Files = new ObservableCollection<AudioFile>();
            CurrentIndex = -1;
        }

        public AudioFile CurrentTrack => (CurrentIndex >= 0 && CurrentIndex < Files.Count)
            ? Files[CurrentIndex]
            : null;

        public bool HasNext => CurrentIndex < Files.Count - 1;
        public bool HasPrevious => CurrentIndex > 0;

        public AudioFile GetNextTrack()
        {
            if (HasNext)
            {
                CurrentIndex++;
                return CurrentTrack;
            }
            return null;
        }

        public AudioFile GetPreviousTrack()
        {
            if (HasPrevious)
            {
                CurrentIndex--;
                return CurrentTrack;
            }
            return null;
        }
    }
}