using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies packed block upload suppression using generated owners, offline SPIR-V and semantic image readback.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ShaderUniformStateTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>One retained block follows real specialized and replacement executables.</summary>
    [Fact]
    public void RealExecutableSwitchesAndReplacementRebindOneUnchangedPublication()
    {
        EnsureContextValid();
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "tests", "uniform_state.csh.spv");
        var settings = new ShaderSettings(UniformStateComputeShader.Contract);
        Assert.True(ComputeShaderAsset.TryLoadFromSpirv(path, settings, out var first, out string log), log);
        Assert.True(ComputeShaderAsset.TryLoadFromSpirv(path, settings.With(UniformStateComputeShader.Alternate, true), out var second, out log), log);
        Assert.True(ComputeShaderAsset.TryLoadFromSpirv(path, settings, out var replacement, out log), log);
        using var a = first!;
        using var b = second!;
        using var c = replacement!;
        using var output = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var inputs = new UniformStateBuffer { Scalar = 7, Values = [2, 3], Vector = Vector3.Zero, Transform = Matrix4x4.Identity };
        Assert.True(RHIUniformRingSystem.TryGetCurrent(out var ring));
#if DEBUG
        long allocations = ring.AllocationsWritten, bytes = ring.BytesWritten;
#endif
        // The same desired values must reach each executable, including A after B and a fresh A replacement.
        foreach (var pipeline in new[] { a, a, b, a, c })
        {
            var context = RHICommandContext.Current;
            context.SetPipeline(pipeline.Pipeline);
            context.BindResources(new(pipeline.Shader,
                images: [new(GpuBindingEntry.Identity(ShaderBindingKind.Image, "result"), new RHIImageBinding(output, ImageAccess.WriteOnly))],
                uniforms: [new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "UniformStateInputs"), inputs.Capture())]));
            context.Dispatch(new(1));
            RHICommandContext.Current.Barrier(new(ResourceAccess.Image | ResourceAccess.Framebuffer));
            Assert.Equal(ReferenceEquals(pipeline, b) ? new float[] { 8, 6, 1, 2 } : new float[] { 7, 5, 0, 1 }, output.ReadPixels());
        }
#if DEBUG
        Assert.Equal(1, ring.AllocationsWritten - allocations);
        Assert.Equal(128, ring.BytesWritten - bytes);
#endif
    }
    /// <summary>Defaults upload once; changed values and failed-resource retry retain correct data.</summary>
    [Fact]
    public void GeneratedValuesUploadOnceAndRetryWithoutLosingPendingWork()
    {
        EnsureContextValid();
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "tests", "uniform_state.csh.spv");
        Assert.True(ComputeShaderAsset.TryLoadFromSpirv(path, new ShaderSettings(UniformStateComputeShader.Contract),
            out var pipeline, out string log), log);
        using var shader = new UniformStateComputeShader(pipeline!);
        using var output = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        Assert.True(RHIUniformRingSystem.TryGetCurrent(out var ring));
#if DEBUG
        long allocations = ring.AllocationsWritten;
#endif
        float[] desired = [2, 3];
        shader.Values = desired;
        desired[0] = 99;
        // Resource failure precedes block publication, including first-use defaults.
        Assert.Throws<InvalidOperationException>(() => shader.Dispatch(1));
#if DEBUG
        Assert.Equal(0, ring.AllocationsWritten - allocations);
#endif
        shader.Output = new(output, ImageAccess.WriteOnly);
        shader.Dispatch(1);
        RHICommandContext.Current.Barrier(new(ResourceAccess.Image | ResourceAccess.Framebuffer));
        Assert.Equal(new float[] { 0, 5, 0, 0 }, output.ReadPixels());
        shader.Dispatch(1);
        // Order repeated writes to the same image before submitting changed values.
        RHICommandContext.Current.Barrier(new(ResourceAccess.Image));
#if DEBUG
        Assert.Equal(1, ring.AllocationsWritten - allocations);
#endif
        shader.Scalar = 7;
        shader.Vector = new Vector3(8, 0, 0);
        shader.Transform = Matrix4x4.CreateScale(9);
        shader.Values = [4, 6];
        shader.Output = default;
        Assert.Throws<InvalidOperationException>(() => shader.Dispatch(1));
#if DEBUG
        Assert.Equal(1, ring.AllocationsWritten - allocations);
#endif
        shader.Output = new(output, ImageAccess.WriteOnly);
        Assert.Equal(7, shader.Scalar);
        Assert.Equal(new float[] { 4, 6 }, shader.Values);
        shader.Dispatch(1);
#if DEBUG
        Assert.Equal(2, ring.AllocationsWritten - allocations);
#endif
        RHICommandContext.Current.Barrier(new(ResourceAccess.Image | ResourceAccess.Framebuffer));
        Assert.Equal(new float[] { 7, 10, 8, 9 }, output.ReadPixels());
#if DEBUG
        Assert.Equal(2, ring.AllocationsWritten - allocations);
#endif
        shader.Scalar = 7;
        shader.Dispatch(1);
#if DEBUG
        Assert.Equal(2, ring.AllocationsWritten - allocations);
#endif
    }
    #endregion

    #region Private
    /// <summary>Exposes the selected prepared executable through the production uniform publication boundary.</summary>
    private sealed record SubmissionTarget(ComputeShaderAsset Pipeline) : IShaderSubmissionTarget
    {
        public int ProgramId => Pipeline.ProgramId;
        public GpuProgramLayout ProgramLayout => Pipeline.ProgramLayout;
    }
    #endregion
}
