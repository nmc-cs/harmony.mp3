using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Dsp;
using NAudio.Wave;

namespace Harmony.Services
{
    public class AudioAnalyzerService : IDisposable
    {
        private readonly WasapiLoopbackCapture _capture;
        private readonly BufferedWaveProvider _bufferedProvider;
        private readonly int _fftLength;
        private readonly float[] _spectrumData;
        private readonly float[] _smoothedSpectrum;
        private readonly float[] _window;
        private readonly float _smoothingFactor;
        private readonly Thread _processingThread;
        private bool _running;
        private readonly object _lock = new object();

        public event EventHandler<float[]> SpectrumDataAvailable;

        /// <summary>
        /// Number of frequency bands delivered per FFT (half of the FFT length).
        /// </summary>
        public int SpectrumSize => _fftLength / 2;

        public AudioAnalyzerService(
            int fftLength = 2048,
            float smoothingFactor = 0.6f,
            int readChunkMilliseconds = 50)
        {
            if (!IsPowerOfTwo(fftLength))
                throw new ArgumentException("FFT length must be a power of two", nameof(fftLength));

            _fftLength = fftLength;
            _smoothingFactor = Math.Clamp(smoothingFactor, 0f, 1f);

            // Precompute Hann window
            _window = Enumerable.Range(0, _fftLength)
                .Select(i => 0.5f * (1 - (float)Math.Cos(2 * Math.PI * i / (_fftLength - 1))))
                .ToArray();

            _spectrumData = new float[SpectrumSize];
            _smoothedSpectrum = new float[SpectrumSize];
            for (int i = 0; i < SpectrumSize; i++)
                _smoothedSpectrum[i] = 0.05f;

            _capture = new WasapiLoopbackCapture();
            _bufferedProvider = new BufferedWaveProvider(_capture.WaveFormat)
            {
                BufferDuration = TimeSpan.FromMilliseconds(readChunkMilliseconds * 2),
                DiscardOnBufferOverflow = true
            };
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += (s, e) => StopProcessing();

            _processingThread = new Thread(ProcessingLoop)
            {
                IsBackground = true,
                Name = "AudioFFTProcessing"
            };
        }

        public void StartAnalyzing()
        {
            if (_running) return;
            _running = true;
            _capture.StartRecording();
            _processingThread.Start();
        }

        public void StopAnalyzing()
        {
            if (!_running) return;
            _running = false;
            _capture.StopRecording();
            _processingThread.Join(500);
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            _bufferedProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);
        }

        private void ProcessingLoop()
        {
            var fftBuffer = new Complex[_fftLength];
            var readBuffer = new byte[_fftLength * sizeof(float)];
            while (_running)
            {
                int bytesRead = _bufferedProvider.Read(readBuffer, 0, readBuffer.Length);
                if (bytesRead < _fftLength * sizeof(float))
                {
                    Thread.Sleep(10);
                    continue;
                }

                // Convert bytes to floats, mono-mix if stereo
                float[] samples = new float[_fftLength];
                int samplesRead = bytesRead / 4;
                for (int i = 0; i < samplesRead; i++)
                    samples[i] = BitConverter.ToSingle(readBuffer, i * 4);

                if (_capture.WaveFormat.Channels == 2)
                {
                    // simple L+R mono mix
                    for (int n = 0; n < samplesRead; n += 2)
                        samples[n / 2] = (samples[n] + samples[n + 1]) * 0.5f;
                }

                // Window + copy into complex buffer
                for (int i = 0; i < _fftLength; i++)
                {
                    float windowed = (i < samplesRead ? samples[i] : 0f) * _window[i];
                    fftBuffer[i].X = windowed;
                    fftBuffer[i].Y = 0;
                }

                FastFourierTransform.FFT(true, (int)Math.Log(_fftLength, 2.0), fftBuffer);

                lock (_lock)
                {
                    for (int i = 0; i < SpectrumSize; i++)
                    {
                        float mag = (float)Math.Sqrt(fftBuffer[i].X * fftBuffer[i].X +
                                                     fftBuffer[i].Y * fftBuffer[i].Y);
                        // scale into [0.05,1]
                        float val = MathF.Min(1f, MathF.Max(0.05f, mag * 50f));
                        // exponential smoothing
                        _smoothedSpectrum[i] =
                            _smoothingFactor * _smoothedSpectrum[i] +
                            (1 - _smoothingFactor) * val;
                        _spectrumData[i] = _smoothedSpectrum[i];
                    }
                }

                SpectrumDataAvailable?.Invoke(this, (float[])_spectrumData.Clone());
            }
        }

        private void StopProcessing()
        {
            _running = false;
        }

        public void Dispose()
        {
            StopAnalyzing();
            _capture.Dispose();
        }

        private static bool IsPowerOfTwo(int x) => (x & (x - 1)) == 0;
    }
}
