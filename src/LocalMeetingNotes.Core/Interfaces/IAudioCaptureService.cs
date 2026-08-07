using LocalMeetingNotes.Core.Models;

namespace LocalMeetingNotes.Core.Interfaces;

public interface IAudioCaptureService
{
    bool IsRecording { get; }

    event EventHandler<AudioMeterEventArgs>? MetersUpdated;

    (float MicrophonePeak, float SystemPeak) GetLivePeaks();

    Task StartAsync(RecordingRequest request, CancellationToken cancellationToken = default);

    Task<RecordingResult> StopAsync(CancellationToken cancellationToken = default);
}
