using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises selector recovery, partial multi-binding and revision-aware double attribute configuration.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuVertexSelectionAndMultiBindingTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Both selector overloads preserve actual selection after rejection and direct subsequent metadata to that selection.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SelectorUsesActualNativeSelection(bool unsigned)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony=new Harmony("VGE.Tests.VertexSelector");

        try
        {
            using var first=NativeResourceSetup.CreateVertexArray(); using var second=NativeResourceSetup.CreateVertexArray(); OpenGLVertexSelection.Bind(OpenGlStateCache.Current, first.VertexArrayId);
            var manager=RHIResourceManagerSystem.CurrentManager!;
            var a=manager.GetResourceRegistration(first)!; var b=manager.GetResourceRegistration(second)!;
            ulong ar=a.MetadataRevision,br=b.MetadataRevision;
            Select(second.VertexArrayId,unsigned);
            Assert.Equal(second.VertexArrayId,OpenGlStateCache.Current.GetCurrentVao());
            Assert.Equal(ar,a.MetadataRevision); Assert.Equal(br,b.MetadataRevision);
            Select(int.MaxValue,unsigned);
            Assert.Equal(ErrorCode.InvalidOperation,GL.GetError());
            GL.EnableVertexAttribArray(0);
            Assert.Equal(second.VertexArrayId,OpenGlStateCache.Current.GetCurrentVao());
            Assert.Equal(ar,a.MetadataRevision); Assert.Equal(br+1,b.MetadataRevision);
            GL.GetVertexAttrib(0,VertexAttribParameter.ArrayEnabled,out int enabled); Assert.Equal(1,enabled);
            GL.Enable((EnableCap)int.MaxValue); Select(first.VertexArrayId,unsigned);
            Assert.Equal(ErrorCode.InvalidEnum,GL.GetError());
            Assert.Equal(first.VertexArrayId,OpenGlStateCache.Current.GetCurrentVao());
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>A rejected multi-bind element does not hide the successful neighboring update or affect another VAO.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MultiBindingPublishesPartialNativeSuccess(bool named)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony=new Harmony("VGE.Tests.VertexMultiBinding");

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
            MultiBind(named,vao.VertexArrayId,new[]{next.BufferId,int.MaxValue});
            Assert.Equal(ErrorCode.InvalidOperation,GL.GetError());
            GL.GetVertexArrayIndexed(vao.VertexArrayId,0,(VertexArrayIndexedParameter)All.VertexBindingBuffer,out int first);
            GL.GetVertexArrayIndexed(vao.VertexArrayId,1,(VertexArrayIndexedParameter)All.VertexBindingBuffer,out int second);
            Assert.Equal(next.BufferId,first); Assert.Equal(old.BufferId,second);
            Assert.Equal(revision+1,entry.MetadataRevision); Assert.Equal(storage,entry.StorageRevision); Assert.Equal(otherRevision,other.MetadataRevision);
            Assert.Equal((named?sentinel:vao).VertexArrayId,GL.GetInteger(GetPName.VertexArrayBinding)); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Double attributes retain offset and divisor semantics through direct and legacy helper execution.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DoubleHelperConfiguresMultipleAttributes(bool direct)
    {
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
    /// <summary>Geometry construction keeps a single mutable helper current while configuring multiple legacy attributes.</summary>
    [Fact]
    public void ManagedGeometrySupportsSharedLegacyBinding()
    {
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
    /// <summary>Preserves native signed and unsigned selector calls for production transpilation.</summary>
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
