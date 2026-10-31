using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Idol.Core;
using Idol.Inference;

var root = Environment.GetEnvironmentVariable("RELIC_DRIFT_ROOT") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var specs = new[] { new AssetSpec("courier", 65536, 1042), new AssetSpec("sun-crystal", 32768, 2042), new AssetSpec("void-rock", 32768, 3042) };
var requested = args.Length == 0 || args.Contains("all") ? specs : specs.Where(x => args.Contains(x.Name)).ToArray();
if (requested.Length == 0) { Console.Error.WriteLine("Use all, courier, sun-crystal, or void-rock."); return 2; }
Directory.CreateDirectory(Path.Combine(root, "Docs/Provenance"));
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    // Offline cache only. No Hugging Face token is read or copied into this game.
    using var pipeline = await IdolPipeline.LoadAsync(new ModelSetupOptions
    {
        DownloadMissing = false,
        HuggingFaceToken = "offline-cached-models"
    }, new ConsoleProgress(), cancellation.Token);
    foreach (var spec in requested)
    {
        var source = Path.Combine(root, "Assets/Game/Art/Sources", spec.Name + ".png");
        var destination = Path.Combine(root, "Assets/Game/Art/Splats", spec.Name + ".idolply");
        if (!File.Exists(source)) throw new FileNotFoundException("Generate the documented 2D source image first.", source);
        if (File.Exists(destination)) { Console.WriteLine($"Already generated: {spec.Name}; delete its .idolply deliberately to regenerate."); continue; }
        var watch = Stopwatch.StartNew();
        var options = new GenerationOptions { RealPhoto = false, Steps = 50, Seed = spec.Seed, NumGaussians = spec.Count, Guidance = 3, TimeShift = 3 };
        Console.WriteLine($"BEGIN {spec.Name} -> {spec.Count:N0} Idol Gaussians");
        var result = await pipeline.GenerateAsync(source, options, new ConsoleProgress(), cancellation.Token);
        PlyFile.Write(destination, result.Cloud);
        var metadata = new
        {
            Asset = spec.Name,
            SourceImage = "Assets/Game/Art/Sources/" + spec.Name + ".png",
            SourceImageSha256 = Hash(source),
            Model = "speridlabs/idol",
            ModelRevision = ModelManifest.IdolRevision,
            Tool = "https://github.com/shohdi/idol-csharp",
            ToolCommit = "1ff3ee61a2f719637cfb6ac8113b2ec9494fa082",
            Options = options,
            GaussianCount = result.Cloud.Count,
            Output = "Assets/Game/Art/Splats/" + spec.Name + ".idolply",
            OutputSha256 = Hash(destination),
            Seconds = watch.Elapsed.TotalSeconds,
            CreatedUtc = DateTime.UtcNow,
            CoordinateConvention = "Idol Studio native Z-up to Y-up; Unity importer reflects Z",
            GeometryEdits = "None. Original Idol output; game applies only rigid transforms and scale."
        };
        await File.WriteAllTextAsync(Path.Combine(root, "Docs/Provenance", spec.Name + ".json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), cancellation.Token);
        Console.WriteLine($"DONE {spec.Name}: {result.Cloud.Count:N0} Gaussians, {watch.Elapsed.TotalSeconds:F1}s");
    }
    return 0;
}
catch (OperationCanceledException) { Console.Error.WriteLine("Canceled."); return 130; }
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
sealed record AssetSpec(string Name, int Count, int Seed);
sealed class ConsoleProgress : IProgress<InferenceProgress>
{
    public void Report(InferenceProgress p) => Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss} {p.Stage} {p.Fraction:P0}: {p.Detail}");
}
