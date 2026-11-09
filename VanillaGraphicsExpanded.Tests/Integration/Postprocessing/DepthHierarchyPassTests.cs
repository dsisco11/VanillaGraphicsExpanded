using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using Assertions = VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Postprocessing;
/// <summary>Checks production conservative mip generation, feedback-safe access and resource retirement.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class DepthHierarchyPassTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>The compute interruption restores borrowed image and indexed storage slots.</summary>
    [Fact]
    public void ComputeRestoresImageAndStorageBindings()
    {
        // Arrange: Bind sentinel image and storage resources before the production hierarchy pass uses those slots.
        EnsureShaderTestAvailable();
        using var owner=new DepthHierarchyPass();
        var compute=Programs.CreateDepthHierarchy();
        using var source=TestFramework.CreateTexture(257,131,TextureFormat.R32f,
            Enumerable.Repeat(.5f,257*131).ToArray());
        using var external=RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = "Tests.Hzb.ExternalStorage" }).RequireResource();
        external.UploadData(new uint[] { 123 }); external.BindBase(0);
        using var image=new VanillaGraphicsExpanded.Tests.Support.GPU.Native.NativeImageSelection(6,source,TextureAccess.ReadOnly);
        OpenGlStateCache.Current.InvalidateAll();
        try {
            // Act: Prepare and dispatch the real depth hierarchy while the borrowed bindings are occupied.
            owner.Prepare(257,131,compute); owner.Render(source);
            GL.GetInteger((GetIndexedPName)All.ShaderStorageBufferBinding,0,out int storage);
            GL.GetInteger((GetIndexedPName)All.ImageBindingName,6,out int imageName);
            GL.GetInteger((GetIndexedPName)All.ImageBindingAccess,6,out int access);
            // Assert: The compute boundary restores both borrowed slots and leaves no GL error.
            Assert.Equal(external.BufferId,storage); Assert.Equal(source.TextureId,imageName);
            Assert.Equal((int)TextureAccess.ReadOnly,access);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        } finally { GpuShaderStorageBuffer.UnbindBase(0); }
    }
    #endregion

    #region Protected API
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
    }
    #endregion
}
