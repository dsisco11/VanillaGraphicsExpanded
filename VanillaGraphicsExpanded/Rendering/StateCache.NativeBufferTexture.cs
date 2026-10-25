using System;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Publishes native buffer-texture reassociations without inferring whole-buffer policy from equal ranges.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Captures an existing destination and its last certified descriptor before native association changes.</summary>
    internal static NativeBufferTexture BeginNativeBufferTexture(int selector, int target, int unit,
        bool named, bool explicitUnit, int buffer, int format, nint offset, nint size, bool whole)
    {
        var manager = GpuResourceManagerSystem.CurrentManager;
        if (manager?.CanObserveNativeMetadata != true && Current.resourceOperation is null || target != (int)TextureTarget.TextureBuffer) return default;
        int id = selector;
        if (!named)
        {
            int selectedUnit = explicitUnit ? unchecked(unit - (int)TextureUnit.Texture0) : Current.GetActiveTextureUnit();
            if ((uint)selectedUnit >= (uint)GpuSupport.Graphics.MaxCombinedTextureImageUnits) return default;
            id = Current.GetBoundTexture(TextureTarget.TextureBuffer, selectedUnit);
        }
        Current.RequireResourceOperationAvailable(GpuResourceKind.Texture, id);
        if (manager?.CanObserveNativeMetadata != true) return default;
        var entry = manager.FindObservedResource(GpuResourceKind.Texture, id) as GpuResourceMetadataEntry<GpuTextureMetadata>;
        if (entry is null || entry.Metadata.Target != 0 && entry.Metadata.Target != TextureTarget.TextureBuffer) return default;
        var previous = entry.IsUsable ? entry.Metadata.Buffer : null;
        entry.BeginRefresh(GpuMetadataChange.Storage);
        return new(entry, new(buffer, (SizedInternalFormat)format, offset, size, whole), previous);
    }
    #endregion

    /// <summary>Retains original destination identity across native dispatch and command-result certification.</summary>
    internal readonly struct NativeBufferTexture(GpuResourceMetadataEntry<GpuTextureMetadata>? entry,
        GpuTextureBufferMetadata requested, GpuTextureBufferMetadata? previous)
    {
        #region Internal API
        /// <summary>Publishes accepted facts or retains a proven rejected-command result without consuming native errors.</summary>
        internal void Complete(bool nativeReturned)
        {
            // A throwing dispatch provides no command-admission proof. Keep availability
            // withdrawn and let the original exception propagate without further queries.
            if (!nativeReturned || entry is null || !entry.Manager.IsRegistered(entry)
                || entry.State != GpuResourceRegistrationState.Live) return;
            if (!TryObserveBufferAssociation(entry, requested, previous, out var association))
                throw new InvalidOperationException("Native buffer texture association metadata is unavailable.");
            var data = new GpuTextureMetadata(TextureTarget.TextureBuffer, ImmutableArray<GpuTextureLevelMetadata>.Empty,
                false, 0, 0, 0, association);
            entry.Manager.PublishMetadata(entry, data, GpuMetadataChange.Storage, renewTextureAssociation: true);
        }
        #endregion
    }

    #region Private
    /// <summary>Combines native association queries with range admission facts, preserving native-width command values.</summary>
    private static bool TryObserveBufferAssociation(GpuResourceMetadataEntry<GpuTextureMetadata> entry,
        GpuTextureBufferMetadata requested, GpuTextureBufferMetadata? previous, out GpuTextureBufferMetadata result)
    {
        result = default;
        if (!ReferenceEquals(entry, entry.Manager.FindObservedResource(entry.Kind, entry.Id))) return false;
        bool named = GpuSupport.Graphics.SupportsArbDirectStateAccess;
        using var binding = !named ? Current.BindTextureScope(TextureTarget.TextureBuffer, 0, checked((int)entry.Id)) : (TextureScope?)null;
        int buffer = ReadBufferTextureParameter((int)entry.Id, (GetTextureParameter)All.TextureBufferDataStoreBindingExt);
        int format = ReadBufferTextureParameter((int)entry.Id, GetTextureParameter.TextureInternalFormat);
        if (format <= 0) return false;
        if (buffer == 0)
        {
            // Detachment ignores the requested range. Preserve the actual queried format.
            result = new(0, (SizedInternalFormat)format, 0, 0, requested.WholeBuffer);
            return true;
        }
        var backing = entry.Manager.AcquireResource(GpuResourceKind.Buffer, (nint)buffer, new GpuBufferMetadataReader());
        if (backing is null) return false;
        nint capacity = backing.Metadata.Capacity;
        int alignment = GpuSupport.Graphics.TextureBufferOffsetAlignment;
        bool validRange = requested.WholeBuffer || requested.Offset >= 0 && requested.Size > 0
            && requested.Offset <= capacity && requested.Size <= capacity - requested.Offset
            && alignment > 0 && requested.Offset % alignment == 0;
        bool accepted = requested.BufferId == buffer && requested.Format == (SizedInternalFormat)format && validRange;
        bool rejected = !accepted;
        // Range getters return GLint, although commands accept pointer-sized ranges. Never
        // narrow a wide command into that result. Native buffer capacity and alignment prove
        // its admission; exactly representable fields additionally cross-check native state.
        if (accepted && GpuSupport.Graphics.SupportsTextureBufferRange)
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
            return true;
        }
        // A rejected association retains its previous range mode. The old descriptor is
        // eligible only when it was certified before this command and the native association
        // still identifies that backing and format.
        if (previous is { } old && old.BufferId == buffer && old.Format == (SizedInternalFormat)format)
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

    /// <summary>Reads a single native association fact through DSA or the binding-preserving fallback.</summary>
    private static int ReadBufferTextureParameter(int texture, GetTextureParameter parameter)
    {
        int value = -1;
        if (GpuSupport.Graphics.SupportsArbDirectStateAccess) GL.GetTextureLevelParameter(texture, 0, parameter, out value);
        else GL.GetTexLevelParameter(TextureTarget.TextureBuffer, 0, parameter, out value);
        return value;
    }
    #endregion
}
