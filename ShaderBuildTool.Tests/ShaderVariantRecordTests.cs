using System.Text;
using ShaderBuildTool.Spirv;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace ShaderBuildTool.Tests;

/// <summary>Qualifies emission and interface reuse against real compiler artifacts.</summary>
public sealed class ShaderVariantRecordTests
{
    #region Public API
    /// <summary>Warm reads skip extraction, while missing metadata and extractor changes repair using existing bytes.</summary>
    [Fact]
    public async Task ValidVariantSkipsProcessingAndRepairsInterfaceWithoutCompilation()
    {
        using var fixture = new ShaderBuildFixture();
        var context = await Create(fixture);
        int extractions = 0;
        PackagedShaderInterface Extract(byte[] bytes, ShaderStageSelection selection)
        {
            extractions++;
            return ShaderInterfaceExtraction.Extract(bytes, selection);
        }
        Assert.True(context.Records.TryRead(context.Inputs, context.Selection, context.Compiler, context.Interfaces,
            "extractor", out var warm, extract: (_, _) => throw new InvalidOperationException("Unexpected reflection")));
        Assert.True(warm.InterfaceReused);
        Assert.Equal(context.Binary, warm.Binary);
        string key = ShaderInterfaceCache.Key(warm.Digest.Digest, context.Selection, "extractor");
        File.Delete(Path.Combine(fixture.Output, "_cache", "interfaces", key + ".json"));
        Assert.True(context.Records.TryRead(context.Inputs, context.Selection, context.Compiler, context.Interfaces,
            "extractor", out var repaired, extract: Extract));
        Assert.False(repaired.InterfaceReused);
        Assert.Equal(1, extractions);
        Assert.True(context.Records.TryRead(context.Inputs, context.Selection, context.Compiler, context.Interfaces,
            "extractor-changed", out var refreshed, extract: Extract));
        Assert.False(refreshed.InterfaceReused);
        Assert.Equal(2, extractions);
        Assert.Equal(context.Binary, refreshed.Binary);
        // New processing identities require emission, but byte-equivalent compiler input retains its artifact.
        var changed = context.Inputs with { ExpandedDigest = ShaderBuildIdentities.Hash("changed-source"), Emission = "new-emitter" };
        Assert.False(context.Records.TryRead(changed, context.Selection, context.Compiler, context.Interfaces, "extractor", out _));
        context.Records.Store(changed, context.Selection, context.Text, context.Compiler, context.Interfaces, "extractor");
        Assert.True(context.Records.TryRead(changed, context.Selection, context.Compiler, context.Interfaces, "extractor", out _));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(fixture.Output, "_cache", "variants"), "*.json").Length);
        Assert.Single(Directory.GetFiles(Path.Combine(fixture.Output, "_cache"), "*.bin"));
    }

    /// <summary>Semantically tampered records are rejected even when their envelope digest is recomputed.</summary>
    [Theory]
    [InlineData("schema")]
    [InlineData("contract")]
    [InlineData("compiler-path")]
    [InlineData("emitted")]
    [InlineData("binary")]
    [InlineData("interface")]
    public async Task InvalidVariantAssociationsAreLocalMisses(string change)
    {
        using var fixture = new ShaderBuildFixture();
        var context = await Create(fixture);
        var store = new ShaderRecordStore(fixture.Output, "variants");
        string key = ShaderVariantRecordCache.Key(context.Inputs, context.Selection, context.Compiler);
        Assert.True(store.TryRead<ShaderVariantRecordCache.Record>(key, out var record));
        record = change switch
        {
            "schema" => record with { Version = 99 },
            "contract" => record with { Contract = ShaderBuildIdentities.Hash("other") },
            "compiler-path" => record with { CompilerKey = "../outside" },
            "emitted" => record with { EmittedText = "tampered" },
            "binary" => record with { Binary = record.Binary with { Length = record.Binary.Length + 1 } },
            _ => record with { InterfaceKey = "../outside" }
        };
        store.Store(key, record);
        var misses = new List<string>();
        Assert.False(context.Records.TryRead(context.Inputs, context.Selection, context.Compiler, context.Interfaces,
            "extractor", out _, misses.Add, (_, _) => throw new InvalidOperationException("Unexpected reflection")));
        Assert.NotEmpty(misses);
    }

    /// <summary>Invalid interface schema repairs locally, but a corrupt binary is never passed to reflection.</summary>
    [Fact]
    public async Task InvalidInterfaceRepairsAndInvalidBinaryMisses()
    {
        using var fixture = new ShaderBuildFixture();
        var context = await Create(fixture);
        var store = new ShaderRecordStore(fixture.Output, "interfaces");
        string key = ShaderInterfaceCache.Key(ShaderVariantCache.Digest(context.Binary).Digest, context.Selection, "extractor");
        Assert.True(store.TryRead<ShaderInterfaceCache.Record>(key, out var record));
        store.Store(key, record with { Payload = record.Payload with { Version = 99 } });
        int calls = 0;
        Assert.True(context.Records.TryRead(context.Inputs, context.Selection, context.Compiler, context.Interfaces, "extractor",
            out var repaired, extract: (bytes, selection) => { calls++; return ShaderInterfaceExtraction.Extract(bytes, selection); }));
        Assert.Equal(1, calls);
        Assert.False(repaired.InterfaceReused);
        string binaryPath = Assert.Single(Directory.GetFiles(Path.Combine(fixture.Output, "_cache"), "*.bin"));
        File.WriteAllBytes(binaryPath, [1, 2, 3]);
        Assert.False(context.Records.TryRead(context.Inputs, context.Selection, context.Compiler, context.Interfaces, "extractor", out _,
            extract: (_, _) => throw new InvalidOperationException("Corrupt binary reached reflection")));
    }
    /// <summary>Concurrent aliases extract missing metadata once and reject malformed numeric declarations on reuse.</summary>
    [Fact]
    public async Task InterfaceAliasesExtractOnceAndInvalidNumericShapeRepairs()
    {
        using var fixture = new ShaderBuildFixture();
        var context = await Create(fixture);
        var digest = ShaderVariantCache.Digest(context.Binary);
        int calls = 0;
        await Task.WhenAll(Enumerable.Range(0, 16).Select(alias => Task.Run(() =>
            context.Interfaces.GetOrExtract(context.Binary, digest, context.Selection, "new-extractor", out _,
                extract: (bytes, selection) => { Interlocked.Increment(ref calls); return ShaderInterfaceExtraction.Extract(bytes, selection); }),
            TestContext.Current.CancellationToken)));
        Assert.Equal(1, calls);
        var records = new ShaderRecordStore(fixture.Output, "interfaces");
        string key = ShaderInterfaceCache.Key(digest.Digest, context.Selection, "new-extractor");
        Assert.True(records.TryRead<ShaderInterfaceCache.Record>(key, out var record));
        var invalid = new PackagedInterfaceVariable(0, 0, 0, ShaderScalarType.Float, 32, 5, 1, []);
        records.Store(key, record with { Payload = record.Payload with { Interface = record.Payload.Interface with { Inputs = [invalid] } } });
        context.Interfaces.GetOrExtract(context.Binary, digest, context.Selection, "new-extractor", out bool reused,
            extract: (bytes, selection) => { calls++; return ShaderInterfaceExtraction.Extract(bytes, selection); });
        Assert.False(reused);
        Assert.Equal(2, calls);
    }

    #endregion

    #region Private
    /// <summary>Holds a single actual compile and populated cache owners for each isolated scenario.</summary>
    private sealed record Context(ShaderVariantRecordCache Records, ShaderVariantCache Compiler, ShaderInterfaceCache Interfaces,
        ShaderVariantRecordInputs Inputs, ShaderStageSelection Selection, string Text, byte[] Binary);

    /// <summary>Compiles once, then establishes records through their normal verified-artifact boundary.</summary>
    private static async Task<Context> Create(ShaderBuildFixture fixture)
    {
        string input = Path.Combine(fixture.Shaders, "fixture.csh");
        string output = Path.Combine(fixture.Root, "compiled.spv");
        string text = File.ReadAllText(input);
        var result = await ShaderCompilerProcess.CompileAsync(fixture.Repository, input, output,
            "compute", "opengl4.5", false, "main", TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, result.StandardError);
        byte[] bytes = await File.ReadAllBytesAsync(output, TestContext.Current.CancellationToken);
        var selection = new ShaderStageSelection(new ShaderStageContract("fixture.csh", "fixture.csh", ShaderStageKind.Compute, new()),
            new Dictionary<string, ShaderScalar>());
        var inputs = new ShaderVariantRecordInputs(ShaderRecordStore.Digest(Encoding.UTF8.GetBytes(text)), "emitter", input, fixture.Repository);
        var compiler = new ShaderVariantCache(fixture.Output, "compiler");
        compiler.Store(compiler.Key(text, "compute", "main", input, fixture.Repository), bytes, ShaderVariantCache.Digest(bytes));
        var interfaces = new ShaderInterfaceCache(fixture.Output);
        var records = new ShaderVariantRecordCache(fixture.Output);
        records.Store(inputs, selection, text, compiler, interfaces, "extractor");
        return new(records, compiler, interfaces, inputs, selection, text, bytes);
    }
    #endregion
}
