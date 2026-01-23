using System;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.DebugView;

public interface IDebugViewPanel : IDisposable
{
    void Compose(GuiComposer composer, ElementBounds bounds, string keyPrefix);

    void OnOpened();

    void OnClosed();

    bool WantsGameTick { get; }

    void OnGameTick(float dt);
}

public abstract class DebugViewPanelBase : IDebugViewPanel
{
    public abstract void Compose(GuiComposer composer, ElementBounds bounds, string keyPrefix);

    public virtual void OnOpened() { }

    public virtual void OnClosed() { }

    public virtual bool WantsGameTick => false;

    public virtual void OnGameTick(float dt) { }

    public virtual void Dispose() { }
}

