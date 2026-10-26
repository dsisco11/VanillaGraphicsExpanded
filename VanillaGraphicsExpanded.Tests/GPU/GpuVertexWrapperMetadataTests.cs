using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using System.Runtime.CompilerServices;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks owned VAO initialization and helper mutations against capability-selected native execution.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuVertexWrapperMetadataTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Creation realizes the VAO without stealing selection; helper configuration publishes and reaches native attributes.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void WrapperConfigurationUsesSelectedAddressing(bool direct)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var sentinel=GpuVao.Create(); sentinel.Bind();
            using var arraySentinel=RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = null }).RequireResource(); arraySentinel.Allocate(64);
            using var buffer=RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = null }).RequireResource(); buffer.Allocate(128);
            OpenGLBufferBindings.BindBuffer(StateCache.Current, BufferTarget.ArrayBuffer,arraySentinel.BufferId);
            using var capabilities=new DirectStateAccessCapabilityScope(direct);
            using var vao=GpuVao.Create();
            Assert.True(GL.IsVertexArray(vao.VertexArrayId));
            Assert.Equal(sentinel.VertexArrayId,GL.GetInteger(GetPName.VertexArrayBinding));
            var entry=GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(vao)!;
            ulong revision=entry.MetadataRevision,storage=entry.StorageRevision;
            var binding=vao.GetBinding(2).BindVertexBuffer(buffer.BufferId,16,16).SetDivisor(3);
            binding.SetFloatAttrib(0,2,VertexAttribPointerType.Float,false,4);
            vao.BindElementBuffer(buffer.BufferId);
            Assert.True(entry.MetadataRevision>revision); Assert.Equal(storage,entry.StorageRevision);
            if(direct)
            {
                Assert.Equal(sentinel.VertexArrayId,GL.GetInteger(GetPName.VertexArrayBinding));
                Assert.Equal(arraySentinel.BufferId,GL.GetInteger(GetPName.ArrayBufferBinding));
            }
            vao.Bind();
            GL.GetVertexAttrib(0,(VertexAttribParameter)All.VertexAttribArrayBufferBinding,out int actualBuffer);
            GL.GetVertexAttrib(0,VertexAttribParameter.ArraySize,out int size);
            GL.GetVertexAttrib(0,VertexAttribParameter.VertexAttribArrayDivisor,out int divisor);
            Assert.Equal(buffer.BufferId,actualBuffer); Assert.Equal(2,size); Assert.Equal(3,divisor);
            Assert.Equal(buffer.BufferId,StateCache.Current.GetBoundBuffer(BufferTarget.ElementArrayBuffer));
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    /// <summary>Element-buffer observation publishes executed changes and lazily resolves rejected commands without guessing bindings.</summary>
    [Fact]
    public void BoundElementBufferRejectsInvalidNameWithoutCachingRequest()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.RejectedExternalElementBinding");
        harmony.Patch(AccessTools.Method(typeof(GpuVertexWrapperMetadataTests), nameof(BindExternalElement)),
            transpiler: new HarmonyMethod(typeof(EngineStateSwitchingHook), nameof(EngineStateSwitchingHook.Transpiler)));
        try
        {
            using var vao=GpuVao.Create(); vao.Bind();
            using var buffer=RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = null }).RequireResource(); buffer.Allocate(64);
            var cache=StateCache.Current;
            var entry=GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(vao)!;
            ulong before=entry.MetadataRevision;
            OpenGLBufferBindings.BindBuffer(cache, BufferTarget.ElementArrayBuffer,buffer.BufferId);
            Assert.Equal(before+1,entry.MetadataRevision);
            Assert.Equal(buffer.BufferId,cache.GetBoundBuffer(BufferTarget.ElementArrayBuffer));
            before=entry.MetadataRevision;
            OpenGLBufferBindings.BindBuffer(cache, BufferTarget.ElementArrayBuffer,buffer.BufferId);
            Assert.Equal(before,entry.MetadataRevision);
            BindExternalElement(int.MaxValue);
            Assert.Equal(ErrorCode.InvalidOperation,GL.GetError());
            // Native error reporting does not replace observation of the actual binding.
            Assert.Equal(GL.GetInteger(GetPName.ElementArrayBufferBinding), cache.GetBoundBuffer(BufferTarget.ElementArrayBuffer));
            Assert.Equal(before+1,entry.MetadataRevision);
            // A pending unrelated error must also survive a successful binding change and later cache recovery.
            GL.Enable((EnableCap)(-1));
            OpenGLBufferBindings.BindBuffer(cache, BufferTarget.ElementArrayBuffer,0);
            Assert.Equal(0,cache.GetBoundBuffer(BufferTarget.ElementArrayBuffer));
            Assert.Equal(ErrorCode.InvalidEnum,GL.GetError());
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Preserves an external element selector for exact native dispatch and lazy rejection recovery.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BindExternalElement(int buffer) => GL.BindBuffer(BufferTarget.ElementArrayBuffer, buffer);
    #endregion
}
