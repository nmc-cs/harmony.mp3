using System;
using System.IO;
using System.Windows.Media.Imaging;
using System.Linq;

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
                    // First, try to extract from metadata
                    Title = file.Tag.Title;
                    Artist = file.Tag.FirstPerformer;
                    Album = file.Tag.Album;
                    Duration = file.Properties.Duration;

                    // Extract lyrics from various sources
                    ExtractLyrics(file);

                    // Load album art if available
                    LoadAlbumArt(file);

                    // If metadata is missing or poor, try to extract from filename
                    if (IsMetadataPoor())
                    {
                        ExtractFromFilename();
                    }

                    // Ensure we have reasonable defaults
                    if (string.IsNullOrEmpty(Title))
                        Title = Path.GetFileNameWithoutExtension(FilePath);
                    if (string.IsNullOrEmpty(Artist))
                        Artist = "Unknown Artist";
                    if (string.IsNullOrEmpty(Album))
                        Album = "Unknown Album";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading metadata for {FilePath}: {ex.Message}");

                // Fallback to filename parsing
                ExtractFromFilename();

                // Set defaults
                if (string.IsNullOrEmpty(Title))
                    Title = Path.GetFileNameWithoutExtension(FilePath);
                if (string.IsNullOrEmpty(Artist))
                    Artist = "Unknown Artist";
                if (string.IsNullOrEmpty(Album))
                    Album = "Unknown Album";
                Lyrics = string.Empty;
            }
        }

        private void ExtractLyrics(TagLib.File file)
        {
            try
            {
                // Try standard lyrics tag first
                if (!string.IsNullOrEmpty(file.Tag.Lyrics))
                {
                    Lyrics = file.Tag.Lyrics;
                    return;
                }

                // Check ID3v2 frames for lyrics
                if (file.GetTag(TagLib.TagTypes.Id3v2) is TagLib.Id3v2.Tag id3v2)
                {
                    // Look for USLT (Unsynchronized Lyrics) frames
                    var usltFrames = id3v2.GetFrames("USLT").ToList();
                    if (usltFrames.Count > 0)
                    {
                        if (usltFrames[0] is TagLib.Id3v2.UnsynchronisedLyricsFrame lyricsFrame)
                        {
                            Lyrics = lyricsFrame.Text ?? string.Empty;
                            return;
                        }
                    }

                    // Look for SYLT (Synchronized Lyrics) frames as fallback
                    var syltFrames = id3v2.GetFrames("SYLT").ToList();
                    if (syltFrames.Count > 0)
                    {
                        if (syltFrames[0] is TagLib.Id3v2.SynchronisedLyricsFrame syncLyricsFrame)
                        {
                            var lyricsText = "";
                            foreach (var item in syncLyricsFrame.Text)
                            {
                                lyricsText += item.Text + "\n";
                            }
                            Lyrics = lyricsText.Trim();
                            return;
                        }
                    }
                }

                // Try other tag types if available
                foreach (TagLib.TagTypes tagType in Enum.GetValues<TagLib.TagTypes>())
                {
                    try
                    {
                        var tag = file.GetTag(tagType);
                        if (tag != null && !string.IsNullOrEmpty(tag.Lyrics))
                        {
                            Lyrics = tag.Lyrics;
                            return;
                        }
                    }
                    catch
                    {
                        // Continue with next tag type
                    }
                }

                Lyrics = string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error extracting lyrics: {ex.Message}");
                Lyrics = string.Empty;
            }
        }

        private void LoadAlbumArt(TagLib.File file)
        {
            try
            {
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading album art: {ex.Message}");
                AlbumArt = null;
            }
        }

        private bool IsMetadataPoor()
        {
            // Check if metadata seems to be from YouTube download or similar
            return string.IsNullOrEmpty(Title) ||
                   string.IsNullOrEmpty(Artist) ||
                   Artist == "Unknown Artist" ||
                   Title.Contains("Official") ||
                   Title.Contains("(Official") ||
                   Title.Contains("[Official") ||
                   Artist.Contains("Various") ||
                   Artist.Contains("YouTube");
        }

        private void ExtractFromFilename()
        {
            try
            {
                string filename = Path.GetFileNameWithoutExtension(FilePath);

                // Common patterns in YouTube downloads and similar files
                var patterns = new[]
                {
                    // "Artist - Title (Official Video)"
                    @"^(.+?)\s*-\s*(.+?)\s*\((Official|Music|Audio|Video|Live|Acoustic|Remix).*?\)\s*$",
                    // "Artist - Title [Official Video]"
                    @"^(.+?)\s*-\s*(.+?)\s*\[(Official|Music|Audio|Video|Live|Acoustic|Remix).*?\]\s*$",
                    // "Artist - Title"
                    @"^(.+?)\s*-\s*(.+)$",
                    // "Title - Artist" (less common)
                    @"^(.+?)\s*-\s*(.+)$"
                };

                foreach (string pattern in patterns)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(filename, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        string firstPart = match.Groups[1].Value.Trim();
                        string secondPart = match.Groups[2].Value.Trim();

                        // Try to determine which is artist and which is title
                        // Usually artist comes first, but not always
                        if (string.IsNullOrEmpty(Artist) || Artist == "Unknown Artist")
                        {
                            Artist = firstPart;
                        }
                        if (string.IsNullOrEmpty(Title))
                        {
                            Title = secondPart;
                        }

                        System.Diagnostics.Debug.WriteLine($"Extracted from filename: Artist='{Artist}', Title='{Title}'");
                        return;
                    }
                }

                // If no pattern matches, use the whole filename as title
                if (string.IsNullOrEmpty(Title))
                {
                    Title = filename;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error extracting from filename: {ex.Message}");
            }
        }

        public override string ToString()
        {
            return $"{Title} - {Artist}";
        }
    }
}