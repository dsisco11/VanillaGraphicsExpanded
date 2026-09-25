using System.Runtime.InteropServices;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Sixteen-byte directional completion metadata; ready radiance stays in the resident answer buffer.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WorldProbeCompletionGpu
{
    public int Outcome, Reason;
    public float Distance;
    public int Descriptor;
}
