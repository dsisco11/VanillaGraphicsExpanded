using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
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
        var cache = OpenGlStateCache.Current;
        var origin = cache.EnterResourceOperation(new(GpuResourceKind.Buffer, 123));
        var temporaryOwner = new object();
        OpenGlStateCache.ResourceOperationGuard? next = null;
        try
        {
            // Re-register the same native fixture with a new lifetime; this checks guard identity, not a second native context.
            RenderContextRegistry.Retire(fixture);
            RenderContextRegistry.RegisterCurrent(temporaryOwner, static _ => true);
            next = cache.EnterResourceOperation(new(GpuResourceKind.Buffer, 123));
            cache.CancelResourceOperation(GpuResourceKind.Buffer, 123);
            Assert.True(next.Deleted);
            Assert.False(origin.Deleted);
            Assert.False(origin.Cancelled);
        }
        finally
        {
            if (next is not null) cache.ExitResourceOperation(next);
            cache.ExitResourceOperation(origin);
            RenderContextRegistry.Retire(temporaryOwner);
            RenderContextRegistry.RegisterCurrent(fixture, static _ => true);
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
                Assert.Throws<InvalidOperationException>(() => operation.Commit(new(64, BufferUsageHint.StaticDraw)));
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
