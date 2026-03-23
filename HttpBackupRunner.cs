using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace BackupService;

public class HttpBackupRunner(ILogger<HttpBackupRunner> logger)
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(30) // Long timeout for backup generation
    };

    private readonly HttpClient _unsecureHttpClient = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
    })
    {
        Timeout = TimeSpan.FromMinutes(30)
    };

    public async Task<bool> RunJobAsync(
        BackupJobOptions job,
        BackupOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(job.EndpointUrl))
            {
                logger.LogError("Backup '{name}' is missing EndpointUrl.", job.Name);
                return false;
            }

            var archiveDir = Path.Combine(job.LocalPath, job.Name);
            Directory.CreateDirectory(archiveDir);

            var client = job.AllowInvalidCertificate ? _unsecureHttpClient : _httpClient;

            var dbSuccess = false;
            var dbAttempted = false;
            if (job.IncludeDatabase)
            {
                dbAttempted = true;
                // 1. Run Database Backup
                dbSuccess = await ExecuteBackupActionAsync(client, job, "db", archiveDir, cancellationToken);
            }
            else
            {
                logger.LogInformation("Skipping database backup for '{name}' (IncludeDatabase = false).", job.Name);
            }

            var filesSuccess = false;
            var filesAttempted = false;
            if (job.IncludeFiles)
            {
                filesAttempted = true;
                // 2. Run Files Backup
                filesSuccess = await ExecuteBackupActionAsync(client, job, "files", archiveDir, cancellationToken);
            }
            else
            {
                logger.LogInformation("Skipping files backup for '{name}' (IncludeFiles = false).", job.Name);
            }

            // 3. Cleanup old local files
            CleanupLocalBackups(archiveDir, job.RetentionDays);

            // Return success if all ATTEMPTED backups succeeded
            var allSucceeded = (!dbAttempted || dbSuccess) && (!filesAttempted || filesSuccess);
            
            if (!allSucceeded)
            {
                logger.LogWarning("HTTP Backup '{name}' partially failed. DB: {db}, Files: {files}", 
                    job.Name, 
                    dbAttempted ? (dbSuccess ? "Success" : "Failed") : "Skipped",
                    filesAttempted ? (filesSuccess ? "Success" : "Failed") : "Skipped");
            }

            return allSucceeded;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "HTTP Backup '{name}' failed with unexpected error.", job.Name);
            return false;
        }
    }

    private async Task<bool> ExecuteBackupActionAsync(
        HttpClient client,
        BackupJobOptions job, 
        string action, 
        string archiveDir, 
        CancellationToken ct)
    {
        logger.LogInformation("Requesting {action} backup for '{name}'...", action, job.Name);

        var requestUrl = $"{job.EndpointUrl}?action={action}";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        
        if (!string.IsNullOrEmpty(job.ApiToken))
        {
            request.Headers.Add("X-Backup-Token", job.ApiToken);
        }

        using var response = await client.SendAsync(request, ct);
        var responseContent = await response.Content.ReadAsStringAsync(ct);
        
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Server returned error for {action}: {code} - {error}", action, response.StatusCode, responseContent);
            return false;
        }

        BackupServerResponse? result;
        try
        {
            result = System.Text.Json.JsonSerializer.Deserialize<BackupServerResponse>(responseContent);
        }
        catch (System.Text.Json.JsonException)
        {
            logger.LogError("Server returned invalid JSON for {action}. Response content: {content}", action, responseContent);
            return false;
        }

        if (result?.Status == "success" && !string.IsNullOrEmpty(result.DownloadUrl))
        {
            logger.LogInformation(
                "Backup {action} generated: {file} ({size} bytes). Downloading from: {url}", 
                action, 
                result.File, 
                result.Size,
                result.DownloadUrl);
            var downloadSuccess = await DownloadFileAsync(client, result.DownloadUrl, Path.Combine(archiveDir, result.File), ct);
            if (downloadSuccess)
            {
                logger.LogInformation("Downloaded {file} successfully.", result.File);
            }
            return downloadSuccess;
        }
        else
        {
            logger.LogError("Backup {action} failed: {msg}", action, result?.Message ?? "Unknown error");
            return false;
        }
    }

    private async Task<bool> DownloadFileAsync(HttpClient client, string url, string destinationPath, CancellationToken ct)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Failed to start download from {url}. Status: {code}, Message: {error}", url, response.StatusCode, errorContent);
            Console.WriteLine($"[ERROR] Failed to download {Path.GetFileName(destinationPath)}: {response.StatusCode}");
            return false;
        }

        var totalBytes = response.Content.Headers.ContentLength;
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        var totalRead = 0L;
        int read;
        var fileName = Path.GetFileName(destinationPath);

        logger.LogInformation("Starting download of {file} ({size})...", fileName, FormatBytes(totalBytes ?? 0));

        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, read, ct);
            totalRead += read;

            if (totalBytes.HasValue)
            {
                var progress = (double)totalRead / totalBytes.Value * 100;
                var progressString = $"\r[DOWNLOAD] {fileName}: {progress:F2}% ({FormatBytes(totalRead)} / {FormatBytes(totalBytes.Value)})";
                Console.Write(progressString);
            }
            else
            {
                Console.Write($"\r[DOWNLOAD] {fileName}: {FormatBytes(totalRead)} received");
            }
        }
        Console.WriteLine(); // Final newline after progress
        logger.LogInformation("Download of {file} completed successfully.", fileName);
        return true;
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        double dblBytes = bytes;
        while (i < suffixes.Length - 1 && bytes >= 1024)
        {
            i++;
            bytes /= 1024;
            dblBytes /= 1024;
        }
        return $"{dblBytes:F2} {suffixes[i]}";
    }

    private void CleanupLocalBackups(string dir, int days)
    {
        var files = Directory.GetFiles(dir);
        var now = DateTime.Now;
        foreach (var file in files)
        {
            var creationDate = File.GetCreationTime(file);
            if ((now - creationDate).TotalDays > days)
            {
                try { File.Delete(file); } catch { /* ignore */ }
            }
        }
    }

    private class BackupServerResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
        
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
        
        [JsonPropertyName("file")]
        public string File { get; set; } = string.Empty;
        
        [JsonPropertyName("size")]
        public long Size { get; set; }
        
        [JsonPropertyName("download_url")]
        public string DownloadUrl { get; set; } = string.Empty;
    }
}
