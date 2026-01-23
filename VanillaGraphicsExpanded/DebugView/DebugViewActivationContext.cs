using System;

using VanillaGraphicsExpanded.LumOn;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.DebugView;

public sealed class DebugViewActivationContext
{
    public ICoreClientAPI Capi { get; }

    public VgeConfig Config { get; }

    public IServiceProvider? Services { get; }

    public DebugViewActivationContext(
        ICoreClientAPI capi,
        VgeConfig config,
        IServiceProvider? services = null)
    {
        Capi = capi ?? throw new ArgumentNullException(nameof(capi));
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Services = services;
    }
}

