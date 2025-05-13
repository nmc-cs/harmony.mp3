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
    /// Enhanced lyrics service with corrected LRCLIB API implementation
    /// </summary>
    public class LyricsService : IDisposable
    {
        private Dictionary<string, string> _lyricsCache = new Dictionary<string, string>();
        private readonly HttpClient _httpClient;

        public LyricsService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Harmony Music Player/1.0 (http://github.com/your-repo)");
            _httpClient.Timeout = TimeSpan.FromSeconds(15);
        }

        /// <summary>
        /// Gets lyrics for the specified audio file using all available sources
        /// </summary>
        public async Task<string> GetLyricsAsync(AudioFile audioFile)
        {
            if (audioFile == null) return string.Empty;

            // Create cache key
            string cacheKey = $"{audioFile.Artist}|{audioFile.Title}|{audioFile.Album}|{audioFile.Duration.TotalSeconds}";

            // Check cache first
            if (_lyricsCache.TryGetValue(cacheKey, out string cachedLyrics))
            {
                System.Diagnostics.Debug.WriteLine($"Found cached lyrics for {audioFile.Title}");
                return cachedLyrics;
            }

            string lyrics = string.Empty;

            try
            {
                // 1. Try to get lyrics from the audio file metadata
                lyrics = await Task.Run(() => GetLyricsFromMetadata(audioFile));
                if (!string.IsNullOrEmpty(lyrics))
                {
                    System.Diagnostics.Debug.WriteLine($"Found lyrics in metadata for {audioFile.Title}");
                    _lyricsCache[cacheKey] = lyrics;
                    return lyrics;
                }

                // 2. Try to find external lyrics files
                lyrics = await Task.Run(() => GetLyricsFromFile(audioFile));
                if (!string.IsNullOrEmpty(lyrics))
                {
                    System.Diagnostics.Debug.WriteLine($"Found lyrics in file for {audioFile.Title}");
                    _lyricsCache[cacheKey] = lyrics;
                    return lyrics;
                }

                // 3. Try LRCLIB API with the exact signature match
                lyrics = await GetLyricsFromLRCLIB(audioFile);
                if (!string.IsNullOrEmpty(lyrics))
                {
                    System.Diagnostics.Debug.WriteLine($"Found lyrics from LRCLIB for {audioFile.Title}");
                    _lyricsCache[cacheKey] = lyrics;
                    // Optionally save to file for future use
                    await SaveLyricsToFileAsync(audioFile, lyrics);
                    return lyrics;
                }

                // 4. Try LRCLIB search API as fallback
                lyrics = await SearchLyricsOnLRCLIB(audioFile);
                if (!string.IsNullOrEmpty(lyrics))
                {
                    System.Diagnostics.Debug.WriteLine($"Found lyrics from LRCLIB search for {audioFile.Title}");
                    _lyricsCache[cacheKey] = lyrics;
                    await SaveLyricsToFileAsync(audioFile, lyrics);
                    return lyrics;
                }

                // Cache empty result to avoid repeated lookups
                _lyricsCache[cacheKey] = string.Empty;
                System.Diagnostics.Debug.WriteLine($"No lyrics found for {audioFile.Artist} - {audioFile.Title}");
                return string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting lyrics for {audioFile.Title}: {ex.Message}");
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
        /// Gets lyrics from LRCLIB using the signature match (/api/get)
        /// </summary>
        private async Task<string> GetLyricsFromLRCLIB(AudioFile audioFile)
        {
            // According to API docs, ALL parameters are required for /api/get
            if (audioFile.Duration.TotalSeconds <= 0)
            {
                System.Diagnostics.Debug.WriteLine("Skipping LRCLIB signature match - no duration available");
                return string.Empty;
            }

            // Try multiple cleaned variations of the track info
            var searchVariations = new List<(string artist, string title, string album)>
            {
                // Original metadata
                (audioFile.Artist, audioFile.Title, audioFile.Album),
                
                // Cleaned versions
                (CleanForAPI(audioFile.Artist), CleanForAPI(audioFile.Title), CleanForAPI(audioFile.Album)),
                
                // Without album
                (CleanForAPI(audioFile.Artist), CleanForAPI(audioFile.Title), ""),
                
                // Extract from filename if metadata is poor
                (ExtractArtistFromTitle(audioFile.Title), ExtractTitleFromTitle(audioFile.Title), "")
            };

            foreach (var (artist, title, album) in searchVariations)
            {
                // Skip invalid combinations
                if (string.IsNullOrEmpty(artist) || string.IsNullOrEmpty(title) ||
                    artist == "Unknown Artist" || artist.Contains("(Official"))
                    continue;

                try
                {
                    var queryParams = new Dictionary<string, string>
                    {
                        { "artist_name", artist },
                        { "track_name", title },
                        { "album_name", album },
                        { "duration", Math.Round(audioFile.Duration.TotalSeconds).ToString() }
                    };

                    string url = BuildLrclibUrl("/api/get", queryParams);

                    System.Diagnostics.Debug.WriteLine($"Trying LRCLIB: {artist} - {title} - {album} ({audioFile.Duration.TotalSeconds}s)");
                    System.Diagnostics.Debug.WriteLine($"URL: {url}");

                    using var response = await _httpClient.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResponse = await response.Content.ReadAsStringAsync();
                        System.Diagnostics.Debug.WriteLine($"LRCLIB Response: {jsonResponse}");

                        var lyrics = ParseLrclibResponse(jsonResponse);
                        if (!string.IsNullOrEmpty(lyrics))
                        {
                            System.Diagnostics.Debug.WriteLine($"Found lyrics from LRCLIB for {title}");
                            return lyrics;
                        }
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        System.Diagnostics.Debug.WriteLine($"Track not found in LRCLIB: {artist} - {title}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"LRCLIB returned {response.StatusCode}");
                        string errorContent = await response.Content.ReadAsStringAsync();
                        System.Diagnostics.Debug.WriteLine($"Error content: {errorContent}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error querying LRCLIB: {ex.Message}");
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Search for lyrics using LRCLIB search API (/api/search)
        /// </summary>
        private async Task<string> SearchLyricsOnLRCLIB(AudioFile audioFile)
        {
            try
            {
                // Try different search strategies
                var searchQueries = new List<string>
                {
                    $"{CleanForAPI(audioFile.Artist)} {CleanForAPI(audioFile.Title)}",
                    CleanForAPI(audioFile.Title),
                    ExtractMainParts(audioFile.Title)
                };

                foreach (var query in searchQueries)
                {
                    if (string.IsNullOrEmpty(query) || query.Length < 3) continue;

                    var queryParams = new Dictionary<string, string>
                    {
                        { "q", query }
                    };

                    string url = BuildLrclibUrl("/api/search", queryParams);
                    System.Diagnostics.Debug.WriteLine($"Searching LRCLIB: {query}");
                    System.Diagnostics.Debug.WriteLine($"URL: {url}");

                    using var response = await _httpClient.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResponse = await response.Content.ReadAsStringAsync();
                        var lyrics = ParseSearchResponse(jsonResponse, audioFile);

                        if (!string.IsNullOrEmpty(lyrics))
                        {
                            System.Diagnostics.Debug.WriteLine($"Found lyrics from LRCLIB search");
                            return lyrics;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error searching LRCLIB: {ex.Message}");
            }

            return string.Empty;
        }

        /// <summary>
        /// Parse search results and find the best match
        /// </summary>
        private string ParseSearchResponse(string jsonResponse, AudioFile audioFile)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonResponse);
                var results = doc.RootElement;

                if (results.ValueKind != JsonValueKind.Array) return string.Empty;

                foreach (var result in results.EnumerateArray())
                {
                    // Check if it's a reasonable match
                    if (IsGoodMatch(result, audioFile))
                    {
                        // Try synced lyrics first
                        if (result.TryGetProperty("syncedLyrics", out var syncedLyrics) &&
                            syncedLyrics.ValueKind != JsonValueKind.Null &&
                            !string.IsNullOrEmpty(syncedLyrics.GetString()))
                        {
                            return CleanLyrics(syncedLyrics.GetString());
                        }

                        // Fallback to plain lyrics
                        if (result.TryGetProperty("plainLyrics", out var plainLyrics) &&
                            plainLyrics.ValueKind != JsonValueKind.Null &&
                            !string.IsNullOrEmpty(plainLyrics.GetString()))
                        {
                            return CleanLyrics(plainLyrics.GetString());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing search response: {ex.Message}");
            }

            return string.Empty;
        }

        /// <summary>
        /// Check if a search result is a good match for our track
        /// </summary>
        private bool IsGoodMatch(JsonElement result, AudioFile audioFile)
        {
            try
            {
                var trackName = result.GetProperty("trackName").GetString() ?? "";
                var artistName = result.GetProperty("artistName").GetString() ?? "";
                var duration = result.TryGetProperty("duration", out var dur) ? dur.GetDouble() : 0;

                // Simple fuzzy matching
                var titleSimilarity = CalculateSimilarity(CleanForAPI(audioFile.Title), CleanForAPI(trackName));
                var artistSimilarity = CalculateSimilarity(CleanForAPI(audioFile.Artist), CleanForAPI(artistName));
                var durationDiff = Math.Abs(audioFile.Duration.TotalSeconds - duration);

                System.Diagnostics.Debug.WriteLine($"Match check: {trackName} by {artistName} - Title: {titleSimilarity:F2}, Artist: {artistSimilarity:F2}, Duration diff: {durationDiff}s");

                // Accept if title similarity is good and either artist matches or duration is close
                return titleSimilarity > 0.7 && (artistSimilarity > 0.5 || durationDiff <= 5);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Calculate similarity between two strings (0-1)
        /// </summary>
        private double CalculateSimilarity(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0;

            s1 = s1.ToLowerInvariant();
            s2 = s2.ToLowerInvariant();

            if (s1 == s2) return 1.0;

            int maxLength = Math.Max(s1.Length, s2.Length);
            if (maxLength == 0) return 1.0;

            int distance = LevenshteinDistance(s1, s2);
            return 1.0 - (double)distance / maxLength;
        }

        /// <summary>
        /// Calculate Levenshtein distance between two strings
        /// </summary>
        private int LevenshteinDistance(string s1, string s2)
        {
            int[,] matrix = new int[s1.Length + 1, s2.Length + 1];

            for (int i = 0; i <= s1.Length; i++)
                matrix[i, 0] = i;
            for (int j = 0; j <= s2.Length; j++)
                matrix[0, j] = j;

            for (int i = 1; i <= s1.Length; i++)
            {
                for (int j = 1; j <= s2.Length; j++)
                {
                    int cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                    matrix[i, j] = Math.Min(Math.Min(
                        matrix[i - 1, j] + 1,      // deletion
                        matrix[i, j - 1] + 1),     // insertion
                        matrix[i - 1, j - 1] + cost); // substitution
                }
            }

            return matrix[s1.Length, s2.Length];
        }

        /// <summary>
        /// Extract artist from title if format is "Artist - Title"
        /// </summary>
        private string ExtractArtistFromTitle(string title)
        {
            var match = Regex.Match(title, @"^([^-]+)\s*-\s*(.+)$");
            if (match.Success)
            {
                return CleanForAPI(match.Groups[1].Value);
            }
            return "";
        }

        /// <summary>
        /// Extract title from "Artist - Title" format
        /// </summary>
        private string ExtractTitleFromTitle(string title)
        {
            var match = Regex.Match(title, @"^([^-]+)\s*-\s*(.+)$");
            if (match.Success)
            {
                return CleanForAPI(match.Groups[2].Value);
            }
            return CleanForAPI(title);
        }

        /// <summary>
        /// Extract main parts from title, removing extra info
        /// </summary>
        private string ExtractMainParts(string title)
        {
            // Remove common suffixes
            string cleaned = Regex.Replace(title, @"\s*\((Official\s+)?(Video|Audio|Music\s+Video|Live|Acoustic|Remix)\)", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\s*\[(Official\s+)?(Video|Audio|Music\s+Video|Live|Acoustic|Remix)\]", "", RegexOptions.IgnoreCase);

            // Extract main title if in "Artist - Title" format
            var match = Regex.Match(cleaned, @"^([^-]+)\s*-\s*(.+)$");
            if (match.Success)
            {
                return $"{match.Groups[1].Value.Trim()} {match.Groups[2].Value.Trim()}";
            }

            return cleaned.Trim();
        }

        /// <summary>
        /// Builds LRCLIB URL with proper parameter encoding
        /// </summary>
        private string BuildLrclibUrl(string endpoint, Dictionary<string, string> parameters)
        {
            var query = new List<string>();
            foreach (var param in parameters)
            {
                if (!string.IsNullOrEmpty(param.Value))
                {
                    query.Add($"{param.Key}={Uri.EscapeDataString(param.Value)}");
                }
            }
            return $"https://lrclib.net{endpoint}?{string.Join("&", query)}";
        }

        /// <summary>
        /// Clean search terms for API calls
        /// </summary>
        private string CleanForAPI(string term)
        {
            if (string.IsNullOrEmpty(term) || term == "Unknown Artist" || term == "Unknown Album")
                return "";

            // Remove featuring info
            term = Regex.Replace(term, @"\s+(feat\.?|ft\.?|featuring)\s+.*$", "", RegexOptions.IgnoreCase);

            // Remove parentheses and brackets with content
            term = Regex.Replace(term, @"\s*\([^)]*\)", "");
            term = Regex.Replace(term, @"\s*\[[^\]]*\]", "");

            // Remove "Official" and similar markers
            term = Regex.Replace(term, @"\s*(Official|Video|Audio|Music Video)\s*", " ", RegexOptions.IgnoreCase);

            // Replace special characters with spaces
            term = Regex.Replace(term, @"[&]", " and ");

            // Remove multiple spaces
            term = Regex.Replace(term, @"\s+", " ");

            return term.Trim();
        }

        /// <summary>
        /// Parses LRCLIB JSON response to extract lyrics
        /// </summary>
        private string ParseLrclibResponse(string jsonResponse)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonResponse);
                var root = doc.RootElement;

                // Try synced lyrics first
                if (root.TryGetProperty("syncedLyrics", out var syncedLyrics) &&
                    syncedLyrics.ValueKind != JsonValueKind.Null &&
                    !string.IsNullOrEmpty(syncedLyrics.GetString()))
                {
                    System.Diagnostics.Debug.WriteLine("Found synced lyrics from LRCLIB");
                    return CleanLyrics(syncedLyrics.GetString());
                }

                // Fallback to plain lyrics
                if (root.TryGetProperty("plainLyrics", out var plainLyrics) &&
                    plainLyrics.ValueKind != JsonValueKind.Null &&
                    !string.IsNullOrEmpty(plainLyrics.GetString()))
                {
                    System.Diagnostics.Debug.WriteLine("Found plain lyrics from LRCLIB");
                    return CleanLyrics(plainLyrics.GetString());
                }

                System.Diagnostics.Debug.WriteLine("No lyrics found in response");
                return string.Empty;
            }
            catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine($"JSON parsing error: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Gets lyrics from file metadata
        /// </summary>
        private string GetLyricsFromMetadata(AudioFile audioFile)
        {
            try
            {
                // Force reload metadata from file
                var fileCopy = new AudioFile(audioFile.FilePath);

                if (!string.IsNullOrEmpty(fileCopy.Lyrics))
                {
                    System.Diagnostics.Debug.WriteLine("Found lyrics in metadata");
                    return CleanLyrics(fileCopy.Lyrics);
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

                if (!Directory.Exists(lyricsDirectory))
                {
                    Directory.CreateDirectory(lyricsDirectory);
                }

                string fileName = $"{CleanFileName(audioFile.Title)}.txt";
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

            // Remove LRC timestamps
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