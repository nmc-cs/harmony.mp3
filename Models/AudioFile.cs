using System;
using System.IO;
using System.Windows.Media.Imaging;
using System.Linq;  // Add this for LINQ methods

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

                    // Extract lyrics if available - this will work with MP3 files that have embedded lyrics
                    Lyrics = file.Tag.Lyrics ?? string.Empty;

                    // Some MP3 files might have lyrics in USLT frames (ID3v2)
                    if (string.IsNullOrEmpty(Lyrics) && file.Tag.Lyrics != null)
                    {
                        // Try to get lyrics from different possible sources
                        if (file.GetTag(TagLib.TagTypes.Id3v2) is TagLib.Id3v2.Tag id3v2)
                        {
                            // Look for USLT (Unsynchronized Lyrics) frames
                            var usltFrames = id3v2.GetFrames("USLT").ToList();
                            if (usltFrames != null && usltFrames.Count > 0)
                            {
                                if (usltFrames.First() is TagLib.Id3v2.UnsynchronisedLyricsFrame lyricsFrame)
                                {
                                    Lyrics = lyricsFrame.Text ?? string.Empty;
                                }
                            }

                            // Also check for SYLT (Synchronized Lyrics) frames as fallback
                            if (string.IsNullOrEmpty(Lyrics))
                            {
                                var syltFrames = id3v2.GetFrames("SYLT").ToList();
                                if (syltFrames != null && syltFrames.Count > 0)
                                {
                                    if (syltFrames.First() is TagLib.Id3v2.SynchronisedLyricsFrame syncLyricsFrame)
                                    {
                                        // Extract text from synchronized lyrics (ignoring timestamps)
                                        var lyricsText = "";
                                        foreach (var item in syncLyricsFrame.Text)
                                        {
                                            lyricsText += item.Text + "\n";
                                        }
                                        Lyrics = lyricsText.Trim();
                                    }
                                }
                            }
                        }
                    }

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
            catch (Exception ex)
            {
                // Log the error for debugging
                System.Diagnostics.Debug.WriteLine($"Error loading metadata for {FilePath}: {ex.Message}");

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