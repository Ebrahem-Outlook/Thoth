using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Thoth.Data.Processing;
using Thoth.Model;
using Thoth.Model.Persistence;
using Thoth.Tokenization;
using Thoth.Training.TokenShards;
using Thoth.Training.Torch;

namespace Thoth.Training.Continuous;

public sealed record ContinuousLearningOptions
{
    public string WorkspaceRoot { get; init; } = Directory.GetCurrentDirectory();
    public string DataDirectory { get; init; } = "data";
    public string ConfigPath { get; init; } = "config/continuous-learning/sources.json";
    public string RunId { get; init; } = $"continuous-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
    public string TokenizerPath { get; init; } = "data/tokenizers/local-bpe-8k";
    public bool Offline { get; init; }
    public bool Rehearsal { get; init; }
    public int MaxCycles { get; init; } = 0;
    public int StopAfterTokens { get; init; } = 0;
    public double StopAfterHours { get; init; } = 0;
    public double MaxCpuPercent { get; init; } = 96;
    public double RamFloorGb { get; init; } = 2;
    public double DiskFloorGb { get; init; } = 25;
    public double SpoolMaxGb { get; init; } = 1;
    public int IngestWorkers { get; init; } = 1;
    public int TrainerThreads { get; init; } = Math.Max(1, Environment.ProcessorCount - 2);
    public int CheckpointEveryTokens { get; init; } = 16_384;
    public int EvaluateEveryTokens { get; init; } = 1_000_000;
    public int Context { get; init; } = 64;
    public int Layers { get; init; } = 1;
    public int Width { get; init; } = 64;
    public int Heads { get; init; } = 4;
    public int Ffn { get; init; } = 256;
    public int StepsPerCycle { get; init; } = 1;
    public bool NoModelGrowth { get; init; } = true;

    public string ContinuousRoot => Path.Combine(Path.GetFullPath(DataDirectory), "continuous");
    public string RunDirectory => Path.Combine(ContinuousRoot, "runs", RunId);
}

public sealed record ContinuousSourceRegistry(int Version, IReadOnlyList<ContinuousSource> Sources);

public sealed record ContinuousSource
{
    public string Id { get; init; } = "";
    public string Type { get; init; } = "";
    public bool Enabled { get; init; }
    public string OfficialLocation { get; init; } = "";
    public string Language { get; init; } = "";
    public string Domain { get; init; } = "";
    public string LicenseIdentifier { get; init; } = "";
    public string LicenseReference { get; init; } = "";
    public string AttributionRequirement { get; init; } = "";
    public string[] AllowedContentTypes { get; init; } = [];
    public string[] ExcludedNamespacesOrPaths { get; init; } = [];
    public string RefreshMode { get; init; } = "manual";
    public string RateLimit { get; init; } = "1 request/minute";
    public int MaximumBytesPerCycle { get; init; } = 512 * 1024;
    public int MaximumDocumentsPerCycle { get; init; } = 1;
    public string TrainingRoute { get; init; } = "neural";
    public string TrustLevel { get; init; } = "reviewed";
    public string LastLegalReviewDate { get; init; } = "";
}

public sealed record ContinuousLearningStatus
{
    public string RunId { get; init; } = "";
    public string State { get; init; } = "unknown";
    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string CurrentSource { get; init; } = "";
    public int SourcesEnabled { get; init; }
    public long FetchedDocuments { get; init; }
    public long AcceptedDocuments { get; init; }
    public long RejectedDocuments { get; init; }
    public long NeuralDocuments { get; init; }
    public long ConceptDocuments { get; init; }
    public long QueuedTokens { get; init; }
    public long ConsumedTokens { get; init; }
    public long ReplayTokens { get; init; }
    public long NewTokens { get; init; }
    public long Step { get; init; }
    public double TokensPerSecond { get; init; }
    public double LastLoss { get; init; }
    public string LatestCheckpoint { get; init; } = "";
    public string CheckpointSha256 { get; init; } = "";
    public string ResourceState { get; init; } = "unknown";
    public double CpuPercent { get; init; }
    public long ProcessWorkingSetBytes { get; init; }
    public long AvailableRamBytes { get; init; }
    public long FreeDiskBytes { get; init; }
    public long SpoolBytes { get; init; }
    public int PendingSegments { get; init; }
    public bool StopRequested { get; init; }
    public string Message { get; init; } = "";
}

public sealed record ContinuousRunReport(
    ContinuousLearningStatus Status,
    string StatusPath,
    string StopCommand,
    string ResumeCommand,
    string MonitorCommand);

public sealed class ContinuousLearningEngine
{
    private const string UserAgent = "ThothContinuousLearning/0.1 (local-only; bounded; license-tracked)";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly TextNormalizer normalizer = new(new TextNormalizationOptions(NormalizeToNfc: true));
    private readonly DocumentQualityAnalyzer quality = new();
    private readonly DocumentDeduplicator deduper = new();
    private readonly HttpClient http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public ContinuousLearningEngine()
    {
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/markdown"));
    }

