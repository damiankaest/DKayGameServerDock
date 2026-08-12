using System.IO.Compression;
using DKay.GameServerDock.Infrastructure.Installation;

namespace DKay.GameServerDock.Tests;

public sealed class SafeZipExtractorTests
{
    [Fact]
    public async Task ExtractAsync_ExtractsNormalPackage()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var archivePath = Path.Combine(root, "package.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("addons/example/plugin.dll");
                await using var writer = new StreamWriter(entry.Open());
                await writer.WriteAsync("plugin");
            }

            var destination = Path.Combine(root, "destination");
            await SafeZipExtractor.ExtractAsync(archivePath, destination, CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(destination, "addons", "example", "plugin.dll")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ExtractAsync_RejectsPathTraversal()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var archivePath = Path.Combine(root, "package.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../outside.dll");
                await using var writer = new StreamWriter(entry.Open());
                await writer.WriteAsync("payload");
            }

            var destination = Path.Combine(root, "destination");
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                SafeZipExtractor.ExtractAsync(archivePath, destination, CancellationToken.None));
            Assert.False(File.Exists(Path.Combine(root, "outside.dll")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTemporaryRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dkay-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
