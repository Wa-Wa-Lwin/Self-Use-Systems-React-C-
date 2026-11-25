using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VideoCompression.Api.Ffmpeg;
using Xunit.Abstractions;

namespace VideoCompression.Tests;

/// <summary>Runs the real API in memory with its own storage folder.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "VideoCompressionTests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("VideoCompression:StorageFolder", StorageRoot);
    }

    public Task<FfmpegStatus> FfmpegAsync() => Services.GetRequiredService<FfmpegTools>().GetStatusAsync();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            Directory.Delete(StorageRoot, recursive: true);
        }
        catch (Exception)
        {
            // best effort
        }
    }
}

/// <summary>Each test gets its own in-memory server and storage folder.</summary>
public sealed class ApiTests(ITestOutputHelper output) : IDisposable
{
    private readonly ApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Health_reports_ffmpeg_and_codecs()
    {
        using var client = _factory.CreateClient();
        using var health = await GetJson(client, "/api/health");

        var root = health.RootElement;
        Assert.Equal(3, root.GetProperty("codecs").GetArrayLength());
        Assert.Equal("h264", root.GetProperty("codecs")[0].GetProperty("id").GetString());
        Assert.True(root.GetProperty("maxUploadBytes").GetInt64() > 0);
        Assert.Contains(".mp4", root.GetProperty("acceptedExtensions").EnumerateArray().Select(e => e.GetString()));
        output.WriteLine($"FFmpeg available: {root.GetProperty("ffmpegAvailable").GetBoolean()} {root.GetProperty("ffmpegVersion")}");
    }

    [Fact]
    public async Task Unknown_routes_and_ids_return_json_404s()
    {
        using var client = _factory.CreateClient();

        var unknown = await client.GetAsync("/api/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("application/problem+json", unknown.Content.Headers.ContentType?.MediaType);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/videos/not-an-id")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/videos/{Guid.NewGuid():N}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/jobs/{Guid.NewGuid():N}")).StatusCode);
    }

