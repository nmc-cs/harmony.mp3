using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace Harmony.Models
{
    public class AudioFile
    {
        public string FilePath { get; set; }
        public string FileName => Path.GetFileName(FilePath);
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public BitmapImage? AlbumArt { get; set; }
        public string Lyrics { get; set; } = string.Empty;

        public AudioFile(string filePath)
        {
            FilePath = filePath;
            LoadMetadata();
        }

        private void LoadMetadata()
        {
            try
            {
                using (var file = TagLib.File.Create(FilePath))
                {
                    // Load basic metadata
                    Title = string.IsNullOrEmpty(file.Tag.Title)
                        ? Path.GetFileNameWithoutExtension(FilePath)
                        : file.Tag.Title;

                    Artist = string.IsNullOrEmpty(file.Tag.FirstPerformer)
                        ? "Unknown Artist"
                        : file.Tag.FirstPerformer;

                    Album = string.IsNullOrEmpty(file.Tag.Album)
                        ? "Unknown Album"
                        : file.Tag.Album;

                    Duration = file.Properties.Duration;

                    // Extract lyrics if available
                    Lyrics = string.IsNullOrEmpty(file.Tag.Lyrics)
                        ? string.Empty
                        : file.Tag.Lyrics;

                    // Load album art if available
                    if (file.Tag.Pictures.Length > 0)
                    {
                        var picture = file.Tag.Pictures[0];
                        using (var stream = new MemoryStream(picture.Data.Data))
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.StreamSource = stream;
                            bitmap.EndInit();
                            bitmap.Freeze(); // Make it thread-safe
                            AlbumArt = bitmap;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Fallback to filename if metadata loading fails
                Title = Path.GetFileNameWithoutExtension(FilePath);
                Artist = "Unknown Artist";
                Album = "Unknown Album";
                Lyrics = string.Empty;
            }
        }

        public override string ToString()
        {
            return $"{Title} - {Artist}";
        }
    }
}