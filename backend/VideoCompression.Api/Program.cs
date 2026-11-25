using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using VideoCompression.Api;
using VideoCompression.Api.Analysis;
using VideoCompression.Api.Compression;
using VideoCompression.Api.Endpoints;
using VideoCompression.Api.Ffmpeg;
using VideoCompression.Api.Media;
using VideoCompression.Api.Storage;

// FFmpeg arguments must always use '.' as the decimal separator.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.SectionName));
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

builder.Services.AddSingleton<FfmpegTools>();
builder.Services.AddSingleton<MediaProbe>();
builder.Services.AddSingleton<VideoStore>();
builder.Services.AddSingleton<SizeEstimator>();
builder.Services.AddSingleton<VideoAnalyzer>();
builder.Services.AddSingleton<AnalysisQueue>();
builder.Services.AddSingleton<CompressionQueue>();
builder.Services.AddHostedService<AnalysisWorker>();
builder.Services.AddHostedService<CompressionWorker>();
builder.Services.AddHostedService<StorageCleanupService>();

var app = builder.Build();

app.UseExceptionHandler();

// In production the built frontend (npm run build) is served from wwwroot.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapApi();
app.MapFallbackToFile("index.html");

var ffmpeg = await app.Services.GetRequiredService<FfmpegTools>().GetStatusAsync();
if (ffmpeg.Available)
    app.Logger.LogInformation("Using FFmpeg {Version} at {Path}", ffmpeg.Version, ffmpeg.FfmpegPath);
else
    app.Logger.LogWarning("{Message}", ffmpeg.Message);
app.Logger.LogInformation("Storing uploads in {Folder}", app.Services.GetRequiredService<VideoStore>().Root);

app.Run();

/// <summary>Exposed for integration tests (WebApplicationFactory).</summary>
public partial class Program;
