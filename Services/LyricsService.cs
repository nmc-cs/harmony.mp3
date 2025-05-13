using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Harmony.Models;
using System.Net.Http;
using System.Text.Json;
using System.Text;

namespace Harmony.Services
{
    /// <summary>
    /// Enhanced lyrics service with LRCLIB integration
    /// </summary>
    public class LyricsService : IDisposable
    {
        private Dictionary<string, string> _lyricsCache = new Dictionary<string, string>();
        private readonly HttpClient _httpClient;

        public LyricsService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Harmony Music Player/1.0");
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        /// <summary>
        /// Gets lyrics for the specified audio file using all available sources
        /// </summary>
        public async Task<string> GetLyricsAsync(AudioFile audioFile)
        {
            if (audioFile == null) return string.Empty;

            // Create cache key
            string cacheKey = $"{audioFile.Artist}|{audioFile.Title}";

            // Check cache first
            if (_lyricsCache.TryGetValue(cacheKey, out string cachedLyrics))
                return cachedLyrics;

            string lyrics = string.Empty;

            try
            {
                // 1. Try to get lyrics from the audio file metadata
                lyrics = await Task.Run(() => GetLyricsFromMetadata(audioFile));
                if (!string.IsNullOrEmpty(lyrics))
                {
                    _lyricsCache[cacheKey] = lyrics;
                    return lyrics;
                }

                // 2. Try to find external lyrics files
                lyrics = await Task.Run(() => GetLyricsFromFile(audioFile));
                if (!string.IsNullOrEmpty(lyrics))
                {
                    _lyricsCache[cacheKey] = lyrics;
                    return lyrics;
                }

                // 3. Try LRCLIB (free online service)
                lyrics = await GetLyricsFromLRCLIB(audioFile);
                if (!string.IsNullOrEmpty(lyrics))
                {
                    _lyricsCache[cacheKey] = lyrics;
                    // Optionally save to file for future use
                    await SaveLyricsToFileAsync(audioFile, lyrics);
                    return lyrics;
                }

                // Cache empty result to avoid repeated lookups
                _lyricsCache[cacheKey] = string.Empty;
                return string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting lyrics: {ex.Message}");
                _lyricsCache[cacheKey] = string.Empty;
                return string.Empty;
            }
        }

