using FluentAssertions;
using LocalMeetingNotes.Core.Models;
using LocalMeetingNotes.Core.Services;

namespace LocalMeetingNotes.Core.Tests.Services;

public class SessionStateMachineTests
{
    private static MeetingSession CreateSession(SessionStatus status = SessionStatus.Draft) =>
        new()
        {
            SessionId = Guid.NewGuid(),
            Metadata = new MeetingMetadata("Test meeting"),
            Status = status,
            MeetingsRoot = @"C:\Meetings",
        };

    [Fact]
    public void CanTransition_happy_path_allows_full_pipeline()
    {
        var path = new[]
        {
            SessionStatus.Draft,
            SessionStatus.Recording,
            SessionStatus.Stopping,
            SessionStatus.Queued,
            SessionStatus.TranscribingMic,
            SessionStatus.TranscribingSystem,
            SessionStatus.Merging,
            SessionStatus.WritingNote,
            SessionStatus.Completed,
        };

        for (var i = 0; i < path.Length - 1; i++)
        {
            SessionStateMachine.CanTransition(path[i], path[i + 1])
                .Should().BeTrue($"expected {path[i]} -> {path[i + 1]}");
        }
    }

    [Fact]
    public void Transition_happy_path_updates_session_status()
    {
        var session = CreateSession();
        var machine = new SessionStateMachine();

        machine.Transition(session, SessionStatus.Recording);
        session.Status.Should().Be(SessionStatus.Recording);

        machine.Transition(session, SessionStatus.Stopping);
        machine.Transition(session, SessionStatus.Queued);
        machine.Transition(session, SessionStatus.TranscribingMic);
        machine.Transition(session, SessionStatus.TranscribingSystem);
        machine.Transition(session, SessionStatus.Merging);
        machine.Transition(session, SessionStatus.WritingNote);
        machine.Transition(session, SessionStatus.Completed);

        session.Status.Should().Be(SessionStatus.Completed);
    }

    [Fact]
    public void CanTransition_Queued_to_WaitingForModel()
    {
        SessionStateMachine.CanTransition(SessionStatus.Queued, SessionStatus.WaitingForModel)
            .Should().BeTrue();
    }

    [Fact]
    public void Transition_Queued_to_WaitingForModel_updates_session()
    {
        var session = CreateSession(SessionStatus.Queued);
        new SessionStateMachine().Transition(session, SessionStatus.WaitingForModel);
        session.Status.Should().Be(SessionStatus.WaitingForModel);
    }

    [Theory]
    [InlineData(SessionStatus.Recording)]
    [InlineData(SessionStatus.Stopping)]
    [InlineData(SessionStatus.Queued)]
    [InlineData(SessionStatus.TranscribingMic)]
    [InlineData(SessionStatus.TranscribingSystem)]
    [InlineData(SessionStatus.Merging)]
    [InlineData(SessionStatus.WritingNote)]
    public void CanTransition_allows_transition_to_Failed(SessionStatus from)
    {
        SessionStateMachine.CanTransition(from, SessionStatus.Failed).Should().BeTrue();
    }

    [Fact]
    public void Transition_to_Failed_updates_session()
    {
        var session = CreateSession(SessionStatus.TranscribingMic);
        new SessionStateMachine().Transition(session, SessionStatus.Failed);
        session.Status.Should().Be(SessionStatus.Failed);
    }

    [Theory]
    [InlineData(SessionStatus.TranscribingMic)]
    [InlineData(SessionStatus.TranscribingSystem)]
    public void CanTransition_Transcribing_to_Interrupted(SessionStatus from)
    {
        SessionStateMachine.CanTransition(from, SessionStatus.Interrupted).Should().BeTrue();
    }

    [Fact]
    public void Transition_TranscribingMic_to_Interrupted_updates_session()
    {
        var session = CreateSession(SessionStatus.TranscribingMic);
        new SessionStateMachine().Transition(session, SessionStatus.Interrupted);
        session.Status.Should().Be(SessionStatus.Interrupted);
    }

    [Fact]
    public void CanTransition_Completed_to_Recording_is_rejected()
    {
        SessionStateMachine.CanTransition(SessionStatus.Completed, SessionStatus.Recording)
            .Should().BeFalse();
    }

    [Fact]
    public void Transition_Completed_to_Recording_throws()
    {
        var session = CreateSession(SessionStatus.Completed);
        var act = () => new SessionStateMachine().Transition(session, SessionStatus.Recording);
        act.Should().Throw<InvalidOperationException>();
        session.Status.Should().Be(SessionStatus.Completed);
    }

    [Fact]
    public void CanTransition_Stopping_to_WaitingForModel_when_no_model()
    {
        SessionStateMachine.CanTransition(SessionStatus.Stopping, SessionStatus.WaitingForModel)
            .Should().BeTrue();
    }

    [Fact]
    public void CanTransition_Failed_to_Queued_for_retry()
    {
        SessionStateMachine.CanTransition(SessionStatus.Failed, SessionStatus.Queued).Should().BeTrue();
    }

    [Fact]
    public void CanTransition_Interrupted_to_Queued_for_retry()
    {
        SessionStateMachine.CanTransition(SessionStatus.Interrupted, SessionStatus.Queued).Should().BeTrue();
    }

    [Fact]
    public void CanTransition_WaitingForModel_to_TranscribingMic()
    {
        SessionStateMachine.CanTransition(SessionStatus.WaitingForModel, SessionStatus.TranscribingMic)
            .Should().BeTrue();
    }
}