    [Fact]
    public async Task Rejects_non_video_uploads()
    {
        using var client = _factory.CreateClient();
        var ffmpeg = await _factory.FfmpegAsync();

        var wrongType = await client.PostAsync("/api/videos?fileName=notes.txt", new ByteArrayContent("hello"u8.ToArray()));
        Assert.Equal(ffmpeg.Available ? HttpStatusCode.UnsupportedMediaType : HttpStatusCode.ServiceUnavailable, wrongType.StatusCode);

        if (!ffmpeg.Available) return;

        var notAVideo = await client.PostAsync("/api/videos?fileName=fake.mp4", new ByteArrayContent(new byte[4096]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, notAVideo.StatusCode);
        Assert.Empty(Directory.EnumerateDirectories(_factory.StorageRoot)); // rejected uploads are cleaned up
    }

    /// <summary>
    /// The whole flow against real FFmpeg: upload → analysis/verdict → estimates → compress → download.
    /// Needs FFmpeg (run scripts/get-ffmpeg.ps1); without it the test only logs that it was skipped.
    /// </summary>
    [Fact]
    public async Task End_to_end_compression_with_real_ffmpeg()
    {
        var ffmpeg = await _factory.FfmpegAsync();
        if (!ffmpeg.Available)
        {
            output.WriteLine("SKIPPED: FFmpeg not found. " + ffmpeg.Message);
            return;
        }

        // A deliberately bloated 20 s 720p clip (near-lossless H.264 + 192 kbps audio), so it should compress well.
        var source = Path.Combine(_factory.StorageRoot, "e2e-source.mp4");
        Directory.CreateDirectory(_factory.StorageRoot);
        await RunFfmpeg(ffmpeg,
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=30",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000",
            "-t", "20", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "8", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "192k", "-shortest", source);
        var originalBytes = new FileInfo(source).Length;
        output.WriteLine($"Source: {originalBytes:N0} bytes");

        using var client = _factory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(5);

        // Upload
        using var body = new StreamContent(File.OpenRead(source));
        var upload = await client.PostAsync("/api/videos?fileName=" + Uri.EscapeDataString("my clip.mp4"), body);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        using var uploaded = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        var id = uploaded.RootElement.GetProperty("id").GetString()!;
        var media = uploaded.RootElement.GetProperty("media");
        Assert.Equal(1280, media.GetProperty("video").GetProperty("width").GetInt32());
        Assert.Equal(20, media.GetProperty("durationSeconds").GetDouble(), 0);
        Assert.Equal(4, uploaded.RootElement.GetProperty("presets").GetArrayLength());

        // Background analysis → verdict
        JsonDocument video = await WaitFor(client, $"/api/videos/{id}", doc => doc.RootElement.GetProperty("analysis").GetProperty("done").GetBoolean(), TimeSpan.FromMinutes(4));
        var verdict = video.RootElement.GetProperty("verdict");
        output.WriteLine($"Verdict: {verdict.GetProperty("level")} — {verdict.GetProperty("summary")}");
        foreach (var preset in video.RootElement.GetProperty("presets").EnumerateArray())
        {
            output.WriteLine($"  {preset.GetProperty("id"),-8} {preset.GetProperty("status")} {preset.GetProperty("estimate")}");
            Assert.Equal("done", preset.GetProperty("status").GetString());
        }
        Assert.True(verdict.GetProperty("recommendedSavingsPercent").GetDouble() > 30, "a near-lossless source should compress a lot");

        var balanced = video.RootElement.GetProperty("presets").EnumerateArray().Single(p => p.GetProperty("id").GetString() == "balanced");
        var estimatedBytes = balanced.GetProperty("estimate").GetProperty("bytes").GetInt64();
        var balancedSettings = balanced.GetProperty("settings").Clone();
        video.Dispose();

        // On-demand estimates (custom + target size)
        using (var custom = await PostJson(client, $"/api/videos/{id}/estimate", new
               {
                   codec = "h264", mode = "quality", crf = 28, targetSizeMb = (double?)null, autoResolution = false,
                   maxResolution = 480, maxFps = (double?)null, audioBitrateKbps = 96, speed = "fast",
               }))
        {
            Assert.True(custom.RootElement.GetProperty("valid").GetBoolean(), custom.RootElement.ToString());
            Assert.Equal(854, custom.RootElement.GetProperty("plan").GetProperty("width").GetInt32());
        }

        var targetMb = Math.Round(originalBytes * 0.25 / 1_000_000, 2);
        var targetSettings = new
        {
            codec = "h264", mode = "targetSize", crf = 23, targetSizeMb = targetMb, autoResolution = false,
            maxResolution = (int?)null, maxFps = (double?)null, audioBitrateKbps = 96, speed = "fast",
        };
        using (var target = await PostJson(client, $"/api/videos/{id}/estimate", targetSettings))
        {
            Assert.True(target.RootElement.GetProperty("valid").GetBoolean(), target.RootElement.ToString());
            Assert.Equal("target", target.RootElement.GetProperty("estimate").GetProperty("method").GetString());
        }

        using (var tooSmall = await PostJson(client, $"/api/videos/{id}/estimate", targetSettings with { targetSizeMb = 0.01 }))
            Assert.False(tooSmall.RootElement.GetProperty("valid").GetBoolean());

        // Compress with the recommended preset and compare against its estimate.
        var actual = await CompressAndDownload(client, id, balancedSettings, "my clip-compressed.mp4");
        var ratio = actual / (double)estimatedBytes;
        output.WriteLine($"Balanced: estimated {estimatedBytes:N0}, actual {actual:N0} (actual/estimate = {ratio:0.00})");
        Assert.True(actual < originalBytes / 2);
        Assert.InRange(ratio, 0.6, 1.4);

        // Two-pass target size should land on (or just under) the requested size.
        var targetActual = await CompressAndDownload(client, id, JsonSerializer.SerializeToElement(targetSettings), null);
        var targetBytes = targetMb * 1_000_000;
        output.WriteLine($"Target {targetBytes:N0} → actual {targetActual:N0} ({targetActual / targetBytes:0.00}×)");
        Assert.InRange(targetActual, targetBytes * 0.7, targetBytes * 1.05);

        // Deleting the upload removes its files.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/videos/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/videos/{id}")).StatusCode);
    }

    private async Task<long> CompressAndDownload(HttpClient client, string videoId, JsonElement settings, string? expectedName)
    {
        var start = await client.PostAsync($"/api/videos/{videoId}/jobs", JsonContent.Create(settings));
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        using var started = JsonDocument.Parse(await start.Content.ReadAsStringAsync());
        var jobId = started.RootElement.GetProperty("id").GetString()!;

        using var job = await WaitFor(client, $"/api/jobs/{jobId}",
            doc => doc.RootElement.GetProperty("status").GetString() is "completed" or "failed" or "canceled", TimeSpan.FromMinutes(4));
        Assert.True(job.RootElement.GetProperty("status").GetString() == "completed", job.RootElement.ToString());
        if (expectedName is not null) Assert.Equal(expectedName, job.RootElement.GetProperty("fileName").GetString());

        var download = await client.GetAsync(job.RootElement.GetProperty("downloadUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("video/mp4", download.Content.Headers.ContentType?.MediaType);
        var bytes = await download.Content.ReadAsByteArrayAsync();
        Assert.Equal("ftyp", System.Text.Encoding.ASCII.GetString(bytes, 4, 4)); // a real MP4
        Assert.Equal(job.RootElement.GetProperty("outputBytes").GetInt64(), bytes.Length);
        Assert.Equal(job.RootElement.GetProperty("fileName").GetString(), DispositionName(download));

        // A name typed by the user is cleaned up and used for the download.
        var named = await client.GetAsync(job.RootElement.GetProperty("downloadUrl").GetString() + "&name=" + Uri.EscapeDataString("Trip: day 1?.mov"));
        Assert.Equal(HttpStatusCode.OK, named.StatusCode);
        Assert.Equal("Trip_ day 1_.mp4", DispositionName(named));

        // Range requests work (needed for the in-browser preview player).
        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, job.RootElement.GetProperty("previewUrl").GetString());
        rangeRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 99);
        var partial = await client.SendAsync(rangeRequest);
        Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
        return bytes.Length;
    }

    private static string? DispositionName(HttpResponseMessage response)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        return disposition?.FileNameStar ?? disposition?.FileName?.Trim('"');
    }

    private static async Task<JsonDocument> GetJson(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.IsSuccessStatusCode, $"{url} → {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonDocument> PostJson(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        Assert.True(response.IsSuccessStatusCode, $"{url} → {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonDocument> WaitFor(HttpClient client, string url, Func<JsonDocument, bool> done, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var document = await GetJson(client, url);
            if (done(document)) return document;
            document.Dispose();
            if (stopwatch.Elapsed > timeout) throw new TimeoutException($"{url} did not finish within {timeout}.");
            await Task.Delay(500);
        }
    }

    private static async Task RunFfmpeg(FfmpegStatus ffmpeg, params string[] args)
    {
        var result = await ProcessRunner.RunAsync(ffmpeg.FfmpegPath!, args, CancellationToken.None);
        Assert.True(result.ExitCode == 0, FfmpegErrors.Summarize(result));
    }
}
