using System.Security.Cryptography;
using VanillaGraphicsExpanded.Tests.Support.Cache;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shaders;

/// <summary>Checks prepared binary keying and metadata admission with controlled in-memory inputs.</summary>
public sealed class ProgramBinaryStoreTests : IDisposable
{
    #region Public API
    /// <summary>Explicit driver cache bypass still requires validated interface metadata.</summary>
    [Fact]
    public void BypassStillRequiresCompilerMetadata()
    {
        // Arrange a compute stage with controlled bytes but no compiler manifest.
        var stage = new ShaderStageContract("test.csh", "test.csh", ShaderStageKind.Compute, new GpuBindingContract());
        var plan = new ShaderLoadPlan(new ShaderSettings(new GpuShaderContract("test", [stage], 1)));
        /// <summary>Returns synthetic stage bytes for paths in the prepared plan.</summary>
        ReadOnlySpan<byte> Read(string path)
        {
            // Provide binary bytes only for stage paths; the manifest callback below remains absent.
            Assert.EndsWith(".spv", path);
            return new byte[] { 1, 2, 3 };
        }
        // Act and assert cache bypass still rejects missing compiler metadata.
        Assert.Throws<InvalidDataException>(() => new PreparedProgramBinary(plan, Read, null, "driver", () => null));
    }

    /// <summary>One manifest serves every stage; a missing stage entry rejects the entire candidate.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GraphicsReadsManifestOnceAndRequiresEveryStage(bool omitFragment)
    {
        // Arrange a two-stage graphics plan and optionally omit its fragment digest row.
        var vertex = new ShaderStageContract("test.vsh", "test.vsh", ShaderStageKind.Vertex, new GpuBindingContract());
        var fragment = new ShaderStageContract("test.fsh", "test.fsh", ShaderStageKind.Fragment, new GpuBindingContract());
        var plan = new ShaderLoadPlan(new ShaderSettings(new GpuShaderContract("test", [vertex, fragment], 1)));
        byte[] binary = [1, 2, 3];
        var entries = plan.Stages.Where(stage => !omitFragment || stage.Stage.Kind != ShaderStageKind.Fragment)
            .ToDictionary(stage => stage.BinaryPath, stage => Entry(stage, binary));
        byte[] metadata = ShaderBinaryDigest.Encode(entries);
        int manifestReads = 0;
        /// <summary>Returns stage data and counts requests for the shared manifest.</summary>
        ReadOnlySpan<byte> Read(string path)
        {
            // Count metadata reads and return the same encoded manifest for each request.
            if (path != ShaderBinaryDigest.FileName) return binary;
            manifestReads++;
            return metadata;
        }
        // Act by preparing the candidate and assert rejection or complete stage availability.
        if (omitFragment)
            Assert.Throws<InvalidDataException>(() => new PreparedProgramBinary(plan, Read, new ProgramBinaryStore(directory, fileSystem: storage), "driver", () => ShaderBinaryDigest.Parse(Read(ShaderBinaryDigest.FileName))));
        else
        {
            var prepared = new PreparedProgramBinary(plan, Read, new ProgramBinaryStore(directory, fileSystem: storage), "driver", () => ShaderBinaryDigest.Parse(Read(ShaderBinaryDigest.FileName)));
            Assert.NotNull(prepared.Key);
            Assert.All(plan.Stages, stage => Assert.Equal(binary, prepared.Read(stage.BinaryPath).ToArray()));
        }
        Assert.Equal(1, manifestReads);
    }

