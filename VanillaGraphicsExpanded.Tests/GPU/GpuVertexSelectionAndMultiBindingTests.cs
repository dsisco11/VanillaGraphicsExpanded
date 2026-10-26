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
        harmony.Patch(AccessTools.Method(typeof(GpuVertexSelectionAndMultiBindingTests),nameof(Select)),transpiler:new HarmonyMethod(typeof(EngineStateSwitchingHook),nameof(EngineStateSwitchingHook.Transpiler)));
        try
        {
            using var first=GpuVao.Create(); using var second=GpuVao.Create(); first.Bind();
            var manager=GpuResourceManagerSystem.CurrentManager!;
            var a=manager.GetResourceRegistration(first)!; var b=manager.GetResourceRegistration(second)!;
            ulong ar=a.MetadataRevision,br=b.MetadataRevision;
            Select(second.VertexArrayId,unsigned);
            Assert.Equal(second.VertexArrayId,StateCache.Current.GetCurrentVao());
            Assert.Equal(ar,a.MetadataRevision); Assert.Equal(br,b.MetadataRevision);
            Select(int.MaxValue,unsigned);
            Assert.Equal(ErrorCode.InvalidOperation,GL.GetError());
            StateCache.Current.EnableVertexAttribArray(0);
            Assert.Equal(second.VertexArrayId,StateCache.Current.GetCurrentVao());
            Assert.Equal(ar,a.MetadataRevision); Assert.Equal(br+1,b.MetadataRevision);
            GL.GetVertexAttrib(0,VertexAttribParameter.ArrayEnabled,out int enabled); Assert.Equal(1,enabled);
            GL.Enable((EnableCap)int.MaxValue); Select(first.VertexArrayId,unsigned);
            Assert.Equal(ErrorCode.InvalidEnum,GL.GetError());
            Assert.Equal(first.VertexArrayId,StateCache.Current.GetCurrentVao());
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    /// <summary>A rejected multi-bind element does not hide the successful neighboring update or affect another VAO.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MultiBindingPublishesPartialNativeSuccess(bool named)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony=new Harmony("VGE.Tests.VertexMultiBinding");
        harmony.Patch(AccessTools.Method(typeof(GpuVertexSelectionAndMultiBindingTests),nameof(MultiBind)),transpiler:new HarmonyMethod(typeof(EngineStateSwitchingHook),nameof(EngineStateSwitchingHook.Transpiler)));
        try
        {
            using var vao=GpuVao.Create(); using var sentinel=GpuVao.Create();
            using var old=RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = null }).RequireResource(); old.Allocate(128);
            using var next=RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = null }).RequireResource(); next.Allocate(128);
            StateCache.Current.VertexArrayVertexBuffer(vao.VertexArrayId,0,old.BufferId,0,16);
            StateCache.Current.VertexArrayVertexBuffer(vao.VertexArrayId,1,old.BufferId,0,16);
            (named?sentinel:vao).Bind();
            var manager=GpuResourceManagerSystem.CurrentManager!; var entry=manager.GetResourceRegistration(vao)!; var other=manager.GetResourceRegistration(sentinel)!;
            ulong revision=entry.MetadataRevision,storage=entry.StorageRevision,otherRevision=other.MetadataRevision;
            MultiBind(named,vao.VertexArrayId,new[]{next.BufferId,int.MaxValue});
            Assert.Equal(ErrorCode.InvalidOperation,GL.GetError());
            GL.GetVertexArrayIndexed(vao.VertexArrayId,0,(VertexArrayIndexedParameter)All.VertexBindingBuffer,out int first);
            GL.GetVertexArrayIndexed(vao.VertexArrayId,1,(VertexArrayIndexedParameter)All.VertexBindingBuffer,out int second);
            Assert.Equal(next.BufferId,first); Assert.Equal(old.BufferId,second);
            Assert.Equal(revision+1,entry.MetadataRevision); Assert.Equal(storage,entry.StorageRevision); Assert.Equal(otherRevision,other.MetadataRevision);
            Assert.Equal((named?sentinel:vao).VertexArrayId,GL.GetInteger(GetPName.VertexArrayBinding)); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
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
            using var vao=GpuVao.Create(); using var sentinel=GpuVao.Create(); sentinel.Bind();
            using var buffer=RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = null }).RequireResource(); buffer.Allocate(256);
            var binding=vao.GetBinding(2).BindVertexBuffer(buffer.BufferId,16,32).SetDivisor(3);
            binding.SetDoubleAttrib(0,2,0); binding.SetDoubleAttrib(1,2,16);
            if(direct) Assert.Equal(sentinel.VertexArrayId,GL.GetInteger(GetPName.VertexArrayBinding));
            vao.Bind();
            GL.GetVertexAttrib(1,(VertexAttribParameter)All.VertexAttribArrayLong,out int isLong);
            GL.GetVertexAttrib(1,VertexAttribParameter.VertexAttribArrayDivisor,out int divisor);
            Assert.Equal(1,isLong); Assert.Equal(3,divisor); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    /// <summary>Geometry construction keeps a single mutable helper current while configuring multiple legacy attributes.</summary>
    [Fact]
    public void ManagedGeometrySupportsSharedLegacyBinding()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var capabilities=new DirectStateAccessCapabilityScope(false);
            var layout=new VertexLayoutDesc(new[]{new VertexAttributeDesc(0,2,VertexAttribPointerType.Double,VertexInterpretation.Double,0,0,32),new VertexAttributeDesc(1,2,VertexAttribPointerType.Double,VertexInterpretation.Double,0,16,32)});
            using var geometry=ManagedGraphicsGeometry.Create(layout,PrimitiveType.Triangles,new[]{new GraphicsVertexData(0,new byte[96])},new uint[]{0,1,2});
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
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
