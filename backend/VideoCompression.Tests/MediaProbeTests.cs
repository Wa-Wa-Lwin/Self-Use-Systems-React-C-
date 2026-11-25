using System.Text.Json;
using VideoCompression.Api.Media;

namespace VideoCompression.Tests;

public class MediaProbeTests
{
    private static MediaInfo? Parse(string json, string extension = ".mp4", long fileSize = 1_000_000)
    {
        using var document = JsonDocument.Parse(json);
        return MediaProbe.Parse(document.RootElement, extension, fileSize);
    }

    [Fact]
    public void Parses_a_typical_mp4()
    {
        var media = Parse("""
        {
          "streams": [
            { "index": 0, "codec_name": "h264", "codec_type": "video", "width": 1920, "height": 1080,
              "pix_fmt": "yuv420p", "avg_frame_rate": "30000/1001", "r_frame_rate": "30000/1001",
              "bit_rate": "8000000", "bits_per_raw_sample": "8", "disposition": { "attached_pic": 0 } },
            { "index": 1, "codec_name": "aac", "codec_type": "audio", "channels": 2, "sample_rate": "48000",
              "bit_rate": "128000", "tags": { "language": "eng" } }
          ],
          "format": { "format_name": "mov,mp4,m4a,3gp,3g2,mj2", "duration": "60.000000", "size": "61000000", "bit_rate": "8133333" }
        }
        """);

        Assert.NotNull(media);
        Assert.Equal("MP4", media.Container);
        Assert.Equal(60, media.DurationSeconds);
        Assert.Equal(61_000_000, media.SizeBytes);
        Assert.Equal((1920, 1080), (media.Video.Width, media.Video.Height));
        Assert.Equal(29.97, media.Video.Fps, 2);
        Assert.Equal(8_000_000, media.Video.BitrateBps);
        Assert.False(media.Video.BitrateEstimated);
        Assert.Equal("H.264 (AVC)", media.Video.CodecLabel);
        var audio = Assert.Single(media.Audio);
        Assert.Equal(128_000, audio.BitrateBps);
        Assert.Equal("eng", audio.Language);
    }

    [Fact]
    public void Swaps_dimensions_for_rotated_phone_videos()
    {
        var media = Parse("""
        {
          "streams": [
            { "index": 0, "codec_name": "hevc", "codec_type": "video", "width": 1920, "height": 1080, "avg_frame_rate": "30/1",
              "bit_rate": "12000000", "side_data_list": [ { "side_data_type": "Display Matrix", "rotation": -90 } ] }
          ],
          "format": { "duration": "10.0", "size": "15000000", "bit_rate": "12000000" }
        }
        """, ".mov");

        Assert.NotNull(media);
        Assert.Equal((1080, 1920), (media.Video.Width, media.Video.Height));
        Assert.Equal(-90, media.Video.Rotation);
        Assert.Empty(media.Audio);
    }

    [Fact]
    public void Uses_matroska_bitrate_tags_and_falls_back_to_total_minus_audio()
    {
        var tagged = Parse("""
        {
          "streams": [
            { "index": 0, "codec_name": "h264", "codec_type": "video", "width": 1280, "height": 720, "avg_frame_rate": "25/1",
              "tags": { "BPS": "3000000", "DURATION": "00:02:00.000000000" } },
            { "index": 1, "codec_name": "opus", "codec_type": "audio", "channels": 2, "sample_rate": "48000", "tags": { "BPS": "96000" } }
          ],
          "format": { "size": "46440000" }
        }
        """, ".mkv");

        Assert.NotNull(tagged);
        Assert.Equal(120, tagged.DurationSeconds, 3);       // from the DURATION tag
        Assert.Equal(3_000_000, tagged.Video.BitrateBps);    // from the BPS tag
        Assert.Equal(96_000, tagged.Audio[0].BitrateBps);
        Assert.Equal("MKV", tagged.Container);

        var untagged = Parse("""
        {
          "streams": [
            { "index": 0, "codec_name": "vp9", "codec_type": "video", "width": 1280, "height": 720, "avg_frame_rate": "30/1" },
            { "index": 1, "codec_name": "opus", "codec_type": "audio", "channels": 2, "sample_rate": "48000" }
          ],
          "format": { "duration": "100", "size": "50000000", "bit_rate": "4000000" }
        }
        """, ".webm");

        Assert.NotNull(untagged);
        Assert.True(untagged.Video.BitrateEstimated);
        Assert.Equal(4_000_000 - 128_000, untagged.Video.BitrateBps); // unknown audio is assumed to be 128 kbps
        Assert.Equal("WebM", untagged.Container);
    }

    [Fact]
    public void Ignores_cover_art_and_rejects_files_without_video()
    {
        var withCover = Parse("""
        {
          "streams": [
            { "index": 0, "codec_name": "mjpeg", "codec_type": "video", "width": 600, "height": 600, "disposition": { "attached_pic": 1 } },
            { "index": 1, "codec_name": "h264", "codec_type": "video", "width": 640, "height": 360, "avg_frame_rate": "24/1", "bit_rate": "900000" }
          ],
          "format": { "duration": "30", "size": "4000000" }
        }
        """);
        Assert.NotNull(withCover);
        Assert.Equal(1, withCover.Video.Index);
        Assert.Equal("h264", withCover.Video.Codec);

        var audioOnly = Parse("""
        {
          "streams": [
            { "index": 0, "codec_name": "mp3", "codec_type": "audio", "channels": 2, "sample_rate": "44100", "bit_rate": "320000" },
            { "index": 1, "codec_name": "mjpeg", "codec_type": "video", "width": 600, "height": 600, "disposition": { "attached_pic": 1 } }
          ],
          "format": { "duration": "200", "size": "8000000" }
        }
        """, ".mp3");
        Assert.Null(audioOnly);
    }

    [Fact]
    public void Detects_ten_bit_hdr()
    {
        var media = Parse("""
        {
          "streams": [
            { "index": 0, "codec_name": "hevc", "codec_type": "video", "width": 3840, "height": 2160, "avg_frame_rate": "60000/1001",
              "pix_fmt": "yuv420p10le", "color_transfer": "smpte2084", "bit_rate": "45000000" }
          ],
          "format": { "duration": "20", "size": "112500000" }
        }
        """);

        Assert.NotNull(media);
        Assert.Equal(10, media.Video.BitDepth);
        Assert.True(media.Video.IsHdr);
    }
}
