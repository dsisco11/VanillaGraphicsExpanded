using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Adapts previous transport settings to the shared cloud camera/material certificate.</summary>
internal sealed class CloudTemporalHistory
{
    private readonly CloudReprojectionHistory reprojection=new();
    private CloudVolumeSettings settings;
    private bool underwater;
    #region Public API
    /// <summary>Withdraws history through the owning view boundary.</summary>
    internal void Invalidate()=>reprojection.Invalidate();
    /// <summary>Adds transport identity to the shared camera and material compatibility checks.</summary>
    internal bool Capture(CloudMapInput input,VgeFrameUniformBuffer camera,CloudVolumeSettings quality,
        CloudVolumeParameters parameters,int width,int height,CloudTemporalParameters output)
    {
        return reprojection.Capture(input,camera,width,height,output,
            quality==settings&&parameters.UsesUnderwaterTransport==underwater,quality.TraceScale);
    }
    /// <summary>Commits settings and pose only after all view passes succeed.</summary>
    internal void Commit(CloudMapInput input,VgeFrameUniformBuffer camera,CloudVolumeSettings quality,CloudVolumeParameters parameters)
    {
        settings=quality;underwater=parameters.UsesUnderwaterTransport;reprojection.Commit(input,camera);
    }
    #endregion
}
