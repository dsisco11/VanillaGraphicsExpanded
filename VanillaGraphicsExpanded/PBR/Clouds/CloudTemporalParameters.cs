using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Packs cloud material motion and sampling state; camera transforms belong to the shared frame.</summary>
internal sealed class CloudTemporalParameters : CpuUniformBuffer
{
    #region Public API
    /// <summary>Matches four std140 vectors without duplicating shared camera matrices.</summary>
    internal CloudTemporalParameters() : base(64) { }
    /// <summary>Uses cloud material motion independently of tile-grid recentering and never encodes depth or coverage.</summary>
    internal void Capture(Vector3 translation,int width,int height,uint frame,bool valid,int divisor=2,
        int debugMode=0,bool stationary=false)
    {
        WriteVector4(0,new(translation,0));
        WriteVector4(16,new(width,height,frame&1,(frame>>1)&1));
        WriteVector4(32,new(valid?1:0,debugMode,valid&&stationary?1:0,0));
        WriteVector4(48,new((width+1)/2,(height+1)/2,2,divisor));
    }
    #endregion
}
