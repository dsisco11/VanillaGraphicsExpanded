using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Shares one structural profile selector between generation, transport and scheduling.</summary>
internal static partial class CloudVolumeQualityOptions
{
    #region Public API
    /// <summary>Compiles only the five declared workload combinations.</summary>
    [ShaderOption("VGE_VOLUME_QUALITY",2,Domain=new object[]{0,1,2,3,4})]
    internal static partial ShaderOption<int> Level {get;}
    #endregion
}
