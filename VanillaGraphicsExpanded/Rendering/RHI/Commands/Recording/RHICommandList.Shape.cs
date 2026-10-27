using System;
namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Checks supplied shape without reading resource validity, capabilities, metadata or native context.</summary>
public sealed partial class RHICommandList
{
    #region Private
    /// <summary>Checks a supplied interval, leaving current capacity and usability to execution.</summary>
    private static void ShapeBuffer(GpuBufferRef buffer, nint offset, nint length)
    {
        if (buffer.IsEmpty || offset < 0 || length < 0) throw new ArgumentException("Supply a resource and nonnegative byte interval.");
        _ = checked(offset + length);
    }
    /// <summary>Checks coordinates and selections without resolving view storage.</summary>
    private static void ShapeImage(GpuTextureRef texture, RHITextureRegion region)
    {
        if (texture.IsEmpty || region.Subresource.MipLevel < 0 || region.Subresource.Layer < 0
            || region.X < 0 || region.Y < 0 || region.Z < 0 || region.Width <= 0 || region.Height <= 0 || region.Depth <= 0)
            throw new ArgumentException("Supply a texture and positive region.");
        _ = checked(region.X + region.Width); _ = checked(region.Y + region.Height); _ = checked(region.Z + region.Depth);
    }
    /// <summary>Checks copied client representation without discovering texture facts.</summary>
    private static void ShapePixels(RHITextureRegion region, RHIPixelEncoding encoding, int length, int rowPitch, int slicePitch)
    {
        int row = checked(region.Width * encoding.BytesPerPixel);
        int stride = rowPitch == 0 ? row : rowPitch;
        int slice = slicePitch == 0 ? checked(stride * region.Height) : slicePitch;
        if (stride < row || slice < checked(stride * region.Height)
            || length < checked((region.Depth - 1) * slice + (region.Height - 1) * stride + row))
            throw new ArgumentException("Client bytes or pitches do not cover the supplied region.");
    }
    /// <summary>Requires a complete local graphics interval without inspecting geometry borrowing state.</summary>
    private void ShapeDraw(RHIGeometryInputs geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (!inPass || pipelineKind != 1) throw new InvalidOperationException("Record a pass and graphics pipeline before drawing.");
    }
    /// <summary>Checks explicit index/instance shape independently of buffer contents.</summary>
    private static void ShapeIndexed(RHIDrawIndexed draw)
    {
        if (draw.FirstIndex < 0 || draw.IndexCount < 0 || draw.InstanceCount < 0) throw new ArgumentOutOfRangeException(nameof(draw));
    }
    /// <summary>Checks authored indirect intervals, without reading producer-owned arguments.</summary>
    private static void ShapeIndirect(RHIIndirectDraw draw)
    {
        ShapeBuffer(draw.Arguments.Buffer, draw.Arguments.Offset, draw.Arguments.Length);
        if (draw.Count < 0 || draw.Stride < 0) throw new ArgumentOutOfRangeException(nameof(draw));
        if (draw.CountBuffer is { } count) ShapeBuffer(count.Buffer, count.Offset, count.Length);
    }
    /// <summary>Requires an explicitly recorded compute selection outside a graphics pass.</summary>
    private void ShapeCompute()
    { if (inPass || pipelineKind != 2) throw new InvalidOperationException("Record a compute pipeline outside a pass before dispatch."); }
    #endregion
}
