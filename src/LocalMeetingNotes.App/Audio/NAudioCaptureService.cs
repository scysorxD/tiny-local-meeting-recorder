using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using LocalMeetingNotes.Core.Interfaces;
using LocalMeetingNotes.Core.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace LocalMeetingNotes.App.Audio;

public sealed class NAudioCaptureService : IAudioCaptureService, IDisposable
{
    private const int TargetSampleRate = 16_000;

    private readonly object stateLock = new();
    private readonly Stopwatch recordingStopwatch = new();
    private WasapiCapture? microphoneCapture;
    private WasapiLoopbackCapture? systemCapture;
    private MMDevice? microphoneDevice;
    private MMDevice? renderDevice;
    private MMDevice? silenceDevice;
    private SilenceLoopbackKeeper? silenceKeeper;
    private TrackWriter? microphoneWriter;
    private TrackWriter? systemWriter;
    private RecordingRequest? request;
    private TimeSpan microphoneStartOffset;
    private TimeSpan systemStartOffset;
    private volatile float latestMicrophonePeak;
    private volatile float latestSystemPeak;
    private bool isStopping;

    public bool IsRecording { get; private set; }

    public event EventHandler<AudioMeterEventArgs>? MetersUpdated;

    public (float MicrophonePeak, float SystemPeak) GetLivePeaks() =>
        (latestMicrophonePeak, latestSystemPeak);

    public Task StartAsync(RecordingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (stateLock)
        {
            if (IsRecording || isStopping)
            {
                throw new InvalidOperationException("A recording is already active.");
            }

            this.request = request;
            recordingStopwatch.Restart();
            latestMicrophonePeak = 0;
            latestSystemPeak = 0;

            Exception? microphoneFailure = null;
            Exception? systemFailure = null;

            try
            {
                StartMicrophone(request);
            }
            catch (Exception exception)
            {
                microphoneFailure = exception;
                DisposeMicrophone_NoLock();
            }

            try
            {
                StartSystemAudio(request);
            }
            catch (Exception exception)
            {
                systemFailure = exception;
                DisposeSystemAudio_NoLock();
            }

            var microphoneStarted = microphoneCapture is not null;
            var systemStarted = systemCapture is not null;
            var mayContinue =
                microphoneStarted && systemStarted ||
                microphoneStarted && systemFailure is not null && request.AllowMicOnly ||
                systemStarted && microphoneFailure is not null && request.AllowSystemOnly;

            if (!mayContinue)
            {
                StopAndDisposeAll_NoLock();
                throw new RecordingStartException(
                    BuildStartFailureMessage(microphoneFailure, systemFailure),
                    microphoneFailure,
                    systemFailure);
            }

            IsRecording = true;
        }

        return Task.CompletedTask;
    }

    public Task<RecordingResult> StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WasapiCapture? mic;
        WasapiLoopbackCapture? system;
        TrackWriter? micWriter;
        TrackWriter? systemWriterLocal;
        SilenceLoopbackKeeper? keeper;
        RecordingRequest activeRequest;
        TimeSpan micOffset;
        TimeSpan systemOffset;
        TimeSpan elapsed;

        lock (stateLock)
        {
            if (!IsRecording || request is null)
            {
                throw new InvalidOperationException("No recording is active.");
            }

            if (isStopping)
            {
                throw new InvalidOperationException("Recording is already stopping.");
            }

            isStopping = true;
            IsRecording = false;

            // Detach callbacks BEFORE StopRecording to avoid capture-thread deadlocks.
            if (microphoneCapture is not null)
            {
                microphoneCapture.DataAvailable -= OnMicrophoneDataAvailable;
            }

            if (systemCapture is not null)
            {
                systemCapture.DataAvailable -= OnSystemDataAvailable;
            }

            mic = microphoneCapture;
            system = systemCapture;
            micWriter = microphoneWriter;
            systemWriterLocal = systemWriter;
            keeper = silenceKeeper;
            activeRequest = request;
            micOffset = microphoneStartOffset;
            systemOffset = systemStartOffset;
            elapsed = recordingStopwatch.Elapsed;

            microphoneCapture = null;
            systemCapture = null;
            microphoneWriter = null;
            systemWriter = null;
            silenceKeeper = null;
            request = null;
        }

