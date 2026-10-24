using System;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Defers packaged compute loading until explicit preparation or first activation.</summary>
internal sealed partial class GpuComputePipeline
{
    private Func<GpuComputePipeline?>? prepare;
    internal Func<GpuComputePipeline?>? Rebuild { get; set; }
    internal Func<string>? RebuildDiagnostic { get; set; }
    internal Integration.ClientRenderingLifetime? RenderingLifetime { get; set; }
    private bool preparationFailed;
    private string preparationLog = string.Empty;

    #region Declaration and preparation
    /// <summary>Prepares a complete executable once, suppressing repeated failed dispatch attempts.</summary>
    internal bool EnsureReady()
    {
        if (IsDisposed) return false;
        if (IsValid) return !NeedsNativePreparation;
        if (preparationFailed || prepare == null) return false;
        using var candidate = prepare();
        if (candidate == null)
        {
            preparationFailed = true;
            return false;
        }
        // Transfer the linked object and its interface together; the temporary owner no longer owns GL state.
        programLayout = candidate.programLayout;
        InstalledSettings = candidate.InstalledSettings;
        ExecutableContext = candidate.ExecutableContext;
        ExecutableRevision++;
        ResourceManager?.ReplaceWrapperResource(this);
        programId = (int)candidate.Detach();
        if (programId != 0) GpuResourceManager.AcquireWrapperMetadata(this, GpuProgramMetadataReader.Instance, refreshExisting: false);
        if (ResourceManager?.GetResourceRegistration(this) is { MetadataAvailable: true } entry)
            programLayout.UseNativeMetadata(programId, metadata, entry.MetadataRevision);
        prepare = null;
        return true;
    }

    /// <summary>Allows another attempt after the owner receives an asset reload notification.</summary>
    internal void RetryPreparation() => preparationFailed = false;

    /// <summary>Provides the failed preparation diagnostic without repeating asset reads or driver work.</summary>
    internal string PreparationLog => preparationLog;
    #endregion
}

