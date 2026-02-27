using System.IO.Compression;
using System.Threading.Channels;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(Channel.CreateUnbounded<ZipRequest>());
builder.Services.AddHostedService<ZipWorker>();

builder.Services.AddLogging();
builder.Services.AddCors();

var app = builder.Build();

var archivesDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "archives");
Directory.CreateDirectory(archivesDir);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")),
    RequestPath = ""
});

app.MapPost("/trigger", async (TriggerDto dto, Channel<ZipRequest> channel) =>
{
    if (dto == null || string.IsNullOrWhiteSpace(dto.SourcePath))
        return Results.BadRequest(new { error = "sourcePath is required" });

    if (!Directory.Exists(dto.SourcePath))
        return Results.BadRequest(new { error = "sourcePath does not exist on server" });

    var fileName = string.IsNullOrWhiteSpace(dto.ArchiveName)
        ? $"{Path.GetFileName(dto.SourcePath).Replace(Path.DirectorySeparatorChar, '_')}-{DateTime.UtcNow:yyyyMMddHHmmss}.zip"
        : dto.ArchiveName!;

    var outputPath = Path.Combine(archivesDir, fileName);

    var req = new ZipRequest(dto.SourcePath, outputPath, dto.Overwrite ?? false);
    await channel.Writer.WriteAsync(req);

    return Results.Accepted($"/archives/{Uri.EscapeDataString(fileName)}");
});

app.Run();

public record TriggerDto(string SourcePath, string? ArchiveName, bool? Overwrite);

public record ZipRequest(string SourcePath, string OutputPath, bool Overwrite);
