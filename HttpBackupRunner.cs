using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace BackupService;

public class HttpBackupRunner(ILogger<HttpBackupRunner> logger)
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(30) // Long timeout for backup generation
    };

    public async Task RunJobAsync(
        BackupJobOptions job,
        BackupOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(job.EndpointUrl))
            {
                logger.LogError("Backup '{name}' is missing EndpointUrl.", job.Name);
                return;
            }

            var archiveDir = Path.Combine(job.LocalPath, job.Name);
            Directory.CreateDirectory(archiveDir);

            // 1. Run Database Backup
            await ExecuteBackupActionAsync(job, "db", archiveDir, cancellationToken);

            // 2. Run Files Backup
            await ExecuteBackupActionAsync(job, "files", archiveDir, cancellationToken);

            // 3. Cleanup old local files
            CleanupLocalBackups(archiveDir, job.RetentionDays);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "HTTP Backup '{name}' failed.", job.Name);
        }
    }

    private async Task ExecuteBackupActionAsync(
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

        using var response = await _httpClient.SendAsync(request, ct);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Server returned error for {action}: {code} - {error}", action, response.StatusCode, error);
            return;
        }

        var result = await response.Content.ReadFromJsonAsync<BackupServerResponse>(cancellationToken: ct);

        if (result?.Status == "success" && !string.IsNullOrEmpty(result.DownloadUrl))
        {
            logger.LogInformation("Backup {action} generated: {file} ({size} bytes). Downloading...", action, result.File, result.Size);
            await DownloadFileAsync(result.DownloadUrl, Path.Combine(archiveDir, result.File), ct);
            logger.LogInformation("Downloaded {file} successfully.", result.File);
        }
        else
        {
            logger.LogError("Backup {action} failed: {msg}", action, result?.Message ?? "Unknown error");
        }
    }

    private async Task DownloadFileAsync(string url, string destinationPath, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
        await stream.CopyToAsync(fileStream, ct);
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
