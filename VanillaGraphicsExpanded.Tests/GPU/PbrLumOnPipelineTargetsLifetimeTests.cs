using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies pipeline target ownership and context-bound deferred cleanup.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
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
                var attachment = framebuffer.GetAttachment(FramebufferAttachment.ColorAttachment0 + index);
                if (attachment is null) break;
                Assert.True(attachment.OwnsStorage);
                textures.Add(attachment.TextureId);
            }
        }
        Assert.Equal(15, textures.Count);

        targets.Dispose();
        Assert.All(textures, id => Assert.True(GL.IsTexture(id)));
        fixture.CleanupGpuResources();
        Assert.All(textures, id => Assert.False(GL.IsTexture(id)));
        Assert.Equal(ErrorCode.NoError, GL.GetError());

        // Cleanup closes the previous lifetime; the next test must be able to allocate again.
        fixture.InitializeResourceDisposal();
        using var nextTargets = new PbrLumOnPipelineTargets();
        Assert.True(nextTargets.Composite.IsValid);
    }

    /// <summary>Drains storage even when an assertion aborts the test.</summary>
    public void Dispose()
    {
        fixture.CleanupGpuResources();
    }

    #endregion
}