    /// <summary>Driver, stage content and specialization changes select distinct executables.</summary>
    [Fact]
    public void KeyTracksExactInputsAndBypassHasNoKey()
    {
        // Arrange one compute plan, deterministic shader bytes, and a manifest reader.
        var store = new ProgramBinaryStore(directory, fileSystem: storage);
        var stage = new ShaderStageContract("test.csh", "test.csh", ShaderStageKind.Compute,
            new GpuBindingContract(), specializations: [new ShaderSpecialization(1, LumOnShaderOptions.DirectVisibility)]);
        var settings = new ShaderSettings(new GpuShaderContract("test", [stage], 1, [LumOnShaderOptions.DirectVisibility]));
        var plan = new ShaderLoadPlan(settings);
        byte[] bytes = [1, 2, 3];
        /// <summary>Encodes manifest data from the current shader bytes.</summary>
        ReadOnlySpan<byte> Read(string path) => path == ShaderBinaryDigest.FileName
            ? ShaderBinaryDigest.Encode(new Dictionary<string, ShaderBinaryDigest.Entry>
            { [plan.Stages[0].BinaryPath] = Entry(plan.Stages[0], bytes) }) : bytes;
        // Act by preparing the baseline executable identity from the original inputs.
        var baseline = new PreparedProgramBinary(plan, Read, store, "driver-a", () => ShaderBinaryDigest.Parse(Read(ShaderBinaryDigest.FileName)));
        // Assert the same plan, bytes, and driver produce the same key, while a driver change does not.
        Assert.Equal(baseline.Key, new PreparedProgramBinary(plan, Read, store, "driver-a", () => ShaderBinaryDigest.Parse(Read(ShaderBinaryDigest.FileName))).Key);
        Assert.NotEqual(baseline.Key, new PreparedProgramBinary(plan, Read, store, "driver-b", () => ShaderBinaryDigest.Parse(Read(ShaderBinaryDigest.FileName))).Key);

        // Act by changing the shader bytes and assert the key follows content.
        bytes = [1, 2, 4];
        Assert.NotEqual(baseline.Key, new PreparedProgramBinary(plan, Read, store, "driver-a", () => ShaderBinaryDigest.Parse(Read(ShaderBinaryDigest.FileName))).Key);

        // Act by restoring bytes and bypassing storage; assert the captured bytes and null key.
        bytes = [1, 2, 3];
        Assert.Equal(new byte[] { 1, 2, 3 }, baseline.Read(plan.Stages[0].BinaryPath).ToArray());
        Assert.Null(new PreparedProgramBinary(plan, Read, null, "driver-a", () => ShaderBinaryDigest.Parse(Read(ShaderBinaryDigest.FileName))).Key);

        // Act by changing specialization settings and the stage entry point, then assert both affect identity.
        var changed = new ShaderLoadPlan(settings.With(LumOnShaderOptions.DirectVisibility, true));
        Assert.NotEqual(baseline.Key, new PreparedProgramBinary(changed, Read, store, "driver-a", () => ShaderBinaryDigest.Parse(Read(ShaderBinaryDigest.FileName))).Key);
        var otherEntry = new ShaderStageContract("test.csh", "test.csh", ShaderStageKind.Compute,
            new GpuBindingContract(), specializations: stage.Specializations, entryPoint: "other");
        var entryPlan = new ShaderLoadPlan(new ShaderSettings(new GpuShaderContract("test", [otherEntry], 1, [LumOnShaderOptions.DirectVisibility])));
        Assert.NotEqual(baseline.Key, new PreparedProgramBinary(entryPlan, Read, store, "driver-a",
            () => new ShaderBinaryDigest.Manifest(2, new() { [entryPlan.Stages[0].BinaryPath] = Entry(entryPlan.Stages[0], bytes) })).Key);
    }

    /// <summary>Invalid compiler metadata prevents preparation even when binary bytes are available.</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("length")]
    [InlineData("version")]
    [InlineData("entry")]
    [InlineData("digest")]
    public void InvalidBuildDigestRejectsPreparation(string failure)
    {
        // Arrange a valid stage and mutate one manifest condition selected by the theory row.
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
        /// <summary>Returns stage bytes or the selected invalid metadata condition.</summary>
        ReadOnlySpan<byte> Read(string path)
        {
            // Return the selected malformed, missing, or inconsistent manifest for this theory case.
            if (path != ShaderBinaryDigest.FileName) return bytes;
            if (failure == "missing") throw new FileNotFoundException(path);
            return metadata;
        }
        Action prepare = () => new PreparedProgramBinary(plan, Read, new ProgramBinaryStore(directory, fileSystem: storage), "driver", () => ShaderBinaryDigest.Parse(Read(ShaderBinaryDigest.FileName)));
        // Act and assert missing or malformed metadata rejects preparation.
        if (failure == "missing") Assert.Throws<FileNotFoundException>(prepare);
        else Assert.Throws<InvalidDataException>(prepare);
    }
    /// <summary>Removes any in-memory cache directory owned by this test class.</summary>
    public void Dispose()
    {
        if (storage.DirectoryExists(directory)) storage.DeleteDirectory(directory, recursive: true);
    }
    #endregion

    #region Private
    private readonly CountingDiskCacheFileSystem storage = new();
    private readonly string directory = Path.Combine(Path.GetTempPath(), "VGE.ProgramBinaryTests", Guid.NewGuid().ToString("N"));
    /// <summary>Creates coherent interface metadata for the synthetic cache identity fixture.</summary>
    private static ShaderBinaryDigest.Entry Entry(ShaderStageSelection stage, byte[] binary) =>
        new(binary.Length, Convert.ToHexString(SHA256.HashData(binary)),
            new(PackagedShaderInterface.CurrentVersion, "test-compiler", stage.Stage.Kind, stage.Stage.EntryPoint,
                stage.Key, "Release", new([], [], [])));
    #endregion
}
