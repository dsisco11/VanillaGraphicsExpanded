using System;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>
/// Owns the GPU textures and upload plumbing for the world-probe clipmap.
/// Phase 18.8 responsibility: correct allocation/recreation and leak-free disposal.
/// </summary>
internal sealed class LumOnWorldProbeClipmapBufferManager : IDisposable
{
    private const int LayoutVersion = 1;

    private readonly ICoreClientAPI capi;
    private readonly LumOnConfig config;

    private LumOnWorldProbeClipmapGpuResources? resources;
    private LumOnWorldProbeClipmapGpuUploader? uploader;

    private bool forceRecreate;
    private bool isDisposed;

    private int lastResolution;
    private int lastLevels;
    private int lastLayoutVersion;

    public LumOnWorldProbeClipmapGpuResources? Resources => resources;
    public LumOnWorldProbeClipmapGpuUploader? Uploader => uploader;

    public LumOnWorldProbeClipmapBufferManager(ICoreClientAPI capi, LumOnConfig config)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
        this.config = config ?? throw new ArgumentNullException(nameof(config));

        // On shader reload, treat clipmap resources as needing rebuild.
        // This is conservative (textures often survive), but keeps attachment state deterministic.
        this.capi.Event.ReloadShader += OnReloadShader;

        lastLayoutVersion = LayoutVersion;
    }

    public void RequestRecreate(string reason)
    {
        forceRecreate = true;
        capi.Logger.Debug("[LumOn][WorldProbes] Clipmap resource recreation requested: {0}", reason);
    }

    /// <summary>
    /// Ensures clipmap textures exist and match current config.
    /// Returns true if resources were already valid, false if they were created/recreated.
    /// </summary>
    public bool EnsureResources()
    {
        if (isDisposed) return false;

        int resolution = config.WorldProbeClipmap.ClipmapResolution;
        int levels = config.WorldProbeClipmap.ClipmapLevels;

        bool needsRecreate =
            forceRecreate ||
            resources is null ||
            resolution != lastResolution ||
            levels != lastLevels ||
            lastLayoutVersion != LayoutVersion;

        if (!needsRecreate)
        {
            return true;
        }

        forceRecreate = false;

        resources?.Dispose();
        uploader?.Dispose();

        resources = new LumOnWorldProbeClipmapGpuResources(resolution, levels);
        uploader = new LumOnWorldProbeClipmapGpuUploader(capi);

        lastResolution = resolution;
        lastLevels = levels;
        lastLayoutVersion = LayoutVersion;

        return false;
    }

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

        capi.Event.ReloadShader -= OnReloadShader;

        uploader?.Dispose();
        uploader = null;

        resources?.Dispose();
        resources = null;
    }

    private bool OnReloadShader()
    {
        RequestRecreate("graphics reload");
        EnsureResources();
        return true;
    }
}
