namespace LocalMeetingNotes.Core.Models;

public enum AudioTrack
{
    Microphone,
    System
}

public sealed class AudioMeterEventArgs : EventArgs
{
    public AudioMeterEventArgs(AudioTrack track, float peak, float rms)
    {
        Track = track;
        Peak = peak;
        Rms = rms;
    }

    public AudioTrack Track { get; }

    public float Peak { get; }

    public float Rms { get; }
}
