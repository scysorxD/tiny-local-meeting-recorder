using FluentAssertions;
using LocalMeetingNotes.Core.Models;
using LocalMeetingNotes.Core.Services;
using LocalMeetingNotes.Core.Settings;

namespace LocalMeetingNotes.Core.Tests.Services;

public class ModelCatalogTests : IDisposable
{
    private readonly string _root;
    private readonly ModelCatalog _catalog = new();

    public ModelCatalogTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "LocalMeetingNotes.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Discover_missing_folder_returns_empty()
    {
        var missingFolder = Path.Combine(_root, "missing-models");

        _catalog.Discover(missingFolder).Should().BeEmpty();
    }

    [Fact]
    public void Discover_empty_folder_returns_empty()
    {
        var modelsFolder = CreateModelsFolder();

        _catalog.Discover(modelsFolder).Should().BeEmpty();
    }

    [Fact]
    public void Discover_finds_bin_files_sorted_by_name()
    {
        var modelsFolder = CreateModelsFolder();
        WriteModel(modelsFolder, "ggml-small.bin", sizeBytes: 1024);
        WriteModel(modelsFolder, "ggml-base.bin", sizeBytes: 2048);
        WriteModel(modelsFolder, "notes.txt", sizeBytes: 512);

        var discovered = _catalog.Discover(modelsFolder);

        discovered.Should().HaveCount(2);
        discovered.Select(m => m.FileName).Should().Equal("ggml-base.bin", "ggml-small.bin");
        discovered[0].FullPath.Should().Be(Path.Combine(modelsFolder, "ggml-base.bin"));
        discovered[0].SizeBytes.Should().Be(2048);
        discovered[0].IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateFile_zero_byte_file_is_invalid()
    {
        var modelsFolder = CreateModelsFolder();
        var path = WriteModel(modelsFolder, "empty.bin", sizeBytes: 0);

        var result = _catalog.ValidateFile(path);

        result.IsValid.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.ModelInvalid);
    }

    [Fact]
    public void ValidateFile_missing_file_is_invalid()
    {
        var path = Path.Combine(_root, "missing.bin");

        var result = _catalog.ValidateFile(path);

        result.IsValid.Should().BeFalse();
        result.ErrorCategory.Should().Be(ErrorCategory.ModelMissing);
    }

    [Fact]
    public void ValidateFile_existing_non_zero_file_is_valid()
    {
        var modelsFolder = CreateModelsFolder();
        var path = WriteModel(modelsFolder, "ggml-base.bin", sizeBytes: 4096);

        var result = _catalog.ValidateFile(path);

        result.IsValid.Should().BeTrue();
        result.ErrorCategory.Should().BeNull();
    }

    [Fact]
    public void ResolveSelectedModelPath_combines_models_folder_and_relative_filename()
    {
        var modelsFolder = CreateModelsFolder();
        WriteModel(modelsFolder, "ggml-base.bin", sizeBytes: 4096);
        var settings = new AppSettings
        {
            ModelsFolder = modelsFolder,
            SelectedModel = "ggml-base.bin",
        };

        var resolved = _catalog.ResolveSelectedModelPath(settings);

        resolved.Should().Be(Path.Combine(modelsFolder, "ggml-base.bin"));
    }

    [Fact]
    public void ResolveSelectedModelPath_missing_selection_returns_null()
    {
        var modelsFolder = CreateModelsFolder();
        WriteModel(modelsFolder, "ggml-base.bin", sizeBytes: 4096);
        var settings = new AppSettings
        {
            ModelsFolder = modelsFolder,
            SelectedModel = null!,
        };

        _catalog.ResolveSelectedModelPath(settings).Should().BeNull();
    }

    [Fact]
    public void ResolveSelectedModelPath_selected_file_not_present_returns_null()
    {
        var modelsFolder = CreateModelsFolder();
        var settings = new AppSettings
        {
            ModelsFolder = modelsFolder,
            SelectedModel = "ggml-base.bin",
        };

        _catalog.ResolveSelectedModelPath(settings).Should().BeNull();
    }

    [Fact]
    public void ResolveSelectedModelPath_uses_updated_models_folder()
    {
        var firstFolder = CreateModelsFolder("first");
        var secondFolder = CreateModelsFolder("second");
        WriteModel(firstFolder, "ggml-base.bin", sizeBytes: 4096);
        WriteModel(secondFolder, "ggml-small.bin", sizeBytes: 8192);

        var settings = new AppSettings
        {
            ModelsFolder = firstFolder,
            SelectedModel = "ggml-base.bin",
        };

        _catalog.ResolveSelectedModelPath(settings).Should().Be(Path.Combine(firstFolder, "ggml-base.bin"));

        settings = new AppSettings
        {
            ModelsFolder = secondFolder,
            SelectedModel = "ggml-small.bin",
        };

        _catalog.ResolveSelectedModelPath(settings).Should().Be(Path.Combine(secondFolder, "ggml-small.bin"));
    }

    [Fact]
    public void Discover_marks_zero_byte_models_as_invalid()
    {
        var modelsFolder = CreateModelsFolder();
        WriteModel(modelsFolder, "empty.bin", sizeBytes: 0);

        var discovered = _catalog.Discover(modelsFolder);

        discovered.Should().ContainSingle();
        discovered[0].IsValid.Should().BeFalse();
    }

    private string CreateModelsFolder(string? name = null)
    {
        var folder = Path.Combine(_root, name ?? "models");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string WriteModel(string modelsFolder, string fileName, long sizeBytes)
    {
        var path = Path.Combine(modelsFolder, fileName);
        using var stream = File.Create(path);
        if (sizeBytes > 0)
        {
            stream.SetLength(sizeBytes);
        }

        return path;
    }
}
