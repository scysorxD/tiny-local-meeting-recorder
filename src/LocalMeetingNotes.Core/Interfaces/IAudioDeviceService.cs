using LocalMeetingNotes.Core.Models;

namespace LocalMeetingNotes.Core.Interfaces;

public interface IAudioDeviceService
{
    IReadOnlyList<AudioDeviceInfo> GetMicrophones();

    IReadOnlyList<AudioDeviceInfo> GetRenderDevices();

    AudioDeviceInfo? GetDefaultCommunicationsMicrophone();

    AudioDeviceInfo? GetDefaultRenderDevice();

    /// <summary>
    /// Live WASAPI peak meters for the current default mic/output (works without an active recording).
    /// </summary>
    (float MicrophonePeak, float SystemPeak) GetLiveDevicePeaks();
}