    public async Task EnsureDefaultConfigAsync(string workspaceRoot, CancellationToken cancellationToken = default)
    {
        var configDirectory = Path.Combine(Path.GetFullPath(workspaceRoot), "config", "continuous-learning");
        Directory.CreateDirectory(configDirectory);
        var sourcesPath = Path.Combine(configDirectory, "sources.json");
        if (!File.Exists(sourcesPath))
        {
            var registry = new ContinuousSourceRegistry(
                1,
                [
                    new ContinuousSource
                    {
                        Id = "owned-curriculum",
                        Type = "owned-curriculum",
                        Enabled = true,
                        OfficialLocation = "local deterministic generator",
                        Language = "ar,en",
                        Domain = "dialogue-code-procedure",
                        LicenseIdentifier = "Thoth-owned",
                        LicenseReference = "docs/continuous-learning/SOURCE_POLICY.md#owned-curriculum",
                        AttributionRequirement = "none",
                        AllowedContentTypes = ["text/plain"],
                        RefreshMode = "continuous",
                        RateLimit = "local",
                        MaximumBytesPerCycle = 256 * 1024,
                        MaximumDocumentsPerCycle = 2,
                        TrainingRoute = "neural",
                        TrustLevel = "owned",
                        LastLegalReviewDate = "2026-07-13"
                    },
                    new ContinuousSource
                    {
                        Id = "mdn-content-readme",
                        Type = "http-document",
                        Enabled = false,
                        OfficialLocation = "https://raw.githubusercontent.com/mdn/content/main/README.md",
                        Language = "en",
                        Domain = "web-docs",
                        LicenseIdentifier = "CC-BY-SA-2.5",
                        LicenseReference = "https://github.com/mdn/content/blob/main/LICENSE.md",
                        AttributionRequirement = "MDN Web Docs contributors; source URL retained in lineage",
                        AllowedContentTypes = ["text/plain", "text/markdown", "application/octet-stream"],
                        ExcludedNamespacesOrPaths = ["files/", "translated-content/"],
                        RefreshMode = "conditional-http",
                        RateLimit = "1 request/minute",
                        MaximumBytesPerCycle = 256 * 1024,
                        MaximumDocumentsPerCycle = 1,
                        TrainingRoute = "mixed",
                        TrustLevel = "official-allowlisted",
                        LastLegalReviewDate = "2026-07-13"
                    },
                    new ContinuousSource
                    {
                        Id = "mdn-js-introduction",
                        Type = "http-document",
                        Enabled = true,
                        OfficialLocation = "https://raw.githubusercontent.com/mdn/content/main/files/en-us/web/javascript/guide/introduction/index.md",
                        Language = "en",
                        Domain = "web-docs-javascript",
                        LicenseIdentifier = "CC-BY-SA-2.5",
                        LicenseReference = "https://github.com/mdn/content/blob/main/LICENSE.md",
                        AttributionRequirement = "MDN Web Docs contributors; source URL retained in lineage",
                        AllowedContentTypes = ["text/plain", "text/markdown", "application/octet-stream"],
                        ExcludedNamespacesOrPaths = ["files/", "translated-content/"],
                        RefreshMode = "conditional-http",
                        RateLimit = "1 request/minute",
                        MaximumBytesPerCycle = 256 * 1024,
                        MaximumDocumentsPerCycle = 1,
                        TrainingRoute = "mixed",
                        TrustLevel = "official-allowlisted",
                        LastLegalReviewDate = "2026-07-13"
                    },
                    new ContinuousSource
                    {
                        Id = "arabic-wikipedia-incremental",
                        Type = "wikimedia-api",
                        Enabled = false,
                        OfficialLocation = "https://ar.wikipedia.org/w/api.php",
                        Language = "ar",
                        Domain = "encyclopedic",
                        LicenseIdentifier = "CC-BY-SA-4.0",
                        LicenseReference = "https://foundation.wikimedia.org/wiki/Policy:Terms_of_Use",
                        AttributionRequirement = "page/revision attribution required",
                        AllowedContentTypes = ["application/json"],
                        ExcludedNamespacesOrPaths = ["user", "talk", "file", "category", "template"],
                        RefreshMode = "revision-cursor",
                        RateLimit = "serial maxlag bounded",
                        MaximumBytesPerCycle = 512 * 1024,
                        MaximumDocumentsPerCycle = 2,
                        TrainingRoute = "mixed",
                        TrustLevel = "official-disabled-until-cursor-tested",
                        LastLegalReviewDate = "2026-07-13"
                    }
                ]);
            await WriteJsonAtomicAsync(sourcesPath, registry, cancellationToken);
        }

        var codeReposPath = Path.Combine(configDirectory, "code-repositories.json");
        if (!File.Exists(codeReposPath))
        {
            var repos = new
            {
                version = 1,
                enabledByDefault = false,
                repositories = new[]
                {
                    Repo("dotnet/runtime", "https://github.com/dotnet/runtime.git", "MIT"),
                    Repo("dotnet/aspnetcore", "https://github.com/dotnet/aspnetcore.git", "MIT"),
                    Repo("dotnet/roslyn", "https://github.com/dotnet/roslyn.git", "MIT"),
                    Repo("microsoft/TypeScript", "https://github.com/microsoft/TypeScript.git", "Apache-2.0"),
                    Repo("angular/angular", "https://github.com/angular/angular.git", "MIT"),
                    Repo("fmtlib/fmt", "https://github.com/fmtlib/fmt.git", "MIT"),
                    Repo("nlohmann/json", "https://github.com/nlohmann/json.git", "MIT"),
                    Repo("google/googletest", "https://github.com/google/googletest.git", "BSD-3-Clause")
                }
            };
            await WriteJsonAtomicAsync(codeReposPath, repos, cancellationToken);
        }

        static object Repo(string id, string url, string license) => new
        {
            id,
            enabled = false,
            url,
            licenseIdentifier = license,
            refreshMode = "shallow-fetch",
            excludedPaths = new[] { ".git", "bin", "obj", "dist", "build", "node_modules", "third_party", "vendor" }
        };
    }

