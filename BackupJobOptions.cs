namespace BackupService;

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
}
