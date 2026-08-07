using LocalMeetingNotes.Core.Models;

namespace LocalMeetingNotes.Core.Interfaces;

public interface IAudioDeviceService
{
    IReadOnlyList<AudioDeviceInfo> GetMicrophones();

    IReadOnlyList<AudioDeviceInfo> GetRenderDevices();

    AudioDeviceInfo? GetDefaultCommunicationsMicrophone();

    AudioDeviceInfo? GetDefaultRenderDevice();
}
