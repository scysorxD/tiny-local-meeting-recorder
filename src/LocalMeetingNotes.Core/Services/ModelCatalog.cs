using LocalMeetingNotes.Core.Interfaces;
using LocalMeetingNotes.Core.Models;
using LocalMeetingNotes.Core.Settings;

namespace LocalMeetingNotes.Core.Services;

public sealed class ModelCatalog : IModelCatalog
{
    public IReadOnlyList<WhisperModelInfo> Discover(string modelsFolder)
    {
        if (string.IsNullOrWhiteSpace(modelsFolder) || !Directory.Exists(modelsFolder))
        {
            return Array.Empty<WhisperModelInfo>();
        }

        return Directory.EnumerateFiles(modelsFolder, "*.bin", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Select(CreateModelInfo)
            .ToList();
    }

    public ModelValidationResult ValidateFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Invalid(ErrorCategory.ModelMissing, "Model path is empty.");
        }

        if (!File.Exists(path))
        {
            return Invalid(ErrorCategory.ModelMissing, "Model file does not exist.");
        }

        var sizeBytes = new FileInfo(path).Length;
        if (sizeBytes <= 0)
        {
            return Invalid(ErrorCategory.ModelInvalid, "Model file is empty.");
        }

        return new ModelValidationResult(true);
    }

    public string? ResolveSelectedModelPath(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SelectedModel)
            || string.IsNullOrWhiteSpace(settings.ModelsFolder))
        {
            return null;
        }

        var fileName = Path.GetFileName(settings.SelectedModel);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var path = Path.Combine(settings.ModelsFolder, fileName);
        return ValidateFile(path).IsValid ? path : null;
    }

    private WhisperModelInfo CreateModelInfo(string path)
    {
        var fileInfo = new FileInfo(path);
        var validation = ValidateFile(path);

        return new WhisperModelInfo(
            Path.GetFileName(path),
            path,
            fileInfo.Length,
            validation.IsValid);
    }

    private static ModelValidationResult Invalid(ErrorCategory errorCategory, string message) =>
        new(false, errorCategory, message);
}
