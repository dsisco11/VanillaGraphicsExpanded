using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Gives destructive registered-context lifecycle checks a private native fixture.</summary>
[CollectionDefinition("OperationContext", DisableParallelization = true)]
public sealed class OperationContextCollection : ICollectionFixture<HeadlessGLFixture> { }

/// <summary>Checks operation guards across context registration lifetimes independently of shared rendering fixtures.</summary>
[Collection("OperationContext")]
[Trait("Category", "GPU")]
public sealed class GpuOperationContextFoundationTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Distinct registered context generations isolate guards sharing a numeric ID without native resource work.</summary>
    [Fact]
    public void GuardAdmissionAndDeletionAreContextScoped()
    {
        fixture.EnsureContextValid();
        var origin = RHICommandContext.EnterResourceOperation(new(GpuResourceKind.Buffer, 123));
        var temporaryOwner = new object();
        IRHIResourceOperation? next = null;
        try
        {
            // Re-register the same native fixture with a new lifetime; this checks guard identity, not a second native context.
            RenderContextRegistry.Retire(fixture);
            RenderContextRegistry.RegisterCurrent(temporaryOwner, static _ => true);
            next = RHICommandContext.EnterResourceOperation(new(GpuResourceKind.Buffer, 123));
            RHICommandContext.CancelResourceOperation(GpuResourceKind.Buffer, 123);
            Assert.True(next.Deleted);
            Assert.False(origin.Deleted);
            Assert.False(origin.Cancelled);
        }
        finally
        {
            if (next is not null) next.Dispose();
            origin.Dispose();
            RenderContextRegistry.Retire(temporaryOwner);
            RenderContextRegistry.RegisterCurrent(fixture, static _ => true);
            OpenGlStateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Out-of-order cleanup and repeated disposal release only the captured exclusion, without publication or admission.</summary>
    [Fact]
    public void SemanticGuardCleanupKeepsUnrelatedOperationAndRejectsReentry()
    {
        fixture.EnsureContextValid();
        var first = RHICommandContext.EnterResourceOperation(new(GpuResourceKind.Buffer, 123));
        using var second = RHICommandContext.EnterResourceOperation(new(GpuResourceKind.Texture, 123));
        try
        {
            Assert.Throws<InvalidOperationException>(() => RHICommandContext.RequireResourceOperationAvailable(GpuResourceKind.Buffer, 123));
            RHICommandContext.RequireResourceOperationAvailable(GpuResourceKind.Buffer, 123, first);
            first.Interrupt();
            Assert.True(first.Cancelled);
            Assert.False(first.Deleted);
            first.Dispose();
            first.Dispose();
            RHICommandContext.RequireResourceOperationAvailable(GpuResourceKind.Buffer, 123);
            Assert.Throws<InvalidOperationException>(() => RHICommandContext.RequireResourceOperationAvailable(GpuResourceKind.Texture, 123));
            RHICommandContext.CancelResourceOperation(GpuResourceKind.Texture, 123);
            Assert.True(second.Cancelled);
            Assert.True(second.Deleted);
            using var replacement = RHICommandContext.EnterResourceOperation(new(GpuResourceKind.Texture, 123));
            Assert.False(replacement.Cancelled);
            Assert.False(replacement.Deleted);
        }
        finally { first.Dispose(); }
    }

    /// <summary>Physical local namespaces isolate same-name guards and reject completion from another local context.</summary>
    [Fact]
    public unsafe void LocalContextsKeepOriginalOperationIdentity()
    {
        fixture.EnsureContextValid();
        var original = GLFW.GetCurrentContext();
        GLFW.WindowHint(WindowHintInt.ContextVersionMajor, 4);
        GLFW.WindowHint(WindowHintInt.ContextVersionMinor, 5);
        GLFW.WindowHint(WindowHintBool.Visible, false);
        var firstContext = GLFW.CreateWindow(8, 8, "Operation local A", null, null);
        var secondContext = GLFW.CreateWindow(8, 8, "Operation local B", null, null);
        GpuBufferObject? owner = null;
        GpuResourceManager.MetadataOperation<GpuBufferMetadata>? operation = null;
        IRHIResourceOperation? second = null;
        try
        {
            Assert.True(firstContext != null && secondContext != null);
            GLFW.MakeContextCurrent(firstContext);
            owner = RHICommandContext.Current.CreateBuffer(new RHIBufferDescription
            {
                Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable,
                Capacity = 16, Usage = BufferUsage.StaticDraw
            }).RequireResource();
            operation = new(owner, GpuMetadataChange.Storage);
            int id = (int)owner.ResourceId;
            Assert.Throws<InvalidOperationException>(() => RHICommandContext.RequireResourceOperationAvailable(GpuResourceKind.Buffer, id));

            // The second namespace may use the same numeric name, but cannot cancel or complete A's transaction.
            GLFW.MakeContextCurrent(secondContext);
            _ = RHICommandContext.Current;
            second = RHICommandContext.EnterResourceOperation(new(GpuResourceKind.Buffer, id));
            RHICommandContext.CancelResourceOperation(GpuResourceKind.Buffer, id);
            Assert.True(second.Deleted);
            Assert.Throws<InvalidOperationException>(() => operation.RequireLive());
            second.Dispose();
            second = null;

            GLFW.MakeContextCurrent(firstContext);
            _ = RHICommandContext.Current;
            operation.RequireLive();
            operation.Commit(new GpuBufferMetadata(16, BufferUsage.StaticDraw));
            Assert.Equal((nint)16, owner.SizeBytes);
            Assert.Equal(OpenTK.Graphics.OpenGL.ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            second?.Dispose();
            if (firstContext != null)
            {
                GLFW.MakeContextCurrent(firstContext);
                operation?.Dispose();
                owner?.Dispose();
                GLFW.DestroyWindow(firstContext);
            }
            if (secondContext != null) GLFW.DestroyWindow(secondContext);
            GLFW.MakeContextCurrent(original);
            GpuSupport.Initialize();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Context retirement prevents publication and does not issue native cleanup in the wrong lifetime.</summary>
    [Fact]
    public void ContextRetirementCancelsOperation()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int id = 0;
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource(); owner.Allocate(16); id = owner.BufferId;
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.GetResourceRegistration(owner));
            using (var operation = new GpuResourceManager.MetadataOperation<GpuBufferMetadata>(owner, GpuMetadataChange.Storage))
            {
                RenderContextRegistry.Retire(fixture);
                Assert.Throws<InvalidOperationException>(() => operation.Commit(new(64, BufferUsage.StaticDraw)));
            }
            Assert.Equal(GpuResourceRegistrationState.ContextRetired, entry.State);
            Assert.False(entry.MetadataAvailable);
            Assert.True(GL.IsBuffer(id));
        }
        finally
        {
            RenderContextRegistry.RegisterCurrent(fixture, static _ => true);
            if (id != 0 && GL.IsBuffer(id)) RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Buffer, id));
            fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll();
        }
    }

    #endregion
}
