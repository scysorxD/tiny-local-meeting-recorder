using FluentAssertions;
using LocalMeetingNotes.Core;

namespace LocalMeetingNotes.IntegrationTests;

public class ScaffoldSmokeTests
{
    [Fact]
    public void Solution_references_core()
    {
        var coreAssembly = typeof(AssemblyMarker).Assembly;
        coreAssembly.GetName().Name.Should().Be("LocalMeetingNotes.Core");
    }
}
