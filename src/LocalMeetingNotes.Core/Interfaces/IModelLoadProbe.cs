using LocalMeetingNotes.Core.Models;

namespace LocalMeetingNotes.Core.Interfaces;

public interface IModelLoadProbe
{
    Task<ModelValidationResult> ProbeLoadAsync(string absolutePath, CancellationToken ct = default);
}
