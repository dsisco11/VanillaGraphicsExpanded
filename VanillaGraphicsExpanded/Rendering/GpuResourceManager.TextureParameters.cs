using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Recovers an externally changed texture parameter without rediscovering its storage.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Reads only the represented field and retains the original registration's other known facts.</summary>
    internal bool RefreshTextureParameter(GpuResourceMetadataEntry<GpuTextureMetadata> entry, TextureParameterName parameter)
    {
        RequireMetadataContext();
        RequireLiveEntry(entry);
        if (entry.OperationInProgress) throw new System.InvalidOperationException("An operation owns this resource result.");
        BeginMetadataCompletion(entry, GpuMetadataChange.Metadata);
        entry.BeginRefresh(GpuMetadataChange.Metadata);
        bool finished = false;
        try
        {
            int value = RHI.RHICommandContext.Current.ReadExternalTextureParameter((int)entry.Id, (GetTextureParameter)parameter);
            if (value < 0) return false;
            RequireMetadataContext();
            RequireLiveEntry(entry);
            RequireUninterruptedCompletion(entry);
            // An external command may be rejected. The targeted result preserves that
            // native outcome without rescanning any images or consuming the error queue.
            var result = parameter == TextureParameterName.TextureBaseLevel
                ? entry.Metadata with { BaseLevel = value } : entry.Metadata with { MaxLevel = value };
            entry.Publish(result, GpuMetadataChange.Metadata);
            finished = true;
            return true;
        }
        finally
        {
            entry.CompletionInProgress = false;
            entry.NotifyImageDependents(suppressErrors: !finished);
        }
    }
    #endregion
}
