using System.Globalization;
using System.Text;
using Signage.E2E.Infrastructure;

namespace Signage.E2E.Soak;

/// <summary>
/// WP-13 soak (ADR-0008): the shipped player runs for 30 minutes over alternating video (3–4 s, real speed) and image
/// slides at timeScale 0.05, roughly 900 transitions. Every 100 transitions it samples the browser's proportional set size
/// (PSS) from /proc after a forced GC, with tracing off and without the CDP Media domain. A regression guard, not a
/// plateau proof (the daily reload bounds growth): it fails if growth from the end of the first third to the end
/// exceeds twice the spike's rate (2 × 0.15 MB per transition). Opt-in: SIGNAGE_E2E_SOAK=1.
/// </summary>
[Trait("Category", "Soak")]
public class PlayerSoak(BrowserFixture fixture) : PlayerTest(fixture)
{
    private static readonly TimeSpan RunFor = TimeSpan.FromMinutes(30);
    private const int SampleEvery = 100;
    private const double SpikeMbPerTransition = 0.15;

    protected override bool Tracing => false;

    public sealed record Sample(int Transitions, double Minutes, double BrowserPssMb, int Layers, int Videos);

    [Fact]
    public async Task The_player_runs_30_minutes_of_video_and_images_without_unexpected_memory_growth()
    {
        Assert.SkipUnless(E2EEnvironment.SoakEnabled, "Soak: set SIGNAGE_E2E_SOAK=1 (30 minutes).");
        var media = E2EEnvironment.MediaBaseUrl;
        var names = new List<string>();
        foreach (var (clip, image) in new[] { ("clip-a", "image-mandelbrot"), ("clip-b", "image-smptebars"), ("clip-c", "image-testsrc2") })
        {
            await AdminActions.CreateUrlSlideAsync(Page, "Video", $"Soak {clip}", new Uri(media, $"/media/{clip}-h264.mp4"));
            await AdminActions.CreateUrlSlideAsync(Page, "Image", $"Soak {image}", new Uri(media, $"/media/{image}.png"), 10);
            names.Add($"Soak {clip}");
            names.Add($"Soak {image}");
        }
        await EmptyScreenAsync("Soak screen", "Soak loop", [.. names]);
        var token = await AdminActions.CreateScreenAddressTokenAsync(Page);
        await Page.GotoAsync("about:blank"); // the admin page stays idle during the run

        var device = await OpenDeviceAsync(token, timeScale: 0.05);
        var cdp = await device.Context.NewCDPSessionAsync(device);
        var started = DateTimeOffset.UtcNow;
        var samples = new List<Sample> { await SampleAsync(device, cdp, 0, started) };
        var next = SampleEvery;
        while (DateTimeOffset.UtcNow - started < RunFor)
        {
            await WaitForActivationsAsync(device, next + 1, timeoutMs: 120_000); // transitions = activations - 1
            samples.Add(await SampleAsync(device, cdp, next, started));
            next += SampleEvery;
        }

        var csv = new StringBuilder("transitions,minutes,browserPssMb,layers,videos\n");
        foreach (var s in samples)
        {
            csv.AppendLine(CultureInfo.InvariantCulture, $"{s.Transitions},{s.Minutes:F1},{s.BrowserPssMb:F0},{s.Layers},{s.Videos}");
        }
        var dir = Path.Combine(E2EEnvironment.ArtifactsDir, "soak");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "soak.csv"), csv.ToString(), TestContext.Current.CancellationToken);

        // Growth from the end of the first third to the end, against twice the spike's rate over the same span.
        var last = samples[^1];
        var thirdAt = samples.First(s => s.Transitions >= last.Transitions / 3);
        var growth = last.BrowserPssMb - thirdAt.BrowserPssMb;
        var allowed = 2 * SpikeMbPerTransition * (last.Transitions - thirdAt.Transitions);
        var summary = $"{last.Transitions} transitions in {last.Minutes:F1} min; PSS {samples[0].BrowserPssMb:F0} → {thirdAt.BrowserPssMb:F0} " +
                      $"(at {thirdAt.Transitions}) → {last.BrowserPssMb:F0} MB; growth {growth:F0} MB over {last.Transitions - thirdAt.Transitions} " +
                      $"transitions = {growth / Math.Max(1, last.Transitions - thirdAt.Transitions):F3} MB/transition; allowed {allowed:F0} MB; " +
                      $"max layers {await device.EvaluateAsync<int>("() => window.__maxLayers")}";
        await File.WriteAllTextAsync(Path.Combine(dir, "summary.txt"), summary + "\n" + csv, TestContext.Current.CancellationToken);
        TestContext.Current.SendDiagnosticMessage(summary);

        Assert.True(last.Transitions >= 600, $"too few transitions: {summary}");
        Assert.True(growth <= allowed, $"memory grew faster than allowed: {summary}");
        Assert.All(samples, s => Assert.InRange(s.Layers, 1, 3));
        Assert.All(samples, s => Assert.InRange(s.Videos, 0, 2)); // ADR-0008: the playing one and the next one at most
        Assert.InRange(await device.EvaluateAsync<int>("() => window.__maxLayers"), 1, 3);
        await device.Context.CloseAsync();
    }

    private static async Task<Sample> SampleAsync(Microsoft.Playwright.IPage device, Microsoft.Playwright.ICDPSession cdp, int transitions, DateTimeOffset started)
    {
        await cdp.SendAsync("HeapProfiler.collectGarbage");
        var dom = await device.EvaluateAsync<int[]>("() => [document.querySelector('.slides').childElementCount, document.querySelectorAll('video').length]");
        return new Sample(transitions, (DateTimeOffset.UtcNow - started).TotalMinutes, BrowserPssMb(), dom[0], dom[1]);
    }

    /// <summary>Proportional set size of all browser processes (shared pages split fairly), in MB.</summary>
    private static double BrowserPssMb()
    {
        double totalKb = 0;
        foreach (var dir in Directory.EnumerateDirectories("/proc").Where(d => int.TryParse(Path.GetFileName(d), out _)))
        {
            try
            {
                var cmd = File.ReadAllText(Path.Combine(dir, "cmdline"));
                if (!cmd.Contains("chrom", StringComparison.OrdinalIgnoreCase) && !cmd.Contains("headless_shell", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var pss = File.ReadLines(Path.Combine(dir, "smaps_rollup")).FirstOrDefault(l => l.StartsWith("Pss:", StringComparison.Ordinal));
                if (pss is not null)
                {
                    totalKb += double.Parse(pss.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The process ended while it was being read.
            }
        }
        return totalKb / 1024;
    }
}
