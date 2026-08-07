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

    private readonly object sync = new();
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
    private System.Threading.Timer? hardwareMeterTimer;
    private bool isStopping;

    public bool IsRecording { get; private set; }

    public event EventHandler<AudioMeterEventArgs>? MetersUpdated;

    public Task StartAsync(RecordingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (sync)
        {
            if (IsRecording || isStopping)
            {
                throw new InvalidOperationException("A recording is already active.");
            }

            this.request = request;
            recordingStopwatch.Restart();

            Exception? microphoneFailure = null;
            Exception? systemFailure = null;

            try
            {
                StartMicrophone(request);
            }
            catch (Exception exception)
            {
                microphoneFailure = exception;
                DisposeMicrophone();
            }

            try
            {
                StartSystemAudio(request);
            }
            catch (Exception exception)
            {
                systemFailure = exception;
                DisposeSystemAudio();
            }

            var microphoneStarted = microphoneCapture is not null;
            var systemStarted = systemCapture is not null;
            var mayContinue =
                microphoneStarted && systemStarted ||
                microphoneStarted && systemFailure is not null && request.AllowMicOnly ||
                systemStarted && microphoneFailure is not null && request.AllowSystemOnly;

            if (!mayContinue)
            {
                StopAndDisposeAll();
                throw new RecordingStartException(
                    BuildStartFailureMessage(microphoneFailure, systemFailure),
                    microphoneFailure,
                    systemFailure);
            }

            StartHardwareMeterPolling();
            IsRecording = true;
        }

        return Task.CompletedTask;
    }

    public Task<RecordingResult> StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (sync)
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
        }

        try
        {
            // Never wait forever for RecordingStopped — it often never arrives on WASAPI.
            StopHardwareMeterPolling();
            ForceStopCapture(microphoneCapture);
            ForceStopCapture(systemCapture);

            // Give the capture thread a brief moment to flush final packets.
            Thread.Sleep(75);

            lock (sync)
            {
                microphoneWriter?.Drain();
                systemWriter?.Drain();

                var activeRequest = request!;
                var result = new RecordingResult(
                    activeRequest.MicrophoneWavPath,
                    activeRequest.SystemWavPath,
                    microphoneCapture is not null || File.Exists(activeRequest.MicrophoneWavPath),
                    systemCapture is not null || File.Exists(activeRequest.SystemWavPath),
                    microphoneStartOffset,
                    systemStartOffset,
                    recordingStopwatch.Elapsed);

                StopAndDisposeAll();
                return Task.FromResult(result);
            }
        }
        finally
        {
            lock (sync)
            {
                isStopping = false;
            }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            StopAndDisposeAll();
        }
    }

    private void StartMicrophone(RecordingRequest recordingRequest)
    {
        microphoneDevice = GetDevice(recordingRequest.MicrophoneDeviceId, DataFlow.Capture, Role.Communications);
        microphoneCapture = new WasapiCapture(microphoneDevice);
        microphoneWriter = new TrackWriter(
            microphoneCapture.WaveFormat,
            recordingRequest.MicrophoneWavPath);
        microphoneCapture.DataAvailable += OnMicrophoneDataAvailable;
        microphoneCapture.StartRecording();
        microphoneStartOffset = recordingStopwatch.Elapsed;
    }

    private void StartSystemAudio(RecordingRequest recordingRequest)
    {
        // Separate MMDevice instances for playback keeper vs loopback capture.
        renderDevice = GetDevice(recordingRequest.RenderDeviceId, DataFlow.Render, Role.Multimedia);
        silenceDevice = GetDevice(recordingRequest.RenderDeviceId, DataFlow.Render, Role.Multimedia);

        silenceKeeper = new SilenceLoopbackKeeper();
        silenceKeeper.Start(silenceDevice);

        systemCapture = new WasapiLoopbackCapture(renderDevice);
        systemWriter = new TrackWriter(
            systemCapture.WaveFormat,
            recordingRequest.SystemWavPath);
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
            microphoneWriter?.Write(eventArgs.Buffer, eventArgs.BytesRecorded);
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
            systemWriter?.Write(eventArgs.Buffer, eventArgs.BytesRecorded);
        }
        catch
        {
            // Never throw on the capture thread.
        }
    }

    private void StartHardwareMeterPolling()
    {
        StopHardwareMeterPolling();
        hardwareMeterTimer = new System.Threading.Timer(
            _ => PublishHardwareMeters(),
            null,
            dueTime: 0,
            period: 100);
    }

    private void StopHardwareMeterPolling()
    {
        hardwareMeterTimer?.Dispose();
        hardwareMeterTimer = null;
    }

    private void PublishHardwareMeters()
    {
        try
        {
            float micPeak;
            float systemPeak;

            lock (sync)
            {
                if (!IsRecording)
                {
                    return;
                }

                micPeak = SafePeak(microphoneDevice);
                systemPeak = SafePeak(renderDevice);
            }

            RaiseMeter(AudioTrack.Microphone, micPeak, micPeak);
            RaiseMeter(AudioTrack.System, systemPeak, systemPeak);
        }
        catch
        {
            // Meter polling must never break recording.
        }
    }

    private static float SafePeak(MMDevice? device)
    {
        if (device is null)
        {
            return 0;
        }

        try
        {
            return Math.Clamp(device.AudioMeterInformation.MasterPeakValue, 0f, 1f);
        }
        catch
        {
            return 0;
        }
    }

    private void RaiseMeter(AudioTrack track, float peak, float rms) =>
        MetersUpdated?.Invoke(this, new AudioMeterEventArgs(track, peak, rms));

    private static void ForceStopCapture(IWaveIn? capture)
    {
        if (capture is null)
        {
            return;
        }

        try
        {
            capture.StopRecording();
        }
        catch
        {
            // Already stopped / disposed.
        }
    }

    private void StopAndDisposeAll()
    {
        StopHardwareMeterPolling();

        silenceKeeper?.Stop();
        silenceKeeper?.Dispose();
        silenceKeeper = null;

        DisposeMicrophone();
        DisposeSystemAudio();

        recordingStopwatch.Reset();
        request = null;
        IsRecording = false;
        microphoneStartOffset = TimeSpan.Zero;
        systemStartOffset = TimeSpan.Zero;
    }

    private void DisposeMicrophone()
    {
        if (microphoneCapture is not null)
        {
            microphoneCapture.DataAvailable -= OnMicrophoneDataAvailable;
            ForceStopCapture(microphoneCapture);
            try
            {
                microphoneCapture.Dispose();
            }
            catch
            {
                // Ignore teardown races.
            }

            microphoneCapture = null;
        }

        microphoneWriter?.Dispose();
        microphoneWriter = null;
        microphoneDevice?.Dispose();
        microphoneDevice = null;
    }

    private void DisposeSystemAudio()
    {
        if (systemCapture is not null)
        {
            systemCapture.DataAvailable -= OnSystemDataAvailable;
            ForceStopCapture(systemCapture);
            try
            {
                systemCapture.Dispose();
            }
            catch
            {
                // Ignore teardown races.
            }

            systemCapture = null;
        }

        systemWriter?.Dispose();
        systemWriter = null;
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

    private sealed class TrackWriter : IDisposable
    {
        private readonly BufferedWaveProvider input;
        private readonly ISampleProvider resampler;
        private readonly WaveFileWriter output;
        private readonly object writerSync = new();

        public TrackWriter(WaveFormat inputFormat, string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

            input = new BufferedWaveProvider(inputFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromSeconds(5),
            };

            ISampleProvider sampleSource = new WaveToSampleProvider(input);
            if (sampleSource.WaveFormat.Channels > 1)
            {
                sampleSource = new MonoSampleProvider(sampleSource);
            }

            resampler = sampleSource.WaveFormat.SampleRate == TargetSampleRate
                ? sampleSource
                : new WdlResamplingSampleProvider(sampleSource, TargetSampleRate);

            output = new WaveFileWriter(path, new WaveFormat(TargetSampleRate, 16, 1));
        }

        public void Write(byte[] buffer, int count)
        {
            if (count <= 0)
            {
                return;
            }

            lock (writerSync)
            {
                input.AddSamples(buffer, 0, count);
                DrainUnlocked();
            }
        }

        public void Drain()
        {
            lock (writerSync)
            {
                DrainUnlocked();
            }
        }

        public void Dispose()
        {
            lock (writerSync)
            {
                DrainUnlocked();
                output.Dispose();
            }
        }

        private void DrainUnlocked()
        {
            var sampleBuffer = new float[4_096];
            while (true)
            {
                var samplesRead = resampler.Read(sampleBuffer, 0, sampleBuffer.Length);
                if (samplesRead <= 0)
                {
                    return;
                }

                WritePcm16(sampleBuffer, samplesRead);
            }
        }

        private void WritePcm16(float[] samples, int count)
        {
            var pcmBytes = new byte[count * sizeof(short)];
            for (var index = 0; index < count; index++)
            {
                var sample = (short)Math.Clamp(samples[index] * short.MaxValue, short.MinValue, short.MaxValue);
                BinaryPrimitives.WriteInt16LittleEndian(
                    pcmBytes.AsSpan(index * sizeof(short), sizeof(short)),
                    sample);
            }

            output.Write(pcmBytes, 0, pcmBytes.Length);
        }

        private sealed class MonoSampleProvider(ISampleProvider source) : ISampleProvider
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
