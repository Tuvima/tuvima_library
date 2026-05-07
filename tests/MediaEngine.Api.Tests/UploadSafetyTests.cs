using MediaEngine.Api.Endpoints;
using MediaEngine.Ingestion.Models;
using MediaEngine.Storage.Models;
using Microsoft.AspNetCore.Http;

namespace MediaEngine.Api.Tests;

public sealed class UploadSafetyTests
{
    private static readonly IReadOnlyList<MediaTypeDefinition> MediaTypes = MediaTypeConfiguration.DefaultTypes();

    [Fact]
    public void CreatePlan_RejectsUnsupportedMediaType()
    {
        using var temp = new TempDirectory();

        var plan = UploadSafety.CreatePlan(
            temp.Path,
            "Documents",
            "notes.epub",
            1024,
            MediaTypes,
            new IngestionOptions());

        Assert.False(plan.IsValid);
        Assert.Equal(StatusCodes.Status400BadRequest, ExecuteStatusCode(plan.Error!));
    }

    [Fact]
    public void CreatePlan_RejectsUnsupportedExtension()
    {
        using var temp = new TempDirectory();

        var plan = UploadSafety.CreatePlan(
            temp.Path,
            "Books",
            "notes.exe",
            1024,
            MediaTypes,
            new IngestionOptions());

        Assert.False(plan.IsValid);
        Assert.Equal(StatusCodes.Status400BadRequest, ExecuteStatusCode(plan.Error!));
    }

    [Fact]
    public void CreatePlan_RejectsOversizedFile()
    {
        using var temp = new TempDirectory();

        var plan = UploadSafety.CreatePlan(
            temp.Path,
            "Movies",
            "movie.mkv",
            1024,
            MediaTypes,
            new IngestionOptions { MaxUploadSizeBytes = 512 });

        Assert.False(plan.IsValid);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, ExecuteStatusCode(plan.Error!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../book.epub")]
    [InlineData("folder/book.epub")]
    public void CreatePlan_RejectsBadFilename(string fileName)
    {
        using var temp = new TempDirectory();

        var plan = UploadSafety.CreatePlan(
            temp.Path,
            "Books",
            fileName,
            1024,
            MediaTypes,
            new IngestionOptions());

        Assert.False(plan.IsValid);
        Assert.Equal(StatusCodes.Status400BadRequest, ExecuteStatusCode(plan.Error!));
    }

    [Fact]
    public void CreatePlan_AcceptsAllowedFileUsingTempDirectory()
    {
        using var temp = new TempDirectory();

        var plan = UploadSafety.CreatePlan(
            temp.Path,
            "Books",
            "book.epub",
            1024,
            MediaTypes,
            new IngestionOptions());

        Assert.True(plan.IsValid);
        Assert.Equal("Books", plan.CanonicalMediaType);
        Assert.EndsWith(Path.Combine("Books", "book.epub"), plan.TargetPath);
    }

    private static int? ExecuteStatusCode(IResult result)
    {
        var statusCodeProperty = result.GetType().GetProperty("StatusCode");
        return statusCodeProperty?.GetValue(result) as int?;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tuvima-upload-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
