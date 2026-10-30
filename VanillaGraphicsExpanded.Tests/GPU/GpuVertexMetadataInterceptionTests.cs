using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks vertex configuration revision publication without treating attribute values as resource metadata.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuVertexMetadataInterceptionTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Bound and named native edits advance the original VAO and recover affected EBO bindings without disturbing sentinels.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    public void NativeConfigurationPublishesAliases(int variant)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); var harmony=Patch();
        try
        {
            using var owner=NativeResourceSetup.CreateVertexArray(); using var sentinel=NativeResourceSetup.CreateVertexArray();
            using var buffer=RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource(); buffer.Allocate(64);
            using var previous=RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource(); previous.Allocate(64);
            var cache=OpenGlStateCache.Current;
            OpenGLVertexSelection.Bind(cache, owner.VertexArrayId); OpenGLBufferBindings.BindBuffer(cache, BufferTarget.ElementArrayBuffer,previous.BufferId);
            OpenGLVertexSelection.Bind(cache, sentinel.VertexArrayId); OpenGLBufferBindings.BindBuffer(cache, BufferTarget.ElementArrayBuffer,previous.BufferId);
            OpenGLBufferBindings.BindBuffer(cache, BufferTarget.ArrayBuffer,previous.BufferId);
            bool named=variant is 1 or 3 or 5 or 7 or 9 or 10 or 11;
            if(!named) OpenGLVertexSelection.Bind(cache, owner.VertexArrayId);
            var manager=RHIResourceManagerSystem.CurrentManager!;
            var entry=(GpuResourceMetadataEntry<GpuVertexArrayMetadata>)manager.GetResourceRegistration(owner)!;
            using var alias=new VertexAlias(owner.VertexArrayId); manager.AssociateResource(entry,alias);
            ulong revision=entry.MetadataRevision,storage=entry.StorageRevision;
            Mutate(variant,owner.VertexArrayId,buffer.BufferId);
            Assert.Equal(revision,entry.MetadataRevision); Assert.Equal(storage,entry.StorageRevision);
            Assert.True(manager.IsResourceMetadataAvailable(alias));
            Assert.Equal(named?sentinel.VertexArrayId:owner.VertexArrayId,GL.GetInteger(GetPName.VertexArrayBinding));
            Assert.Equal(previous.BufferId,GL.GetInteger(GetPName.ArrayBufferBinding));
            OpenGLVertexSelection.Bind(cache, owner.VertexArrayId);
            if(variant is 0 or 1)
            {
                GL.GetInteger((GetIndexedPName)All.VertexBindingBuffer,2,out int actual); Assert.Equal(buffer.BufferId,actual);
            }
            else if(variant is 2 or 3)
            {
                GL.GetVertexAttrib(0,VertexAttribParameter.ArraySize,out int actual); Assert.Equal(2,actual);
            }
            else if(variant is 4 or 5)
            {
                GL.GetVertexAttrib(0,(VertexAttribParameter)All.VertexAttribBinding,out int actual); Assert.Equal(2,actual);
            }
            else if(variant is 6 or 7)
            {
                GL.GetVertexAttrib(0,VertexAttribParameter.ArrayEnabled,out int actual); Assert.Equal(1,actual);
            }
            else if(variant is 8 or 9)
            {
                GL.GetInteger((GetIndexedPName)All.VertexBindingDivisor,2,out int actual); Assert.Equal(3,actual);
            }
            else Assert.Equal(buffer.BufferId,cache.GetBoundBuffer(BufferTarget.ElementArrayBuffer));
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Proven rejected configuration and numeric attribute contents both preserve original configuration revisions.</summary>
    [Fact]
    public void RejectedConfigurationAndNumericValuesHaveDifferentEffects()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); var harmony=Patch();
        try
        {
            using var vao=NativeResourceSetup.CreateVertexArray(); OpenGLVertexSelection.Bind(OpenGlStateCache.Current, vao.VertexArrayId);
            var entry=RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(vao)!;
            ulong revision=entry.MetadataRevision;
            Mutate(12,vao.VertexArrayId,0);
            Assert.Equal(ErrorCode.InvalidValue,GL.GetError()); Assert.Equal(revision,entry.MetadataRevision);
            revision=entry.MetadataRevision;
            Mutate(13,vao.VertexArrayId,0);
            Assert.Equal(revision,entry.MetadataRevision);
            float[] value=new float[4]; GL.GetVertexAttrib(0,VertexAttribParameter.CurrentVertexAttrib,value);
            Assert.Equal(0.5f,value[0]); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Publication failure cannot cancel native configuration or expose stale canonical facts.</summary>
    [Fact]
    public void ObserverPublicationFailureStillExecutesNativeConfiguration()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.VertexPublicationFailure." + Guid.NewGuid());
        try
        {
            using var vao = NativeResourceSetup.CreateVertexArray();
            var publication = AccessTools.Method(typeof(GpuResourceMetadataEntry<GpuVertexArrayMetadata>), "Publish");
            harmony.Patch(publication, prefix: new HarmonyMethod(typeof(GpuVertexMetadataInterceptionTests), nameof(ThrowMetadataPublication)));
            GL.EnableVertexArrayAttrib(vao.VertexArrayId, 0);
            GL.GetVertexArrayIndexed(vao.VertexArrayId, 0, VertexArrayIndexedParameter.VertexAttribArrayEnabled, out int enabled);
            Assert.Equal(1, enabled);
            Assert.False(RHIResourceManagerSystem.CurrentManager!.IsResourceMetadataAvailable(vao));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Untracked native VAOs receive actual configuration without speculative registration.</summary>
    [Fact]
    public void UntrackedConfigurationDoesNotCreateEntry()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); var harmony=Patch(); OpenGLDriverCalls.CreateVertexArrays(1,out int vao);
        try
        {
            Mutate(7,vao,0);
            Assert.Null(RHIResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.VertexArray,vao));
            GL.GetVertexArrayIndexed(vao,0,VertexArrayIndexedParameter.VertexAttribArrayEnabled,out int enabled); Assert.Equal(1,enabled);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.VertexArray, vao)); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion
    #region Private
    /// <summary>Redirects actual fixture native calls through the production metadata adapter.</summary>
    private static Harmony Patch()
    {
        var harmony=new Harmony("VGE.Tests.VertexMetadataInterception");

        return harmony;
    }
    /// <summary>Preserves native addressing and error semantics for representative configuration families.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Mutate(int variant,int vao,int buffer)
    {
        switch(variant)
        {
            case 0: GL.BindVertexBuffer(2,buffer,IntPtr.Zero,16); break;
            case 1: GL.VertexArrayVertexBuffer(vao,2,buffer,IntPtr.Zero,16); break;
            case 2: GL.VertexAttribFormat(0,2,VertexAttribType.Float,false,0); break;
            case 3: GL.VertexArrayAttribFormat(vao,0,2,VertexAttribType.Float,false,0); break;
            case 4: GL.VertexAttribBinding(0,2); break;
            case 5: GL.VertexArrayAttribBinding(vao,0,2); break;
            case 6: GL.EnableVertexAttribArray(0); break;
            case 7: GL.EnableVertexArrayAttrib(vao,0); break;
            case 8: GL.VertexBindingDivisor(2,3); break;
            case 9: GL.VertexArrayBindingDivisor(vao,2,3); break;
            case 10: GL.VertexArrayElementBuffer(vao,buffer); break;
            case 11: GL.VertexArrayElementBuffer((uint)vao,(uint)buffer); break;
            case 12: GL.VertexAttribFormat(-1,2,VertexAttribType.Float,false,0); break;
            case 13: GL.VertexAttrib1(0,0.5f); break;
        }
    }


    /// <summary>Injects a managed publication fault after the real native operation has executed.</summary>
    private static void ThrowMetadataPublication() => throw new ApplicationException("Injected publication failure.");
    /// <summary>Receives configuration publication without owning the VAO.</summary>
    private sealed class VertexAlias(int id) : GpuResource<GpuVertexArrayMetadata>
    {
        /// <inheritdoc />
        public override nint ResourceId { get; protected set; }=id;
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => GpuResourceKind.VertexArray;
        /// <inheritdoc />
        protected override bool OwnsResource => false;
        /// <inheritdoc />
        public override void SetDebugName(string? name) { }
    }
    #endregion
}

