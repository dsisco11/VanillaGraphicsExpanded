using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;


using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Integration.LumOn;
/// <summary>Verifies pipeline target ownership and context-bound deferred cleanup.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class PbrLumOnPipelineTargetsLifetimeTests : IDisposable
{
    private readonly HeadlessGLFixture fixture;

    #region Public API

    /// <summary>Uses the collection's shared OpenGL context.</summary>
    public PbrLumOnPipelineTargetsLifetimeTests(HeadlessGLFixture fixture)
    {
        this.fixture = fixture;
    }

    /// <summary>All target attachments own storage that survives retirement until fixture cleanup.</summary>
    [Fact]
    public void OwnedTargetStorageIsDeletedByFixtureCleanup()
    {
        // Arrange: Create the production target set and enumerate every attached owned texture handle.
        fixture.InitializeResourceDisposal();
        using var targets = new PbrLumOnPipelineTargets();
        GpuFramebuffer[] framebuffers = [targets.DirectLightingMrt, targets.Velocity,
            targets.ProbeAnchor, targets.AtlasTrace, targets.AtlasTemporal, targets.AtlasFiltered,
            targets.IndirectHalf, targets.IndirectFull, targets.Composite];
        var textures = new List<int>();
        foreach (var framebuffer in framebuffers)
        {
            // Enumerate actual slots so MRT storage is checked alongside single-output targets.
            for (int index = 0; ; index++)
            {
                var attachment = framebuffer.GetAttachment(FramebufferSlot.Color0 + index);
                if (attachment is null) break;
                Assert.True(attachment.OwnsStorage);
                textures.Add(attachment.TextureId);
            }
        }
        Assert.Equal(17, textures.Count);

        // Act: Retire the target owner, then drain the fixture's deferred native-resource queue.
        targets.Dispose();
        Assert.All(textures, id => Assert.True(GL.IsTexture(id)));
        fixture.CleanupGpuResources();
        Assert.All(textures, id => Assert.False(GL.IsTexture(id)));
        Assert.Equal(ErrorCode.NoError, GL.GetError());

        // Draining retired targets leaves the fixture service available for a fresh allocation.
        fixture.InitializeResourceDisposal();
        using var nextTargets = new PbrLumOnPipelineTargets();
        // Assert: Cleanup deleted retired handles and left the fixture able to allocate a fresh target set.
        Assert.True(nextTargets.Composite.IsValid);
    }

    /// <summary>Drains storage even when an assertion aborts the test.</summary>
    public void Dispose()
    {
        fixture.CleanupGpuResources();
    }

    #endregion
}
