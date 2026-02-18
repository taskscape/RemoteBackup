using System.IO.Compression;
using System.Threading.Channels;

public class ZipWorker : BackgroundService
{
    private readonly Channel<ZipRequest> _channel;
    private readonly ILogger<ZipWorker> _logger;

    public ZipWorker(Channel<ZipRequest> channel, ILogger<ZipWorker> logger)
    {
        _channel = channel;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var req in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                _logger.LogInformation("ZipWorker: creating archive {file}", req.OutputPath);

                if (File.Exists(req.OutputPath) && !req.Overwrite)
                {
                    _logger.LogInformation("ZipWorker: {file} already exists and overwrite=false, skipping.", req.OutputPath);
                    continue;
                }

                var tmp = req.OutputPath + ".tmp";
                if (File.Exists(tmp)) File.Delete(tmp);

                ZipFile.CreateFromDirectory(req.SourcePath, tmp, CompressionLevel.Optimal, includeBaseDirectory: false);

                if (File.Exists(req.OutputPath)) File.Delete(req.OutputPath);
                File.Move(tmp, req.OutputPath);

                _logger.LogInformation("ZipWorker: created archive {file}", req.OutputPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ZipWorker: failed to create archive {file}", req.OutputPath);
            }
        }
    }
}
