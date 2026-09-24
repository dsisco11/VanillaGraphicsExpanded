using System;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.DebugView;

/// <summary>Owns a debug view's controls and requests rebuilding when their layout changes.</summary>
public interface IDebugViewPanel : IDisposable
{
    /// <summary>Raised when the dialog must recreate the panel's mode-specific controls.</summary>
    event Action? LayoutChanged;

    void Compose(GuiComposer composer, ElementBounds bounds, string keyPrefix);

    void OnOpened();

    void OnClosed();

    bool WantsGameTick { get; }

    void OnGameTick(float dt);
}

/// <summary>Provides optional lifecycle hooks and layout notifications for debug panels.</summary>
public abstract class DebugViewPanelBase : IDebugViewPanel
{
    #region Layout
    /// <inheritdoc />
    public event Action? LayoutChanged;

    /// <summary>Asks the owning dialog to rebuild controls after panel state changes.</summary>
    protected void RequestLayoutRefresh() => LayoutChanged?.Invoke();

    /// <inheritdoc />
    public abstract void Compose(GuiComposer composer, ElementBounds bounds, string keyPrefix);
    #endregion

    #region Lifetime and updates
    /// <inheritdoc />
    public virtual void OnOpened() { }

    /// <inheritdoc />
    public virtual void OnClosed() { }

    public virtual bool WantsGameTick => false;

    /// <inheritdoc />
    public virtual void OnGameTick(float dt) { }

    /// <inheritdoc />
    public virtual void Dispose() { }
    #endregion
}
