using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks shared graphics and compute declaration boundaries independently of debug grouping.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ShaderDemandPreparationTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Graphics demand and ownership
    /// <summary>Each supported activation path prepares its declaration once without eager registry lookup.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ActivationPreparesOnlySelectedOwner(int activation)
    {
        EnsureContextValid();
        using var engine = new EngineShaderPlatformScope();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        Assert.True(VgeShaderPrograms.RegisterAll(assets.Api));
        Assert.Empty(assets.Reads);
        Assert.Empty(assets.RegisteredPrograms);
        var program = Assert.IsType<LumOnHzbCopyShaderProgram>(GpuShaderPrograms.Get<GpuProgram>(assets.Api, "lumon_hzb_copy"));
        Assert.Equal(0, program.ProgramId);
        if (activation == 0)
        {
            Assert.True(program.TryUse());
            program.Stop();
        }
        else if (activation == 1)
        {
            using var use = program.UseScope();
            Assert.Equal(program.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
        }
        else
        {
            IShaderProgram contract = program;
            contract.Use();
            Assert.Equal(program.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            contract.Stop();
        }
        Assert.True(GL.IsProgram(program.ProgramId));
        Assert.Single(assets.RegisteredPrograms);
        int reads = assets.Reads.Count;
        Assert.True(program.EnsureReady());
        Assert.Equal(reads, assets.Reads.Count);
        Assert.All(GpuShaderPrograms.GetAll(assets.Api).Where(other => !ReferenceEquals(program, other)), other => Assert.Equal(0, other.ProgramId));
        Assert.Empty(assets.ScheduledTasks);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Explicit preloads prepare exactly the chosen declarations and reuse unchanged executable generations.</summary>
    [Fact]
    public void ExplicitPreloadHonorsSubsetAndSettings()
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        Assert.True(VgeShaderPrograms.RegisterAll(assets.Api));
        var selected = GpuShaderPrograms.GetAll(assets.Api).Where(program => program.PassName is "lumon_hzb_copy" or "lumon_hzb_downsample").ToImmutableArray();
        Assert.Equal(2, selected.Length);
        Assert.True(GpuShaderPrograms.Preload(assets.Api, selected));
        Assert.Equal(2, assets.RegisteredPrograms.Count);
        int reads = assets.Reads.Count;
        Assert.True(GpuShaderPrograms.Preload(assets.Api, selected));
        Assert.Equal(reads, assets.Reads.Count);
        Assert.All(selected, program => Assert.True(GL.IsProgram(program.ProgramId)));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Interface disposal of a never-prepared declaration performs safe empty-owner cleanup.</summary>
    [Fact]
    public void InterfaceDisposalOfUnusedDeclarationDoesNotCreateGpuWork()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        var program = GpuShaderPrograms.Declare(assets.Api, new LumOnHzbCopyShaderProgram());
        ((IShaderProgram)program).Dispose();
        Assert.True(program.Disposed);
        Assert.False(program.EnsureReady());
        Assert.False(program.TryUse());
        Assert.False(program.CompileAndLink());
        Assert.Empty(assets.Reads);
        Assert.Empty(assets.RegisteredPrograms);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Final owner disposal prevents subsequent activation or explicit compilation from reviving a retired executable.</summary>
    [Fact]
    public void PreparedOwnerCannotReviveAfterExplicitDisposal()
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        var program = GpuShaderPrograms.Declare(assets.Api, new LumOnHzbCopyShaderProgram());
        Assert.True(program.EnsureReady());
        int installed = program.ProgramId;
        int reads = assets.Reads.Count;
        program.Dispose();
        Assert.False(GL.IsProgram(installed));
        Assert.False(program.EnsureReady());
        Assert.False(program.TryUse());
        Assert.False(program.CompileAndLink());
        Assert.Equal(reads, assets.Reads.Count);
        Assert.False(GpuShaderPrograms.Preload(assets.Api, [program]));
        Assert.Equal(reads, assets.Reads.Count);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Compute demand and retry
    /// <summary>The first dispatch prepares declared compute inputs and executes the original binding contract.</summary>
    [Fact]
    public void ComputeFirstDispatchPreparesAndExecutes()
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        var settings = new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/GpuUniformRingBufferIntegrationTests_1"));
        using var pipeline = GpuComputePipeline.DeclareFromAssets(assets.Api, settings);
        using var texture = Texture3D.Create(1, 1, 1, PixelInternalFormat.Rgba32ui, TextureFilterMode.Nearest, TextureTarget.Texture3D);
        Assert.True(GpuUniformRingSystem.TryGetCurrent(out var ring));
        byte[] data = new byte[16];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data, 123);
        var allocation = ring.AllocateAndWrite(data);
        allocation.Buffer.BindRange(0, allocation.OffsetBytes, allocation.SizeBytes);
        GL.BindImageTexture(0, texture.TextureId, 0, true, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba32ui);
        Assert.Empty(assets.Reads);
        pipeline.Dispatch(1);
        GL.MemoryBarrier(MemoryBarrierFlags.TextureUpdateBarrierBit | MemoryBarrierFlags.ShaderImageAccessBarrierBit);
        uint[] actual = new uint[4];
        GL.BindTexture(TextureTarget.Texture3D, texture.TextureId);
        GL.GetTexImage(TextureTarget.Texture3D, 0, PixelFormat.RgbaInteger, PixelType.UnsignedInt, actual);
        Assert.Equal(123u, actual[0]);
        GL.BindTexture(TextureTarget.Texture3D, 0);
        GL.BindImageTexture(0, 0, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba32ui);
        GlStateCache.Current.InvalidateAll();
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Compute declarations delay binary reads until preparation and reuse their installed interface.</summary>
    [Fact]
    public void ComputeDeclarationPreparesOnce()
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        var settings = new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/GpuUniformRingBufferIntegrationTests_1"));
        using var pipeline = GpuComputePipeline.DeclareFromAssets(assets.Api, settings);
        Assert.Equal(0, pipeline.ProgramId);
        Assert.Empty(assets.Reads);
        Assert.True(pipeline.EnsureReady(), pipeline.PreparationLog);
        Assert.True(GL.IsProgram(pipeline.ProgramId));
        Assert.NotNull(pipeline.ProgramLayout.BinaryInterface);
        int reads = assets.Reads.Count;
        Assert.True(pipeline.EnsureReady());
        Assert.Equal(reads, assets.Reads.Count);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Failed compute preparation reads once per explicit retry and never exposes an invalid executable.</summary>
    [Fact]
    public void ComputeFailureWaitsForExplicitRetry()
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        var settings = new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/GpuUniformRingBufferIntegrationTests_1"));
        assets.BeforeRead = path => assets.Overrides[path] = new byte[20];
        using var pipeline = GpuComputePipeline.DeclareFromAssets(assets.Api, settings);
        Assert.False(pipeline.EnsureReady());
        Assert.NotEmpty(pipeline.PreparationLog);
        int reads = assets.Reads.Count;
        Assert.False(pipeline.EnsureReady());
        Assert.Equal(reads, assets.Reads.Count);
        Assert.Equal(0, pipeline.ProgramId);
        assets.BeforeRead = null;
        assets.Overrides.Clear();
        pipeline.RetryPreparation();
        Assert.True(pipeline.EnsureReady(), pipeline.PreparationLog);
        Assert.True(GL.IsProgram(pipeline.ProgramId));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
