using FluentAssertions;
using LocalMeetingNotes.Core.Services;

namespace LocalMeetingNotes.Core.Tests.Services;

public class DiskSpaceCheckerTests
{
    [Fact]
    public void Check_temp_path_is_sufficient_or_reports_clearly()
    {
        var checker = new DiskSpaceChecker();
        var result = checker.Check(Path.GetTempPath(), minimumFreeBytes: 1);
        result.CouldCheck.Should().BeTrue();
        result.IsSufficient.Should().BeTrue();
        result.AvailableFreeBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Check_with_huge_threshold_reports_insufficient()
    {
        var checker = new DiskSpaceChecker();
        var result = checker.Check(Path.GetTempPath(), minimumFreeBytes: long.MaxValue / 2);
        result.CouldCheck.Should().BeTrue();
        result.IsSufficient.Should().BeFalse();
        result.Message.Should().NotBeNullOrWhiteSpace();
    }
}
