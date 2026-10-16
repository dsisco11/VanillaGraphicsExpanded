using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Packs the previous cloud-relative camera transform and exact rotating trace phases.</summary>
internal sealed class CloudTemporalParameters : CpuUniformBuffer
{
    #region Public API
    /// <summary>Matches one column-major matrix and three std140 vectors.</summary>
    internal CloudTemporalParameters() : base(112) { }
    /// <summary>Uses cloud material motion independently of tile-grid recentering and never encodes depth or coverage.</summary>
    internal void Capture(System.ReadOnlySpan<float> previousProjection,Vector3 translation,int width,int height,uint frame,uint previousFrame,bool valid)
    {
        WriteMatrix4(0,previousProjection); WriteVector4(64,new(translation,0));
        WriteVector4(80,new(width,height,frame&1,(frame>>1)&1));
        WriteVector4(96,new(valid?1:0,previousFrame&1,(previousFrame>>1)&1,.75f));
    }
    #endregion
}
