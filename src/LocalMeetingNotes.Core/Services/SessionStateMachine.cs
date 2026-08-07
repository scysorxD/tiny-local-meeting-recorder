using LocalMeetingNotes.Core.Models;

namespace LocalMeetingNotes.Core.Services;

public sealed class SessionStateMachine
{
    private static readonly IReadOnlyDictionary<SessionStatus, HashSet<SessionStatus>> AllowedTransitions =
        new Dictionary<SessionStatus, HashSet<SessionStatus>>
        {
            [SessionStatus.Draft] =
            [
                SessionStatus.Recording,
                SessionStatus.Failed,
            ],
            [SessionStatus.Recording] =
            [
                SessionStatus.Stopping,
                SessionStatus.Failed,
                SessionStatus.Interrupted,
            ],
            [SessionStatus.Stopping] =
            [
                SessionStatus.Queued,
                SessionStatus.WaitingForModel,
                SessionStatus.Failed,
                SessionStatus.Interrupted,
            ],
            [SessionStatus.Queued] =
            [
                SessionStatus.WaitingForModel,
                SessionStatus.TranscribingMic,
                SessionStatus.Failed,
            ],
            [SessionStatus.WaitingForModel] =
            [
                SessionStatus.Queued,
                SessionStatus.TranscribingMic,
                SessionStatus.Failed,
            ],
            [SessionStatus.TranscribingMic] =
            [
                SessionStatus.TranscribingSystem,
                SessionStatus.Failed,
                SessionStatus.Interrupted,
            ],
            [SessionStatus.TranscribingSystem] =
            [
                SessionStatus.Merging,
                SessionStatus.Failed,
                SessionStatus.Interrupted,
            ],
            [SessionStatus.Merging] =
            [
                SessionStatus.WritingNote,
                SessionStatus.Failed,
            ],
            [SessionStatus.WritingNote] =
            [
                SessionStatus.Completed,
                SessionStatus.Failed,
            ],
            [SessionStatus.Failed] =
            [
                SessionStatus.Queued,
            ],
            [SessionStatus.Interrupted] =
            [
                SessionStatus.Queued,
            ],
            [SessionStatus.Completed] = [],
        };

    public static bool CanTransition(SessionStatus from, SessionStatus to) =>
        AllowedTransitions.TryGetValue(from, out var targets) && targets.Contains(to);

    public void Transition(MeetingSession session, SessionStatus to)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!CanTransition(session.Status, to))
        {
            throw new InvalidOperationException(
                $"Illegal session transition from {session.Status} to {to}.");
        }

        session.Status = to;
    }
}
