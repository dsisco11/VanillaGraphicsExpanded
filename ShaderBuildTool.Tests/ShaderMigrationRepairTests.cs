using System.Text.Json.Nodes;
using ShaderBuildTool.Spirv;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace ShaderBuildTool.Tests;

/// <summary>Qualifies upgrade and damaged-state recovery through the selective production pipeline.</summary>
public sealed class ShaderMigrationRepairTests
{
    private const string Domain = "vanillagraphicsexpanded";
    private static readonly ShaderBuildIdentities Identities = new("preprocessor", "emitter", "compiler", "extractor");

    #region Public API
    /// <summary>Every processing family repairs missing, malformed, incompatible and digest-mismatched records locally.</summary>
    [Theory]
    [InlineData("sourceHeads", "schema", 3, 0, 0)]
    [InlineData("sourceHeads", "missing", 3, 0, 0)]
    [InlineData("sourceHeads", "malformed", 3, 0, 0)]
    [InlineData("sourceHeads", "digest", 3, 0, 0)]
    [InlineData("sources", "schema", 3, 0, 0)]
    [InlineData("sources", "missing", 3, 0, 0)]
    [InlineData("sources", "malformed", 3, 0, 0)]
    [InlineData("sources", "digest", 3, 0, 0)]
    [InlineData("variants", "schema", 0, 3, 0)]
    [InlineData("variants", "missing", 0, 3, 0)]
    [InlineData("variants", "malformed", 0, 3, 0)]
    [InlineData("variants", "digest", 0, 3, 0)]
    [InlineData("interfaces", "schema", 0, 0, 3)]
    [InlineData("interfaces", "missing", 0, 0, 3)]
    [InlineData("interfaces", "malformed", 0, 0, 3)]
    [InlineData("interfaces", "digest", 0, 0, 3)]
    public async Task IncompatibleRecordsRepopulateBeforeWarmReuse(string family, string damage, int expansions, int emissions, int extractions)
    {
        using var fixture = new ShaderBuildFixture();
        await Build(fixture);
        var expected = Snapshot(fixture);
        foreach (string path in Directory.GetFiles(Path.Combine(fixture.Output, "_cache", family), "*.json"))
        {
            if (damage == "missing") { File.Delete(path); continue; }
            if (damage == "malformed") { File.WriteAllText(path, "{"); continue; }
            // Keep envelope integrity valid so this exercises the inner schema gate itself.
            var envelope = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var payload = JsonNode.Parse(Convert.FromBase64String(envelope["Payload"]!.GetValue<string>()))!.AsObject();
            if (damage == "schema") payload["Version"] = 99;
            byte[] bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload);
            envelope["Payload"] = Convert.ToBase64String(bytes);
            envelope["Digest"] = damage == "digest" ? new string('0', 64) : ShaderRecordStore.Digest(bytes);
            File.WriteAllText(path, envelope.ToJsonString());
        }
        var repaired = await Build(fixture);
        Assert.Equal(expansions, repaired.RootsExpanded);
        Assert.Equal(emissions, repaired.VariantsEmitted);
        Assert.Equal(extractions, repaired.InterfacesExtracted);
        Assert.Equal(0, repaired.CompilerInvocations);
        Assert.Equal(expected, Snapshot(fixture));
        AssertWarm(await Build(fixture));
    }

    /// <summary>Corrupt compiler metadata is a local compile miss, never a valid binary hit.</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("digest")]
    [InlineData("length")]
    public async Task UnverifiableCompilerMetadataRecompilesOnlyItsVariant(string damage)
    {
        using var fixture = new ShaderBuildFixture();
        await Build(fixture);
        var expected = Snapshot(fixture);
        string path = Directory.GetFiles(Path.Combine(fixture.Output, "_cache"), "*.json").Single(p =>
            Path.GetFileNameWithoutExtension(p) == CompilerKeyFor(fixture, "fixture.csh.spv"));
        if (damage == "missing") File.Delete(path);
        else if (damage == "malformed") File.WriteAllText(path, "{");
        else
        {
            var metadata = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            metadata[damage == "digest" ? "Digest" : "Length"] = damage == "digest"
                ? JsonValue.Create(new string('0', 64)) : JsonValue.Create(1);
            File.WriteAllText(path, metadata.ToJsonString());
        }
        var repaired = await Build(fixture);
        Assert.Equal(0, repaired.RootsExpanded);
        Assert.Equal(1, repaired.VariantsEmitted);
        Assert.Equal(2, repaired.VariantsReused);
        Assert.Equal(1, repaired.CompilerInvocations);
        Assert.Equal(expected, Snapshot(fixture));
        AssertWarm(await Build(fixture));
    }

    /// <summary>Receipt and manifest damage cannot bypass validation or force unrelated compiler work.</summary>
    [Theory]
    [InlineData("receipt", "missing")]
    [InlineData("receipt", "malformed")]
    [InlineData("receipt", "schema")]
    [InlineData("manifest", "missing")]
    [InlineData("manifest", "malformed")]
    [InlineData("manifest", "schema")]
    public void PublishedMetadataRepairsThroughInvocation(string artifact, string damage)
    {
        using var fixture = new ShaderBuildFixture();
        Assert.Equal(0, fixture.Build(2));
        var expected = Snapshot(fixture);
        var cacheTimes = Directory.GetFiles(Path.Combine(fixture.Output, "_cache"), "*.bin")
            .ToDictionary(p => p, File.GetLastWriteTimeUtc);
        string path = artifact == "receipt" ? Path.Combine(fixture.Output, "build-receipt.json")
            : Path.Combine(Published(fixture), ShaderBinaryDigest.FileName);
        if (damage == "missing") File.Delete(path);
        else if (damage == "malformed") File.WriteAllText(path, "{");
        else
        {
            var value = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            string version = value.Select(p => p.Key).Single(k => k.Equals("version", StringComparison.OrdinalIgnoreCase));
            value[version] = 99;
            File.WriteAllText(path, value.ToJsonString());
        }
        Assert.Equal(0, fixture.Build(2, clean: false, incremental: true));
        Assert.Equal(expected, Snapshot(fixture));
        Assert.All(cacheTimes, p => Assert.Equal(p.Value, File.GetLastWriteTimeUtc(p.Key)));
        Assert.True(File.Exists(Path.Combine(fixture.Output, "build-receipt.json")));
    }

    /// <summary>An upgrade without processing records cold-fills incompatible compiler keys and then reuses edits and reversions.</summary>
    [Fact]
    public async Task LegacyCacheColdFillsOnceAndRetainsHistoryUntilClean()
    {
        using var fixture = new ShaderBuildFixture();
        await Build(fixture);
        var original = Snapshot(fixture);
        string cache = Path.Combine(fixture.Output, "_cache");
        foreach (string directory in Directory.GetDirectories(cache)) Directory.Delete(directory, true);
        // Historical hexadecimal filenames cannot prove the current policy identity, even with valid bytes.
        foreach (string path in Directory.GetFiles(cache).Where(p => p.EndsWith(".bin") || p.EndsWith(".json")))
        {
            if (!ShaderCacheKey.IsValid(Path.GetFileNameWithoutExtension(path))) continue;
            string legacy = ShaderRecordStore.Digest(System.Text.Encoding.UTF8.GetBytes(Path.GetFileNameWithoutExtension(path)));
            File.Move(path, Path.Combine(cache, legacy + Path.GetExtension(path)));
        }
        string[] history = Directory.GetFiles(cache, "*.bin");
        Assert.Equal(3, (await Build(fixture)).CompilerInvocations);
        AssertWarm(await Build(fixture));
        string include = Path.Combine(fixture.Shaders, "fixture.inc");
        string before = File.ReadAllText(include);
        File.WriteAllText(include, before.Replace("0.5", "0.7"));
        var edit = await Build(fixture);
        Assert.Equal(1, edit.RootsExpanded);
        Assert.Equal(1, edit.CompilerInvocations);
        File.WriteAllText(include, before);
        Assert.Equal(0, (await Build(fixture)).CompilerInvocations);
        Assert.Equal(original, Snapshot(fixture));
        Assert.All(history, p => Assert.True(File.Exists(p)));
        Assert.Equal(0, fixture.Build(2, clean: true, incremental: true));
        Assert.All(history, p => Assert.False(File.Exists(p)));
        Assert.Equal(original, Snapshot(fixture));
    }

    /// <summary>Reflection rejection after successful compilation keeps cache work but cannot publish a partial catalogue.</summary>
    [Fact]
    public void ReflectionFailurePreservesGenerationAndSuccessfulCompilerArtifact()
    {
        using var fixture = new ShaderBuildFixture();
        Assert.Equal(0, fixture.Build(2));
        var expected = Snapshot(fixture);
        string vertex = Path.Combine(fixture.Shaders, "fixture.vsh");
        string original = File.ReadAllText(vertex);
        string cache = Path.Combine(fixture.Output, "_cache");
        var before = Directory.GetFiles(cache, "*.bin").ToHashSet(StringComparer.Ordinal);
        // A legal GLSL interface block compiles, but the owning reflector explicitly rejects member reflection.
        File.WriteAllText(vertex, "#version 450 core\nlayout(location=0) in vec3 position; layout(location=0) out Block { vec3 color; } blockValue; void main(){gl_Position=vec4(position,1);blockValue.color=vec3(1);}");
        Assert.Equal(1, fixture.Build(2, clean: false, incremental: true));
        Assert.False(File.Exists(Path.Combine(fixture.Output, "build-receipt.json")));
        Assert.Equal(expected, Snapshot(fixture));
        string added = Assert.Single(Directory.GetFiles(cache, "*.bin").Except(before));
        Assert.True(new FileInfo(added).Length > 0);
        DateTime compiled = File.GetLastWriteTimeUtc(added);
        Assert.Equal(1, fixture.Build(2, clean: false, incremental: true));
        Assert.Equal(compiled, File.GetLastWriteTimeUtc(added));
        Assert.Equal(expected, Snapshot(fixture));
        File.WriteAllText(vertex, original);
        Assert.Equal(0, fixture.Build(2, clean: false, incremental: true));
        Assert.Equal(expected, Snapshot(fixture));
    }

    /// <summary>A cancelled invocation invalidates success and leaves the complete previous generation available.</summary>
    [Fact]
    public async Task CancelledInvocationPreservesGenerationAndCanResume()
    {
        using var fixture = new ShaderBuildFixture();
        Assert.Equal(0, fixture.Build(2));
        var expected = Snapshot(fixture);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ShaderBuildInvocation.RunAsync(fixture.Assets,
            fixture.Output, Domain, fixture.Repository, "opengl4.5", false, () => BuildValidationShaderPrograms.Create(),
            "build-validation", 2, true, false, true, cancelled.Token));
        Assert.False(File.Exists(Path.Combine(fixture.Output, "build-receipt.json")));
        Assert.Equal(expected, Snapshot(fixture));
        Assert.Equal(0, fixture.Build(2, clean: false, incremental: true));
        Assert.Equal(expected, Snapshot(fixture));
    }
    #endregion

    #region Private
    /// <summary>Uses stable implementation identities to isolate repair work from executable rebuilding.</summary>
    private static Task<ShaderBuildStatistics> Build(ShaderBuildFixture fixture) => ShaderVariantBuild.RunAsync(
        fixture.Assets, fixture.Output, Domain, fixture.Repository, "opengl4.5", false,
        BuildValidationShaderPrograms.Create(), 2, TestContext.Current.CancellationToken, incremental: true,
        execution: new ShaderBuildExecution(Identities, new ShaderFileHashIndex(fixture.Output, true)));

    /// <summary>Finds the compiler association by matching the variant record artifact to the published bytes.</summary>
    private static string CompilerKeyFor(ShaderBuildFixture fixture, string binary)
    {
        foreach (string path in Directory.GetFiles(Path.Combine(fixture.Output, "_cache", "variants"), "*.json"))
        {
            var envelope = JsonNode.Parse(File.ReadAllText(path))!;
            var payload = JsonNode.Parse(Convert.FromBase64String(envelope["Payload"]!.GetValue<string>()))!;
            string key = payload["CompilerKey"]!.GetValue<string>();
            byte[] cached = File.ReadAllBytes(Path.Combine(fixture.Output, "_cache", key + ".bin"));
            if (cached.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(Published(fixture), binary)))) return key;
        }
        throw new InvalidDataException("Fixture compiler association missing.");
    }

    /// <summary>Checks that all expensive processing operations are skipped after repair.</summary>
    private static void AssertWarm(ShaderBuildStatistics statistics)
    {
        Assert.Equal(0, statistics.RootsExpanded);
        Assert.Equal(0, statistics.VariantsEmitted);
        Assert.Equal(0, statistics.CompilerInvocations);
        Assert.Equal(0, statistics.InterfacesExtracted);
        Assert.Equal(3, statistics.VariantsReused);
    }

    /// <summary>Locates runtime outputs independently of private cache and work files.</summary>
    private static string Published(ShaderBuildFixture fixture) => Path.Combine(fixture.Output, Domain, "shaders");

    /// <summary>Compares binaries and packaged manifests at identical paths, including Debug source attribution.</summary>
    private static string[] Snapshot(ShaderBuildFixture fixture) => Directory.GetFiles(Published(fixture)).Order(StringComparer.Ordinal)
        .Select(p => Path.GetFileName(p) + ":" + Convert.ToBase64String(File.ReadAllBytes(p))).ToArray();
    #endregion
}
