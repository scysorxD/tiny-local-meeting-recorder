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
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);

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

            IsRecording = true;
        }

        return Task.CompletedTask;
    }

    public async Task<RecordingResult> StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WasapiCapture? microphone;
        WasapiLoopbackCapture? system;
        RecordingRequest activeRequest;

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
            microphone = microphoneCapture;
            system = systemCapture;
            activeRequest = request;
        }

        try
        {
            // Stop outside the lock so WASAPI callbacks are not blocked.
            await Task.WhenAll(
                StopCaptureAsync(microphone),
                StopCaptureAsync(system)).ConfigureAwait(false);

            lock (sync)
            {
                microphoneWriter?.Drain();
                systemWriter?.Drain();
                silenceKeeper?.Stop();

                var result = new RecordingResult(
                    activeRequest.MicrophoneWavPath,
                    activeRequest.SystemWavPath,
                    microphone is not null,
                    system is not null,
                    microphoneStartOffset,
                    systemStartOffset,
                    recordingStopwatch.Elapsed);

                StopAndDisposeAll();
                return result;
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
            recordingRequest.MicrophoneWavPath,
            (peak, rms) => RaiseMeter(AudioTrack.Microphone, peak, rms));
        microphoneCapture.DataAvailable += OnMicrophoneDataAvailable;
        microphoneCapture.RecordingStopped += OnCaptureStopped;
        microphoneCapture.StartRecording();
        microphoneStartOffset = recordingStopwatch.Elapsed;
    }

    private void StartSystemAudio(RecordingRequest recordingRequest)
    {
        // Separate MMDevice instances: sharing one between WasapiOut and loopback is unreliable.
        renderDevice = GetDevice(recordingRequest.RenderDeviceId, DataFlow.Render, Role.Multimedia);
        silenceDevice = GetDevice(recordingRequest.RenderDeviceId, DataFlow.Render, Role.Multimedia);

        silenceKeeper = new SilenceLoopbackKeeper();
        silenceKeeper.Start(silenceDevice);

        systemCapture = new WasapiLoopbackCapture(renderDevice);
        systemWriter = new TrackWriter(
            systemCapture.WaveFormat,
            recordingRequest.SystemWavPath,
            (peak, rms) => RaiseMeter(AudioTrack.System, peak, rms));
        systemCapture.DataAvailable += OnSystemDataAvailable;
        systemCapture.RecordingStopped += OnCaptureStopped;
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

    private static void OnCaptureStopped(object? sender, StoppedEventArgs eventArgs)
    {
        // RecordingStopped is observed via StopCaptureAsync's local handler; this keeps NAudio happy.
        _ = eventArgs.Exception;
    }

    private void RaiseMeter(AudioTrack track, float peak, float rms) =>
        MetersUpdated?.Invoke(this, new AudioMeterEventArgs(track, peak, rms));

    private static async Task StopCaptureAsync(IWaveIn? capture)
    {
        if (capture is null)
        {
            return;
        }

        if (capture is WasapiCapture { CaptureState: CaptureState.Stopped })
        {
            return;
        }

        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<StoppedEventArgs>? handler = null;
        handler = (_, _) =>
        {
            capture.RecordingStopped -= handler;
            stopped.TrySetResult();
        };

        capture.RecordingStopped += handler;
        try
        {
            capture.StopRecording();

            // If StopRecording completed synchronously, CaptureState may already be Stopped.
            if (capture is WasapiCapture { CaptureState: CaptureState.Stopped })
            {
                stopped.TrySetResult();
            }

            await stopped.Task.WaitAsync(StopTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Fall through and let Dispose tear the capture down.
        }
        catch (Exception)
        {
            // Capture may already be torn down; continue cleanup.
        }
        finally
        {
            capture.RecordingStopped -= handler;
        }
    }

    private void StopAndDisposeAll()
    {
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
            microphoneCapture.RecordingStopped -= OnCaptureStopped;
            try
            {
                if (microphoneCapture.CaptureState != CaptureState.Stopped)
                {
                    microphoneCapture.StopRecording();
                }
            }
            catch
            {
                // Ignore teardown races.
            }

            microphoneCapture.Dispose();
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
            systemCapture.RecordingStopped -= OnCaptureStopped;
            try
            {
                if (systemCapture.CaptureState != CaptureState.Stopped)
                {
                    systemCapture.StopRecording();
                }
            }
            catch
            {
                // Ignore teardown races.
            }

            systemCapture.Dispose();
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
        private readonly WaveFormat inputFormat;
        private readonly BufferedWaveProvider input;
        private readonly ISampleProvider resampler;
        private readonly WaveFileWriter output;
        private readonly Action<float, float> meterCallback;
        private readonly Stopwatch meterStopwatch = Stopwatch.StartNew();
        private readonly object writerSync = new();

        public TrackWriter(WaveFormat inputFormat, string path, Action<float, float> meterCallback)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

            this.inputFormat = inputFormat;
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
            this.meterCallback = meterCallback;
        }

        public void Write(byte[] buffer, int count)
        {
            if (count <= 0)
            {
                return;
            }

            lock (writerSync)
            {
                // Meter from the raw capture buffer so UI feedback is immediate.
                if (meterStopwatch.ElapsedMilliseconds >= 100)
                {
                    meterStopwatch.Restart();
                    var (peak, rms) = MeasurePcm(buffer, count, inputFormat);
                    meterCallback(peak, rms);
                }

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

        private static (float Peak, float Rms) MeasurePcm(byte[] buffer, int count, WaveFormat format)
        {
            if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
            {
                var floats = count / sizeof(float);
                if (floats <= 0)
                {
                    return (0, 0);
                }

                double squareSum = 0;
                var peak = 0f;
                for (var i = 0; i < floats; i++)
                {
                    var sample = MathF.Abs(BitConverter.ToSingle(buffer, i * sizeof(float)));
                    if (sample > peak)
                    {
                        peak = sample;
                    }

                    squareSum += sample * sample;
                }

                return (Math.Clamp(peak, 0, 1), Math.Clamp(MathF.Sqrt((float)(squareSum / floats)), 0, 1));
            }

            if (format.BitsPerSample == 16)
            {
                var samples = count / sizeof(short);
                if (samples <= 0)
                {
                    return (0, 0);
                }

                double squareSum = 0;
                var peak = 0f;
                for (var i = 0; i < samples; i++)
                {
                    var sample = Math.Abs(BitConverter.ToInt16(buffer, i * sizeof(short))) / (float)short.MaxValue;
                    if (sample > peak)
                    {
                        peak = sample;
                    }

                    squareSum += sample * sample;
                }

                return (Math.Clamp(peak, 0, 1), Math.Clamp(MathF.Sqrt((float)(squareSum / samples)), 0, 1));
            }

            return (0, 0);
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
