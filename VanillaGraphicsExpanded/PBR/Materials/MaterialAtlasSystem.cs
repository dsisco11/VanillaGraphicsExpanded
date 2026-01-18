using System;

using Vintagestory.API.Client;

using VanillaGraphicsExpanded.PBR.Materials.Async;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>
/// Thin orchestrator for the material atlas subsystems.
/// Owns lifecycle/event-friendly entry points and exposes the texture store for shader binding hooks.
/// </summary>
internal sealed class MaterialAtlasSystem : IDisposable
{
    public static MaterialAtlasSystem Instance { get; } = new();

    private readonly PbrMaterialAtlasTextures impl;

    private MaterialAtlasSystem()
    {
        impl = PbrMaterialAtlasTextures.Instance;
    }

    public bool IsInitialized => impl.IsInitialized;

    public bool IsBuildComplete => impl.IsBuildComplete;

    public bool AreTexturesCreated => impl.AreTexturesCreated;

    public MaterialAtlasTextureStore TextureStore => impl.TextureStore;

    public void CreateTextureObjects(ICoreClientAPI capi) => impl.CreateTextureObjects(capi);

    public void PopulateAtlasContents(ICoreClientAPI capi) => impl.PopulateAtlasContents(capi);

    public void RequestRebuild(ICoreClientAPI capi) => impl.RequestRebuild(capi);

    public void RebakeNormalDepthAtlas(ICoreClientAPI capi) => impl.RebakeNormalDepthAtlas(capi);

    public bool TryGetMaterialParamsTextureId(int atlasTextureId, out int materialParamsTextureId)
        => impl.TryGetMaterialParamsTextureId(atlasTextureId, out materialParamsTextureId);

    public bool TryGetNormalDepthTextureId(int atlasTextureId, out int normalDepthTextureId)
        => impl.TryGetNormalDepthTextureId(atlasTextureId, out normalDepthTextureId);

    public bool TryGetAsyncBuildDiagnostics(out MaterialAtlasAsyncBuildDiagnostics diagnostics)
        => impl.TryGetAsyncBuildDiagnostics(out diagnostics);

    public void Dispose() => impl.Dispose();
}
