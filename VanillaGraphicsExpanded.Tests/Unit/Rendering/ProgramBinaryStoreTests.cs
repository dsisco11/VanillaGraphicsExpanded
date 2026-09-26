using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;
using System.Security.Cryptography;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks that the optional disk cache remains bounded and recovers from damaged entries.</summary>
public sealed class ProgramBinaryStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "VGE.ProgramBinaryTests", Guid.NewGuid().ToString("N"));
    private static readonly string First = new('a', 64), Second = new('b', 64);

    #region Storage behavior
    /// <summary>Persists the format and exact bytes through a newly created store owner.</summary>
    [Fact]
    public void RoundTripSurvivesNewOwnerAndRemoval()
    {
        var store = new ProgramBinaryStore(directory);
        Assert.False(store.TryRead(First, out _, out _));
        store.Write(First, 17, [1, 3, 7, 9]);
        var reopened = new ProgramBinaryStore(directory);
        Assert.True(reopened.TryRead(First, out int format, out byte[] data));
        Assert.Equal(17, format);
        Assert.Equal(new byte[] { 1, 3, 7, 9 }, data);
        reopened.Remove(First);
        Assert.False(reopened.TryRead(First, out _, out _));
    }

    /// <summary>A damaged index is a miss and does not prevent rebuilding the disposable cache.</summary>
    [Fact]
    public void CorruptIndexFallsBackAndCanBeRepopulated()
    {
        var store = new ProgramBinaryStore(directory);
        store.Write(First, 1, [1, 2, 3]);
        string index = Assert.Single(Directory.GetFiles(directory, "*.json"));
        File.WriteAllText(index, "not json");
        var reopened = new ProgramBinaryStore(directory);
        Assert.False(reopened.TryRead(First, out _, out _));
        reopened.Write(Second, 2, [4, 5]);
        Assert.True(reopened.TryRead(Second, out int format, out byte[] data));
        Assert.Equal(2, format);
        Assert.Equal(new byte[] { 4, 5 }, data);
    }

    /// <summary>A single-entry budget evicts the older executable while retaining the new one.</summary>
    [Fact]
    public void EntryBudgetIsEnforced()
    {
        var store = new ProgramBinaryStore(directory, maxEntries: 1);
        store.Write(First, 1, [1, 2]);
        store.Write(Second, 2, [3, 4]);
        Assert.False(store.TryRead(First, out _, out _));
        Assert.True(store.TryRead(Second, out _, out _));
    }

    /// <summary>Driver, stage content and specialization changes select distinct executables.</summary>
    [Fact]
    public void KeyTracksExactInputsAndBypassHasNoKey()
    {
        var store = new ProgramBinaryStore(directory);
        var stage = new ShaderStageContract("test.csh", "test.csh", ShaderStageKind.Compute,
            new GpuBindingContract(), specializations: [new ShaderSpecialization(1, LumOnShaderOptions.DirectVisibility)]);
        var settings = new ShaderSettings(new GpuShaderContract("test", [stage], 1, [LumOnShaderOptions.DirectVisibility]));
        var plan = new ShaderLoadPlan(settings);
        byte[] bytes = [1, 2, 3];
        ReadOnlySpan<byte> Read(string path) => path == ShaderBinaryDigest.FileName
            ? ShaderBinaryDigest.Encode(new Dictionary<string, ShaderBinaryDigest.Entry>
            { [plan.Stages[0].BinaryPath] = new(bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))) }) : bytes;
        var baseline = new PreparedProgramBinary(plan, Read, store, "driver-a", () => ShaderBinaryDigest.Parse(Read(ShaderBinaryDigest.FileName)));
        Assert.Equal(baseline.Key, new PreparedProgramBinary(plan, Read, store, "driver-a", () => ShaderBinaryDigest.Parse(Read(ShaderBinaryDigest.FileName))).Key);
        Assert.NotEqual(baseline.Key, new PreparedProgramBinary(plan, Read, store, "driver-b", () => ShaderBinaryDigest.Parse(Read(ShaderBinaryDigest.FileName))).Key);
        bytes = [1, 2, 4];
        Assert.NotEqual(baseline.Key, new PreparedProgramBinary(plan, Read, store, "driver-a", () => ShaderBinaryDigest.Parse(Read(ShaderBinaryDigest.FileName))).Key);
        bytes = [1, 2, 3];
        Assert.Equal(new byte[] { 1, 2, 3 }, baseline.Read(plan.Stages[0].BinaryPath).ToArray());
        Assert.Null(new PreparedProgramBinary(plan, Read, null, "driver-a", () => ShaderBinaryDigest.Parse(Read(ShaderBinaryDigest.FileName))).Key);
        var changed = new ShaderLoadPlan(settings.With(LumOnShaderOptions.DirectVisibility, true));
        Assert.NotEqual(baseline.Key, new PreparedProgramBinary(changed, Read, store, "driver-a", () => ShaderBinaryDigest.Parse(Read(ShaderBinaryDigest.FileName))).Key);
        var otherEntry = new ShaderStageContract("test.csh", "test.csh", ShaderStageKind.Compute,
            new GpuBindingContract(), specializations: stage.Specializations, entryPoint: "other");
        var entryPlan = new ShaderLoadPlan(new ShaderSettings(new GpuShaderContract("test", [otherEntry], 1, [LumOnShaderOptions.DirectVisibility])));
        Assert.NotEqual(baseline.Key, new PreparedProgramBinary(entryPlan, Read, store, "driver-a", () => ShaderBinaryDigest.Parse(Read(ShaderBinaryDigest.FileName))).Key);
    }

    /// <summary>Missing, malformed or mismatched build metadata bypasses caching without hiding shader bytes.</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("length")]
    [InlineData("version")]
    [InlineData("entry")]
    [InlineData("digest")]
    public void InvalidBuildDigestBypassesCache(string failure)
    {
        var stage = new ShaderStageContract("test.csh", "test.csh", ShaderStageKind.Compute, new GpuBindingContract());
        var plan = new ShaderLoadPlan(new ShaderSettings(new GpuShaderContract("test", [stage], 1)));
        byte[] bytes = [1, 2, 3];
        byte[] metadata = ShaderBinaryDigest.Encode(new Dictionary<string, ShaderBinaryDigest.Entry>
        {
            [failure == "entry" ? "absent.spv" : plan.Stages[0].BinaryPath] =
                new(failure == "length" ? 4 : bytes.Length, failure == "digest" ? "invalid" : Convert.ToHexString(SHA256.HashData(bytes)))
        });
        if (failure == "malformed") metadata = [1];
        if (failure == "version") metadata = "{\"Version\":99,\"Binaries\":{}}"u8.ToArray();
        ReadOnlySpan<byte> Read(string path)
        {
            if (path != ShaderBinaryDigest.FileName) return bytes;
            if (failure == "missing") throw new FileNotFoundException(path);
            return metadata;
        }
        var prepared = new PreparedProgramBinary(plan, Read, new ProgramBinaryStore(directory), "driver", () => ShaderBinaryDigest.Parse(Read(ShaderBinaryDigest.FileName)));
        Assert.Null(prepared.Key);
        Assert.Equal(bytes, prepared.Read(plan.Stages[0].BinaryPath).ToArray());
    }

    /// <summary>Explicit cache bypass never asks the asset owner for optional metadata.</summary>
    [Fact]
    public void BypassOnlyReadsShaderBinary()
    {
        var stage = new ShaderStageContract("test.csh", "test.csh", ShaderStageKind.Compute, new GpuBindingContract());
        var plan = new ShaderLoadPlan(new ShaderSettings(new GpuShaderContract("test", [stage], 1)));
        ReadOnlySpan<byte> Read(string path)
        {
            Assert.EndsWith(".spv", path);
            return new byte[] { 1, 2, 3 };
        }
        Assert.Null(new PreparedProgramBinary(plan, Read, null, "driver", () => ShaderBinaryDigest.Parse(Read(ShaderBinaryDigest.FileName))).Key);
    }

    /// <summary>One manifest serves every program stage; a missing stage entry disables the whole cache key.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GraphicsReadsManifestOnceAndRequiresEveryStage(bool omitFragment)
    {
        var vertex = new ShaderStageContract("test.vsh", "test.vsh", ShaderStageKind.Vertex, new GpuBindingContract());
        var fragment = new ShaderStageContract("test.fsh", "test.fsh", ShaderStageKind.Fragment, new GpuBindingContract());
        var plan = new ShaderLoadPlan(new ShaderSettings(new GpuShaderContract("test", [vertex, fragment], 1)));
        byte[] binary = [1, 2, 3];
        var entries = plan.Stages.Where(stage => !omitFragment || stage.Stage.Kind != ShaderStageKind.Fragment)
            .ToDictionary(stage => stage.BinaryPath, _ => new ShaderBinaryDigest.Entry(binary.Length, Convert.ToHexString(SHA256.HashData(binary))));
        byte[] metadata = ShaderBinaryDigest.Encode(entries);
        int manifestReads = 0;
        ReadOnlySpan<byte> Read(string path)
        {
            if (path != ShaderBinaryDigest.FileName) return binary;
            manifestReads++;
            return metadata;
        }
        var prepared = new PreparedProgramBinary(plan, Read, new ProgramBinaryStore(directory), "driver", () => ShaderBinaryDigest.Parse(Read(ShaderBinaryDigest.FileName)));
        Assert.Equal(1, manifestReads);
        Assert.Equal(omitFragment, prepared.Key is null);
        Assert.All(plan.Stages, stage => Assert.Equal(binary, prepared.Read(stage.BinaryPath).ToArray()));
    }

    /// <summary>Truncated or tampered payloads and missing files cannot be returned to the driver.</summary>
    [Fact]
    public void PayloadIntegrityAndByteBudgetAreEnforced()
    {
        var store = new ProgramBinaryStore(directory, maxBytes: 4);
        store.Write(First, 1, [1, 2, 3, 4, 5]);
        Assert.False(store.TryRead(First, out _, out _));
        store.Write(First, 1, [1, 2, 3, 4]);
        string file = Assert.Single(Directory.GetFiles(directory, "*.bin"));
        File.WriteAllBytes(file, [1, 2, 3, 5]);
        Assert.False(store.TryRead(First, out _, out _));
        store.Write(First, 1, [1, 2, 3, 4]);
        File.Delete(file);
        Assert.False(store.TryRead(First, out _, out _));
    }

    /// <summary>Unavailable storage is optional and never escapes into shader loading.</summary>
    [Fact]
    public void FileInsteadOfDirectoryIsHarmless()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "blocked");
        File.WriteAllText(path, "file");
        var store = new ProgramBinaryStore(path);
        store.Write(First, 1, [1]);
        Assert.False(store.TryRead(First, out _, out _));
        store.Remove(First);
    }

    /// <summary>Another process's lease makes caching a miss without blocking subsequent recovery.</summary>
    [Fact]
    public void ContendedLeaseSkipsStorageAndRecovers()
    {
        Directory.CreateDirectory(directory);
        var store = new ProgramBinaryStore(directory);
        using (var lease = new FileStream(Path.Combine(directory, "index.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            store.Write(First, 1, [1, 2]);
            Assert.False(store.TryRead(First, out _, out _));
            store.Remove(First);
        }
        store.Write(First, 1, [1, 2]);
        Assert.True(store.TryRead(First, out _, out _));
    }

    /// <summary>Cache deletion is confined to the temporary directory created by this test.</summary>
    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
    #endregion
}
