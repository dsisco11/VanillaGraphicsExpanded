using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Classifies represented vertex configuration before invalidating original certificates.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Private
    /// <summary>Establishes specified EXT initialization separately from acceptance of the configuration arguments.</summary>
    private bool PrepareVertexObservation(int array, bool named, bool ext, bool rejected)
    {
        if (ext) RealizeExtVertexArray(array);
        if (rejected) return false;
        int selected = named ? array : GetCurrentVao();
        if (selected == 0 && GpuSupport.HasCachedCapabilitiesForCurrentContext && GpuSupport.Graphics.CoreProfile) return false;
        if (named && GpuResourceManagerSystem.CurrentManager?.FindObservedResource(GpuResourceKind.VertexArray, selected)
            is GpuResourceMetadataEntry<GpuVertexArrayMetadata> { IsRealized: false, InitialDiscovery: GpuInitialDiscovery.None, PendingChange: GpuMetadataChange.None }) return false;
        return true;
    }

    /// <summary>Rejects indices only against already captured limits; unknown limits expire certificates without querying.</summary>
    private static bool RejectVertexIndex(int index, bool binding = false) => index < 0
        || GpuSupport.HasCachedCapabilitiesForCurrentContext
            && index >= (binding ? GpuSupport.Graphics.MaxVertexBindings : GpuSupport.Graphics.MaxVertexAttributes);

    /// <summary>Checks supplied scalar encoding and offsets without acquiring the native layout.</summary>
    private static bool RejectVertexFormat(int size, int type, int mode, bool normalized, int relativeOffset)
    {
        if (relativeOffset < 0 || GpuSupport.HasCachedCapabilitiesForCurrentContext
            && relativeOffset > GpuSupport.Graphics.MaxVertexRelativeOffset) return true;
        bool bgra = size == (int)All.Bgra;
        if (size is < 1 or > 4 && !(mode == 0 && bgra)) return true;
        if (mode == 2) return type != (int)All.Double;
        bool integer = type is (int)All.Byte or (int)All.UnsignedByte or (int)All.Short or (int)All.UnsignedShort
            or (int)All.Int or (int)All.UnsignedInt;
        if (mode == 1) return !integer;
        if (bgra && (!normalized || type is not ((int)All.UnsignedByte) and not ((int)All.Int2101010Rev) and not ((int)All.UnsignedInt2101010Rev))) return true;
        if (type is (int)All.Int2101010Rev or (int)All.UnsignedInt2101010Rev) return size != 4 && !bgra;
        if (type == (int)All.UnsignedInt10F11F11FRev) return size != 3;
        return !integer && type is not ((int)All.HalfFloat) and not ((int)All.Float) and not ((int)All.Double) and not ((int)All.Fixed);
    }

    /// <summary>Checks stride/offset rejection, leaving buffer-side realization to its separate specified rules.</summary>
    private static bool RejectVertexRange(nint offset, int stride) => offset < 0 || stride < 0
        || GpuSupport.HasCachedCapabilitiesForCurrentContext && stride > GpuSupport.Graphics.MaxVertexStride;

    /// <summary>Distinguishes client pointer bits from buffer offsets and classifies known core rejection without a query.</summary>
    private bool RejectVertexPointer(nint pointer, int stride, int? explicitBuffer = null)
    {
        if (RejectVertexRange(0, stride)) return true;
        int? buffer = explicitBuffer;
        if (!buffer.HasValue && bufferBindingByTarget.TryGetValue(OpenTK.Graphics.OpenGL.BufferTarget.ArrayBuffer, out var known)) buffer = known;
        if (buffer is 0 && GpuSupport.HasCachedCapabilitiesForCurrentContext)
            return GpuSupport.Graphics.CoreProfile && pointer != 0;
        return explicitBuffer is not null and not 0 && pointer < 0;
    }
    /// <summary>Preserves certificates when a multi-bind span is rejected or every individual update is rejected.</summary>
    private bool RejectVertexBatch(int first, int count, int* buffers, nint* offsets, int* strides)
    {
        if (count <= 0 || first < 0 || (long)first + count > int.MaxValue
            || GpuSupport.HasCachedCapabilitiesForCurrentContext && (long)first + count > GpuSupport.Graphics.MaxVertexBindings) return true;
        if (buffers == null) return false;
        if (offsets == null || strides == null) return false;
        for (int index = 0; index < count; index++)
            if (buffers[index] == 0 || !RejectVertexRange(offsets[index], strides[index])
                && GpuResourceManagerSystem.CurrentManager?.FindObservedResource(GpuResourceKind.Buffer, buffers[index]) is not GpuResourceMetadataEntry<GpuBufferMetadata> { IsRealized: false, PendingChange: GpuMetadataChange.None }) return false;
        return true;
    }
    #endregion
}