    public async Task<IReadOnlyList<ContinuousSource>> LoadSourcesAsync(string configPath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(Path.GetFullPath(configPath));
        var registry = await JsonSerializer.DeserializeAsync<ContinuousSourceRegistry>(stream, JsonOptions, cancellationToken)
                       ?? new ContinuousSourceRegistry(1, []);
        foreach (var source in registry.Sources.Where(source => source.Enabled))
        {
            ValidateSource(source);
        }

        return registry.Sources;
    }

    public async Task<ContinuousRunReport> RunOnceAsync(ContinuousLearningOptions options, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(options.RunDirectory);
        await EnsureRuntimeDirectoriesAsync(options, cancellationToken);
        var sources = await LoadSourcesAsync(Path.Combine(options.WorkspaceRoot, options.ConfigPath), cancellationToken);
        var tokenizer = await ResolveTokenizerAsync(options.TokenizerPath, cancellationToken);
        var (status, _) = await ExecuteCycleAsync(options, sources, tokenizer, null, cancellationToken);
        return BuildReport(options, status);
    }

    public async Task<ContinuousRunReport> RunAsync(ContinuousLearningOptions options, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(options.RunDirectory);
        await EnsureRuntimeDirectoriesAsync(options, cancellationToken);
        var lockPath = LockPath(options);
        if (File.Exists(lockPath))
        {
            throw new InvalidOperationException($"Continuous run lock already exists: {lockPath}");
        }

        await File.WriteAllTextAsync(lockPath, Environment.ProcessId.ToString(), cancellationToken);
        try
        {
            var sources = await LoadSourcesAsync(Path.Combine(options.WorkspaceRoot, options.ConfigPath), cancellationToken);
            var tokenizer = await ResolveTokenizerAsync(options.TokenizerPath, cancellationToken);
            TorchTransformerLanguageModel? model = null;
            var started = DateTimeOffset.UtcNow;
            var cycle = 0;
            var lastStatus = new ContinuousLearningStatus { RunId = options.RunId, State = "starting" };
            while (!cancellationToken.IsCancellationRequested)
            {
                if (File.Exists(StopPath(options)))
                {
                    lastStatus = await SaveEmergencyCheckpointAsync(options, tokenizer, model, lastStatus, "stop requested", cancellationToken);
                    await WriteStatusAsync(options, lastStatus with { State = "stopped", StopRequested = true }, cancellationToken);
                    break;
                }

                var (status, trainedModel) = await ExecuteCycleAsync(options, sources, tokenizer, model, cancellationToken);
                model = trainedModel;
                lastStatus = status;
                cycle++;

                if (options.MaxCycles > 0 && cycle >= options.MaxCycles)
                {
                    lastStatus = await SaveEmergencyCheckpointAsync(options, tokenizer, model, status, "max cycles reached", cancellationToken);
                    await WriteStatusAsync(options, lastStatus with { State = "stopped", Message = "max cycles reached" }, cancellationToken);
                    break;
                }

                if (options.StopAfterTokens > 0 && status.ConsumedTokens >= options.StopAfterTokens)
                {
                    lastStatus = await SaveEmergencyCheckpointAsync(options, tokenizer, model, status, "token target reached", cancellationToken);
                    await WriteStatusAsync(options, lastStatus with { State = "stopped", Message = "token target reached" }, cancellationToken);
                    break;
                }

                if (options.StopAfterHours > 0 && (DateTimeOffset.UtcNow - started).TotalHours >= options.StopAfterHours)
                {
                    lastStatus = await SaveEmergencyCheckpointAsync(options, tokenizer, model, status, "hour target reached", cancellationToken);
                    await WriteStatusAsync(options, lastStatus with { State = "stopped", Message = "hour target reached" }, cancellationToken);
                    break;
                }

                await Task.Delay(options.Rehearsal ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(15), cancellationToken);
            }

            return BuildReport(options, lastStatus);
        }
        finally
        {
            if (File.Exists(lockPath))
            {
                File.Delete(lockPath);
            }
        }
    }

