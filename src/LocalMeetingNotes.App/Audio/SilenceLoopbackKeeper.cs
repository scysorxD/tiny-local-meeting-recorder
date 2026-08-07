using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LocalMeetingNotes.App.Audio;

/// <summary>
/// Keeps a render endpoint active so WASAPI loopback packets retain their wall-clock timeline.
/// </summary>
public sealed class SilenceLoopbackKeeper : IDisposable
{
    private WasapiOut? output;

    public void Start(MMDevice renderDevice)
    {
        if (output is not null)
        {
            throw new InvalidOperationException("The silence keeper is already running.");
        }

        var waveFormat = renderDevice.AudioClient.MixFormat;
        output = new WasapiOut(renderDevice, AudioClientShareMode.Shared, true, 50);
        output.Init(new SilenceWaveProvider(waveFormat));
        output.Play();
    }

    public void Stop()
    {
        output?.Stop();
        output?.Dispose();
        output = null;
    }

    public void Dispose() => Stop();

    private sealed class SilenceWaveProvider(WaveFormat waveFormat) : IWaveProvider
    {
        public WaveFormat WaveFormat { get; } = waveFormat;

        public int Read(byte[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }
    }
}
