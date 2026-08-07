using LocalMeetingNotes.Core.Models;

namespace LocalMeetingNotes.Core.Services;

public static class TranscriptMerger
{
    private static readonly TimeSpan DefaultGroupGap = TimeSpan.FromSeconds(2);

    public static IReadOnlyList<MergedTranscriptSegment> Merge(
        IReadOnlyList<TranscriptSegment> micSegments,
        IReadOnlyList<TranscriptSegment> systemSegments,
        TimeSpan micOffset,
        TimeSpan systemOffset,
        TimeSpan? groupGap = null)
    {
        var gap = groupGap ?? DefaultGroupGap;
        var tagged = new List<(MergedTranscriptSegment Segment, int Order)>();
        var order = 0;

        foreach (var segment in micSegments)
        {
            tagged.Add((
                new MergedTranscriptSegment(
                    segment.Start + micOffset,
                    segment.End + micOffset,
                    Speaker.You,
                    segment.Text),
                order++));
        }

        foreach (var segment in systemSegments)
        {
            tagged.Add((
                new MergedTranscriptSegment(
                    segment.Start + systemOffset,
                    segment.End + systemOffset,
                    Speaker.Remote,
                    segment.Text),
                order++));
        }

        var sorted = tagged
            .OrderBy(entry => entry.Segment.Start)
            .ThenBy(entry => entry.Order)
            .Select(entry => entry.Segment)
            .ToList();

        return GroupConsecutive(sorted, gap);
    }

    private static IReadOnlyList<MergedTranscriptSegment> GroupConsecutive(
        IReadOnlyList<MergedTranscriptSegment> segments,
        TimeSpan groupGap)
    {
        if (segments.Count == 0)
        {
            return segments;
        }

        var result = new List<MergedTranscriptSegment>();
        var current = segments[0];

        for (var i = 1; i < segments.Count; i++)
        {
            var next = segments[i];
            var gap = next.Start - current.End;

            if (next.Speaker == current.Speaker && gap <= groupGap)
            {
                current = new MergedTranscriptSegment(
                    current.Start,
                    next.End > current.End ? next.End : current.End,
                    current.Speaker,
                    JoinText(current.Text, next.Text));
            }
            else
            {
                result.Add(current);
                current = next;
            }
        }

        result.Add(current);
        return result;
    }

    private static string JoinText(string left, string right)
    {
        if (string.IsNullOrEmpty(left))
        {
            return right;
        }

        if (string.IsNullOrEmpty(right))
        {
            return left;
        }

        return $"{left} {right}";
    }
}
