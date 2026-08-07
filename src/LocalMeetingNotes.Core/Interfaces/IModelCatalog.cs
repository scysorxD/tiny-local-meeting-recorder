using LocalMeetingNotes.Core.Models;
using LocalMeetingNotes.Core.Settings;

namespace LocalMeetingNotes.Core.Interfaces;

public interface IModelCatalog
{
    IReadOnlyList<WhisperModelInfo> Discover(string modelsFolder);

    ModelValidationResult ValidateFile(string path);

    string? ResolveSelectedModelPath(AppSettings settings);
}
