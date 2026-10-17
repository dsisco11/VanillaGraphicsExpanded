using System;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Preserves the native preference values at the VGE ownership boundary.</summary>
internal enum CloudMode { Off = 0, Volumetric = 1, Simple = 2 }

/// <summary>Transfers visible ownership only with a functioning selected draw consumer; owns no borrowed native resource.</summary>
internal sealed class CloudModeOwnership : IDisposable
{
    private IDisposable? consumer;
    private CloudMode selected, owned;
    private bool enabled;

    #region Public API
    /// <summary>Signals actual volumetric consumer activation or release, independently of mode polling.</summary>
    internal event Action? VolumetricChanged;
    /// <summary>Reports a committed draw lifetime, independently of temporary per-frame input availability.</summary>
    internal bool Owns(CloudMode mode) => consumer is not null && owned == mode;

    /// <summary>Withdraws incompatible VGE resources when demand changes; preferences themselves are never modified.</summary>
    internal void Update(bool isEnabled, int nativeMode)
    {
        var next = Enum.IsDefined(typeof(CloudMode), nativeMode) ? (CloudMode)nativeMode : CloudMode.Off;
        if (consumer is not null && (!isEnabled || next != owned)) Release();
        enabled = isEnabled; selected = next;
    }

    /// <summary>Accepts ownership only after the future draw has prepared its dedicated shaders and resources. Failed admission retains caller ownership.</summary>
    internal bool TryActivate(CloudMode mode, IDisposable drawResources, bool functioningDraw)
    {
        ArgumentNullException.ThrowIfNull(drawResources);
        if (!enabled || !functioningDraw || selected != mode || mode == CloudMode.Off || consumer is not null) return false;
        consumer = drawResources; owned = mode;
        if(mode==CloudMode.Volumetric) VolumetricChanged?.Invoke();
        return true;
    }

    /// <summary>Releases VGE resources; native callbacks resume through their ownership gates without registration changes.</summary>
    public void Dispose()
    {
        try { Release(); }
        finally { enabled = false; selected = CloudMode.Off; }
    }
    #endregion

    #region Private
    /// <summary>Clears admission before resource cleanup to prevent callbacks from observing a retiring owner.</summary>
    private void Release()
    {
        var retiring = consumer;
        bool wasVolumetric=retiring is not null && owned==CloudMode.Volumetric;
        consumer = null; owned = CloudMode.Off;
        // Clear admission before notifying consumers; cleanup still runs if an observer fails.
        try { if(wasVolumetric) VolumetricChanged?.Invoke(); }
        finally { retiring?.Dispose(); }
    }
    #endregion
}
