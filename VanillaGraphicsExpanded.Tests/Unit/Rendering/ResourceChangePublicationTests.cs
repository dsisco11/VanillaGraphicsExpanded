using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Verifies structural value semantics for resource metadata records without exercising manager publication.</summary>
[Collection("CPU state")] // Resource manager construction captures the process-wide lifecycle service.
public sealed class ResourceChangePublicationTests
{
    #region Public API
    /// <summary>Equivalent immutable metadata compares structurally across ordering, while default and empty remain distinct.</summary>
    [Fact]
    public void CanonicalCollectionsUseStructuralEqualityAndHashing()
    {
        // Arrange: Build the same texture levels in different orders and values with distinct defaults.
        var first = new GpuTextureLevelMetadata(0, 0, 8, 4, 1, TextureFormat.Rgba8, 0, true);
        var second = first with { Level = 1, Width = 4, Height = 2 };
        var texture = new GpuTextureMetadata(TextureDimension.Two, [first, second], true, 2, 0, 1);
        var reordered = texture with { Levels = [second, first] };

        // Act / Assert: Record equality and hashing ignore allocation order but preserve explicit distinctions.
        Assert.Equal(texture, reordered);
        Assert.Equal(texture.GetHashCode(), reordered.GetHashCode());
        Assert.NotEqual(texture with { Levels = default }, texture with { Levels = [] });
        Assert.NotEqual(texture, texture with { BaseLevel = 1 });

        // Arrange: Build equivalent framebuffer attachments in reverse insertion order.
        var selection = new GpuFramebufferImageMetadata(GpuResourceKind.Texture, 7, 0, 0, 0, false);
        var attachments = ImmutableDictionary<FramebufferSlot, GpuFramebufferImageMetadata>.Empty
            .Add(FramebufferSlot.Color0, selection).Add(FramebufferSlot.Color1, selection with { Id = 8 });
        var framebuffer = new GpuFramebufferMetadata(attachments, 0, 0, 0, 0, true);
        var rebuilt = framebuffer with { Attachments = attachments.Reverse().ToImmutableDictionary() };

        // Act / Assert: Structural equality is independent of immutable dictionary insertion order.
        Assert.Equal(framebuffer, rebuilt);
        Assert.Equal(framebuffer.GetHashCode(), rebuilt.GetHashCode());
        Assert.NotEqual(framebuffer, framebuffer with { DefaultWidth = 1 });

        // Arrange: Give identical numeric names different canonical backing registrations.
        using var manager = Manager();
        var original = manager.RegisterCreatedResource<GpuTextureMetadata>(GpuResourceKind.Texture, 7);
        var sameName = manager.RegisterCreatedResource<GpuTextureMetadata>(GpuResourceKind.Texture, 8);

        // Act / Assert: Backing identity participates in attachment value equality.
        Assert.NotEqual(selection with { OriginalBacking = original }, selection with { OriginalBacking = sameName });

        // Arrange: Reorder program resources and texture-unit pairs without changing their values.
        var resource = new GpuProgramResourceMetadata(ProgramResourceKind.Uniform, 0, "source");
        var program = new GpuProgramMetadata(true, [resource, resource with { Index = 1 }], [new(2, 3), new(4, 5)]);
        var equivalent = program with { Resources = [resource with { Index = 1 }, resource], TextureUnits = [new(4, 5), new(2, 3)] };

        // Act / Assert: Program records use structural collection equality and distinguish default from empty.
        Assert.Equal(program, equivalent);
        Assert.Equal(program.GetHashCode(), equivalent.GetHashCode());
        Assert.NotEqual(program with { Resources = default }, program with { Resources = [] });
    }
    #endregion

    #region Private
    /// <summary>Creates a deterministic manager context without loading a graphics driver.</summary>
    private static RHIResourceManager Manager() => new(static () => (1, 1), static _ => true);
    #endregion
}