        // Stop/dispose outside the lock and never wait on RecordingStopped.
        try
        {
            try { mic?.StopRecording(); } catch { /* ignore */ }
            try { system?.StopRecording(); } catch { /* ignore */ }
            try { keeper?.Stop(); } catch { /* ignore */ }

            try { mic?.Dispose(); } catch { /* ignore */ }
            try { system?.Dispose(); } catch { /* ignore */ }
            try { keeper?.Dispose(); } catch { /* ignore */ }

            // Bounded flush only — never an open-ended resample drain loop.
            try { micWriter?.FlushAndDispose(); } catch { /* ignore */ }
            try { systemWriterLocal?.FlushAndDispose(); } catch { /* ignore */ }

            lock (stateLock)
            {
                microphoneDevice?.Dispose();
                microphoneDevice = null;
                silenceDevice?.Dispose();
                silenceDevice = null;
                renderDevice?.Dispose();
                renderDevice = null;
                recordingStopwatch.Reset();
                latestMicrophonePeak = 0;
                latestSystemPeak = 0;
            }

            return Task.FromResult(new RecordingResult(
                activeRequest.MicrophoneWavPath,
                activeRequest.SystemWavPath,
                File.Exists(activeRequest.MicrophoneWavPath),
                File.Exists(activeRequest.SystemWavPath),
                micOffset,
                systemOffset,
                elapsed));
        }
        finally
        {
            lock (stateLock)
            {
                isStopping = false;
            }
        }
    }

    public void Dispose()
    {
        if (IsRecording || request is not null)
        {
            try
            {
                StopAsync().GetAwaiter().GetResult();
            }
            catch
            {
                lock (stateLock)
                {
                    StopAndDisposeAll_NoLock();
                }
            }
        }
        else
        {
            lock (stateLock)
            {
                StopAndDisposeAll_NoLock();
            }
        }
    }

    private void StartMicrophone(RecordingRequest recordingRequest)
    {
        microphoneDevice = GetDevice(recordingRequest.MicrophoneDeviceId, DataFlow.Capture, Role.Communications);
        microphoneCapture = new WasapiCapture(microphoneDevice);
        microphoneWriter = new TrackWriter(microphoneCapture.WaveFormat, recordingRequest.MicrophoneWavPath);
        microphoneCapture.DataAvailable += OnMicrophoneDataAvailable;
        microphoneCapture.StartRecording();
        microphoneStartOffset = recordingStopwatch.Elapsed;
    }

    private void StartSystemAudio(RecordingRequest recordingRequest)
    {
        renderDevice = GetDevice(recordingRequest.RenderDeviceId, DataFlow.Render, Role.Multimedia);
        silenceDevice = GetDevice(recordingRequest.RenderDeviceId, DataFlow.Render, Role.Multimedia);

        silenceKeeper = new SilenceLoopbackKeeper();
        silenceKeeper.Start(silenceDevice);

        systemCapture = new WasapiLoopbackCapture(renderDevice);
        systemWriter = new TrackWriter(systemCapture.WaveFormat, recordingRequest.SystemWavPath);
        systemCapture.DataAvailable += OnSystemDataAvailable;
        systemCapture.StartRecording();
        systemStartOffset = recordingStopwatch.Elapsed;
    }

    private static MMDevice GetDevice(string? id, DataFlow dataFlow, Role role)
    {
        using var enumerator = new MMDeviceEnumerator();
        return string.IsNullOrWhiteSpace(id)
            ? enumerator.GetDefaultAudioEndpoint(dataFlow, role)
            : enumerator.GetDevice(id);
    }

    private void OnMicrophoneDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        try
        {
            var peak = MeasurePeak(eventArgs.Buffer, eventArgs.BytesRecorded, microphoneCapture?.WaveFormat);
            latestMicrophonePeak = peak;
            microphoneWriter?.Write(eventArgs.Buffer, eventArgs.BytesRecorded);
            MetersUpdated?.Invoke(this, new AudioMeterEventArgs(AudioTrack.Microphone, peak, peak));
        }
        catch
        {
            // Never throw on the capture thread.
        }
    }

    private void OnSystemDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        try
        {
            var peak = MeasurePeak(eventArgs.Buffer, eventArgs.BytesRecorded, systemCapture?.WaveFormat);
            latestSystemPeak = peak;
            systemWriter?.Write(eventArgs.Buffer, eventArgs.BytesRecorded);
            MetersUpdated?.Invoke(this, new AudioMeterEventArgs(AudioTrack.System, peak, peak));
        }
        catch
        {
            // Never throw on the capture thread.
        }
    }

    private static float MeasurePeak(byte[] buffer, int count, WaveFormat? format)
    {
        if (format is null || count <= 0)
        {
            return 0;
        }

        try
        {
            // Prefer WASAPI float buffers.
            if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
            {
                var floats = count / sizeof(float);
                var peak = 0f;
                for (var i = 0; i < floats; i++)
                {
                    var sample = MathF.Abs(BitConverter.ToSingle(buffer, i * sizeof(float)));
                    if (sample > peak)
                    {
                        peak = sample;
                    }
                }

                return Math.Clamp(peak, 0f, 1f);
            }

            if (format.BitsPerSample == 16)
            {
                var samples = count / sizeof(short);
                var peak = 0f;
                for (var i = 0; i < samples; i++)
                {
                    var sample = Math.Abs(BitConverter.ToInt16(buffer, i * sizeof(short))) / (float)short.MaxValue;
                    if (sample > peak)
                    {
                        peak = sample;
                    }
                }

                return Math.Clamp(peak, 0f, 1f);
            }
        }
        catch
        {
            return 0;
        }

        return 0;
    }

    private void StopAndDisposeAll_NoLock()
    {
        if (microphoneCapture is not null)
        {
            microphoneCapture.DataAvailable -= OnMicrophoneDataAvailable;
        }

        if (systemCapture is not null)
        {
            systemCapture.DataAvailable -= OnSystemDataAvailable;
        }

        try { microphoneCapture?.StopRecording(); } catch { /* ignore */ }
        try { systemCapture?.StopRecording(); } catch { /* ignore */ }
        try { silenceKeeper?.Stop(); } catch { /* ignore */ }

        try { microphoneCapture?.Dispose(); } catch { /* ignore */ }
        try { systemCapture?.Dispose(); } catch { /* ignore */ }
        try { silenceKeeper?.Dispose(); } catch { /* ignore */ }
        try { microphoneWriter?.FlushAndDispose(); } catch { /* ignore */ }
        try { systemWriter?.FlushAndDispose(); } catch { /* ignore */ }

        microphoneCapture = null;
        systemCapture = null;
        silenceKeeper = null;
        microphoneWriter = null;
        systemWriter = null;

        DisposeMicrophone_NoLock();
        DisposeSystemAudio_NoLock();

        recordingStopwatch.Reset();
        request = null;
        IsRecording = false;
        isStopping = false;
        latestMicrophonePeak = 0;
        latestSystemPeak = 0;
        microphoneStartOffset = TimeSpan.Zero;
        systemStartOffset = TimeSpan.Zero;
    }

    private void DisposeMicrophone_NoLock()
    {
        microphoneDevice?.Dispose();
        microphoneDevice = null;
    }

    private void DisposeSystemAudio_NoLock()
    {
        silenceDevice?.Dispose();
        silenceDevice = null;
        renderDevice?.Dispose();
        renderDevice = null;
    }

    private static string BuildStartFailureMessage(Exception? microphoneFailure, Exception? systemFailure)
    {
        if (microphoneFailure is not null && systemFailure is not null)
        {
            return $"Microphone: {microphoneFailure.Message} | System audio: {systemFailure.Message}";
        }

        if (microphoneFailure is not null)
        {
            return $"Microphone capture failed: {microphoneFailure.Message}";
        }

        if (systemFailure is not null)
        {
            return $"System audio capture failed: {systemFailure.Message}";
        }

        return "Unable to start the requested audio capture sources.";
    }

    /// <summary>
    /// Writes capture audio to 16 kHz mono PCM16. Resampling happens in small bounded chunks
    /// so Stop never blocks on an unbounded drain loop.
    /// </summary>
    private sealed class TrackWriter
    {
        private readonly BufferedWaveProvider input;
        private readonly ISampleProvider resampler;
        private readonly WaveFileWriter output;
        private readonly object writerSync = new();
        private bool disposed;

        public TrackWriter(WaveFormat inputFormat, string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

            input = new BufferedWaveProvider(inputFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromSeconds(2),
            };

            ISampleProvider sampleSource = new WaveToSampleProvider(input);
            if (sampleSource.WaveFormat.Channels > 1)
            {
                sampleSource = new StereoToMonoSampleProvider(sampleSource);
            }

            resampler = sampleSource.WaveFormat.SampleRate == TargetSampleRate
                ? sampleSource
                : new WdlResamplingSampleProvider(sampleSource, TargetSampleRate);

            output = new WaveFileWriter(path, new WaveFormat(TargetSampleRate, 16, 1));
        }

        public void Write(byte[] buffer, int count)
        {
            if (count <= 0 || disposed)
            {
                return;
            }

            lock (writerSync)
            {
                if (disposed)
                {
                    return;
                }

                input.AddSamples(buffer, 0, count);
                Pump(maxReads: 8);
            }
        }

        public void FlushAndDispose()
        {
            lock (writerSync)
            {
                if (disposed)
                {
                    return;
                }

                Pump(maxReads: 64);
                output.Dispose();
                disposed = true;
            }
        }

        private void Pump(int maxReads)
        {
            var sampleBuffer = new float[2_048];
            for (var i = 0; i < maxReads; i++)
            {
                var samplesRead = resampler.Read(sampleBuffer, 0, sampleBuffer.Length);
                if (samplesRead <= 0)
                {
                    return;
                }

                var pcmBytes = new byte[samplesRead * sizeof(short)];
                for (var index = 0; index < samplesRead; index++)
                {
                    var sample = (short)Math.Clamp(sampleBuffer[index] * short.MaxValue, short.MinValue, short.MaxValue);
                    BinaryPrimitives.WriteInt16LittleEndian(
                        pcmBytes.AsSpan(index * sizeof(short), sizeof(short)),
                        sample);
                }

                output.Write(pcmBytes, 0, pcmBytes.Length);
            }
        }

        private sealed class StereoToMonoSampleProvider(ISampleProvider source) : ISampleProvider
        {
            public WaveFormat WaveFormat { get; } =
                WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);

            public int Read(float[] buffer, int offset, int count)
            {
                var channels = source.WaveFormat.Channels;
                if (channels <= 1)
                {
                    return source.Read(buffer, offset, count);
                }

                var sourceBuffer = new float[count * channels];
                var sourceSamplesRead = source.Read(sourceBuffer, 0, sourceBuffer.Length);
                var framesRead = sourceSamplesRead / channels;

                for (var frame = 0; frame < framesRead; frame++)
                {
                    var sum = 0f;
                    for (var channel = 0; channel < channels; channel++)
                    {
                        sum += sourceBuffer[(frame * channels) + channel];
                    }

                    buffer[offset + frame] = sum / channels;
                }

                return framesRead;
            }
        }
    }
}
