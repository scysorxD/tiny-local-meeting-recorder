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

    public (float MicrophonePeak, float SystemPeak) GetLiveDevicePeaks()
    {
        float microphone = 0;
        float system = 0;

        using var enumerator = new MMDeviceEnumerator();

        try
        {
            using var mic = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            microphone = Math.Clamp(mic.AudioMeterInformation.MasterPeakValue, 0f, 1f);
        }
        catch (COMException)
        {
            // Device unavailable.
        }

        try
        {
            using var render = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            system = Math.Clamp(render.AudioMeterInformation.MasterPeakValue, 0f, 1f);
        }
        catch (COMException)
        {
            // Device unavailable.
        }

        return (microphone, system);
    }

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
