using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Classifies image definitions against maintained pixel-unpack buffer facts.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Private
    /// <summary>Returns rejection, acceptance, or uncertainty without reading image descriptors.</summary>
    private bool? ClassifyImageUnpack(nint pointer, int width, int height, int depth, int axes,
        int format, int type, int? compressedSize)
    {
        if (!TryGetCachedBoundBuffer(BufferTarget.PixelUnpackBuffer, out int buffer))
            buffer = GetBoundBuffer(BufferTarget.PixelUnpackBuffer);
        if (buffer == 0) return true;
        if (pointer < 0) return false;
        var entry = RHIResourceManagerSystem.CurrentManager?.FindObservedResource(GpuResourceKind.Buffer, buffer)
            as GpuResourceMetadataEntry<GpuBufferMetadata>;
        if (entry?.IsUsable != true || !TryGetBufferMapping(buffer, out bool mapped, out bool persistent)) return null;
        if (mapped && !persistent) return false;
        long capacity = (long)entry.Metadata.Capacity;
        long offset = (long)pointer;
        if (offset > capacity) return false;
        if (compressedSize.HasValue)
        {
            if (compressedSize.Value > capacity - offset) return false;
            // Compressed block pixel-store settings are not represented by this cache.
            return null;
        }
        if (!TryGetPixelUnpackField(PixelStoreParameter.UnpackAlignment, out int alignment)
            || !TryGetPixelUnpackField(PixelStoreParameter.UnpackRowLength, out int rowLength)
            || !TryGetPixelUnpackField(PixelStoreParameter.UnpackSkipPixels, out int skipPixels)
            || axes > 1 && !TryGetPixelUnpackField(PixelStoreParameter.UnpackSkipRows, out _)
            || axes > 2 && (!TryGetPixelUnpackField(PixelStoreParameter.UnpackImageHeight, out _)
                || !TryGetPixelUnpackField(PixelStoreParameter.UnpackSkipImages, out _))) return null;
        int componentAlignment = (PixelType)type switch
        {
            PixelType.Byte or PixelType.UnsignedByte => 1,
            PixelType.Short or PixelType.UnsignedShort or PixelType.HalfFloat => 2,
            PixelType.Int or PixelType.UnsignedInt or PixelType.Float or PixelType.UnsignedInt248 => 4,
            PixelType.Float32UnsignedInt248Rev => 8,
            _ => 0
        };
        if (componentAlignment == 0) return null;
        if (offset % componentAlignment != 0) return false;
        if (width == 0 || height == 0 || depth == 0) return true;
        try
        {
            int bytes = OpenGLPixelValues.Encoding((PixelFormat)format, (PixelType)type).BytesPerPixel;
            TryGetPixelUnpackField(PixelStoreParameter.UnpackSkipRows, out int skipRows);
            TryGetPixelUnpackField(PixelStoreParameter.UnpackImageHeight, out int imageHeight);
            TryGetPixelUnpackField(PixelStoreParameter.UnpackSkipImages, out int skipImages);
            checked
            {
                long rowBytes = (long)(rowLength == 0 ? width : rowLength) * bytes;
                long rowStride = (rowBytes + alignment - 1) / alignment * alignment;
                long imageStride = rowStride * (imageHeight == 0 ? height : imageHeight);
                long start = (long)skipPixels * bytes + (axes > 1 ? (long)skipRows * rowStride : 0)
                    + (axes > 2 ? (long)skipImages * imageStride : 0);
                long extent = start + (axes > 2 ? (long)(depth - 1) * imageStride : 0)
                    + (axes > 1 ? (long)(height - 1) * rowStride : 0) + (long)width * bytes;
                return extent <= capacity - offset;
            }
        }
        catch (OverflowException) { return false; }
        catch (ArgumentException) { return null; }
    }
    #endregion
}