        /// <summary>
        /// Synchronous version for backward compatibility
        /// </summary>
        public string GetLyrics(AudioFile audioFile)
        {
            return GetLyricsAsync(audioFile).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Gets lyrics from LRCLIB (free service)
        /// </summary>
        private async Task<string> GetLyricsFromLRCLIB(AudioFile audioFile)
        {
            try
            {
                string encodedArtist = Uri.EscapeDataString(CleanSearchTerm(audioFile.Artist));
                string encodedTitle = Uri.EscapeDataString(CleanSearchTerm(audioFile.Title));

                string url = $"https://lrclib.net/api/get?artist_name={encodedArtist}&track_name={encodedTitle}";

                // Add album if available for better matching
                if (!string.IsNullOrEmpty(audioFile.Album) && audioFile.Album != "Unknown Album")
                {
                    string encodedAlbum = Uri.EscapeDataString(CleanSearchTerm(audioFile.Album));
                    url += $"&album_name={encodedAlbum}";
                }

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(jsonResponse);

                    // Try synced lyrics first (LRC format)
                    if (doc.RootElement.TryGetProperty("syncedLyrics", out var syncedLyrics) &&
                        !string.IsNullOrEmpty(syncedLyrics.GetString()))
                    {
                        System.Diagnostics.Debug.WriteLine($"Found synced lyrics from LRCLIB for {audioFile.Title}");
                        return CleanLyrics(syncedLyrics.GetString());
                    }

                    // Fallback to plain lyrics
                    if (doc.RootElement.TryGetProperty("plainLyrics", out var plainLyrics) &&
                        !string.IsNullOrEmpty(plainLyrics.GetString()))
                    {
                        System.Diagnostics.Debug.WriteLine($"Found plain lyrics from LRCLIB for {audioFile.Title}");
                        return CleanLyrics(plainLyrics.GetString());
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LRCLIB API error: {ex.Message}");
            }

            return string.Empty;
        }

        /// <summary>
        /// Gets lyrics from file metadata using comprehensive approach
        /// </summary>
        private string GetLyricsFromMetadata(AudioFile audioFile)
        {
            try
            {
                using var file = TagLib.File.Create(audioFile.FilePath);

                // Try standard lyrics first
                if (!string.IsNullOrEmpty(file.Tag.Lyrics))
                {
                    System.Diagnostics.Debug.WriteLine("Found lyrics in standard tag");
                    return CleanLyrics(file.Tag.Lyrics);
                }

                // Check all available tag types
                foreach (TagLib.TagTypes tagType in Enum.GetValues<TagLib.TagTypes>())
                {
                    try
                    {
                        var tag = file.GetTag(tagType);
                        if (tag != null && !string.IsNullOrEmpty(tag.Lyrics))
                        {
                            System.Diagnostics.Debug.WriteLine($"Found lyrics in {tagType} tag");
                            return CleanLyrics(tag.Lyrics);
                        }
                    }
                    catch
                    {
                        // Continue with next tag type
                    }
                }

                // Check ID3v2 frames specifically
                if (file.GetTag(TagLib.TagTypes.Id3v2) is TagLib.Id3v2.Tag id3v2)
                {
                    // USLT frames
                    foreach (var frame in id3v2.GetFrames<TagLib.Id3v2.UnsynchronisedLyricsFrame>())
                    {
                        if (!string.IsNullOrEmpty(frame.Text))
                        {
                            System.Diagnostics.Debug.WriteLine("Found lyrics in USLT frame");
                            return CleanLyrics(frame.Text);
                        }
                    }

                    // SYLT frames
                    foreach (var frame in id3v2.GetFrames<TagLib.Id3v2.SynchronisedLyricsFrame>())
                    {
                        if (frame.Text != null && frame.Text.Length > 0)
                        {
                            var lyricsText = new StringBuilder();
                            foreach (var item in frame.Text)
                            {
                                lyricsText.AppendLine(item.Text);
                            }
                            string result = lyricsText.ToString().Trim();
                            if (!string.IsNullOrEmpty(result))
                            {
                                System.Diagnostics.Debug.WriteLine("Found lyrics in SYLT frame");
                                return CleanLyrics(result);
                            }
                        }
                    }
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading metadata: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Attempts to find a matching lyrics file
        /// </summary>
        private string GetLyricsFromFile(AudioFile audioFile)
        {
            try
            {
                string directory = Path.GetDirectoryName(audioFile.FilePath) ?? string.Empty;
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(audioFile.FilePath);

                var possibleFiles = new List<string>
                {
                    Path.Combine(directory, $"{fileNameWithoutExt}.lrc"),
                    Path.Combine(directory, $"{fileNameWithoutExt}.txt"),
                    Path.Combine(directory, $"{fileNameWithoutExt} lyrics.txt"),
                    Path.Combine(directory, $"{CleanFileName(audioFile.Artist)} - {CleanFileName(audioFile.Title)}.lrc"),
                    Path.Combine(directory, $"{CleanFileName(audioFile.Artist)} - {CleanFileName(audioFile.Title)}.txt"),
                    Path.Combine(directory, "Lyrics", $"{fileNameWithoutExt}.lrc"),
                    Path.Combine(directory, "Lyrics", $"{fileNameWithoutExt}.txt")
                };

                foreach (string filePath in possibleFiles)
                {
                    if (File.Exists(filePath))
                    {
                        string lyrics = File.ReadAllText(filePath, Encoding.UTF8);
                        if (!string.IsNullOrEmpty(lyrics))
                        {
                            System.Diagnostics.Debug.WriteLine($"Found lyrics file: {filePath}");
                            return CleanLyrics(lyrics);
                        }
                    }
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading lyrics file: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Saves lyrics to a file for future use
        /// </summary>
        private async Task SaveLyricsToFileAsync(AudioFile audioFile, string lyrics)
        {
            try
            {
                string directory = Path.GetDirectoryName(audioFile.FilePath) ?? string.Empty;
                string lyricsDirectory = Path.Combine(directory, "Lyrics");

                // Create lyrics directory if it doesn't exist
                if (!Directory.Exists(lyricsDirectory))
                {
                    Directory.CreateDirectory(lyricsDirectory);
                }

                string fileName = $"{CleanFileName(audioFile.Artist)} - {CleanFileName(audioFile.Title)}.txt";
                string filePath = Path.Combine(lyricsDirectory, fileName);

                await File.WriteAllTextAsync(filePath, lyrics, Encoding.UTF8);
                System.Diagnostics.Debug.WriteLine($"Saved lyrics to: {filePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving lyrics: {ex.Message}");
            }
        }

        /// <summary>
        /// Cleans search terms for better API matching
        /// </summary>
        private string CleanSearchTerm(string term)
        {
            if (string.IsNullOrEmpty(term)) return string.Empty;

            // Remove common prefixes/suffixes that might interfere with search
            term = term.Replace(" feat.", " ft.").Replace(" featuring ", " ft. ");

            // Remove content in parentheses and brackets (like "Remastered" or "Radio Edit")
            term = Regex.Replace(term, @"\s*\([^)]*\)", "");
            term = Regex.Replace(term, @"\s*\[[^\]]*\]", "");

            return term.Trim();
        }

        /// <summary>
        /// Cleans filename for saving
        /// </summary>
        private string CleanFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name.Trim();
        }

        /// <summary>
        /// Cleans and formats lyrics text
        /// </summary>
        private string CleanLyrics(string lyrics)
        {
            if (string.IsNullOrEmpty(lyrics)) return string.Empty;

            // Remove LRC timestamps (like [00:12.34])
            lyrics = Regex.Replace(lyrics, @"\[\d{2}:\d{2}(?:\.\d{2,3})?\]", "");

            // Remove LRC metadata tags
            lyrics = Regex.Replace(lyrics, @"\[ar:.*?\]", "", RegexOptions.IgnoreCase);
            lyrics = Regex.Replace(lyrics, @"\[ti:.*?\]", "", RegexOptions.IgnoreCase);
            lyrics = Regex.Replace(lyrics, @"\[al:.*?\]", "", RegexOptions.IgnoreCase);
            lyrics = Regex.Replace(lyrics, @"\[by:.*?\]", "", RegexOptions.IgnoreCase);
            lyrics = Regex.Replace(lyrics, @"\[offset:.*?\]", "", RegexOptions.IgnoreCase);

            // Normalize line endings
            lyrics = lyrics.Replace("\r\n", "\n").Replace("\r", "\n");

            // Remove excessive empty lines
            lyrics = Regex.Replace(lyrics, @"\n\s*\n\s*\n", "\n\n");

            return lyrics.Trim();
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}