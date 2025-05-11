using System;
using System.IO;
using System.Threading.Tasks;
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

        // Waveform data for visualization
        public float[] WaveformData { get; set; } = Array.Empty<float>();
        public bool IsWaveformAnalyzed { get; set; } = false;

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

        /// <summary>
        /// Asynchronously analyzes the audio file to generate waveform data
        /// </summary>
        public async Task AnalyzeWaveformAsync(Services.WaveformAnalyzer analyzer, int resolution = 2000)
        {
            if (!IsWaveformAnalyzed && File.Exists(FilePath))
            {
                try
                {
                    WaveformData = await analyzer.AnalyzeWaveformAsync(FilePath, resolution);
                    IsWaveformAnalyzed = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error analyzing waveform for {FilePath}: {ex.Message}");
                    // Create a fallback waveform
                    WaveformData = CreateFallbackWaveform(resolution);
                    IsWaveformAnalyzed = true;
                }
            }
        }

        private float[] CreateFallbackWaveform(int resolution)
        {
            var fallback = new float[resolution];

            // Generate a somewhat interesting pattern based on the file hash
            int seed = FilePath.GetHashCode();
            var random = new Random(seed);

            for (int i = 0; i < resolution; i++)
            {
                double position = (double)i / resolution;

                // Create a semi-random waveform that looks plausible
                double noiseComponent = random.NextDouble() * 0.3;
                double sinComponent = 0.7 * Math.Sin(position * Math.PI * 8) * Math.Sin(position * Math.PI * 2.5);

                fallback[i] = (float)Math.Min(0.95, Math.Max(0.05, Math.Abs(noiseComponent + sinComponent)));
            }

            return fallback;
        }

        public override string ToString()
        {
            return $"{Title} - {Artist}";
        }
    }
}