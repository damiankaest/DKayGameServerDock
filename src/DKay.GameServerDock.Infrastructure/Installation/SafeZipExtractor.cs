using System.IO.Compression;

namespace DKay.GameServerDock.Infrastructure.Installation;

public static class SafeZipExtractor
{
    public const long MaximumUncompressedBytes = 512L * 1024 * 1024;
    public const int MaximumEntries = 10_000;

    public static async Task ExtractAsync(string archivePath, string destinationRoot, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(destinationRoot) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);

        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaximumEntries)
        {
            throw new InvalidDataException("The mod archive contains too many files.");
        }

        long totalLength = 0;
        var targets = new List<(ZipArchiveEntry Entry, string Target)>();
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalLength = checked(totalLength + entry.Length);
            if (totalLength > MaximumUncompressedBytes)
            {
                throw new InvalidDataException("The expanded mod archive is larger than 512 MB.");
            }

            var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixMode == 0xA000)
            {
                throw new InvalidDataException("Symbolic links are not allowed in mod archives.");
            }

            var relative = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(relative) || relative.StartsWith('/') || relative.Contains(':'))
            {
                throw new InvalidDataException("The mod archive contains an invalid path.");
            }

            var target = Path.GetFullPath(Path.Combine(root, relative));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The mod archive attempted to write outside the CS2 staging directory.");
            }

            targets.Add((entry, target));
        }

        foreach (var (entry, target) in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, cancellationToken);
        }
    }
}
