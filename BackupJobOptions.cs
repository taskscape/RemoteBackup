namespace BackupService;

public enum BackupMode
{
    /// <summary>
    /// Mirror the entire remote directory locally and then produce a dated
    /// zip archive. This is the default (formerly "Full").
    /// </summary>
    Local,

    /// <summary>
    /// **Obsolete:** kept for compatibility with earlier configurations. It
    /// behaves identically to <see cref="Local" />.
    /// </summary>
    [Obsolete("Use Local instead")] 
    ArchivesOnly = Local,

    /// <summary>
    /// Request an archive from a remote endpoint and download it. Supports
    /// sequential triggers when multiple URLs are provided (formerly
    /// "RemoteZip").
    /// </summary>
    Remote
}

public class BackupJobOptions
{
    public string Name { get; set; } = "Backup";
    public string BackupType { get; set; } = "FTP"; // FTP or HTTP
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 21;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string RemotePath { get; set; } = "/";
    public string LocalPath { get; set; } = string.Empty;
    public string Encryption { get; set; } = "Explicit";
    public bool Passive { get; set; } = true;
    public bool AllowInvalidCertificate { get; set; }
    
    // HTTP specific
    public string EndpointUrl { get; set; } = string.Empty;
    public string ApiToken { get; set; } = string.Empty;

    public int? TimeoutMinutes { get; set; }
    public int? HistoryCopies { get; set; }
    public int RetentionDays { get; set; } = 7;
    public int OperationTimeoutMinutes { get; set; } = 10;
    public int CompletionTimeoutMinutes { get; set; } = 180;
    public BackupMode Mode { get; set; } = BackupMode.Local;
    /// <summary>
    /// Single HTTP endpoint to request a server-side zip archive.
    /// kept for backwards compatibility; if <see cref="RemoteTriggerUrls"/> is
    /// non-empty this value will be ignored.
    /// </summary>
    public string? RemoteTriggerUrl { get; set; }

    /// <summary>
    /// New: a list of HTTP endpoints that should be triggered one after another.
    /// Each endpoint may return a location (or JSON containing "location") of
    /// an archive to download. This allows a sequence such as "files backup",
    /// "db dump" etc. The runner will iterate through the list sequentially.
    /// </summary>
    public List<string>? RemoteTriggerUrls { get; set; }

    public int RemoteTriggerPollIntervalSeconds { get; set; } = 5;

    public int RemoteTriggerTimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// When downloading archives or polling the trigger URL, how many retries
    /// should be attempted upon transient HTTP failures. The policy is
    /// exponential backoff with a delay of <see cref="HttpRetryDelaySeconds" />
    /// between attempts.
    /// </summary>
    public int HttpRetryCount { get; set; } = 3;

    /// <summary>
    /// The base delay in seconds to wait between retry attempts. Actual wait is
    /// this value multiplied by the retry attempt number (i.e. 1×, 2×, etc).
    /// </summary>
    public int HttpRetryDelaySeconds { get; set; } = 5;
}