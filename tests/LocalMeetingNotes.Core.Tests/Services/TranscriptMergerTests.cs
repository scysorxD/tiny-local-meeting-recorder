using FluentAssertions;
using LocalMeetingNotes.Core.Models;
using LocalMeetingNotes.Core.Services;

namespace LocalMeetingNotes.Core.Tests.Services;

public class TranscriptMergerTests
{
    [Fact]
    public void Merge_mic_before_remote_orders_by_start()
    {
        var mic = new[] { new TranscriptSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), "Hello") };
        var system = new[] { new TranscriptSegment(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(7), "Hi there") };

        var result = TranscriptMerger.Merge(mic, system, TimeSpan.Zero, TimeSpan.Zero);

        result.Should().HaveCount(2);
        result[0].Should().Be(new MergedTranscriptSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), Speaker.You, "Hello"));
        result[1].Should().Be(new MergedTranscriptSegment(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(7), Speaker.Remote, "Hi there"));
    }

    [Fact]
    public void Merge_remote_before_mic_orders_by_start()
    {
        var mic = new[] { new TranscriptSegment(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(12), "Late mic") };
        var system = new[] { new TranscriptSegment(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), "Early remote") };

        var result = TranscriptMerger.Merge(mic, system, TimeSpan.Zero, TimeSpan.Zero);

        result.Should().HaveCount(2);
        result[0].Speaker.Should().Be(Speaker.Remote);
        result[0].Text.Should().Be("Early remote");
        result[1].Speaker.Should().Be(Speaker.You);
        result[1].Text.Should().Be("Late mic");
    }

    [Fact]
    public void Merge_overlap_keeps_both_segments()
    {
        var mic = new[] { new TranscriptSegment(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6), "You talking") };
        var system = new[] { new TranscriptSegment(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), "Remote talking") };

        var result = TranscriptMerger.Merge(mic, system, TimeSpan.Zero, TimeSpan.Zero);

        result.Should().HaveCount(2);
        result[0].Should().Be(new MergedTranscriptSegment(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6), Speaker.You, "You talking"));
        result[1].Should().Be(new MergedTranscriptSegment(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), Speaker.Remote, "Remote talking"));
    }

    [Fact]
    public void Merge_same_start_time_keeps_mic_before_remote()
    {
        var mic = new[] { new TranscriptSegment(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(7), "Mic first") };
        var system = new[] { new TranscriptSegment(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(7), "System second") };

        var result = TranscriptMerger.Merge(mic, system, TimeSpan.Zero, TimeSpan.Zero);

        result.Should().HaveCount(2);
        result[0].Speaker.Should().Be(Speaker.You);
        result[0].Text.Should().Be("Mic first");
        result[1].Speaker.Should().Be(Speaker.Remote);
        result[1].Text.Should().Be("System second");
    }

    [Fact]
    public void Merge_applies_capture_offsets()
    {
        var mic = new[] { new TranscriptSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "Mic") };
        var system = new[] { new TranscriptSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "System") };

        var result = TranscriptMerger.Merge(
            mic,
            system,
            micOffset: TimeSpan.FromSeconds(10),
            systemOffset: TimeSpan.FromSeconds(20));

        result[0].Start.Should().Be(TimeSpan.FromSeconds(11));
        result[0].End.Should().Be(TimeSpan.FromSeconds(12));
        result[1].Start.Should().Be(TimeSpan.FromSeconds(21));
        result[1].End.Should().Be(TimeSpan.FromSeconds(22));
    }

    [Fact]
    public void Merge_groups_same_speaker_when_gap_is_two_seconds_or_less()
    {
        var mic = new[]
        {
            new TranscriptSegment(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), "First"),
            new TranscriptSegment(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6), "Second"),
        };

        var result = TranscriptMerger.Merge(mic, [], TimeSpan.Zero, TimeSpan.Zero);

        result.Should().ContainSingle();
        result[0].Should().Be(new MergedTranscriptSegment(
            TimeSpan.FromSeconds(0),
            TimeSpan.FromSeconds(6),
            Speaker.You,
            "First Second"));
    }

    [Fact]
    public void Merge_does_not_group_same_speaker_when_gap_exceeds_two_seconds()
    {
        var mic = new[]
        {
            new TranscriptSegment(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), "First"),
            new TranscriptSegment(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(7), "Second"),
        };

        var result = TranscriptMerger.Merge(mic, [], TimeSpan.Zero, TimeSpan.Zero);

        result.Should().HaveCount(2);
        result[0].Text.Should().Be("First");
        result[1].Text.Should().Be("Second");
    }

    [Fact]
    public void Merge_does_not_group_same_speaker_when_other_speaker_intervenes()
    {
        var mic = new[]
        {
            new TranscriptSegment(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), "You one"),
            new TranscriptSegment(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6), "You two"),
        };
        var system = new[] { new TranscriptSegment(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "Remote") };

        var result = TranscriptMerger.Merge(mic, system, TimeSpan.Zero, TimeSpan.Zero);

        result.Should().HaveCount(3);
        result[0].Text.Should().Be("You one");
        result[1].Text.Should().Be("Remote");
        result[2].Text.Should().Be("You two");
    }

    [Fact]
    public void Merge_empty_mic_returns_only_system_segments()
    {
        var system = new[] { new TranscriptSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), "Remote only") };

        var result = TranscriptMerger.Merge([], system, TimeSpan.Zero, TimeSpan.Zero);

        result.Should().ContainSingle();
        result[0].Should().Be(new MergedTranscriptSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), Speaker.Remote, "Remote only"));
    }

    [Fact]
    public void Merge_empty_system_returns_only_mic_segments()
    {
        var mic = new[] { new TranscriptSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), "Mic only") };

        var result = TranscriptMerger.Merge(mic, [], TimeSpan.Zero, TimeSpan.Zero);

        result.Should().ContainSingle();
        result[0].Should().Be(new MergedTranscriptSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), Speaker.You, "Mic only"));
    }

    [Fact]
    public void Merge_both_empty_returns_empty_list()
    {
        var result = TranscriptMerger.Merge([], [], TimeSpan.Zero, TimeSpan.Zero);

        result.Should().BeEmpty();
    }
}
