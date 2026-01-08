namespace BackupService;

public class BackupJobOptions
{
    public string Name { get; set; } = "Backup";
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 21;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string RemotePath { get; set; } = "/";
    public string LocalPath { get; set; } = string.Empty;
    public string Encryption { get; set; } = "Explicit";
    public bool Passive { get; set; } = true;
    public bool AllowInvalidCertificate { get; set; }
    public int? TimeoutMinutes { get; set; }
    public int? HistoryCopies { get; set; }
}
