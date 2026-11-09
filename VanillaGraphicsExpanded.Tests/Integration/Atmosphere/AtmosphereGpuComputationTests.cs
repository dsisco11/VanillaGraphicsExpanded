using VanillaGraphicsExpanded.Rendering.RHI;
using System.Diagnostics;
using System.Numerics;
using System.Collections.Immutable;
using System.Reflection;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.LumOn.Scene.Shaders;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
namespace VanillaGraphicsExpanded.Tests.Integration.Atmosphere;

/// <summary>Verifies atmospheric GPU publication across real graphics and lifecycle boundaries.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class AtmosphereGpuComputationTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Atmosphere's internal dispatches preserve the enclosing compute owner's storage binding.</summary>
    [Fact]
    public void CompletePublicationRestoresEnclosingComputeStorage()
    {
        // Arrange
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumonSceneFeedbackCompactPagesComputeShader.TryCreate(assets.Api, out var loaded, out string log), log);
        using var outer = loaded!;
        using var texture = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.R32ui, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var requests = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource();
        requests.EnsureCapacity(64, growExponentially: false);
        using var counter = new ComponentAtomicCounters(0, 1);
        outer.PageUsageStamp = texture;
        outer.PageTableMip0 = texture;
        outer.BindRequestsSsbo(requests);
        outer.BindRequestCounter(counter.Buffer);
        using var gpu = AtmosphereGpuComputation.Create(assets.Api);
        RHICommandContext.Current.SetPipeline(outer.Pipeline);
        RHICommandContext.Current.BindResources(outer.CaptureBindings());
        // Act
        Complete(gpu, Vector3.UnitY, 0, 0);
        // Observe the driver after the whole producer finishes, including its cleanup path.
        // Assert
        Assert.Equal(outer.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
        GL.GetInteger(GetIndexedPName.ShaderStorageBufferBinding, 0, out int restored);
        Assert.Equal(requests.BufferId, restored);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Waits on submitted GPU dependencies rather than adding unconditional timing delays.</summary>
    private static AtmosphereLighting Complete(AtmosphereGpuComputation gpu, Vector3 sun, float altitude, float clouds, int width = 32, int height = 24, int quality = 0, float groundAlbedo = .1f)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(30))
        {
            var result = gpu.Update(sun, altitude, clouds, width, height, quality, groundAlbedo);
            if (result is not null) return result;
            using var fence = RHICommandContext.Current.InsertFence();
            Assert.Contains(RHICommandContext.Current.WaitCpu(fence, TimeSpan.FromSeconds(5)), new[] { CompletionStatus.Ready, CompletionStatus.Ready });
        }
        throw new TimeoutException("Atmosphere GPU publication did not complete.");
    }
    #endregion
}
