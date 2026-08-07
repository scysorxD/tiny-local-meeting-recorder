namespace LocalMeetingNotes.Core.Models;

public sealed record ModelValidationResult(
    bool IsValid,
    ErrorCategory? ErrorCategory = null,
    string? Message = null);
