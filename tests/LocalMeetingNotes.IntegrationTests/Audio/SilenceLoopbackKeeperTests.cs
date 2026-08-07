namespace LocalMeetingNotes.IntegrationTests.Audio;

public class SilenceLoopbackKeeperTests
{
    [Fact(Skip = "hardware")]
    public void System_track_preserves_a_thirty_second_silence_gap()
    {
        // Manual scenario: play 10s audio, wait 30s, play 10s audio. The second
        // audible block in system.wav must begin near 00:40 rather than 00:10.
    }
}
