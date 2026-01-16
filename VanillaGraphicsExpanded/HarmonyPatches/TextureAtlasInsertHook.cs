using System;
using System.Linq;
using System.Reflection;

using HarmonyLib;

using VanillaGraphicsExpanded.ModSystems;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.HarmonyPatches;

internal static class TextureAtlasInsertHook
{
    private const string TargetMethodName = "GetOrInsertTexture";

    private static ICoreClientAPI? capi;
    private static Action<string>? runtimeLog;

    public static void TryApplyPatches(Harmony? harmony, ICoreClientAPI api, Action<string> log)
    {
        if (harmony is null) return;

        capi = api;
        runtimeLog = log;

        Type atlasType = api.BlockTextureAtlas.GetType();
        log($"[VGE] TextureAtlasInsertHook: BlockTextureAtlas runtime type: {atlasType.FullName}");

        MethodInfo? target = FindGetOrInsertTextureAssetLocationOverload(atlasType);
        if (target is null)
        {
            log("[VGE] TextureAtlasInsertHook: unable to find GetOrInsertTexture(AssetLocation, out int, out TextureAtlasPosition, ...) overload");
            return;
        }

        var postfix = new HarmonyMethod(typeof(TextureAtlasInsertHook), nameof(GetOrInsertTexture_Postfix));
        try
        {
            harmony.Patch(target, postfix: postfix);
            log($"[VGE] TextureAtlasInsertHook: patched {atlasType.FullName}.{TargetMethodName}(AssetLocation, out int, out TextureAtlasPosition, ...) ");
        }
        catch (Exception ex)
        {
            log($"[VGE] TextureAtlasInsertHook: failed to patch: {ex.Message}");
        }
    }

    private static MethodInfo? FindGetOrInsertTextureAssetLocationOverload(Type atlasType)
    {
        // bool GetOrInsertTexture(AssetLocation path, out int textureSubId, out TextureAtlasPosition texPos, CreateTextureDelegate onCreate = null, float alphaTest = 0f)
        return atlasType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(m => string.Equals(m.Name, TargetMethodName, StringComparison.Ordinal))
            .FirstOrDefault(m =>
            {
                ParameterInfo[] p = m.GetParameters();
                return p.Length >= 3
                    && p[0].ParameterType == typeof(AssetLocation)
                    && p[1].ParameterType == typeof(int).MakeByRefType()
                    && p[2].ParameterType == typeof(TextureAtlasPosition).MakeByRefType();
            });
    }

    // Signature must match the target method's first parameters; include __result for bool return.
    public static void GetOrInsertTexture_Postfix(bool __result, AssetLocation path, ref int textureSubId, ref TextureAtlasPosition texPos)
    {
        try
        {
            if (!__result || capi is null || runtimeLog is null)
            {
                return;
            }

            if (!ConfigModSystem.Config.DebugLogNormalDepthAtlas)
            {
                return;
            }

            string p = path?.Path ?? string.Empty;
            if (p.Length == 0)
            {
                return;
            }

            // We care about runtime composite keys, especially those using the ++0~ overlay syntax.
            if (p.Contains("clay/brick/", StringComparison.OrdinalIgnoreCase) && (p.Contains("++", StringComparison.Ordinal) || p.Contains('~')))
            {
                runtimeLog(
                    $"[VGE] Atlas insert: subId={textureSubId} atlasTexId={texPos?.atlasTextureId} reloadIt={texPos?.reloadIteration} key={path.Domain}:{path.Path}");
            }
        }
        catch
        {
            // Never crash the game from a debug hook.
        }
    }
}
