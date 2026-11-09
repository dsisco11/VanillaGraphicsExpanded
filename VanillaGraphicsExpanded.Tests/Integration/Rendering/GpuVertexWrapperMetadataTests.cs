using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using System.Runtime.CompilerServices;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks native VAO attribute configuration, cache observation, and owner metadata boundaries.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuVertexWrapperMetadataTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Native VAO attribute configuration updates cache observations while preserving resource metadata revisions.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NativeAttributeConfigurationUpdatesCacheWithoutChangingResourceMetadata(bool direct)
    {
        // Arrange: Seed sentinel VAO/buffer selections, then create a separately registered VAO and backing buffer.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var sentinel=NativeResourceSetup.CreateVertexArray(); OpenGLVertexSelection.Bind(OpenGlStateCache.Current, sentinel.VertexArrayId);
            using var arraySentinel=RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource(); arraySentinel.Allocate(64);
            using var buffer=RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource(); buffer.Allocate(128);
            OpenGLBufferBindings.BindBuffer(OpenGlStateCache.Current, BufferTarget.ArrayBuffer,arraySentinel.BufferId);
            using var capabilities=new DirectStateAccessCapabilityScope(direct);
            using var vao=NativeResourceSetup.CreateVertexArray();
            Assert.True(GL.IsVertexArray(vao.VertexArrayId));
            Assert.Equal(sentinel.VertexArrayId,GL.GetInteger(GetPName.VertexArrayBinding));
            var entry=RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(vao)!;
            ulong revision=entry.MetadataRevision,storage=entry.StorageRevision;
            var cache=OpenGlStateCache.Current;
            ulong configuration=cache.GetVertexConfigurationRevision(vao.VertexArrayId);
            // Act: Configure the new VAO through named calls while the selected root controls direct-state-access availability.
            GL.VertexArrayVertexBuffer(vao.VertexArrayId,2,buffer.BufferId,(IntPtr)16,16);
            GL.VertexArrayBindingDivisor(vao.VertexArrayId,2,3);
            GL.VertexArrayAttribFormat(vao.VertexArrayId,0,2,VertexAttribType.Float,false,4);
            GL.VertexArrayAttribBinding(vao.VertexArrayId,0,2);
            GL.EnableVertexArrayAttrib(vao.VertexArrayId,0);
            OpenTK.Graphics.OpenGL4.GL.VertexArrayElementBuffer(vao.VertexArrayId, buffer.BufferId);
            // Assert: Configuration observations advance independently while resource allocation metadata remains unchanged.
            Assert.Equal(revision,entry.MetadataRevision); Assert.Equal(storage,entry.StorageRevision);
            Assert.True(cache.GetVertexConfigurationRevision(vao.VertexArrayId)>configuration);
            if(direct)
            {
                Assert.Equal(sentinel.VertexArrayId,GL.GetInteger(GetPName.VertexArrayBinding));
                Assert.Equal(arraySentinel.BufferId,GL.GetInteger(GetPName.ArrayBufferBinding));
            }
            OpenGLVertexSelection.Bind(OpenGlStateCache.Current, vao.VertexArrayId);
            GL.GetVertexAttrib(0,(VertexAttribParameter)All.VertexAttribArrayBufferBinding,out int actualBuffer);
            GL.GetVertexAttrib(0,VertexAttribParameter.ArraySize,out int size);
            GL.GetVertexAttrib(0,VertexAttribParameter.VertexAttribArrayDivisor,out int divisor);
            Assert.Equal(buffer.BufferId,actualBuffer); Assert.Equal(2,size); Assert.Equal(3,divisor);
            Assert.Equal(buffer.BufferId,OpenGlStateCache.Current.GetBoundBuffer(BufferTarget.ElementArrayBuffer));
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Element-buffer observation publishes executed changes and lazily resolves rejected commands without guessing bindings.</summary>
    [Fact]
    public void BoundElementBufferRejectsInvalidNameWithoutCachingRequest()
    {
        // Arrange: Bind a real VAO and element buffer before testing repeated, rejected, and pending-error transitions.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var vao=NativeResourceSetup.CreateVertexArray(); OpenGLVertexSelection.Bind(OpenGlStateCache.Current, vao.VertexArrayId);
            using var buffer=RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource(); buffer.Allocate(64);
            var cache=OpenGlStateCache.Current;
            var entry=RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(vao)!;
            ulong before=entry.MetadataRevision;
            ulong configuration=cache.GetVertexConfigurationRevision(vao.VertexArrayId);
            // Act: Bind the same EBO twice, reject an invalid external name, then clear it with an unrelated error pending.
            OpenGLBufferBindings.BindBuffer(cache, BufferTarget.ElementArrayBuffer,buffer.BufferId);
            // Assert: Configuration revisions track real changes, metadata revisions stay stable, and rejected requests are not cached.
            Assert.Equal(before,entry.MetadataRevision);
            Assert.True(cache.GetVertexConfigurationRevision(vao.VertexArrayId)>configuration);
            Assert.Equal(buffer.BufferId,cache.GetBoundBuffer(BufferTarget.ElementArrayBuffer));
            configuration=cache.GetVertexConfigurationRevision(vao.VertexArrayId);
            OpenGLBufferBindings.BindBuffer(cache, BufferTarget.ElementArrayBuffer,buffer.BufferId);
            Assert.Equal(before,entry.MetadataRevision);
            Assert.Equal(configuration,cache.GetVertexConfigurationRevision(vao.VertexArrayId));
            BindExternalElement(int.MaxValue);
            Assert.Equal(ErrorCode.InvalidOperation,GL.GetError());
            // Native error reporting does not replace observation of the actual binding.
            Assert.Equal(GL.GetInteger(GetPName.ElementArrayBufferBinding), cache.GetBoundBuffer(BufferTarget.ElementArrayBuffer));
            Assert.Equal(before,entry.MetadataRevision);
            Assert.True(cache.GetVertexConfigurationRevision(vao.VertexArrayId)>configuration);
            // A pending unrelated error must also survive a successful binding change and later cache recovery.
            GL.Enable((EnableCap)(-1));
            OpenGLBufferBindings.BindBuffer(cache, BufferTarget.ElementArrayBuffer,0);
            Assert.Equal(0,cache.GetBoundBuffer(BufferTarget.ElementArrayBuffer));
            Assert.Equal(ErrorCode.InvalidEnum,GL.GetError());
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Preserves an external element selector for exact native dispatch and lazy rejection recovery.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BindExternalElement(int buffer) => GL.BindBuffer(BufferTarget.ElementArrayBuffer, buffer);
    #endregion
}
