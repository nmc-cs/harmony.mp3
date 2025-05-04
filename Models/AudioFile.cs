using System;
using System.IO;

namespace Harmony.Models
{
    public class AudioFile
    {
        public string FilePath { get; set; }
        public string FileName => Path.GetFileName(FilePath);
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public TimeSpan Duration { get; set; }
        public byte[] AlbumArt { get; set; }

        public AudioFile(string filePath)
        {
            FilePath = filePath;
            // Basic initialization - we'll add metadata loading later
            Title = Path.GetFileNameWithoutExtension(filePath);
        }
    }
}