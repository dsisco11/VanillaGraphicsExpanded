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
    // Reserved1 == 1: OriginWS.xyz is chunk-local and the three W lanes contain integer
    // chunk XYZ bits. Mesh captures (Reserved1 == 0) retain their world-space origin.
    public Vector4 OriginWS;
    public Vector4 AxisUWS;   // xyz: U basis
    public Vector4 AxisVWS;   // xyz: V basis
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
