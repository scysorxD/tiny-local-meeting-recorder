using System.Runtime.InteropServices;
using LocalMeetingNotes.Core.Interfaces;
using LocalMeetingNotes.Core.Models;
using NAudio.CoreAudioApi;

namespace LocalMeetingNotes.App.Audio;

public sealed class NAudioDeviceService : IAudioDeviceService
{
    public IReadOnlyList<AudioDeviceInfo> GetMicrophones() =>
        GetDevices(DataFlow.Capture, GetDefaultCommunicationsMicrophone()?.Id);

    public IReadOnlyList<AudioDeviceInfo> GetRenderDevices() =>
        GetDevices(DataFlow.Render, GetDefaultRenderDevice()?.Id);

    public AudioDeviceInfo? GetDefaultCommunicationsMicrophone() =>
        GetDefault(DataFlow.Capture, Role.Communications);

    public AudioDeviceInfo? GetDefaultRenderDevice() =>
        GetDefault(DataFlow.Render, Role.Multimedia);

    private static IReadOnlyList<AudioDeviceInfo> GetDevices(DataFlow dataFlow, string? defaultId)
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator
            .EnumerateAudioEndPoints(dataFlow, DeviceState.Active)
            .Select(device =>
            {
                using (device)
                {
                    return new AudioDeviceInfo(device.ID, device.FriendlyName, device.ID == defaultId);
                }
            })
            .ToArray();
    }

    private static AudioDeviceInfo? GetDefault(DataFlow dataFlow, Role role)
    {
        using var enumerator = new MMDeviceEnumerator();

        try
        {
            using var device = enumerator.GetDefaultAudioEndpoint(dataFlow, role);
            return new AudioDeviceInfo(device.ID, device.FriendlyName, true);
        }
        catch (COMException)
        {
            return null;
        }
    }
}
