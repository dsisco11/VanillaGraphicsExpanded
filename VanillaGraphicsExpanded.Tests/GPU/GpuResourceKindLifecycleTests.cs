using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies every resource kind's owning disposal removes its native name and canonical registration.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuResourceKindLifecycleTests(HeadlessGLFixture fixture, ITestOutputHelper output)
{
    #region Public API
    /// <summary>Runs actual owning wrappers through their common OpenGlStateCache deletion and manager retirement flow.</summary>
    [Theory]
    [InlineData(GpuResourceKind.Buffer)]
    [InlineData(GpuResourceKind.VertexArray)]
    [InlineData(GpuResourceKind.Texture)]
    [InlineData(GpuResourceKind.Framebuffer)]
    [InlineData(GpuResourceKind.Renderbuffer)]
    [InlineData(GpuResourceKind.Query)]
    [InlineData(GpuResourceKind.Sync)]
    [InlineData(GpuResourceKind.Program)]
    [InlineData(GpuResourceKind.TransformFeedback)]
    [InlineData(GpuResourceKind.Sampler)]
    [InlineData(GpuResourceKind.ProgramPipeline)]
    [InlineData(GpuResourceKind.Shader)]
    [InlineData(GpuResourceKind.Semaphore)]
    [InlineData(GpuResourceKind.MemoryObject)]
    public void OwningDisposalRetiresNativeNameAndCanonicalEntry(GpuResourceKind kind)
    {
        fixture.EnsureContextValid();
        fixture.CleanupGpuResources();
        OpenGlStateCache.Current.InvalidateAll();
        using var manager = new RHIResourceManager();
        RHIResourceManagerSystem.Initialize(manager);
        try
        {
            if (kind == GpuResourceKind.Semaphore && !RHICommandContext.Current.Capabilities.ExternalSemaphores
                || kind == GpuResourceKind.MemoryObject && !RHICommandContext.Current.Capabilities.ExternalMemory)
            {
                // Keep registry retirement independently exercised without claiming native extension execution.
                output.WriteLine($"Native {kind} lifecycle NOT EXECUTED: required EXT capability unavailable; registry-only retirement follows.");
                GpuResourceRegistration unsupported = kind == GpuResourceKind.Semaphore
                    ? manager.RegisterCreatedResource<GpuSemaphoreMetadata>(kind, 71)
                    : manager.RegisterCreatedResource<GpuMemoryObjectMetadata>(kind, 71);
                manager.ObserveDeletion(kind, 71);
                Assert.Equal(GpuResourceRegistrationState.Deleted, unsupported.State);
                Assert.Null(manager.FindResource(kind, 71));
                return;
            }

            using var owner = CreateOwner(kind);
            RealizeName(owner);
            nint id = owner.ResourceId;
            var original = manager.GetResourceRegistration(owner);
            Assert.NotNull(original);
            Assert.Equal(kind, original.Kind);
            Assert.Equal(id, original.Id);
            // EXT_external_objects reserves semaphore names before external payload import. The
            // fixture exercises deletion of that generated name without fabricating an external handle.
            if (kind == GpuResourceKind.Semaphore)
                output.WriteLine("Native semaphore generated-name deletion executed; imported external semaphore payload lifecycle NOT EXECUTED.");
            else Assert.True(IsNativeResource(kind, id));
            owner.Dispose();
            owner.Dispose();
            Assert.True(owner.IsDisposed);
            Assert.Equal((nint)0, owner.ResourceId);
            Assert.Equal(GpuResourceRegistrationState.Deleted, original.State);
            Assert.Null(manager.FindResource(kind, id));
            Assert.False(IsNativeResource(kind, id));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            output.WriteLine($"Native {kind} owning disposal and canonical entry retirement executed.");
        }
        finally
        {
            manager.Dispose();
            RHIResourceManagerSystem.Shutdown();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }
    #endregion

    #region Private
    /// <summary>Creates actual production owners without compiling shaders or performing rendering.</summary>
    private static GpuResource CreateOwner(GpuResourceKind kind)
    {
        return kind switch
        {
            GpuResourceKind.Buffer => RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource(),
            GpuResourceKind.VertexArray => NativeResourceSetup.CreateVertexArray(),
            GpuResourceKind.Texture => RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource(),
            GpuResourceKind.Framebuffer => new DynamicGpuFramebuffer(true),
            GpuResourceKind.Renderbuffer => RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba8, Width = 2, Height = 2, Samples = 0, Label = null }).RequireResource(),
            GpuResourceKind.Query => RHICommandContext.Current.CreateQuery(new(QueryKind.TimeElapsed)).RequireResource(),
            GpuResourceKind.Sync => RHICommandContext.Current.InsertFence(),
            GpuResourceKind.Program => NativeResourceSetup.CreateProgram(),
            GpuResourceKind.TransformFeedback => RHICommandContext.Current.CreateTransformFeedback(new()).RequireResource(),
            GpuResourceKind.Sampler => RHICommandContext.Current.CreateSampler(new RHISamplerDescription {  }).RequireResource(),
            GpuResourceKind.ProgramPipeline => NativeResourceSetup.CreateProgramPipeline(),
            GpuResourceKind.Shader => new GpuShaderModule(GL.CreateShader(ShaderType.VertexShader), ShaderStage.Vertex),
            GpuResourceKind.Semaphore => RHICommandContext.Current.CreateSemaphore(new()).RequireResource(),
            GpuResourceKind.MemoryObject => RHICommandContext.Current.CreateMemoryObject(new(Dedicated: false)).RequireResource(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    /// <summary>Realizes reserved generated names so native existence checks distinguish deletion from unrealized names.</summary>
    private static void RealizeName(GpuResource resource)
    {
        switch (resource)
        {
            case GpuVbo buffer: buffer.Allocate(16); break;
            case GpuVao array: OpenGLVertexSelection.Bind(OpenGlStateCache.Current, array.VertexArrayId); break;
            case GpuFramebuffer framebuffer: framebuffer.Bind(); break;
            case GpuQuery: break;
            case GpuTransformFeedback: break;
            case GpuProgramPipeline pipeline: OpenGLProgramPipelineSelection.Bind(OpenGlStateCache.Current, pipeline.PipelineId); break;
        }
    }

    /// <summary>Asks the driver about actual resource existence using each kind's native identity width.</summary>
    private static bool IsNativeResource(GpuResourceKind kind, nint id)
    {
        int name = unchecked((int)id);
        return kind switch
        {
            GpuResourceKind.Buffer => GL.IsBuffer(name),
            GpuResourceKind.VertexArray => GL.IsVertexArray(name),
            GpuResourceKind.Texture => GL.IsTexture(name),
            GpuResourceKind.Framebuffer => GL.IsFramebuffer(name),
            GpuResourceKind.Renderbuffer => GL.IsRenderbuffer(name),
            GpuResourceKind.Query => GL.IsQuery(name),
            GpuResourceKind.Sync => GL.IsSync((IntPtr)id),
            GpuResourceKind.Program => GL.IsProgram(name),
            GpuResourceKind.TransformFeedback => GL.IsTransformFeedback(name),
            GpuResourceKind.Sampler => GL.IsSampler(name),
            GpuResourceKind.ProgramPipeline => GL.IsProgramPipeline(name),
            GpuResourceKind.Shader => GL.IsShader(name),
            GpuResourceKind.Semaphore => GL.Ext.IsSemaphore(name),
            GpuResourceKind.MemoryObject => GL.Ext.IsMemoryObject(name),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
    #endregion
}
