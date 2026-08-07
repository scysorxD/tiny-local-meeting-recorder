using FluentAssertions;
using LocalMeetingNotes.Core.Services;

namespace LocalMeetingNotes.Core.Tests.Services;

public class FilenameSanitizerTests
{
    [Theory]
    [InlineData("Payment API", "Payment API")]
    [InlineData("  Payment API  ", "Payment API")]
    [InlineData("Pay:ment/API\\Test?", "Pay_ment_API_Test_")]
    [InlineData("Bad<>\"|*", "Bad_____")]
    [InlineData("Réunion café", "Réunion café")]
    public void Sanitize_replaces_invalid_chars_and_trims(string input, string expected)
    {
        FilenameSanitizer.Sanitize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Sanitize_empty_or_whitespace_returns_untitled(string input)
    {
        FilenameSanitizer.Sanitize(input).Should().Be("Untitled Meeting");
    }

    [Fact]
    public void Sanitize_long_title_truncates_to_max_filename_length()
    {
        var longTitle = new string('A', 300);
        var sanitized = FilenameSanitizer.Sanitize(longTitle);

        sanitized.Length.Should().BeLessThanOrEqualTo(FilenameSanitizer.MaxSanitizedTitleLength);
        sanitized.Should().EndWith("A");
    }

    [Fact]
    public void BuildNoteFileName_formats_date_time_and_title()
    {
        var startedAt = new DateTimeOffset(2026, 8, 7, 11, 0, 14, TimeSpan.Zero);

        var fileName = FilenameSanitizer.BuildNoteFileName(startedAt, "Payment API");

        fileName.Should().Be("2026-08-07_1100 - Payment API.md");
    }

    [Fact]
    public void BuildNoteFileName_sanitizes_title()
    {
        var startedAt = new DateTimeOffset(2026, 8, 7, 11, 0, 0, TimeSpan.Zero);

        var fileName = FilenameSanitizer.BuildNoteFileName(startedAt, "Pay:ment/API");

        fileName.Should().Be("2026-08-07_1100 - Pay_ment_API.md");
    }

    [Fact]
    public void BuildNoteFileName_includes_disambiguator_when_provided()
    {
        var startedAt = new DateTimeOffset(2026, 8, 7, 11, 0, 0, TimeSpan.Zero);

        var fileName = FilenameSanitizer.BuildNoteFileName(startedAt, "Payment API", "a1b2c3d4");

        fileName.Should().Be("2026-08-07_1100 - Payment API (a1b2c3d4).md");
    }
}
