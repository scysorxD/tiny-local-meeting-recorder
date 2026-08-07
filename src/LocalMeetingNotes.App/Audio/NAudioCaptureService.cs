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
                    "Unable to start the requested audio capture sources.",
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
            await Task.WhenAll(StopCaptureAsync(microphone), StopCaptureAsync(system));

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
        microphoneCapture.StartRecording();
        microphoneStartOffset = recordingStopwatch.Elapsed;
    }

    private void StartSystemAudio(RecordingRequest recordingRequest)
    {
        renderDevice = GetDevice(recordingRequest.RenderDeviceId, DataFlow.Render, Role.Multimedia);
        silenceKeeper = new SilenceLoopbackKeeper();
        silenceKeeper.Start(renderDevice);

        systemCapture = new WasapiLoopbackCapture(renderDevice);
        systemWriter = new TrackWriter(
            systemCapture.WaveFormat,
            recordingRequest.SystemWavPath,
            (peak, rms) => RaiseMeter(AudioTrack.System, peak, rms));
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

    private void OnMicrophoneDataAvailable(object? sender, WaveInEventArgs eventArgs) =>
        microphoneWriter?.Write(eventArgs.Buffer, eventArgs.BytesRecorded);

    private void OnSystemDataAvailable(object? sender, WaveInEventArgs eventArgs) =>
        systemWriter?.Write(eventArgs.Buffer, eventArgs.BytesRecorded);

    private void RaiseMeter(AudioTrack track, float peak, float rms) =>
        MetersUpdated?.Invoke(this, new AudioMeterEventArgs(track, peak, rms));

    private static Task StopCaptureAsync(IWaveIn? capture)
    {
        if (capture is null)
        {
            return Task.CompletedTask;
        }

        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<StoppedEventArgs>? handler = null;
        handler = (_, _) =>
        {
            capture.RecordingStopped -= handler;
            stopped.TrySetResult();
        };

        capture.RecordingStopped += handler;
        capture.StopRecording();
        return stopped.Task;
    }

    private void StopAndDisposeAll()
    {
        silenceKeeper?.Stop();
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
            systemCapture.Dispose();
            systemCapture = null;
        }

        systemWriter?.Dispose();
        systemWriter = null;
        renderDevice?.Dispose();
        renderDevice = null;
    }

    private sealed class TrackWriter : IDisposable
    {
        private readonly BufferedWaveProvider input;
        private readonly WdlResamplingSampleProvider resampler;
        private readonly WaveFileWriter output;
        private readonly Action<float, float> meterCallback;
        private readonly Stopwatch meterStopwatch = Stopwatch.StartNew();
        private readonly object writerSync = new();

        public TrackWriter(WaveFormat inputFormat, string path, Action<float, float> meterCallback)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

            input = new BufferedWaveProvider(inputFormat)
            {
                DiscardOnBufferOverflow = false
            };
            resampler = new WdlResamplingSampleProvider(
                new MonoSampleProvider(new WaveToSampleProvider(input)),
                TargetSampleRate);
            output = new WaveFileWriter(path, new WaveFormat(TargetSampleRate, 16, 1));
            this.meterCallback = meterCallback;
        }

        public void Write(byte[] buffer, int count)
        {
            lock (writerSync)
            {
                input.AddSamples(buffer, 0, count);
                Drain();
            }
        }

        public void Drain()
        {
            lock (writerSync)
            {
                var sampleBuffer = new float[4_096];
                while (true)
                {
                    var samplesRead = resampler.Read(sampleBuffer, 0, sampleBuffer.Length);
                    if (samplesRead == 0)
                    {
                        return;
                    }

                    WritePcm16(sampleBuffer, samplesRead);
                    if (meterStopwatch.ElapsedMilliseconds >= 125)
                    {
                        meterStopwatch.Restart();
                        meterCallback(GetPeak(sampleBuffer, samplesRead), GetRms(sampleBuffer, samplesRead));
                    }
                }
            }
        }

        public void Dispose() => output.Dispose();

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

        private static float GetPeak(float[] samples, int count) =>
            samples.Take(count).Select(MathF.Abs).DefaultIfEmpty().Max();

        private static float GetRms(float[] samples, int count)
        {
            if (count == 0)
            {
                return 0;
            }

            var squareSum = samples.Take(count).Sum(sample => sample * sample);
            return MathF.Sqrt(squareSum / count);
        }

        private sealed class MonoSampleProvider(ISampleProvider source) : ISampleProvider
        {
            public WaveFormat WaveFormat { get; } =
                WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);

            public int Read(float[] buffer, int offset, int count)
            {
                var channels = source.WaveFormat.Channels;
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
