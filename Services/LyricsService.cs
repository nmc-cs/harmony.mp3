using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Harmony.Models;

namespace Harmony.Services
{
    /// <summary>
    /// Service responsible for obtaining lyrics for audio files
    /// </summary>
    public class LyricsService
    {
        // Dictionary to cache lyrics so we don't need to reload them
        private Dictionary<string, string> _lyricsCache = new Dictionary<string, string>();

        /// <summary>
        /// Gets lyrics for the specified audio file
        /// </summary>
        public string GetLyrics(AudioFile audioFile)
        {
            if (audioFile == null)
                return string.Empty;

            // Check if we already have lyrics in the cache
            if (_lyricsCache.TryGetValue(audioFile.FilePath, out string cachedLyrics))
                return cachedLyrics;

            // First try to get lyrics from the audio file metadata
            string lyrics = GetLyricsFromMetadata(audioFile);

            // If metadata has no lyrics, try to find a lyrics text file
            if (string.IsNullOrEmpty(lyrics))
            {
                lyrics = GetLyricsFromFile(audioFile);
            }

            // If still no lyrics, try to generate simulated lyrics for demo purposes
            if (string.IsNullOrEmpty(lyrics))
            {
                lyrics = GenerateSimulatedLyrics(audioFile);
            }

            // Cache the result
            _lyricsCache[audioFile.FilePath] = lyrics;

            return lyrics;
        }

        /// <summary>
        /// Gets lyrics from the audio file metadata
        /// </summary>
        private string GetLyricsFromMetadata(AudioFile audioFile)
        {
            try
            {
                using (var file = TagLib.File.Create(audioFile.FilePath))
                {
                    return file.Tag.Lyrics ?? string.Empty;
                }
            }
            catch (Exception)
            {
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
                // Try to find a .lrc or .txt file with the same name in the same directory
                string directory = Path.GetDirectoryName(audioFile.FilePath) ?? string.Empty;
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(audioFile.FilePath);

                string[] possibleLyricFiles = {
                    Path.Combine(directory, $"{fileNameWithoutExt}.lrc"),
                    Path.Combine(directory, $"{fileNameWithoutExt}.txt"),
                    Path.Combine(directory, $"{fileNameWithoutExt} lyrics.txt"),
                    Path.Combine(directory, $"{audioFile.Artist} - {audioFile.Title}.lrc"),
                    Path.Combine(directory, $"{audioFile.Artist} - {audioFile.Title}.txt")
                };

                foreach (string filePath in possibleLyricFiles)
                {
                    if (File.Exists(filePath))
                    {
                        return File.ReadAllText(filePath);
                    }
                }

                return string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Generates simulated lyrics for demo purposes
        /// </summary>
        private string GenerateSimulatedLyrics(AudioFile audioFile)
        {
            // Since this is for demo purposes and will show only if no real lyrics exist
            if (string.IsNullOrEmpty(audioFile.Artist) || audioFile.Artist == "Unknown Artist")
                return string.Empty;

            // Create a deterministic but random-seeming set of lyrics based on the song information
            return $"[Simulated lyrics for demonstration]\n\n" +
                   $"♪ {audioFile.Title} ♪\n" +
                   $"By {audioFile.Artist}\n\n" +
                   $"Verse 1:\n" +
                   $"The melody flows through the air\n" +
                   $"Like a dream we all can share\n" +
                   $"When the music starts to play\n" +
                   $"All my troubles fade away\n\n" +
                   $"Chorus:\n" +
                   $"This is {audioFile.Title}\n" +
                   $"The rhythm takes control\n" +
                   $"Let the music move your soul\n" +
                   $"We'll be dancing 'til we're old\n\n" +
                   $"Verse 2:\n" +
                   $"The beats echo in my mind\n" +
                   $"Leaving everything behind\n" +
                   $"The sound of {audioFile.Artist}'s voice\n" +
                   $"Makes my heart and soul rejoice\n\n" +
                   $"[Repeat Chorus]\n\n" +
                   $"Bridge:\n" +
                   $"Time stands still when this song plays\n" +
                   $"Memories of better days\n" +
                   $"Hold on to this feeling\n" +
                   $"While the world keeps on spinning\n\n" +
                   $"[Final Chorus]\n" +
                   $"This is {audioFile.Title}\n" +
                   $"The rhythm takes control\n" +
                   $"Let the music move your soul\n" +
                   $"We'll be dancing 'til we're old\n\n";
        }
    }
}