    public async Task<ContinuousLearningStatus> ReadStatusAsync(string runDirectory, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(Path.GetFullPath(runDirectory), "status.json");
        if (!File.Exists(path))
        {
            return new ContinuousLearningStatus { State = "missing", Message = $"No status file: {path}" };
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ContinuousLearningStatus>(stream, JsonOptions, cancellationToken)
               ?? new ContinuousLearningStatus { State = "invalid" };
    }

    public void RequestStop(string runDirectory)
    {
        Directory.CreateDirectory(Path.GetFullPath(runDirectory));
        File.WriteAllText(Path.Combine(Path.GetFullPath(runDirectory), "STOP_REQUESTED"), DateTimeOffset.UtcNow.ToString("O"));
    }

    private async Task<(ContinuousLearningStatus Status, TorchTransformerLanguageModel? Model)> ExecuteCycleAsync(
        ContinuousLearningOptions options,
        IReadOnlyList<ContinuousSource> sources,
        ITextTokenizer tokenizer,
        TorchTransformerLanguageModel? model,
        CancellationToken cancellationToken)
    {
        var runState = await LoadCountersAsync(options, cancellationToken);
        var resources = ContinuousResourceSnapshot.Capture(options);
        var enabled = sources.Where(source => source.Enabled).ToArray();
        var status = BaseStatus(options, resources, enabled.Length, "ingesting", runState);
        await WriteStatusAsync(options, status, cancellationToken);

        if (resources.State is "MemoryPressure" or "DiskPressure")
        {
            status = status with { State = "paused", Message = resources.State };
            await WriteStatusAsync(options, status, cancellationToken);
            return (status, model);
        }

        var fetched = new List<ContinuousDocument>();
        foreach (var source in enabled)
        {
            status = status with { CurrentSource = source.Id };
            await WriteStatusAsync(options, status, cancellationToken);
            fetched.AddRange(await FetchSourceAsync(options, source, cancellationToken));
        }

        var accepted = new List<ContinuousAcceptedDocument>();
        foreach (var document in fetched)
        {
            var acceptedDocument = await ProcessDocumentAsync(options, document, tokenizer, cancellationToken);
            if (acceptedDocument is not null)
            {
                accepted.Add(acceptedDocument);
            }
        }

        runState.FetchedDocuments += fetched.Count;
        runState.AcceptedDocuments += accepted.Count;
        runState.RejectedDocuments += fetched.Count - accepted.Count;
        runState.NeuralDocuments += accepted.Count(document => document.Route is "neural" or "mixed");
        runState.ConceptDocuments += accepted.Count(document => document.Route is "concept" or "mixed");
        runState.NewTokens += accepted.Sum(document => document.TokenCount);
        runState.QueuedTokens += accepted.Sum(document => document.TokenCount);

        var mixedTokens = await BuildMixedBatchAsync(options, tokenizer, accepted, cancellationToken);
        runState.ReplayTokens += Math.Max(0, mixedTokens.Count - accepted.Sum(document => document.TokenCount));

        if (mixedTokens.Count > options.Context + 1)
        {
            (model, var trainStatus) = await TrainCycleAsync(options, tokenizer, model, mixedTokens, runState, cancellationToken);
            status = trainStatus;
        }
        else
        {
            status = BaseStatus(options, ContinuousResourceSnapshot.Capture(options), enabled.Length, "idle", runState)
                with { Message = "No trainable mixed batch yet." };
            await WriteStatusAsync(options, status, cancellationToken);
        }

        await SaveCountersAsync(options, runState, cancellationToken);
        return (status, model);
    }

    private async Task<IReadOnlyList<ContinuousDocument>> FetchSourceAsync(
        ContinuousLearningOptions options,
        ContinuousSource source,
        CancellationToken cancellationToken)
    {
        var documents = new List<ContinuousDocument>();
        if (source.Type.Equals("owned-curriculum", StringComparison.OrdinalIgnoreCase))
        {
            for (var index = 0; index < Math.Max(1, source.MaximumDocumentsPerCycle); index++)
            {
                var id = $"{source.Id}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{index}";
                var curriculumText = BuildOwnedCurriculum(id);
                documents.Add(new ContinuousDocument(source, id, source.OfficialLocation, curriculumText, Encoding.UTF8.GetByteCount(curriculumText), "text/plain", null, null));
            }

            return documents;
        }

        if (options.Offline || !source.Type.Equals("http-document", StringComparison.OrdinalIgnoreCase))
        {
            return documents;
        }

        var uri = new Uri(source.OfficialLocation);
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotModified)
        {
            return documents;
        }

        response.EnsureSuccessStatusCode();
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        if (source.AllowedContentTypes.Length > 0 &&
            !source.AllowedContentTypes.Any(allowed => contentType.Contains(allowed, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException($"Source {source.Id} returned disallowed content type {contentType}.");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length > source.MaximumBytesPerCycle)
        {
            throw new InvalidDataException($"Source {source.Id} exceeded per-cycle byte cap.");
        }

        var text = Encoding.UTF8.GetString(bytes);
        documents.Add(new ContinuousDocument(
            source,
            $"{source.Id}-{Sha256Hex(bytes)[..16]}",
            source.OfficialLocation,
            text,
            bytes.Length,
            contentType,
            response.Headers.ETag?.Tag,
            response.Content.Headers.LastModified?.ToString("O")));
        return documents;
    }

    private async Task<ContinuousAcceptedDocument?> ProcessDocumentAsync(
        ContinuousLearningOptions options,
        ContinuousDocument document,
        ITextTokenizer tokenizer,
        CancellationToken cancellationToken)
    {
        var incomingPath = Path.Combine(options.ContinuousRoot, "spool", "incoming", $"{SafeFileName(document.Id)}.txt");
        await File.WriteAllTextAsync(incomingPath, document.Text, cancellationToken);
        var trainingText = document.Source.TrustLevel.Contains("official-allowlisted", StringComparison.OrdinalIgnoreCase)
            ? RedactHighEntropyIdentifiers(document.Text)
            : document.Text;
        var normalized = normalizer.Normalize(trainingText);
        var qualityReport = quality.Analyze(normalized);
        var poison = PoisonScore(normalized);
        if (!qualityReport.Accepted || poison > 0.8)
        {
            await WriteRejectionAsync(options, document, qualityReport.RejectionReasons.Concat(poison > 0.8 ? ["poison_score"] : []).ToArray(), cancellationToken);
            MoveFile(incomingPath, Path.Combine(options.ContinuousRoot, "spool", "failed", Path.GetFileName(incomingPath)));
            return null;
        }

        var dedup = deduper.InspectAndRemember(document.Text, normalized);
        if (!dedup.Accepted)
        {
            await WriteRejectionAsync(options, document, [dedup.RejectionReason ?? "duplicate"], cancellationToken);
            MoveFile(incomingPath, Path.Combine(options.ContinuousRoot, "spool", "failed", Path.GetFileName(incomingPath)));
            return null;
        }

        var route = RouteDocument(document.Source, normalized);
        var acceptedPath = Path.Combine(options.ContinuousRoot, "spool", "accepted", $"{SafeFileName(document.Id)}.txt");
        await File.WriteAllTextAsync(acceptedPath, normalized, cancellationToken);
        File.Delete(incomingPath);

        var tokens = tokenizer.Encode(normalized).ToArray();
        var accepted = new ContinuousAcceptedDocument(document, normalized, route, tokens.Length, dedup.NormalizedHash, acceptedPath);
        await WriteLineJsonAsync(Path.Combine(options.ContinuousRoot, "lineage", "accepted.jsonl"), new
        {
            sourceId = document.Source.Id,
            document.Id,
            document.Uri,
            document.RetrievedUtc,
            document.ContentType,
            document.ByteLength,
            sha256 = dedup.DocumentHash,
            normalizedSha256 = dedup.NormalizedHash,
            declaredLicense = document.Source.LicenseIdentifier,
            licenseReference = document.Source.LicenseReference,
            attribution = document.Source.AttributionRequirement,
            route,
            tokenCount = tokens.Length,
            tokenizer = tokenizer.GetType().Name,
            filterVersion = "continuous-filter-v1"
        }, cancellationToken);

        if (route is "concept" or "mixed")
        {
            await WriteLineJsonAsync(Path.Combine(options.ContinuousRoot, "concept-memory", "facts.jsonl"), new
            {
                concept = ExtractConcept(normalized),
                relation = "observed_from_source",
                value = normalized.Length > 500 ? normalized[..500] : normalized,
                validFrom = document.RetrievedUtc,
                retrievedUtc = document.RetrievedUtc,
                source = document.Uri,
                confidence = 0.55,
                license = document.Source.LicenseIdentifier,
                provenance = dedup.NormalizedHash
            }, cancellationToken);
        }

        if (route is "neural" or "mixed")
        {
            var segmentPath = Path.Combine(options.ContinuousRoot, "token-queue", "pending", $"{SafeFileName(document.Id)}.tokens.json");
            await WriteJsonAtomicAsync(segmentPath, new
            {
                documentId = document.Id,
                sourceId = document.Source.Id,
                route,
                license = document.Source.LicenseIdentifier,
                normalizedSha256 = dedup.NormalizedHash,
                tokenCount = tokens.Length,
                tokens
            }, cancellationToken);
        }

        return accepted;
    }

    private async Task<IReadOnlyList<int>> BuildMixedBatchAsync(
        ContinuousLearningOptions options,
        ITextTokenizer tokenizer,
        IReadOnlyList<ContinuousAcceptedDocument> accepted,
        CancellationToken cancellationToken)
    {
        var tokens = new List<int>();
        foreach (var document in accepted.Where(document => document.Route is "neural" or "mixed"))
        {
            tokens.AddRange(tokenizer.Encode(document.NormalizedText));
        }

        var replayPath = Path.Combine(Path.GetFullPath(options.DataDirectory), "splits", "local-corpus-v1", "train");
        if (Directory.Exists(replayPath))
        {
            foreach (var file in Directory.EnumerateFiles(replayPath, "*.txt").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Take(3))
            {
                var text = await File.ReadAllTextAsync(file, cancellationToken);
                tokens.AddRange(tokenizer.Encode(text).Take(Math.Max(options.Context * 2, 256)));
            }
        }

        var ownedRepair = BuildOwnedCurriculum($"repair-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}");
        tokens.AddRange(tokenizer.Encode(ownedRepair));
        await WriteLineJsonAsync(Path.Combine(options.ContinuousRoot, "mixer", "batches.jsonl"), new
        {
            createdUtc = DateTimeOffset.UtcNow,
            newDocuments = accepted.Count,
            newTokens = accepted.Sum(document => document.TokenCount),
            totalMixedTokens = tokens.Count,
            targetRatio = new { newest = 0.40, replay = 0.35, owned = 0.15, repair = 0.10 }
        }, cancellationToken);
        return tokens;
    }

    private async Task<(TorchTransformerLanguageModel Model, ContinuousLearningStatus Status)> TrainCycleAsync(
        ContinuousLearningOptions options,
        ITextTokenizer tokenizer,
        TorchTransformerLanguageModel? model,
        IReadOnlyList<int> tokens,
        ContinuousCounters counters,
        CancellationToken cancellationToken)
    {
        model ??= await LoadOrCreateModelAsync(options, tokenizer, cancellationToken);
        var windows = CreateTokenWindows(tokens, options.Context, Math.Max(options.StepsPerCycle + 2, 4)).ToArray();
        var trainingOptions = new TorchTrainingOptions
        {
            RunId = options.RunId,
            MaxOptimizerSteps = options.StepsPerCycle,
            GradientAccumulationSteps = 1,
            LearningRate = 3e-4,
            MinimumLearningRate = 3e-5,
            WarmupSteps = 1,
            CheckpointEverySteps = 1,
            Seed = 1337
        };
        var trainingDirectory = Path.Combine(options.RunDirectory, "training");
        var report = await new TorchTransformerTrainer(model).TrainAsync(windows, trainingOptions, trainingDirectory, cancellationToken);
        counters.ConsumedTokens += windows.Sum(window => window.Inputs.Length);
        var checkpoint = FindLatestCheckpoint(trainingDirectory);
        var checkpointHash = checkpoint is null ? "" : await Sha256FileAsync(Path.Combine(checkpoint, "model.bin"), cancellationToken);
        var resources = ContinuousResourceSnapshot.Capture(options);
        var status = BaseStatus(options, resources, 0, "running", counters) with
        {
            Step = report.CompletedStep,
            TokensPerSecond = report.TokensPerSecond,
            LastLoss = report.FinalLoss,
            LatestCheckpoint = checkpoint ?? "",
            CheckpointSha256 = checkpointHash,
            Message = "trained continuous replay batch"
        };
        await WriteStatusAsync(options, status, cancellationToken);
        return (model, status);
    }

    private async Task<TorchTransformerLanguageModel> LoadOrCreateModelAsync(
        ContinuousLearningOptions options,
        ITextTokenizer tokenizer,
        CancellationToken cancellationToken)
    {
        var trainingDirectory = Path.Combine(options.RunDirectory, "training");
        var latest = FindLatestCheckpoint(trainingDirectory);
        if (latest is not null)
        {
            return await TorchTransformerCheckpoint.LoadAsync(Path.Combine(latest, "model.bin"), cancellationToken);
        }

        var config = new TorchTransformerConfig(
            tokenizer.VocabularySize,
            options.Context,
            options.Layers,
            options.Width,
            options.Heads,
            options.Ffn,
            Dropout: 0,
            Seed: 1337,
            PaddingToken: tokenizer.PaddingTokenId,
            Device: "cpu",
            TieOutputEmbeddings: true);
        return new TorchTransformerLanguageModel(config);
    }

    private async Task<ContinuousLearningStatus> SaveEmergencyCheckpointAsync(
        ContinuousLearningOptions options,
        ITextTokenizer tokenizer,
        TorchTransformerLanguageModel? model,
        ContinuousLearningStatus status,
        string reason,
        CancellationToken cancellationToken)
    {
        model ??= await LoadOrCreateModelAsync(options, tokenizer, cancellationToken);
        var directory = await TorchCheckpointDirectory.SaveAsync(
            Path.Combine(options.RunDirectory, "training"),
            model,
            new TorchTrainingOptions { RunId = options.RunId, MaxOptimizerSteps = 1, CheckpointEverySteps = 1 },
            cancellationToken);
        var emergencyDirectory = Path.Combine(options.RunDirectory, "checkpoints", "emergency-stop");
        if (Directory.Exists(emergencyDirectory))
        {
            Directory.Delete(emergencyDirectory, recursive: true);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(emergencyDirectory)!);
        CopyDirectory(directory, emergencyDirectory);
        var hash = await Sha256FileAsync(Path.Combine(emergencyDirectory, "model.bin"), cancellationToken);
        return status with
        {
            LatestCheckpoint = emergencyDirectory,
            CheckpointSha256 = hash,
            Message = $"emergency checkpoint saved: {reason}"
        };
    }

    private static IEnumerable<TokenWindow> CreateTokenWindows(IReadOnlyList<int> tokens, int context, int maximumWindows)
    {
        var usable = tokens.Count - context - 1;
        if (usable <= 0)
        {
            yield break;
        }

        for (var window = 0; window < maximumWindows; window++)
        {
            var offset = (window * context) % usable;
            var inputs = new int[context];
            var targets = new int[context];
            for (var index = 0; index < context; index++)
            {
                inputs[index] = tokens[offset + index];
                targets[index] = tokens[offset + index + 1];
            }

            yield return new TokenWindow(inputs, targets, Enumerable.Repeat(true, context).ToArray(), "continuous-mixed-batch", offset);
        }
    }

    private static ContinuousLearningStatus BaseStatus(
        ContinuousLearningOptions options,
        ContinuousResourceSnapshot resources,
        int enabledSources,
        string state,
        ContinuousCounters counters) =>
        new()
        {
            RunId = options.RunId,
            State = state,
            UpdatedUtc = DateTimeOffset.UtcNow,
            SourcesEnabled = enabledSources,
            FetchedDocuments = counters.FetchedDocuments,
            AcceptedDocuments = counters.AcceptedDocuments,
            RejectedDocuments = counters.RejectedDocuments,
            NeuralDocuments = counters.NeuralDocuments,
            ConceptDocuments = counters.ConceptDocuments,
            QueuedTokens = counters.QueuedTokens,
            ConsumedTokens = counters.ConsumedTokens,
            ReplayTokens = counters.ReplayTokens,
            NewTokens = counters.NewTokens,
            ResourceState = resources.State,
            CpuPercent = resources.CpuPercent,
            ProcessWorkingSetBytes = resources.WorkingSetBytes,
            AvailableRamBytes = resources.AvailableRamBytes,
            FreeDiskBytes = resources.FreeDiskBytes,
            SpoolBytes = DirectorySize(Path.Combine(options.ContinuousRoot, "spool")),
            PendingSegments = Directory.Exists(Path.Combine(options.ContinuousRoot, "token-queue", "pending"))
                ? Directory.EnumerateFiles(Path.Combine(options.ContinuousRoot, "token-queue", "pending"), "*.tokens.json").Count()
                : 0,
            StopRequested = File.Exists(StopPath(options))
        };

    private static void ValidateSource(ContinuousSource source)
    {
        if (string.IsNullOrWhiteSpace(source.Id) ||
            string.IsNullOrWhiteSpace(source.Type) ||
            string.IsNullOrWhiteSpace(source.OfficialLocation) ||
            string.IsNullOrWhiteSpace(source.LicenseIdentifier) ||
            string.IsNullOrWhiteSpace(source.LicenseReference) ||
            string.IsNullOrWhiteSpace(source.LastLegalReviewDate))
        {
            throw new InvalidDataException($"Enabled continuous source has incomplete license/provenance metadata: {source.Id}");
        }
    }

    private static string RouteDocument(ContinuousSource source, string text)
    {
        if (source.TrainingRoute.Equals("concept", StringComparison.OrdinalIgnoreCase))
        {
            return "concept";
        }

        if (Regex.IsMatch(text, @"\b(today|current|latest|price|president|release date|breaking|version \d+\.\d+)\b", RegexOptions.IgnoreCase))
        {
            return source.TrainingRoute.Equals("mixed", StringComparison.OrdinalIgnoreCase) ? "mixed" : "concept";
        }

        return source.TrainingRoute.Equals("mixed", StringComparison.OrdinalIgnoreCase) ? "mixed" : "neural";
    }

    private static double PoisonScore(string text)
    {
        var score = 0.0;
        if (Regex.IsMatch(text, @"ignore (all )?(previous|system) instructions", RegexOptions.IgnoreCase))
        {
            score += 0.5;
        }

        if (Regex.IsMatch(text, @"[A-Za-z0-9+/]{200,}={0,2}"))
        {
            score += 0.4;
        }

        if (text.Any(character => char.GetUnicodeCategory(character) is System.Globalization.UnicodeCategory.Format))
        {
            score += 0.3;
        }

        return Math.Min(1, score);
    }

    private static string RedactHighEntropyIdentifiers(string text) =>
        Regex.Replace(
            text,
            @"[A-Za-z0-9+/=_-]{32,}",
            match => Regex.IsMatch(match.Value, @"^(AKIA|gh[pousr]_|sk-)", RegexOptions.IgnoreCase)
                ? "[redacted-secret-like-token]"
                : "[redacted-long-identifier]",
            RegexOptions.Compiled);

    private static string BuildOwnedCurriculum(string id)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# Thoth owned continuous learning item {id}");
        builder.AppendLine("License: Thoth-owned.");
        builder.AppendLine();
        builder.AppendLine("User: \u0627\u0639\u0645\u0644 method \u0628\u0644\u063a\u0629 C# \u062a\u0639\u0645\u0644 calculator \u0648\u062a\u062a\u0639\u0627\u0645\u0644 \u0645\u0639 \u0627\u0644\u0642\u0633\u0645\u0629 \u0639\u0644\u0649 \u0635\u0641\u0631.");
        builder.AppendLine("Assistant: Match the user's language, ask only for missing inputs, and provide a direct safe implementation.");
        builder.AppendLine();
        builder.AppendLine("Stable procedure:");
        builder.AppendLine("1. Identify target programming language.");
        builder.AppendLine("2. Identify operations and validation rules.");
        builder.AppendLine("3. Generate code with explicit divide-by-zero handling.");
        builder.AppendLine("4. Avoid internal diagnostics in the user-facing answer.");
        builder.AppendLine();
        builder.AppendLine("C#:");
        builder.AppendLine("public static decimal Calculate(decimal a, decimal b, string op) => op switch");
        builder.AppendLine("{");
        builder.AppendLine("    \"+\" => a + b,");
        builder.AppendLine("    \"-\" => a - b,");
        builder.AppendLine("    \"*\" => a * b,");
        builder.AppendLine("    \"/\" when b != 0 => a / b,");
        builder.AppendLine("    \"/\" => throw new DivideByZeroException(),");
        builder.AppendLine("    _ => throw new ArgumentOutOfRangeException(nameof(op))");
        builder.AppendLine("};");
        return builder.ToString();
    }

    private static string ExtractConcept(string text)
    {
        var words = Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}#++]{3,}")
            .Select(match => match.Value)
            .Take(6);
        return string.Join(" ", words);
    }

