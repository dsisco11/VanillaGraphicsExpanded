using System.Collections.Generic;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Combines native attachment facts with proven command semantics where GL has no mode query.</summary>
internal sealed partial class GpuResourceManager
{
    // These are pending observation arguments, consumed by the next canonical read.
    // Original entry keys prevent queued observations from attaching to a reused name.
    private readonly ConditionalWeakTable<GpuResourceRegistration, Dictionary<FramebufferAttachment, FramebufferAttachmentCommand>> framebufferCommands = new();

    #region Internal API
    /// <summary>Retains a completed command for the next canonical read, including deferred attachment groups.</summary>
    internal void RecordFramebufferAttachmentCommand(GpuResourceMetadataEntry<GpuFramebufferMetadata> entry,
        FramebufferAttachmentCommand command)
    {
        RequireMetadataContext();
        RequireLiveEntry(entry);
        var commands = framebufferCommands.GetValue(entry, static _ => new());
        if (command.Slot == FramebufferAttachment.DepthStencilAttachment)
        {
            commands[FramebufferAttachment.DepthAttachment] = command;
            commands[FramebufferAttachment.StencilAttachment] = command;
        }
        else commands[command.Slot] = command;
    }
    #endregion

    #region Private
    /// <summary>Preserves compatible prior evidence and certifies only matching, admitted attachment commands.</summary>
    private T ReconcileFramebufferAttachmentModes<T>(GpuResourceMetadataEntry<T> entry, T candidate) where T : struct
    {
        if (candidate is not GpuFramebufferMetadata actual || actual.Attachments is null) return candidate;
        var previous = entry.Metadata is GpuFramebufferMetadata old ? old.Attachments : null;
        framebufferCommands.TryGetValue(entry, out var commands);
        var attachments = actual.Attachments.ToBuilder();
        foreach (var pair in actual.Attachments)
        {
            var image = pair.Value;
            var mode = image.Mode != GpuFramebufferAttachmentMode.Unknown ? image.Mode : RHI.RHICommandContext.Current.Capabilities.Multiview
                ? GpuFramebufferAttachmentMode.Unknown : GpuFramebufferAttachmentMode.Ordinary;
            if (image.Kind != GpuResourceKind.Texture || FindResource(image.Kind, image.Id)
                is GpuResourceMetadataEntry<GpuTextureMetadata> backing
                    && backing.Metadata.Target != 0 && backing.Metadata.Target != TextureTarget.Texture2DArray)
                mode = GpuFramebufferAttachmentMode.Ordinary;
            bool unchanged = previous is not null && previous.TryGetValue(pair.Key, out var prior)
                && (prior with { Mode = GpuFramebufferAttachmentMode.Unknown }) == (image with { Mode = GpuFramebufferAttachmentMode.Unknown });
            if (unchanged && image.Mode == GpuFramebufferAttachmentMode.Unknown) mode = previous![pair.Key].Mode;
            if (commands is not null && commands.TryGetValue(pair.Key, out var command))
            {
                if (command.Eligible && command.Matches(image)) mode = command.Mode;
                else if (!command.Rejected || !unchanged) mode = GpuFramebufferAttachmentMode.Unknown;
            }
            attachments[pair.Key] = image with { Mode = mode };
        }
        framebufferCommands.Remove(entry);
        return (T)(object)(actual with { Attachments = attachments.ToImmutable() });
    }
    #endregion
}
