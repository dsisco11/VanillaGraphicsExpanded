using System.Numerics;
using System.Runtime.InteropServices;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// GPU patch/page metadata (std430-friendly).
/// v1 indexes this by <c>physicalPageId</c> so shaders can resolve metadata after page-table translation.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct LumonScenePatchMetadataGpu
{
    public Vector4 OriginWS;  // xyz: world origin, w: unused
    public Vector4 AxisUWS;   // xyz: U basis, w: unused
    public Vector4 AxisVWS;   // xyz: V basis, w: unused
    public Vector4 NormalWS;  // xyz: normal, w: flags (optional)

    // Virtual-space placement for this patch/page.
    public uint VirtualBasePageX;
    public uint VirtualBasePageY;
    public uint VirtualSizePagesX;
    public uint VirtualSizePagesY;

    // Identity/debug / reverse mapping.
    public uint ChunkSlot;
    public uint PatchId;

    // Reserved/padding for 16-byte alignment.
    public uint Reserved0;
    public uint Reserved1;
}

