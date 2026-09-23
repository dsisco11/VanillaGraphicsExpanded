using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>
/// Owns reusable production resources for one explicitly identified component scene/history branch.
/// Equal dimensions never imply shared identity. Borrowed attachments remain valid until their owner is resized,
/// recreated or disposed; callers must not dispose them. Unrequested resource families are not allocated.
/// </summary>
internal sealed class ShaderLightingResources : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly VgeConfig config = new();
    private LumOnBufferManager? screen;
    private LumOnWorldProbeClipmapGpuResources? world;
    private bool disposed;
    public ShaderSceneInputs Scene { get; } = new();

    #region Resource ownership
    /// <summary>Retains a borrowed engine boundary; this branch owns only its GPU resources.</summary>
    public ShaderLightingResources(ICoreClientAPI api) => this.api = api;

    /// <summary>Retains the screen/history owner and delegates allocation, resize and topology recreation to production.</summary>
    public LumOnBufferManager EnsureScreen(int width, int height, int probeSpacing)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(probeSpacing);
        // The production manager watches screen size, so explicitly invalidate when only spacing changes.
        if (screen != null && config.LumOn.ProbeSpacingPx != probeSpacing)
            screen.RequestRecreateBuffers("Component scene probe spacing changed");
        config.LumOn.ProbeSpacingPx = probeSpacing;
        screen ??= new(api, config);
        screen.EnsureBuffers(width, height);
        return screen;
    }

    /// <summary>Reuses world atlases until their topology changes, preparing a replacement before retiring old borrows.</summary>
    public LumOnWorldProbeClipmapGpuResources EnsureWorldProbes(int resolution, int levels, int tileSize)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (world?.Resolution == resolution && world.Levels == levels && world.WorldProbeTileSize == tileSize)
            return world;
        var replacement = new LumOnWorldProbeClipmapGpuResources(resolution, levels, tileSize);
        world?.Dispose();
        world = replacement;
        return world;
    }
    #endregion

    #region Lifetime
    /// <summary>Releases this branch only; independent branches and borrowed engine services remain untouched.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        world?.Dispose(); screen?.Dispose(); Scene.Dispose();
        world = null; screen = null;
    }
    #endregion
}
