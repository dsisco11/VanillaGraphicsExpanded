using OpenTK.Graphics.OpenGL;
using System.Runtime.CompilerServices;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks retained binding helpers against original ownership, canonical revisions and current native facts.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuVertexBindingLifetimeTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>External named edits refresh helper copies without disturbing bindings or restoring a stale divisor.</summary>
    [Fact]
    public void DirectHelperRefreshesActualBindingAndPreservesSentinels()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var vao=GpuVao.Create(); using var sentinel=GpuVao.Create();
            using var first=GpuShaderStorageBuffer.Create(); first.Allocate(128);
            using var second=GpuShaderStorageBuffer.Create(); second.Allocate(128);
            var helper=vao.GetBinding(2).BindVertexBuffer(first.BufferId,0,16).SetDivisor(3);
            var copied=helper;
            sentinel.Bind(); StateCache.Current.BindBuffer(BufferTarget.ArrayBuffer,first.BufferId);
            StateCache.Current.VertexArrayVertexBuffer(vao.VertexArrayId,2,second.BufferId,32,24);
            StateCache.Current.VertexArrayBindingDivisor(vao.VertexArrayId,2,7);
            Assert.Equal(second.BufferId,helper.BufferId); Assert.Equal(32,helper.BaseOffsetBytes); Assert.Equal(24,helper.StrideBytes); Assert.Equal(7,helper.Divisor);
            Assert.Equal(second.BufferId,copied.BufferId);
            helper.SetFloatAttrib(0,2,VertexAttribPointerType.Float,false,4);
            GL.GetVertexArrayIndexed(vao.VertexArrayId,2,(VertexArrayIndexedParameter)All.VertexBindingDivisor,out int divisor);
            Assert.Equal(7,divisor);
            Assert.Equal(sentinel.VertexArrayId,GL.GetInteger(GetPName.VertexArrayBinding));
            Assert.Equal(first.BufferId,GL.GetInteger(GetPName.ArrayBufferBinding));
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    /// <summary>An intercepted rejected DSA binding request cannot become the helper's recovered buffer, offset or stride.</summary>
    [Fact]
    public void RejectedDirectBindingRetainsActualFacts()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.RejectedExternalVertexBinding");
        harmony.Patch(AccessTools.Method(typeof(GpuVertexBindingLifetimeTests), nameof(RejectExternalBinding)),
            transpiler: new HarmonyMethod(typeof(EngineStateSwitchingHook), nameof(EngineStateSwitchingHook.Transpiler)));
        try
        {
            using var vao=GpuVao.Create(); using var buffer=GpuShaderStorageBuffer.Create(); buffer.Allocate(128);
            var helper=vao.GetBinding(1).BindVertexBuffer(buffer.BufferId,16,16);
            RejectExternalBinding(vao.VertexArrayId);
            Assert.Equal(ErrorCode.InvalidOperation,GL.GetError());
            Assert.Equal(buffer.BufferId,helper.BufferId); Assert.Equal(16,helper.BaseOffsetBytes); Assert.Equal(16,helper.StrideBytes);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    /// <summary>Disposal or native deletion prevents old helpers from acquiring a replacement registration under the same name.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RetiredOwnerCannotBeRetargeted(bool nativeDeletion)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var vao=GpuVao.Create(); var helper=vao.GetBinding(0); int id=vao.VertexArrayId;
            if(nativeDeletion) StateCache.Current.DeleteVertexArray(id); else vao.Dispose();
            if(nativeDeletion)
            {
                var manager=GpuResourceManagerSystem.CurrentManager!;
                var replacement=manager.RegisterCreatedResource<GpuVertexArrayMetadata>(GpuResourceKind.VertexArray,id);
                manager.PublishMetadata(replacement,default(GpuVertexArrayMetadata),GpuMetadataChange.Metadata);
            }
            Assert.Throws<ObjectDisposedException>(() => helper.SetFloatAttrib(0,2,VertexAttribPointerType.Float,false,0));
            Assert.Throws<ObjectDisposedException>(() => { _=helper.BufferId; });
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    /// <summary>Unavailable metadata and a changed manager context reject retained helpers; explicit recovery permits real readback.</summary>
    [Fact]
    public void AvailabilityAndContextGateRetainedHelper()
    {
        fixture.EnsureContextValid(); var origin=RenderContextRegistry.Current(); var current=origin;
        using var manager=new GpuResourceManager(() => current,static _ => true); GpuResourceManagerSystem.Initialize(manager);
        try
        {
            using var vao=GpuVao.Create(); var helper=vao.GetBinding(0);
            var entry=(GpuResourceMetadataEntry<GpuVertexArrayMetadata>)manager.GetResourceRegistration(vao)!;
            GpuResourceManager.InvalidateWrapperMetadata(vao);
            Assert.Throws<InvalidOperationException>(() => { _=helper.BufferId; });
            manager.PublishMetadata(entry,default(GpuVertexArrayMetadata),GpuMetadataChange.Metadata);
            Assert.Equal(0,helper.BufferId);
            try
            {
                current=(origin.Item1,origin.Item2+1);
                Assert.Throws<InvalidOperationException>(() => helper.SetDivisor(1));
            }
            finally { current=origin; }
            Assert.Equal(0,helper.Divisor); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { manager.Dispose(); GpuResourceManagerSystem.Shutdown(); StateCache.Current.InvalidateAll(); }
    }
    /// <summary>Legacy authored helpers support successive own attributes but reject copies and external changes instead of replaying stale state.</summary>
    [Fact]
    public void LegacyHelperRejectsStaleCopiesAndCanResetDivisor()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var capabilities=new DirectStateAccessCapabilityScope(false);
            using var vao=GpuVao.Create(); using var buffer=GpuShaderStorageBuffer.Create(); buffer.Allocate(128);
            var helper=vao.GetBinding(2).BindVertexBuffer(buffer.BufferId,0,16).SetDivisor(3);
            var copied=helper;
            helper.SetFloatAttrib(0,2,VertexAttribPointerType.Float,false,0);
            helper.SetFloatAttrib(1,2,VertexAttribPointerType.Float,false,8);
            Assert.Throws<InvalidOperationException>(() => copied.SetFloatAttrib(2,2,VertexAttribPointerType.Float,false,0));
            StateCache.Current.EnableVertexAttribArray(3);
            Assert.Throws<InvalidOperationException>(() => { _=helper.BufferId; });
            helper=vao.GetBinding(2).BindVertexBuffer(buffer.BufferId,0,16).SetDivisor(0);
            helper.SetFloatAttrib(0,2,VertexAttribPointerType.Float,false,0);
            GL.GetVertexAttrib(0,VertexAttribParameter.VertexAttribArrayDivisor,out int divisor); Assert.Equal(0,divisor);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Retains an exact external native call for production interception and rejection recovery.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RejectExternalBinding(int vao) => GL.VertexArrayVertexBuffer(vao, 1, int.MaxValue, (IntPtr)32, 24);
    #endregion
}
