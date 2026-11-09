using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Checks operation guards across context registration lifetimes independently of shared rendering fixtures.</summary>
[Collection("OperationContext")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuOperationContextFoundationTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Distinct registered context generations isolate guards sharing a numeric ID without native resource work.</summary>
    [Fact]
    public void GuardAdmissionAndDeletionAreContextScoped()
    {
        // Arrange a guard in the fixture's registered lifetime before assigning it a replacement generation.
        fixture.EnsureContextValid();
        var origin = RHICommandContext.EnterResourceOperation(new(GpuResourceKind.Buffer, 123));
        var temporaryOwner = new object();
        IRHIResourceOperation? next = null;
        try
        {
            // Re-register the same native fixture with a new lifetime; this checks guard identity, not a second native context.
            // Act by retiring A, admitting B with the same numeric key, and canceling only B's guard.
            RenderContextRegistry.Retire(fixture);
            RenderContextRegistry.RegisterCurrent(temporaryOwner, static _ => true);
            next = RHICommandContext.EnterResourceOperation(new(GpuResourceKind.Buffer, 123));
            RHICommandContext.CancelResourceOperation(GpuResourceKind.Buffer, 123);
            // Assert cancellation is scoped to B even though both lifetimes use the same resource key.
            Assert.True(next.Deleted);
            Assert.False(origin.Deleted);
            Assert.False(origin.Cancelled);
        }
        finally
        {
            if (next is not null) next.Dispose();
            origin.Dispose();
            RenderContextRegistry.Retire(temporaryOwner);
            fixture.RestoreContextRegistration();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }


    /// <summary>Out-of-order cleanup and repeated disposal release only the captured exclusion, without publication or admission.</summary>
    [Fact]
    public void SemanticGuardCleanupKeepsUnrelatedOperationAndRejectsReentry()
    {
        // Arrange independent resource-kind guards sharing one numeric name.
        fixture.EnsureContextValid();
        var first = RHICommandContext.EnterResourceOperation(new(GpuResourceKind.Buffer, 123));
        using var second = RHICommandContext.EnterResourceOperation(new(GpuResourceKind.Texture, 123));
        try
        {
            // Act by interrupting and disposing the first guard out of order, then canceling the second.
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
            // Assert each cleanup affects only its captured guard and allows a fresh texture operation.
            Assert.True(second.Cancelled);
            Assert.True(second.Deleted);
            using var replacement = RHICommandContext.EnterResourceOperation(new(GpuResourceKind.Texture, 123));
            Assert.False(replacement.Cancelled);
            Assert.False(replacement.Deleted);
        }
        finally { first.Dispose(); }
    }

    #endregion
}
