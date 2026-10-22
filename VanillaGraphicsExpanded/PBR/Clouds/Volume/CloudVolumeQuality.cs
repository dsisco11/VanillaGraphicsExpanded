using System;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Resolves the five coherent cloud workloads without changing grid allocation or view resolution.</summary>
internal readonly record struct CloudVolumeQuality(int Level,int ShapeAxis,int Directions,int GatherDistance,float ViewStop,float TransportStop,int LightingRays)
{
    #region Public API
    /// <summary>Returns an immutable profile; Medium preserves the original numerical workload.</summary>
    internal static CloudVolumeQuality Resolve(int level)=>level switch
    {
        0=>new(0,1,2,100,.02f,.02f,20480),
        1=>new(1,1,4,200,.01f,.005f,40960),
        2=>new(2,2,8,400,.005f,0,81920),
        3=>new(3,3,16,600,.0025f,0,163840),
        4=>new(4,4,32,800,.00125f,0,327680),
        _=>throw new ArgumentOutOfRangeException(nameof(level))
    };
    #endregion
}
