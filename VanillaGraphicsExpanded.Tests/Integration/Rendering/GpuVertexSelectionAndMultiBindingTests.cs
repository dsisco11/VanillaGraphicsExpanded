using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Exercises selector recovery, partial multi-binding and revision-aware double attribute configuration.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuVertexSelectionAndMultiBindingTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Both selector overloads preserve actual selection after rejection and direct subsequent metadata to that selection.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SelectorUsesActualNativeSelection(bool unsigned)
    {
        // Arrange: Bind the first VAO and record both VAOs' metadata/configuration revisions.
        // Act: Select the second through this overload, try an invalid selection, then enable an attribute.
        // Assert: Native selection remains the second VAO after rejection and only its configuration revision advances.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var first=NativeResourceSetup.CreateVertexArray(); using var second=NativeResourceSetup.CreateVertexArray(); OpenGLVertexSelection.Bind(OpenGlStateCache.Current, first.VertexArrayId);
            var manager=RHIResourceManagerSystem.CurrentManager!;
            var a=manager.GetResourceRegistration(first)!; var b=manager.GetResourceRegistration(second)!;
            ulong ar=a.MetadataRevision,br=b.MetadataRevision;
            var cache=OpenGlStateCache.Current;
            ulong ac=cache.GetVertexConfigurationRevision(first.VertexArrayId),bc=cache.GetVertexConfigurationRevision(second.VertexArrayId);
            Select(second.VertexArrayId,unsigned);
            Assert.Equal(second.VertexArrayId,OpenGlStateCache.Current.GetCurrentVao());
            Assert.Equal(ar,a.MetadataRevision); Assert.Equal(br,b.MetadataRevision);
            Assert.Equal(ac,cache.GetVertexConfigurationRevision(first.VertexArrayId));
            Assert.Equal(bc,cache.GetVertexConfigurationRevision(second.VertexArrayId));
            Select(int.MaxValue,unsigned);
            Assert.Equal(ErrorCode.InvalidOperation,GL.GetError());
            GL.EnableVertexAttribArray(0);
            Assert.Equal(second.VertexArrayId,OpenGlStateCache.Current.GetCurrentVao());
            Assert.Equal(ar,a.MetadataRevision); Assert.Equal(br,b.MetadataRevision);
            Assert.Equal(ac,cache.GetVertexConfigurationRevision(first.VertexArrayId));
            Assert.True(cache.GetVertexConfigurationRevision(second.VertexArrayId)>bc);
            GL.GetVertexAttrib(0,VertexAttribParameter.ArrayEnabled,out int enabled); Assert.Equal(1,enabled);
            GL.Enable((EnableCap)int.MaxValue); Select(first.VertexArrayId,unsigned);
            Assert.Equal(ErrorCode.InvalidEnum,GL.GetError());
            Assert.Equal(first.VertexArrayId,OpenGlStateCache.Current.GetCurrentVao());
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>A rejected multi-bind element does not hide the successful neighboring update or affect another VAO.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MultiBindingPublishesPartialNativeSuccess(bool named)
    {
        // Arrange: Bind old buffers to two slots and select the VAO that should remain current for this overload.
        // Act: Replace slot zero while supplying an invalid buffer for slot one.
        // Assert: Native slot zero changes, slot one stays old, and only the target VAO configuration revision advances.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var vao=NativeResourceSetup.CreateVertexArray(); using var sentinel=NativeResourceSetup.CreateVertexArray();
            using var old=RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource(); old.Allocate(128);
            using var next=RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource(); next.Allocate(128);
            GL.VertexArrayVertexBuffer(vao.VertexArrayId,0,old.BufferId,0,16);
            GL.VertexArrayVertexBuffer(vao.VertexArrayId,1,old.BufferId,0,16);
            OpenGLVertexSelection.Bind(OpenGlStateCache.Current,(named?sentinel:vao).VertexArrayId);
            var manager=RHIResourceManagerSystem.CurrentManager!; var entry=manager.GetResourceRegistration(vao)!; var other=manager.GetResourceRegistration(sentinel)!;
            ulong revision=entry.MetadataRevision,storage=entry.StorageRevision,otherRevision=other.MetadataRevision;
            var cache=OpenGlStateCache.Current;
            ulong configuration=cache.GetVertexConfigurationRevision(vao.VertexArrayId),otherConfiguration=cache.GetVertexConfigurationRevision(sentinel.VertexArrayId);
            MultiBind(named,vao.VertexArrayId,new[]{next.BufferId,int.MaxValue});
            Assert.Equal(ErrorCode.InvalidOperation,GL.GetError());
            GL.GetVertexArrayIndexed(vao.VertexArrayId,0,(VertexArrayIndexedParameter)All.VertexBindingBuffer,out int first);
            GL.GetVertexArrayIndexed(vao.VertexArrayId,1,(VertexArrayIndexedParameter)All.VertexBindingBuffer,out int second);
            Assert.Equal(next.BufferId,first); Assert.Equal(old.BufferId,second);
            Assert.Equal(revision,entry.MetadataRevision); Assert.Equal(storage,entry.StorageRevision); Assert.Equal(otherRevision,other.MetadataRevision);
            Assert.True(cache.GetVertexConfigurationRevision(vao.VertexArrayId)>configuration);
            Assert.Equal(otherConfiguration,cache.GetVertexConfigurationRevision(sentinel.VertexArrayId));
            Assert.Equal((named?sentinel:vao).VertexArrayId,GL.GetInteger(GetPName.VertexArrayBinding)); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Native double-format setup records long attribute format and the configured binding divisor.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NativeDoubleAttributeConfigurationPreservesFormatAndDivisor(bool direct)
    {
        // Arrange: Configure two double-format attributes on binding two with divisor three, with DSA mode selected by the row.
        // Act: Select the configured VAO and query attribute one's long-format flag and divisor.
        // Assert: Both native properties retain their configured values and GL reports no error.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var capabilities=new DirectStateAccessCapabilityScope(direct);
            using var vao=NativeResourceSetup.CreateVertexArray(); using var sentinel=NativeResourceSetup.CreateVertexArray(); OpenGLVertexSelection.Bind(OpenGlStateCache.Current, sentinel.VertexArrayId);
            using var buffer=RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource(); buffer.Allocate(256);
            GL.VertexArrayVertexBuffer(vao.VertexArrayId, 2, buffer.BufferId, (IntPtr)16, 32);
            GL.VertexArrayBindingDivisor(vao.VertexArrayId, 2, 3);
            for (int attribute = 0; attribute < 2; attribute++)
            {
                GL.VertexArrayAttribLFormat(vao.VertexArrayId, attribute, 2, VertexAttribDoubleType.Double, attribute * 16);
                GL.VertexArrayAttribBinding(vao.VertexArrayId, attribute, 2);
                GL.EnableVertexArrayAttrib(vao.VertexArrayId, attribute);
            }
            if(direct) Assert.Equal(sentinel.VertexArrayId,GL.GetInteger(GetPName.VertexArrayBinding));
            OpenGLVertexSelection.Bind(OpenGlStateCache.Current, vao.VertexArrayId);
            GL.GetVertexAttrib(1,(VertexAttribParameter)All.VertexAttribArrayLong,out int isLong);
            GL.GetVertexAttrib(1,VertexAttribParameter.VertexAttribArrayDivisor,out int divisor);
            Assert.Equal(1,isLong); Assert.Equal(3,divisor); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Managed geometry creation completes without native errors when direct state access is disabled.</summary>
    [Fact]
    public void ManagedGeometryCreationLeavesNoGlErrorsWithDirectStateAccessDisabled()
    {
        // Arrange: Disable direct state access and describe two double-format attributes plus indexed geometry.
        // Act: Create managed geometry through the production geometry path.
        // Assert: The GL error query returns NoError.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var capabilities=new DirectStateAccessCapabilityScope(false);
            var layout=new RHIVertexLayout(new[]{new RHIVertexAttribute(0,2,VertexComponentFormat.Double,VertexInterpretation.Double,0,0,32),new RHIVertexAttribute(1,2,VertexComponentFormat.Double,VertexInterpretation.Double,0,16,32)});
            using var geometry=ManagedGraphicsGeometry.Create(layout,PrimitiveTopology.Triangles,new[]{new GraphicsVertexData(0,new byte[96])},new uint[]{0,1,2});
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion
    #region Private
    /// <summary>Executes either native overload so the driver-selected VAO remains observable.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Select(int vao,bool unsigned) { if(unsigned) GL.BindVertexArray((uint)vao); else GL.BindVertexArray(vao); }
    /// <summary>Forwards two native bindings so driver partial-success behavior remains observable.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void MultiBind(bool named,int vao,int[] buffers)
    {
        var offsets=new IntPtr[2]; var strides=new[]{16,16};
        if(named) GL.VertexArrayVertexBuffers(vao,0,2,buffers,offsets,strides);
        else GL.BindVertexBuffers(0,2,buffers,offsets,strides);
    }
    #endregion
}
