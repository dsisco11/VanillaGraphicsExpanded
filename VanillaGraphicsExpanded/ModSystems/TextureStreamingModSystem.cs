using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.ModSystems;

public sealed class TextureStreamingModSystem : ModSystem, ILiveConfigurable
{
    private ICoreClientAPI? capi;
    private TextureStreamingManagerRenderer? renderer;
    private bool rendererRegistered;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;

        renderer ??= new TextureStreamingManagerRenderer();
        api.Event.RegisterRenderer(renderer, EnumRenderStage.Before, "vge_texture_streaming");
        rendererRegistered = true;

        ConfigModSystem.Config.Sanitize();
        TextureStreamingSystem.Configure(BuildTextureStreamingSettings(ConfigModSystem.Config));
    }

    public void OnConfigReloaded(ICoreAPI api)
    {
        if (api is not ICoreClientAPI)
        {
            return;
        }

        ConfigModSystem.Config.Sanitize();
        TextureStreamingSystem.Configure(BuildTextureStreamingSettings(ConfigModSystem.Config));
    }

    public override void Dispose()
    {
        base.Dispose();

        if (capi != null && rendererRegistered && renderer is not null)
        {
            try
            {
                capi.Event.UnregisterRenderer(renderer, EnumRenderStage.Before);
            }
            catch
            {
                // Best-effort.
            }
        }

        renderer?.Dispose();
        renderer = null;
        rendererRegistered = false;

        TextureStreamingSystem.Dispose();

        capi = null;
    }

    private static TextureStreamingSettings BuildTextureStreamingSettings(VgeConfig cfg)
    {
        return new TextureStreamingSettings(
            EnablePboStreaming: cfg.TextureStreaming.Enabled,
            AllowDirectUploads: cfg.TextureStreaming.AllowDirectUploads,
            ForceDisablePersistent: cfg.TextureStreaming.ForceDisablePersistent,
            UseCoherentMapping: cfg.TextureStreaming.UseCoherentMapping,
            MaxUploadsPerFrame: cfg.TextureStreaming.MaxUploadsPerFrame,
            MaxBytesPerFrame: cfg.TextureStreaming.MaxBytesPerFrame,
            MaxStagingBytes: cfg.TextureStreaming.MaxStagingBytes,
            PersistentRingBytes: cfg.TextureStreaming.PersistentRingBytes,
            TripleBufferBytes: cfg.TextureStreaming.TripleBufferBytes,
            PboAlignment: cfg.TextureStreaming.PboAlignment);
    }
}
