using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies exact buffer-binding overloads preserve VAO metadata effects and cache recovery.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuVertexBufferBindingInterceptionTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Only element binding advances the backend configuration epoch; the empty canonical VAO snapshot stays equal.</summary>
    [Theory]
    [InlineData(0,false)] [InlineData(1,false)] [InlineData(2,false)]
    [InlineData(0,true)] [InlineData(1,true)] [InlineData(2,true)]
    public void ExactBindingOverloadsRecoverActualNativeResult(int variant,bool element)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony=new Harmony("VGE.Tests.VertexBufferBindingInterception");

        try
        {
            using var vao=NativeResourceSetup.CreateVertexArray(); OpenGLVertexSelection.Bind(OpenGlStateCache.Current, vao.VertexArrayId);
            using var first=RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource(); first.Allocate(64);
            using var second=RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource(); second.Allocate(64);
            var target=element?BufferTarget.ElementArrayBuffer:BufferTarget.ArrayBuffer;
            var cache=OpenGlStateCache.Current;
            OpenGLBufferBindings.BindBuffer(cache, target,first.BufferId);
            Assert.Equal(first.BufferId,cache.GetBoundBuffer(target));
            var entry=RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(vao)!;
            ulong revision=entry.MetadataRevision;
            ulong configuration=cache.GetVertexConfigurationRevision(vao.VertexArrayId);
            BindNative(variant,target,second.BufferId);
            Assert.Equal(second.BufferId,cache.GetBoundBuffer(target));
            Assert.Equal(revision,entry.MetadataRevision);
            if(element) Assert.True(cache.GetVertexConfigurationRevision(vao.VertexArrayId)>configuration);
            else Assert.Equal(configuration,cache.GetVertexConfigurationRevision(vao.VertexArrayId));
            configuration=cache.GetVertexConfigurationRevision(vao.VertexArrayId);
            BindNative(variant,target,int.MaxValue);
            Assert.Equal(ErrorCode.InvalidOperation,GL.GetError());
            // Recover the driver-reported binding even when the rejected command changes observable state.
            Assert.Equal(GL.GetInteger(element ? GetPName.ElementArrayBufferBinding : GetPName.ArrayBufferBinding), cache.GetBoundBuffer(target));
            Assert.Equal(revision,entry.MetadataRevision);
            if(element) Assert.True(cache.GetVertexConfigurationRevision(vao.VertexArrayId)>configuration);
            else Assert.Equal(configuration,cache.GetVertexConfigurationRevision(vao.VertexArrayId));
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion
    #region Private
    /// <summary>Uses signed and unsigned native binding overloads that are not the fixed core-int replacement.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BindNative(int variant,BufferTarget target,int buffer)
    {
        if(variant==0) GL.BindBuffer(target,(uint)buffer);
        else if(variant==1) GL.Arb.BindBuffer((BufferTargetArb)target,buffer);
        else GL.Arb.BindBuffer((BufferTargetArb)target,(uint)buffer);
    }
    #endregion
}

