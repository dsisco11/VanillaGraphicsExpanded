using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises exact native parameter/getter ABIs that initialize generated sampler names.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed unsafe class GpuSamplerRealizationTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Every retained sampler parameter shape realizes the same original reservation in both roots.</summary>
    [Theory]
    [InlineData(false, 0)] [InlineData(true, 0)]
    [InlineData(false, 1)] [InlineData(true, 1)]
    [InlineData(false, 2)] [InlineData(true, 2)]
    [InlineData(false, 3)] [InlineData(true, 3)]
    [InlineData(false, 4)] [InlineData(true, 4)]
    [InlineData(false, 5)] [InlineData(true, 5)]
    [InlineData(false, 6)] [InlineData(true, 6)]
    [InlineData(false, 7)] [InlineData(true, 7)]
    [InlineData(false, 8)] [InlineData(true, 8)]
    [InlineData(false, 9)] [InlineData(true, 9)]
    public void ParameterAccessRealizesWithoutDescriptorDiscovery(bool core, int variant)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        _ = RHICommandContext.Current.Capabilities;
        int sampler = GL.GenSampler();
        try
        {
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var original = manager.FindResource(GpuResourceKind.Sampler, sampler)!;
            Assert.False(original.IsRealized);
            var table = OpenGLBindingTable.For(core ? typeof(OpenTK.Graphics.OpenGL4.GL) : typeof(GL));
            // Directly invoke the exact installed native slot: marshalling is not under test,
            // while native output pointers remain valid throughout generated observation.
            string[] symbols = ["glSamplerParameteri", "glSamplerParameterf", "glSamplerParameteriv", "glSamplerParameterIiv",
                "glSamplerParameterIuiv", "glSamplerParameterfv", "glGetSamplerParameteriv", "glGetSamplerParameterIiv",
                "glGetSamplerParameterIuiv", "glGetSamplerParameterfv"];
            nint call = table.Read(symbols[variant]);
            int parameter = (int)SamplerParameterName.TextureBorderColor;
            int* integers = stackalloc int[4];
            float* floats = stackalloc float[4];
            if (variant == 0) ((delegate* unmanaged[Stdcall]<uint, int, int, void>)call)((uint)sampler, (int)SamplerParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            else if (variant == 1) ((delegate* unmanaged[Stdcall]<uint, int, float, void>)call)((uint)sampler, (int)SamplerParameterName.TextureMinLod, 0);
            else if (variant is 5 or 9) ((delegate* unmanaged[Stdcall]<uint, int, float*, void>)call)((uint)sampler, parameter, floats);
            else if (variant is 4 or 8) ((delegate* unmanaged[Stdcall]<uint, int, uint*, void>)call)((uint)sampler, parameter, (uint*)integers);
            else ((delegate* unmanaged[Stdcall]<uint, int, int*, void>)call)((uint)sampler, parameter, integers);
            Assert.True(original.IsRealized); Assert.True(original.IsUsable); Assert.Equal(1UL, original.MetadataRevision);
            Assert.Same(original, manager.FindResource(GpuResourceKind.Sampler, sampler));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteSampler(sampler); fixture.CleanupGpuResources(); }
    }
    /// <summary>Partial multi-bind updates accepted slots and null arrays establish unbound units without metadata queries.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SamplerBatchPreservesPartialNativeResults(bool core)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var first = RHICommandContext.Current.CreateSampler(new()).RequireResource();
            using var second = RHICommandContext.Current.CreateSampler(new()).RequireResource();
            GL.BindSampler(0, first.SamplerId); GL.BindSampler(1, first.SamplerId);
            if (core) OpenTK.Graphics.OpenGL4.GL.BindSamplers(0, 2, new[] { second.SamplerId, int.MaxValue });
            else GL.BindSamplers(0, 2, new[] { second.SamplerId, int.MaxValue });
            Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
            Assert.Equal(second.SamplerId, OpenGlStateCache.Current.SamplerKnowledge(0));
            GL.GetInteger((GetIndexedPName)All.SamplerBinding, 1, out int rejected);
            Assert.Equal(first.SamplerId, rejected);
            // An unknown numeric name cannot prove rejection observationally; only that slot loses certainty.
            Assert.Null(OpenGlStateCache.Current.SamplerKnowledge(1));
            if (core) OpenTK.Graphics.OpenGL4.GL.BindSamplers(0, 2, (int*)null);
            else GL.BindSamplers(0, 2, (int*)null);
            Assert.Equal(0, OpenGlStateCache.Current.SamplerKnowledge(0));
            Assert.Equal(0, OpenGlStateCache.Current.SamplerKnowledge(1));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.BindSampler(0, 0); GL.BindSampler(1, 0); fixture.CleanupGpuResources(); }
    }
    #endregion
}
