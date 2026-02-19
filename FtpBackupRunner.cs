using FluentFTP;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Linq;
using Polly;

namespace BackupService;

public class FtpBackupRunner(ILogger<FtpBackupRunner> logger)
{
    public async Task RunJobAsync(
        BackupJobOptions job,
        BackupOptions options,
        CancellationToken cancellationToken)
    {
        using var completionCts =
            new CancellationTokenSource(TimeSpan.FromMinutes(job.CompletionTimeoutMinutes));

        using var linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                completionCts.Token);

        var completionToken = linkedCts.Token;

        try
        {
            if (string.IsNullOrWhiteSpace(job.Host))
            {
                logger.LogError("Backup '{name}' is missing Host.", job.Name);
                return;
            }

            if (string.IsNullOrWhiteSpace(job.LocalPath))
            {
                logger.LogError("Backup '{name}' is missing LocalPath.", job.Name);
                return;
            }

            if (!IsLocalDrivePath(job.LocalPath))
            {
                logger.LogError(
                    "Backup '{name}' LocalPath '{path}' is not a local drive path.",
                    job.Name,
                    job.LocalPath);
                return;
            }

            var archiveDir = job.LocalPath;
            Directory.CreateDirectory(archiveDir);

            var remotePath = string.IsNullOrWhiteSpace(job.RemotePath)
                ? "/"
                : job.RemotePath;

            using var client = new FtpClient(job.Host, job.Username, job.Password, job.Port);

            client.Config.EncryptionMode = ParseEncryptionMode(job.Encryption);
            client.Config.DataConnectionType = job.Passive
                ? FtpDataConnectionType.PASV
                : FtpDataConnectionType.PORT;
            client.Config.DataConnectionEncryption = true;
            client.Config.ConnectTimeout = 15000;
            client.Config.ReadTimeout = 15000;
            client.Config.DataConnectionConnectTimeout = 15000;
            client.Config.DataConnectionReadTimeout = 15000;
            client.Config.ValidateAnyCertificate = job.AllowInvalidCertificate;

            if (!job.AllowInvalidCertificate)
            {
                client.ValidateCertificate += (_, e) =>
                {
                    if (e.PolicyErrors != System.Net.Security.SslPolicyErrors.None)
                    {
                        logger.LogError(
                            "Certificate validation failed for backup '{name}': {errors}",
                            job.Name,
                            e.PolicyErrors);
                    }
                };
            }

            logger.LogInformation(
                "Connecting to {host}:{port} for backup '{name}'.",
                job.Host,
                job.Port,
                job.Name);

            client.Connect();
            logger.LogInformation(
                "Connected to {host}.",
                job.Host);

            if (job.Mode == BackupMode.Local)
            {
                // local mode encompasses the previous Full and ArchivesOnly
                // behaviours; for backwards compatibility a job originally set
                // to ArchivesOnly will still be interpreted as Local and will
                // download any .zip files present (same as before).
                logger.LogInformation(
                    "Local mode: mirroring and/or downloading archives from {remotePath} for '{name}'.",
                    remotePath,
                    job.Name);

                var listings = client.GetListing(remotePath);
                var zipItems = listings
                    .Where(i => i.Type == FtpObjectType.File &&
                                i.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (!zipItems.Any())
                {
                    logger.LogInformation(
                        "No .zip files found in {remotePath} for backup '{name}'.",
                        remotePath,
                        job.Name);
                }

                int downloaded = 0;
                int failed = 0;

                foreach (var item in zipItems)
                {
                    completionToken.ThrowIfCancellationRequested();

                    var localFile = Path.Combine(archiveDir, item.Name);

                    logger.LogInformation(
                        "Downloading archive {remoteName} to {localFile} for '{name}'.",
                        item.Name,
                        localFile,
                        job.Name);

                    var status = client.DownloadFile(
                        localFile,
                        item.FullName,
                        FtpLocalExists.Overwrite,
                        FtpVerify.None);

                    if (status == FluentFTP.FtpStatus.Success)
                    {
                        downloaded++;

                        if (item.Modified != DateTime.MinValue)
                        {
                            File.SetLastWriteTime(localFile, item.Modified);
                            File.SetCreationTime(localFile, item.Modified);
                        }

                        logger.LogInformation("Successfully downloaded {name}.", item.Name);
                    }
                    else
                    {
                        failed++;
                        logger.LogWarning("Failed to download {name}.", item.Name);
                    }
                }

                logger.LogInformation(
                    "ArchivesOnly backup completed for '{name}'. Downloaded {downloaded} archive(s), {failed} failed.",
                    job.Name,
                    downloaded,
                    failed);
            }
            else if (job.Mode == BackupMode.Remote)
            {
                logger.LogInformation(
                    "Remote mode: attempting remote trigger or downloading .zip from {remotePath} for '{name}'.",
                    remotePath,
                    job.Name);

                var downloadedAny = false;

                // build a list of trigger URLs to process sequentially
                var triggers = new List<string>();
                if (job.RemoteTriggerUrls != null && job.RemoteTriggerUrls.Any())
                {
                    triggers.AddRange(job.RemoteTriggerUrls.Where(u => !string.IsNullOrWhiteSpace(u))!);
                }
                else if (!string.IsNullOrWhiteSpace(job.RemoteTriggerUrl))
                {
                    triggers.Add(job.RemoteTriggerUrl!);
                }

                if (triggers.Count > 0)
                {
                    using var http = new HttpClient();
                    http.Timeout = TimeSpan.FromSeconds(Math.Max(30, Math.Min(job.RemoteTriggerTimeoutSeconds, 3600)));

                    // simple retry policy for transient http failures
                    var retryPolicy = Policy
                        .Handle<Exception>()
                        .WaitAndRetryAsync(job.HttpRetryCount, attempt =>
                            TimeSpan.FromSeconds(job.HttpRetryDelaySeconds * attempt));

                    foreach (var trigger in triggers)
                    {
                        if (downloadedAny && triggers.Count == 1)
                        {
                            // already have something and only one trigger => nothing to do
                        }

                        try
                        {
                            logger.LogInformation("Triggering remote zip at {url} for '{name}'.", trigger, job.Name);
                            using var resp = await retryPolicy.ExecuteAsync(() => http.PostAsync(trigger, null, completionToken));

                            string? location = resp.Headers.Location?.ToString();
                            if (string.IsNullOrWhiteSpace(location))
                            {
                                try
                                {
                                    var body = await resp.Content.ReadAsStringAsync(completionToken);
                                    if (!string.IsNullOrWhiteSpace(body))
                                    {
                                        try
                                        {
                                            using var doc = JsonDocument.Parse(body);
                                            if (doc.RootElement.TryGetProperty("location", out var locEl))
                                            {
                                                location = locEl.GetString();
                                            }
                                        }
                                        catch
                                        {
                                            var trimmed = body.Trim();
                                            if (Uri.IsWellFormedUriString(trimmed, UriKind.Absolute))
                                            {
                                                location = trimmed;
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    logger.LogWarning(ex, "Failed to read trigger response body.");
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(location))
                            {
                                logger.LogInformation("Polling {location} for archive availability.", location);
                                var sw = System.Diagnostics.Stopwatch.StartNew();
                                while (sw.Elapsed.TotalSeconds < job.RemoteTriggerTimeoutSeconds)
                                {
                                    completionToken.ThrowIfCancellationRequested();

                                    try
                                    {
                                        using var headReq = new HttpRequestMessage(HttpMethod.Head, location);
                                        using var headResp = await retryPolicy.ExecuteAsync(() => http.SendAsync(headReq, completionToken));
                                        if (headResp.IsSuccessStatusCode)
                                        {
                                            var uri = new Uri(location);
                                            var fileName = Path.GetFileName(uri.LocalPath);
                                            if (string.IsNullOrWhiteSpace(fileName))
                                            {
                                                fileName = DateTime.Now.ToString("yyyy-MM-dd") + ".zip";
                                            }

                                            var localFile = Path.Combine(archiveDir, fileName);
                                            logger.LogInformation("Downloading remote archive {loc} to {localFile} for '{name}'.", location, localFile, job.Name);
                                            using var getResp = await retryPolicy.ExecuteAsync(() => http.GetAsync(location, HttpCompletionOption.ResponseHeadersRead, completionToken));
                                            getResp.EnsureSuccessStatusCode();
                                            await using var stream = await getResp.Content.ReadAsStreamAsync(completionToken);
                                            await using var fs = File.Create(localFile);
                                            await stream.CopyToAsync(fs, cancellationToken: completionToken);

                                            downloadedAny = true;
                                            logger.LogInformation("Successfully downloaded remote archive {name}.", fileName);
                                            break;
                                        }
                                    }
                                    catch (OperationCanceledException) { throw; }
                                    catch (Exception) { /* ignore and retry */ }

                                    await Task.Delay(TimeSpan.FromSeconds(job.RemoteTriggerPollIntervalSeconds), completionToken);
                                }
                            }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Remote trigger failed for '{name}' url {trigger}", job.Name, trigger);
                        }

                        // if we downloaded something we could continue to next trigger as well
                        // or break early if we only wanted one archive. behaviour is sequential.
                    }
                }

                if (!downloadedAny)
                {
                    logger.LogInformation("RemoteZip: falling back to checking .zip files on FTP.");

                    var listings = client.GetListing(remotePath);
                    var zipItems = listings
                        .Where(i => i.Type == FtpObjectType.File &&
                                    i.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (!zipItems.Any())
                    {
                        logger.LogInformation("No .zip files found in {remotePath} for backup '{name}'. Falling back to Full.", remotePath, job.Name);
                    }
                    else
                    {
                        int downloaded = 0;
                        int failed = 0;

                        foreach (var item in zipItems)
                        {
                            completionToken.ThrowIfCancellationRequested();

                            var localFile = Path.Combine(archiveDir, item.Name);

                            logger.LogInformation(
                                "Downloading archive {remoteName} to {localFile} for '{name}'.",
                                item.Name,
                                localFile,
                                job.Name);

                            var status = client.DownloadFile(
                                localFile,
                                item.FullName,
                                FtpLocalExists.Overwrite,
                                FtpVerify.None);

                            if (status == FluentFTP.FtpStatus.Success)
                            {
                                downloaded++;

                                if (item.Modified != DateTime.MinValue)
                                {
                                    File.SetLastWriteTime(localFile, item.Modified);
                                    File.SetCreationTime(localFile, item.Modified);
                                }

                                logger.LogInformation("Successfully downloaded {name}.", item.Name);
                                downloadedAny = true;
                            }
                            else
                            {
                                failed++;
                                logger.LogWarning("Failed to download {name}.", item.Name);
                            }
                        }

                        logger.LogInformation(
                            "RemoteZip (FTP) completed for '{name}'. Downloaded {downloaded} archive(s), {failed} failed.",
                            job.Name,
                            downloaded,
                            failed);
                    }
                }

                if (!downloadedAny)
                {
                    logger.LogInformation("RemoteZip: falling back to Full mode for '{name}'.", job.Name);

                    var tempDir = Path.Combine(job.LocalPath, "temp", job.Host);
                    Directory.CreateDirectory(tempDir);

                    logger.LogInformation(
                        "Full mode: mirroring {remotePath} to temp directory.",
                        remotePath);

                    var results = client.DownloadDirectory(
                        tempDir,
                        remotePath,
                        FtpFolderSyncMode.Mirror,
                        FtpLocalExists.Overwrite,
                        FtpVerify.None);

                    LogResults(job.Name, results);

                    string zipPath = Path.Combine(archiveDir, DateTime.Now.ToString("yyyy-MM-dd") + ".zip");

                    if (File.Exists(zipPath))
                    {
                        File.Delete(zipPath);
                    }

                    ZipFile.CreateFromDirectory(tempDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, recursive: true);
                    }
                }
            }
            else
            {
                var tempDir = Path.Combine(job.LocalPath, "temp", job.Host);
                Directory.CreateDirectory(tempDir);

                logger.LogInformation(
                    "Full mode: mirroring {remotePath} to temp directory.",
                    remotePath);

                var results = client.DownloadDirectory(
                    tempDir,
                    remotePath,
                    FtpFolderSyncMode.Mirror,
                    FtpLocalExists.Overwrite,
                    FtpVerify.None);

                LogResults(job.Name, results);

                string zipPath = Path.Combine(archiveDir, DateTime.Now.ToString("yyyy-MM-dd") + ".zip");

                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }

                ZipFile.CreateFromDirectory(tempDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }

            foreach (var file in Directory.GetFiles(archiveDir, "*.zip"))
            {
                var creationDate = File.GetCreationTime(file);
                if ((DateTime.Now - creationDate).TotalDays > job.RetentionDays)
                {
                    try
                    {
                        File.Delete(file);
                        logger.LogInformation(
                            "Deleted old archive {file} (older than {days} days) for '{name}'.",
                            Path.GetFileName(file),
                            job.RetentionDays,
                            job.Name);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to delete old archive {file}.", file);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Backup '{name}' cancelled.", job.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Backup '{name}' failed.", job.Name);
        }
    }

    private static bool IsLocalDrivePath(string path)
    {
        if (!Path.IsPathRooted(path))
        {
            return false;
        }

        var root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        if (root.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return false;
        }

        return root.Contains(':');
    }

    private static FtpEncryptionMode ParseEncryptionMode(string? mode)
    {
        if (Enum.TryParse<FtpEncryptionMode>(mode, true, out var parsed))
        {
            return parsed;
        }

        return FtpEncryptionMode.Explicit;
    }

    private void LogResults(string jobName, List<FtpResult> results)
    {
        var failed = results.Count(r => !r.IsSuccess);
        if (failed == 0)
        {
            logger.LogInformation(
                "Backup '{name}' downloaded {count} items.",
                jobName,
                results.Count);
            return;
        }

        logger.LogWarning(
            "Backup '{name}' downloaded {count} items with {failed} failures.",
            jobName,
            results.Count,
            failed);

        foreach (var result in results.Where(r => !r.IsSuccess))
        {
            logger.LogWarning(
                "Backup '{name}' failed item {item} with exception {message}.",
                jobName,
                result.Name,
                result.Exception?.Message);
        }
    }

    public static async Task CopyDirectory(
        string sourceDir,
        string targetDir,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetFile = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, targetFile, overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directoryName = Path.GetFileName(directory);
            var targetSubdir = Path.Combine(targetDir, directoryName);
            await CopyDirectory(directory, targetSubdir, cancellationToken);
        }
    }

    private void CleanupHistory(
        string historyRoot,
        BackupJobOptions job,
        BackupOptions options)
    {
        var keep = job.HistoryCopies ?? options.HistoryCopies;
        if (keep <= 0)
        {
            return;
        }

        var snapshots = new DirectoryInfo(historyRoot)
            .GetDirectories()
            .OrderByDescending(d => d.Name)
            .ToList();

        if (snapshots.Count <= keep)
        {
            return;
        }

        foreach (var old in snapshots.Skip(keep))
        {
            try
            {
                old.Delete(recursive: true);
                logger.LogInformation(
                    "Deleted old history snapshot {snapshot} for backup '{name}'.",
                    old.FullName,
                    job.Name);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to delete old history snapshot {snapshot} for backup '{name}'.",
                    old.FullName,
                    job.Name);
            }
        }
    }
}