    private static async Task<ITextTokenizer> ResolveTokenizerAsync(string tokenizerPath, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(tokenizerPath) &&
            (File.Exists(tokenizerPath) || Directory.Exists(tokenizerPath)))
        {
            return await BpeTokenizer.LoadAsync(tokenizerPath, cancellationToken);
        }

        return new ByteTokenizer();
    }

    private static async Task EnsureRuntimeDirectoriesAsync(ContinuousLearningOptions options, CancellationToken cancellationToken)
    {
        var directories = new[]
        {
            options.RunDirectory,
            Path.Combine(options.ContinuousRoot, "spool", "incoming"),
            Path.Combine(options.ContinuousRoot, "spool", "quarantine"),
            Path.Combine(options.ContinuousRoot, "spool", "accepted"),
            Path.Combine(options.ContinuousRoot, "spool", "failed"),
            Path.Combine(options.ContinuousRoot, "token-queue", "pending"),
            Path.Combine(options.ContinuousRoot, "token-queue", "leased"),
            Path.Combine(options.ContinuousRoot, "token-queue", "consumed"),
            Path.Combine(options.ContinuousRoot, "lineage"),
            Path.Combine(options.ContinuousRoot, "concept-memory"),
            Path.Combine(options.ContinuousRoot, "mixer")
        };
        foreach (var directory in directories)
        {
            Directory.CreateDirectory(directory);
        }

        await Task.CompletedTask.WaitAsync(cancellationToken);
    }

    private async Task<ContinuousCounters> LoadCountersAsync(ContinuousLearningOptions options, CancellationToken cancellationToken)
    {
        var path = CountersPath(options);
        if (!File.Exists(path))
        {
            return new ContinuousCounters();
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ContinuousCounters>(stream, JsonOptions, cancellationToken) ?? new ContinuousCounters();
    }

    private static async Task SaveCountersAsync(ContinuousLearningOptions options, ContinuousCounters counters, CancellationToken cancellationToken) =>
        await WriteJsonAtomicAsync(CountersPath(options), counters, cancellationToken);

    private static async Task WriteStatusAsync(ContinuousLearningOptions options, ContinuousLearningStatus status, CancellationToken cancellationToken) =>
        await WriteJsonAtomicAsync(StatusPath(options), status, cancellationToken);

    private static async Task WriteRejectionAsync(ContinuousLearningOptions options, ContinuousDocument document, IReadOnlyList<string> reasons, CancellationToken cancellationToken) =>
        await WriteLineJsonAsync(Path.Combine(options.ContinuousRoot, "lineage", "rejected.jsonl"), new
        {
            sourceId = document.Source.Id,
            document.Id,
            document.Uri,
            document.RetrievedUtc,
            reasons,
            declaredLicense = document.Source.LicenseIdentifier
        }, cancellationToken);

    private static ContinuousRunReport BuildReport(ContinuousLearningOptions options, ContinuousLearningStatus status) =>
        new(
            status,
            StatusPath(options),
            $"scripts\\continuous-learning\\Stop-ThothContinuousLearning.ps1 -RunId {options.RunId}",
            $"scripts\\continuous-learning\\Resume-ThothContinuousLearning.ps1 -RunId {options.RunId}",
            $"scripts\\continuous-learning\\Get-ThothContinuousLearningStatus.ps1 -RunId {options.RunId}");

    private static string StatusPath(ContinuousLearningOptions options) => Path.Combine(options.RunDirectory, "status.json");
    private static string CountersPath(ContinuousLearningOptions options) => Path.Combine(options.RunDirectory, "counters.json");
    private static string LockPath(ContinuousLearningOptions options) => Path.Combine(options.RunDirectory, "run.lock");
    private static string StopPath(ContinuousLearningOptions options) => Path.Combine(options.RunDirectory, "STOP_REQUESTED");

    private static string? FindLatestCheckpoint(string runDirectory)
    {
        var root = Path.Combine(Path.GetFullPath(runDirectory), "checkpoints");
        return Directory.Exists(root)
            ? Directory.EnumerateDirectories(root, "step-*", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).LastOrDefault()
            : null;
    }

    private static async Task WriteLineJsonAsync(string path, object value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var line = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)) + Environment.NewLine;
        await File.AppendAllTextAsync(path, line, cancellationToken);
    }

    private static async Task WriteJsonAtomicAsync(string path, object value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var fullPath = Path.GetFullPath(path);
        var tempPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(value, JsonOptions), cancellationToken);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                File.Copy(tempPath, fullPath, overwrite: true);
                File.Delete(tempPath);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                await Task.Delay(100, cancellationToken);
            }
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                await Task.Delay(100, cancellationToken);
            }
        }

        File.Copy(tempPath, fullPath, overwrite: true);
        File.Delete(tempPath);
    }

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static async Task<string> Sha256FileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        return safe.Length > 120 ? safe[..120] : safe;
    }

    private static long DirectorySize(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length)
            : 0;

    private static void MoveFile(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        if (File.Exists(destination))
        {
            File.Delete(destination);
        }

        File.Move(source, destination);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(source, destination, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(source, destination, StringComparison.OrdinalIgnoreCase), overwrite: true);
        }
    }

    private sealed record ContinuousDocument(
        ContinuousSource Source,
        string Id,
        string Uri,
        string Text,
        int ByteLength,
        string ContentType,
        string? ETag,
        string? LastModified)
    {
        public DateTimeOffset RetrievedUtc { get; init; } = DateTimeOffset.UtcNow;
    }

    private sealed record ContinuousAcceptedDocument(
        ContinuousDocument Document,
        string NormalizedText,
        string Route,
        int TokenCount,
        string NormalizedHash,
        string AcceptedPath);

    private sealed record ContinuousCounters
    {
        public long FetchedDocuments { get; set; }
        public long AcceptedDocuments { get; set; }
        public long RejectedDocuments { get; set; }
        public long NeuralDocuments { get; set; }
        public long ConceptDocuments { get; set; }
        public long QueuedTokens { get; set; }
        public long ConsumedTokens { get; set; }
        public long ReplayTokens { get; set; }
        public long NewTokens { get; set; }
    }

    private sealed record ContinuousResourceSnapshot(
        string State,
        double CpuPercent,
        long WorkingSetBytes,
        long AvailableRamBytes,
        long FreeDiskBytes)
    {
        public static ContinuousResourceSnapshot Capture(ContinuousLearningOptions options)
        {
            var process = Process.GetCurrentProcess();
            var memory = MemoryStatus.Query();
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(options.DataDirectory)) ?? "C:\\");
            var ramFloor = (long)(options.RamFloorGb * 1024 * 1024 * 1024);
            var diskFloor = (long)(options.DiskFloorGb * 1024 * 1024 * 1024);
            var state = memory.AvailableBytes < ramFloor
                ? "MemoryPressure"
                : drive.AvailableFreeSpace < diskFloor
                    ? "DiskPressure"
                    : "Aggressive";
            return new ContinuousResourceSnapshot(
                state,
                0,
                process.WorkingSet64,
                memory.AvailableBytes,
                drive.AvailableFreeSpace);
        }
    }

    private sealed record MemoryStatus(long TotalBytes, long AvailableBytes)
    {
        public static MemoryStatus Query()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && TryGlobalMemoryStatus(out var status))
            {
                return new MemoryStatus((long)status.ullTotalPhys, (long)status.ullAvailPhys);
            }

            var info = GC.GetGCMemoryInfo();
            return new MemoryStatus(info.TotalAvailableMemoryBytes, Math.Max(0, info.TotalAvailableMemoryBytes - GC.GetTotalMemory(false)));
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    private static bool TryGlobalMemoryStatus(out MemoryStatusEx status)
    {
        status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status);
    }
}
