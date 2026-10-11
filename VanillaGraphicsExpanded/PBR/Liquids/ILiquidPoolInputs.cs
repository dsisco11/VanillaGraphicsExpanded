using System.Numerics;
namespace VanillaGraphicsExpanded.PBR.Liquids;
/// <summary>Receives the logical draw inputs staged by engine pool traversal.</summary>
internal interface ILiquidPoolInputs
{
    /// <summary>Stages the camera-relative pool origin.</summary>
    Vector3 Origin { set; }
    /// <summary>Stages a combined model-view transform, including restoration writes.</summary>
    float[] ModelViewMatrix { set; }
    /// <summary>Stages preview transparency, including restoration to zero.</summary>
    float ForcedTransparency { set; }
}
