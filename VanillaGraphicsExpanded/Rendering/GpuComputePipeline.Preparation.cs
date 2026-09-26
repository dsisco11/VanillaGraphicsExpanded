using System;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Defers packaged compute loading until explicit preparation or first activation.</summary>
internal sealed partial class GpuComputePipeline
{
    private Func<GpuComputePipeline?>? prepare;
    private bool preparationFailed;
    private string preparationLog = string.Empty;

    #region Declaration and preparation
    /// <summary>Declares immutable compute inputs without reading assets or issuing GL commands.</summary>
    internal static GpuComputePipeline DeclareFromAssets(ICoreAPI api, ShaderSettings settings,
        string? debugName = null, GpuProgramLayout? layout = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        var plan = new ShaderLoadPlan(settings);
        if (plan.Stages.Count != 1 || plan.Stages[0].Stage.Kind != ShaderStageKind.Compute)
            throw new ArgumentException("A compute declaration requires one compute stage.", nameof(settings));
        var owner = new GpuComputePipeline(0, layout?.CreateCandidate() ?? new GpuProgramLayout(), message => api.Logger.Warning(message));
        owner.prepare = () =>
        {
            TryCreateFromAssets(api, settings, out var candidate, out owner.preparationLog, debugName, layout: layout);
            return candidate;
        };
        return owner;
    }

    /// <summary>Prepares a complete executable once, suppressing repeated failed dispatch attempts.</summary>
    internal bool EnsureReady()
    {
        if (IsDisposed) return false;
        if (IsValid) return true;
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
        programId = (int)candidate.Detach();
        prepare = null;
        return true;
    }

    /// <summary>Allows another attempt after the owner receives an asset reload notification.</summary>
    internal void RetryPreparation() => preparationFailed = false;

    /// <summary>Provides the failed preparation diagnostic without repeating asset reads or driver work.</summary>
    internal string PreparationLog => preparationLog;
    #endregion
}

