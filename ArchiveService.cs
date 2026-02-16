using System.IO.Compression;

namespace BackupService;

public class ArchiveService(ILogger<ArchiveService> logger)
{
    public async Task CompressDirectoryAsync(string sourceDir, string archPath, CancellationToken cancellationToken)
    {
        if (File.Exists(archPath))
        {
            File.Delete(archPath);
        }
        logger.LogInformation("Starting compression to file: {archPath}", archPath);

        await Task.Run(() =>
        {
            ZipFile.CreateFromDirectory(sourceDir, archPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        }, cancellationToken);
        logger.LogInformation("Compression completed successfully");
    }
}