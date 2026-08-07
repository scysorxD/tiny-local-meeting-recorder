using FluentAssertions;
using LocalMeetingNotes.Core;

namespace LocalMeetingNotes.Core.Tests;

public class ScaffoldSmokeTests
{
    [Fact]
    public void Core_assembly_loads()
    {
        typeof(AssemblyMarker).Assembly.GetName().Name.Should().Be("LocalMeetingNotes.Core");
    }
}
