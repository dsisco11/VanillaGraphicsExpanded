using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using System;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes native buffer-texture reassociations without inferring whole-buffer policy from equal ranges.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Certifies one completed reassociation while excluding partial acquisition and publishing aliases once.</summary>
    internal static void ObserveBufferTexture(int selector, int target, int unit,
        bool named, bool explicitUnit, int buffer, int format, nint offset, nint size, bool whole)
    {
        var manager = GpuResourceManagerSystem.CurrentManager;
        if (manager?.CanObserveNativeMetadata != true && Current.resourceOperation is null || target != (int)TextureTarget.TextureBuffer) return;
        int id = selector;
        if (!named)
        {
            int selectedUnit = explicitUnit ? unchecked(unit - (int)TextureUnit.Texture0) : Current.GetActiveTextureUnit();
            if ((uint)selectedUnit >= (uint)GpuSupport.Graphics.MaxCombinedTextureImageUnits) return;
            id = Current.GetBoundTexture(TextureTarget.TextureBuffer, selectedUnit);
        }
        Current.InterruptNativeResourceOperation(GpuResourceKind.Texture, id);
        if (manager?.CanObserveNativeMetadata != true) return;
        var entry = manager.FindObservedResource(GpuResourceKind.Texture, id) as GpuResourceMetadataEntry<GpuTextureMetadata>;
        if (entry is null || entry.Metadata.Target != 0 && entry.Metadata.Target != TextureDimension.Buffer) return;
        if (InterruptNativeMetadataCompletion(entry, GpuMetadataChange.Storage)) return;
        // Invalid native formats must reach the exact native call without withdrawing known facts.
        TextureFormat requestedFormat;
        try { requestedFormat = OpenGLImageValues.Format((PixelInternalFormat)format); }
        catch (ArgumentOutOfRangeException) { return; }
        var previous = entry.IsUsable ? entry.Metadata.Buffer : null;
        entry.CompletionInterrupted = false;
        entry.OperationInProgress = true;
        entry.BeginRefresh(GpuMetadataChange.Storage);
        var requested = new GpuTextureBufferMetadata(buffer, requestedFormat, offset, size, whole);

        try
        {
            // Keep acquisition and reentry blocked while native association/range facts
            // are collected. Publication takes over the completion guard synchronously.
            if (!entry.Manager.IsRegistered(entry)
                || entry.State != GpuResourceRegistrationState.Live) return;
            if (!TryObserveBufferAssociation(entry, requested, previous, out var association, out bool renew))
                throw new InvalidOperationException("Native buffer texture association metadata is unavailable.");
            if (entry.CompletionInterrupted)
                throw new InvalidOperationException("Buffer texture observation was interrupted by a nested mutation.");
            var data = new GpuTextureMetadata(TextureDimension.Buffer, ImmutableArray<GpuTextureLevelMetadata>.Empty,
                false, 0, 0, 0, association);
            entry.OperationInProgress = false;
            entry.Manager.PublishMetadata(entry, data, GpuMetadataChange.Storage, renewTextureAssociation: renew);
        }
        finally { entry.OperationInProgress = false; }
    }
    #endregion

    #region Private
    /// <summary>Combines native association queries with range admission facts, preserving native-width command values.</summary>
    private static bool TryObserveBufferAssociation(GpuResourceMetadataEntry<GpuTextureMetadata> entry,
        GpuTextureBufferMetadata requested, GpuTextureBufferMetadata? previous, out GpuTextureBufferMetadata result, out bool renew)
    {
        result = default;
        renew = false;
        if (!ReferenceEquals(entry, entry.Manager.FindObservedResource(entry.Kind, entry.Id))) return false;
        if (!entry.Manager.CanObserveNativeMetadata) return false;
        var established = entry.Manager.FindObservedResource(GpuResourceKind.Buffer, requested.BufferId)
            as GpuResourceMetadataEntry<GpuBufferMetadata>;
        if (requested.BufferId != 0 && established?.IsUsable == true)
        {
            var initialization = new GpuBufferTextureInitialization
            {
                Buffer = unchecked((int)requested.BufferId), Format = requested.Format,
                Mode = requested.WholeBuffer ? GpuBufferTextureMode.Whole : GpuBufferTextureMode.Range
            };
            if (!requested.WholeBuffer) { initialization.Offset = requested.Offset; initialization.Size = requested.Size; }
            bool eligible = true;
            try { initialization.Validate(); }
            catch (ArgumentException) { eligible = false; }
            int knownAlignment = GpuSupport.Graphics.TextureBufferOffsetAlignment;
            eligible &= requested.WholeBuffer || requested.Offset >= 0 && requested.Size > 0
                && requested.Offset <= established.Metadata.Capacity
                && requested.Size <= established.Metadata.Capacity - requested.Offset
                && knownAlignment > 0 && requested.Offset % knownAlignment == 0;
            if (eligible)
            {
                // Existing destination/backing realization, legal format and known byte range
                // prove this allocation-free association; no descriptor verification is needed.
                result = requested.WholeBuffer ? requested with { Offset = 0, Size = 0 } : requested;
                renew = true;
                return true;
            }
            if (previous is { } retained)
            {
                // Defined rejection does not reconnect a retained old name to a replacement
                // allocation. The manager rechecks the original backing identity at publication.
                result = retained;
                return true;
            }
        }
        int buffer = ReadBufferTextureParameter((int)entry.Id, (GetTextureParameter)All.TextureBufferDataStoreBindingExt);
        int format = ReadBufferTextureParameter((int)entry.Id, GetTextureParameter.TextureInternalFormat);
        if (format <= 0) return false;
        if (buffer == 0)
        {
            // Detachment ignores the requested range. Preserve the actual queried format.
            result = new(0, (TextureFormat)format, 0, 0, requested.WholeBuffer);
            return true;
        }
        var backing = entry.Manager.AcquireResource(GpuResourceKind.Buffer, (nint)buffer, new GpuBufferMetadataReader());
        if (backing is null) return false;
        nint capacity = backing.Metadata.Capacity;
        int alignment = GpuSupport.Graphics.TextureBufferOffsetAlignment;
        bool validRange = requested.WholeBuffer || requested.Offset >= 0 && requested.Size > 0
            && requested.Offset <= capacity && requested.Size <= capacity - requested.Offset
            && alignment > 0 && requested.Offset % alignment == 0;
        bool accepted = requested.BufferId == buffer && requested.Format == (TextureFormat)format && validRange;
        bool rejected = !accepted;
        // Range getters return GLint, although commands accept pointer-sized ranges. Never
        // narrow a wide command into that result. Native buffer capacity and alignment prove
        // its admission; exactly representable fields additionally cross-check native state.
        if (accepted)
        {
            nint expectedOffset = requested.WholeBuffer ? 0 : requested.Offset;
            nint expectedSize = requested.WholeBuffer ? capacity : requested.Size;
            if (expectedOffset <= int.MaxValue)
            {
                int observed = ReadBufferTextureParameter((int)entry.Id, (GetTextureParameter)All.TextureBufferOffset);
                if (observed < 0) return false;
                accepted &= observed == expectedOffset;
            }
            if (expectedSize <= int.MaxValue)
            {
                int observed = ReadBufferTextureParameter((int)entry.Id, (GetTextureParameter)All.TextureBufferSize);
                if (observed < 0) return false;
                accepted &= observed == expectedSize;
            }
        }
        if (accepted)
        {
            result = requested.WholeBuffer ? requested with { Offset = 0, Size = 0 } : requested;
            renew = true;
            return true;
        }
        // A rejected association retains its previous range mode. The old descriptor is
        // eligible only when it was certified before this command and the native association
        // still identifies that backing and format.
        if (previous is { } old && old.BufferId == buffer && old.Format == (TextureFormat)format)
        {
            if (!rejected)
            {
                nint oldOffset = old.WholeBuffer ? 0 : old.Offset;
                nint oldSize = old.WholeBuffer ? capacity : old.Size;
                if (oldOffset > int.MaxValue || oldSize > int.MaxValue
                    || ReadBufferTextureParameter((int)entry.Id, (GetTextureParameter)All.TextureBufferOffset) != oldOffset
                    || ReadBufferTextureParameter((int)entry.Id, (GetTextureParameter)All.TextureBufferSize) != oldSize) return false;
            }
            result = old;
            return true;
        }
        return false;
    }

    /// <summary>Reads the required backing association through its native query service.</summary>
    private static int ReadBufferTextureParameter(int texture, GetTextureParameter parameter)
    {
        int value = RHI.RHICommandContext.Current.ReadExternalTextureLevelParameter(texture, 0, OpenGLResourceValues.FromNative(parameter));
        return value;
    }
    #endregion
}
