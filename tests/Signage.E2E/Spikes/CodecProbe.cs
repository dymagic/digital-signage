using System.Text.Json;
using Signage.E2E.Infrastructure;

namespace Signage.E2E.Spikes;

/// <summary>WP-07: which video formats the test browser can decode. Run with SIGNAGE_E2E_SPIKE=1 -trait "Category=Spike".</summary>
[Trait("Category", "Spike")]
public class CodecProbe(BrowserFixture fixture) : E2ETest(fixture)
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [Fact]
    public async Task Report_codec_support_and_actual_playback()
    {
        Assert.SkipUnless(E2EEnvironment.SpikeEnabled, "Spike: set SIGNAGE_E2E_SPIKE=1.");
        await Page.GotoAsync(new Uri(E2EEnvironment.MediaBaseUrl, "/spikes/").ToString());

        var result = await Page.EvaluateAsync<JsonElement>("""
            async (base) => {
              const v = document.createElement('video');
              const types = {
                h264: 'video/mp4; codecs="avc1.640028"',
                h264aac: 'video/mp4; codecs="avc1.640028, mp4a.40.2"',
                av1mp4: 'video/mp4; codecs="av01.0.08M.08"',
                vp9mp4: 'video/mp4; codecs="vp09.00.40.08"',
                vp9webm: 'video/webm; codecs="vp9"',
              };
              const canPlay = Object.fromEntries(Object.entries(types).map(([k, t]) => [k, v.canPlayType(t)]));
              async function plays(file) {
                const video = document.createElement('video');
                video.muted = true;
                video.src = base + file;
                document.body.append(video);
                try {
                  await video.play();
                  await new Promise((r) => setTimeout(r, 500));
                  return { ok: video.currentTime > 0, time: video.currentTime, error: video.error?.code ?? null };
                } catch (e) {
                  return { ok: false, error: String(e) };
                } finally {
                  video.remove();
                }
              }
              return { userAgent: navigator.userAgent, canPlay, h264: await plays('clip-a-h264.mp4') };
            }
            """, new Uri(E2EEnvironment.MediaBaseUrl, "/media/").ToString());

        var json = JsonSerializer.Serialize(result, Indented);
        Directory.CreateDirectory(Path.Combine(E2EEnvironment.ArtifactsDir, "spike"));
        await File.WriteAllTextAsync(Path.Combine(E2EEnvironment.ArtifactsDir, "spike", "codecs.json"), json, TestContext.Current.CancellationToken);
        TestContext.Current.SendDiagnosticMessage(json);
    }
}
