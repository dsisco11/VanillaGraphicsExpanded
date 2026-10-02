using System.Numerics;
using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies ordinary upload suppression using generated owners, offline SPIR-V and semantic image readback.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ShaderUniformStateTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>One retained publication history follows real specialized and replacement executables.</summary>
    [Fact]
    public void RealExecutableSwitchesAndReplacementReuploadUnchangedValues()
    {
        EnsureContextValid();
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "tests", "uniform_state.csh.spv");
        var settings = new ShaderSettings(UniformStateComputeShader.Contract);
        Assert.True(GpuComputePipeline.TryLoadFromSpirv(path, settings, out var first, out string log), log);
        Assert.True(GpuComputePipeline.TryLoadFromSpirv(path, settings.With(UniformStateComputeShader.Alternate, true), out var second, out log), log);
        Assert.True(GpuComputePipeline.TryLoadFromSpirv(path, settings, out var replacement, out log), log);
        using var a = first!;
        using var b = second!;
        using var c = replacement!;
        using var output = Texture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        var scalar = new ShaderUniformPublication<float>();
        var values = new ShaderUniformPublication<float[]>();
        var vector = new ShaderUniformPublication<Vector3>();
        var matrix = new ShaderUniformPublication<Matrix4x4>();
        // The same desired values must reach each executable, including A after B and a fresh A replacement.
        foreach (var pipeline in new[] { a, a, b, a, c })
        {
            var owner = new SubmissionTarget(pipeline);
            using (pipeline.UseScope())
            {
                scalar.Validate(owner, 120, 7f);
                values.Validate(owner, 121, new float[] { 2, 3 });
                vector.Validate(owner, 123, Vector3.Zero);
                matrix.Validate(owner, 124, Matrix4x4.Identity);
                scalar.Publish(owner, 120, 7f);
                values.Publish(owner, 121, new float[] { 2, 3 });
                vector.Publish(owner, 123, Vector3.Zero);
                matrix.Publish(owner, 124, Matrix4x4.Identity);
                output.BindImageUnit(0, TextureAccess.WriteOnly);
                pipeline.Dispatch(1);
            }
            GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.FramebufferBarrierBit);
            Assert.Equal(ReferenceEquals(pipeline, b) ? new float[] { 8, 6, 1, 2 } : new float[] { 7, 5, 0, 1 }, output.ReadPixels());
        }
        Assert.Equal(4, scalar.Uploads);
        Assert.Equal(4, values.Uploads);
    }
    /// <summary>Defaults upload once; changed values and failed-resource retry retain correct data.</summary>
    [Fact]
    public void GeneratedValuesUploadOnceAndRetryWithoutLosingPendingWork()
    {
        EnsureContextValid();
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "tests", "uniform_state.csh.spv");
        Assert.True(GpuComputePipeline.TryLoadFromSpirv(path, new ShaderSettings(UniformStateComputeShader.Contract),
            out var pipeline, out string log), log);
        using var shader = new UniformStateComputeShader(pipeline!);
        using var output = Texture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        float[] desired = [2, 3];
        shader.Values = desired;
        desired[0] = 99;
        // Resource failure precedes every uniform write, including first-use defaults.
        Assert.Throws<InvalidOperationException>(() => shader.Dispatch(1));
        Assert.Equal(0, History<float>(shader, "Scalar").Uploads);
        shader.Output = new(output, TextureAccess.WriteOnly);
        shader.Dispatch(1);
        GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.FramebufferBarrierBit);
        Assert.Equal(new float[] { 0, 5, 0, 0 }, output.ReadPixels());
        shader.Dispatch(1);
        // Order repeated writes to the same image before submitting changed values.
        GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit);
        Assert.Equal(1, History<float>(shader, "Scalar").Uploads);
        Assert.Equal(1, History<float[]>(shader, "Values").Uploads);
        shader.Scalar = 7;
        shader.Vector = new Vector3(8, 0, 0);
        shader.Transform = Matrix4x4.CreateScale(9);
        shader.Values = [4, 6];
        shader.Output = default;
        Assert.Throws<InvalidOperationException>(() => shader.Dispatch(1));
        Assert.Equal(1, History<float>(shader, "Scalar").Uploads);
        shader.Output = new(output, TextureAccess.WriteOnly);
        Assert.Equal(7, shader.Scalar);
        Assert.Equal(new float[] { 4, 6 }, shader.Values);
        shader.Dispatch(1);
        Assert.Equal(2, History<float>(shader, "Scalar").Uploads);
        Assert.Equal(2, History<float[]>(shader, "Values").Uploads);
        GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.FramebufferBarrierBit);
        Assert.Equal(new float[] { 7, 10, 8, 9 }, output.ReadPixels());
        Assert.Equal(2, History<float>(shader, "Scalar").Uploads);
        shader.Scalar = 7;
        shader.Dispatch(1);
        Assert.Equal(2, History<float>(shader, "Scalar").Uploads);
    }
    #endregion

    #region Private
    /// <summary>Exposes the selected prepared executable through the production uniform publication boundary.</summary>
    private sealed record SubmissionTarget(GpuComputePipeline Pipeline) : IShaderSubmissionTarget
    {
        public int ProgramId => Pipeline.ProgramId;
        public GpuProgramLayout ProgramLayout => Pipeline.ProgramLayout;
    }
    /// <summary>Observes generated shader-local history independently of desired assignments or GL resource binds.</summary>
    private static ShaderUniformPublication<T> History<T>(UniformStateComputeShader shader, string property) =>
        (ShaderUniformPublication<T>)typeof(UniformStateComputeShader)
            .GetField("__validation_" + property, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shader)!;
    #endregion
}
