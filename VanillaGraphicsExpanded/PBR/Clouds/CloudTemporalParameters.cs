using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Packs cloud material motion and sampling state; camera transforms belong to the shared frame.</summary>
internal sealed class CloudTemporalParameters : CpuUniformBuffer
{
    #region Public API
    #region Layout
    internal const int MaterialTranslationOffset = 0;
    internal const int SamplingOffset = 16;
    internal const int HistoryOffset = 32;
    internal const int TargetOffset = 48;
    #endregion

    /// <summary>Byte extent of this concrete std140 layout.</summary>
    internal const int BlockSize = 64;
    /// <summary>Declares the immutable byte extent owned by this layout.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Matches four std140 vectors without duplicating shared camera matrices.</summary>
    internal CloudTemporalParameters() : base() { }
    /// <summary>Uses cloud material motion independently of tile-grid recentering and never encodes depth or coverage.</summary>
    internal void Capture(Vector3 translation,int width,int height,uint frame,bool valid,int divisor=2,
        int debugMode=0,bool stationary=false)
    {
        WriteVector4(MaterialTranslationOffset,new(translation,0));
        WriteVector4(SamplingOffset,new(width,height,frame&1,(frame>>1)&1));
        WriteVector4(HistoryOffset,new(valid?1:0,debugMode,valid&&stationary?1:0,0));
        WriteVector4(TargetOffset,new((width+1)/2,(height+1)/2,2,divisor));
    }
    #endregion
}
