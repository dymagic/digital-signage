using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using Signage.E2E.Infrastructure;

namespace Signage.E2E.Spikes;

/// <summary>
/// WP-07 spike (not a regression test): runs the harness in tests/Signage.E2E/spikes/video-lifecycle with each
/// strategy for 240 transitions and samples memory every 20. Results go to artifacts/e2e/spike/.
/// Run: SIGNAGE_E2E_SPIKE=1 scripts/test-e2e.sh -trait "Category=Spike"
/// </summary>
[Trait("Category", "Spike")]
public class VideoLifecycleSpike(BrowserFixture fixture) : E2ETest(fixture)
{
    private const int Transitions = 240;
    private const int SampleEvery = 20;
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public sealed record Sample(int Transitions, double JsHeapMb, double Nodes, double Documents, double BrowserPssMb, int VideosInDom, int VideoElementsCreated, int PlayersCreated, int PlayersDestroyed);

    [Theory]
    [InlineData("release")]
    [InlineData("remove")]
    [InlineData("keep")]
    [InlineData("reuse")]
    public Task Measure(string strategy)
    {
        Assert.SkipUnless(E2EEnvironment.SpikeEnabled && Environment.GetEnvironmentVariable("SIGNAGE_E2E_SPIKE_LONG") != "1", "Spike: set SIGNAGE_E2E_SPIKE=1.");
        return RunAsync(strategy, Transitions, SampleEvery, imagesOnly: false);
    }

    /// <summary>Does the growth level off? release vs. an images-only baseline over a long run (SIGNAGE_E2E_SPIKE_LONG=1).</summary>
    [Theory]
    [InlineData("release", false)]
    [InlineData("release", true)]
    public Task MeasureLong(string strategy, bool imagesOnly)
    {
        Assert.SkipUnless(E2EEnvironment.SpikeEnabled && Environment.GetEnvironmentVariable("SIGNAGE_E2E_SPIKE_LONG") == "1", "Long spike: set SIGNAGE_E2E_SPIKE_LONG=1.");
        return RunAsync(strategy, 1200, 100, imagesOnly);
    }

    private async Task RunAsync(string strategy, int transitions, int sampleEvery, bool imagesOnly)
    {
        var label = imagesOnly ? "images-only" : $"{strategy}{(transitions == Transitions ? "" : $"-{transitions}")}";
        var cdp = await Context.NewCDPSessionAsync(Page);
        await cdp.SendAsync("Performance.enable");
        await cdp.SendAsync("Media.enable");
        var created = 0;
        var destroyed = 0;
        var mediaEvents = new Dictionary<string, int>();
        cdp.Event("Media.playersCreated").OnEvent += (_, e) => Interlocked.Add(ref created, e?.GetProperty("players").GetArrayLength() ?? 0);
        cdp.Event("Media.playerEventsAdded").OnEvent += (_, e) =>
        {
            foreach (var ev in e?.GetProperty("events").EnumerateArray() ?? default)
            {
                var value = ev.GetProperty("value").GetString() ?? "";
                lock (mediaEvents)
                {
                    mediaEvents[value] = mediaEvents.GetValueOrDefault(value) + 1;
                }
                if (value.Contains("Destroyed", StringComparison.OrdinalIgnoreCase))
                {
                    Interlocked.Increment(ref destroyed);
                }
            }
        };

        var media = new Uri(E2EEnvironment.MediaBaseUrl, "/media/").ToString();
        await Page.GotoAsync(new Uri(E2EEnvironment.MediaBaseUrl, $"/spikes/video-lifecycle/harness.html?strategy={strategy}&transitions={transitions}&imagesOnly={(imagesOnly ? 1 : 0)}&media={Uri.EscapeDataString(media)}").ToString());

        var samples = new List<Sample> { await SampleAsync(cdp, 0, created, destroyed) };
        for (var checkpoint = sampleEvery; checkpoint <= transitions; checkpoint += sampleEvery)
        {
            await Page.WaitForFunctionAsync($"() => window.spike && (window.spike.transitions >= {checkpoint} || window.spike.done)", null, new() { Timeout = 600_000, PollingInterval = 200 });
            samples.Add(await SampleAsync(cdp, checkpoint, created, destroyed));
        }

        var errors = await Page.EvaluateAsync<string[]>("() => window.spike.errors");
        var maxLayers = await Page.EvaluateAsync<int>("() => window.spike.maxLayers");
        var dir = Path.Combine(E2EEnvironment.ArtifactsDir, "spike");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, $"video-{label}.json"),
            JsonSerializer.Serialize(new { strategy, imagesOnly, transitions, maxLayers, errors, mediaEvents, samples }, Indented), TestContext.Current.CancellationToken);

        var csv = new StringBuilder("transitions,jsHeapMb,nodes,documents,browserPssMb,videosInDom,videoElementsCreated,playersCreated,playersDestroyed\n");
        foreach (var s in samples)
        {
            csv.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{s.Transitions},{s.JsHeapMb:F1},{s.Nodes},{s.Documents},{s.BrowserPssMb:F0},{s.VideosInDom},{s.VideoElementsCreated},{s.PlayersCreated},{s.PlayersDestroyed}"));
        }
        await File.WriteAllTextAsync(Path.Combine(dir, $"video-{label}.csv"), csv.ToString(), TestContext.Current.CancellationToken);
        TestContext.Current.SendDiagnosticMessage($"{label}\n{csv}");
        Assert.Empty(errors);
    }

    private async Task<Sample> SampleAsync(ICDPSession cdp, int transitions, int created, int destroyed)
    {
        await cdp.SendAsync("HeapProfiler.collectGarbage");
        var metrics = (await cdp.SendAsync("Performance.getMetrics"))!.Value.GetProperty("metrics").EnumerateArray()
            .ToDictionary(m => m.GetProperty("name").GetString()!, m => m.GetProperty("value").GetDouble());
        var videos = await Page.EvaluateAsync<int[]>("() => [document.querySelectorAll('video').length, window.spike?.videoElementsCreated ?? 0]");
        return new Sample(
            transitions,
            metrics.GetValueOrDefault("JSHeapUsedSize") / 1024 / 1024,
            metrics.GetValueOrDefault("Nodes"),
            metrics.GetValueOrDefault("Documents"),
            BrowserPssMb(),
            videos[0],
            videos[1],
            created,
            destroyed);
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
                // Process ended while reading.
            }
        }
        return totalKb / 1024;
    }
}
