using System.IO;
using LocalMeetingNotes.Core.Interfaces;
using LocalMeetingNotes.Core.Models;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace LocalMeetingNotes.App.Transcription;

public sealed class WhisperModelLoadProbe : IModelLoadProbe
{
    public Task<ModelValidationResult> ProbeLoadAsync(
        string absolutePath,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
        {
            return Task.FromResult(Invalid(ErrorCategory.ModelMissing, "Model file does not exist."));
        }

        if (!string.Equals(Path.GetExtension(absolutePath), ".bin", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Invalid(ErrorCategory.ModelInvalid, "Model file must have a .bin extension."));
        }

        if (new FileInfo(absolutePath).Length == 0)
        {
            return Task.FromResult(Invalid(ErrorCategory.ModelInvalid, "Model file is empty."));
        }

        try
        {
            RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cpu];
            using var factory = WhisperFactory.FromPath(Path.GetFullPath(absolutePath));
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new ModelValidationResult(true));
        }
        catch (WhisperModelLoadException exception)
        {
            return Task.FromResult(Invalid(ErrorCategory.ModelInvalid, exception.Message));
        }
        catch (Exception exception) when (IsNativeRuntimeLoadFailure(exception))
        {
            return Task.FromResult(Invalid(ErrorCategory.NativeRuntimeLoadFailure, exception.Message));
        }
        catch (Exception exception)
        {
            return Task.FromResult(Invalid(ErrorCategory.ModelInvalid, exception.Message));
        }
    }

    private static ModelValidationResult Invalid(ErrorCategory errorCategory, string message) =>
        new(false, errorCategory, message);

    private static bool IsNativeRuntimeLoadFailure(Exception exception) =>
        exception is DllNotFoundException
            or BadImageFormatException
            or EntryPointNotFoundException
            or TypeInitializationException
            or FileLoadException;
}
