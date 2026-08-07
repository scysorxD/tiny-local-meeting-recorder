using FluentAssertions;
using LocalMeetingNotes.Core.Models;

namespace LocalMeetingNotes.Core.Tests.Models;

public class RecordingContractsTests
{
    [Fact]
    public void Recording_request_preserves_track_paths_and_partial_start_policy()
    {
        var request = new RecordingRequest(
            MicrophoneDeviceId: "mic-1",
            RenderDeviceId: "render-1",
            MicrophoneWavPath: @"C:\meetings\mic.wav",
            SystemWavPath: @"C:\meetings\system.wav",
            AllowMicOnly: true,
            AllowSystemOnly: false);

        request.MicrophoneDeviceId.Should().Be("mic-1");
        request.RenderDeviceId.Should().Be("render-1");
        request.MicrophoneWavPath.Should().Be(@"C:\meetings\mic.wav");
        request.SystemWavPath.Should().Be(@"C:\meetings\system.wav");
        request.AllowMicOnly.Should().BeTrue();
        request.AllowSystemOnly.Should().BeFalse();
    }
}
