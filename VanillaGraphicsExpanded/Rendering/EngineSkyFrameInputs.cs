using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Maintains legacy frame noise and lookup resource inputs while VGE owns the sky callback.</summary>
internal static class EngineSkyFrameInputs
{
    private static readonly AccessTools.FieldRef<ClientMain, int> Seed = AccessTools.FieldRefAccess<ClientMain, int>("frameSeed");
    private static readonly AccessTools.FieldRef<ClientMain, int> Sky = AccessTools.FieldRefAccess<ClientMain, int>("skyTextureId");
    private static readonly AccessTools.FieldRef<ClientMain, int> Glow = AccessTools.FieldRefAccess<ClientMain, int>("skyGlowTextureId");

    #region Public API
    /// <summary>Advances once per enabled frame; the night-sky renderer's independent counter is untouched.</summary>
    internal static void Publish(ICoreClientAPI api)
    {
        if (api.World is not ClientMain game) return;
        long pixels = System.Math.Max(1L, (long)api.Render.FrameWidth * api.Render.FrameHeight);
        Seed(game) = (int)(((long)Seed(game) + 1) % System.Math.Min(int.MaxValue, pixels));
        var uniforms = api.Render.ShaderUniforms;
        uniforms.DitherSeed = Seed(game);
        // Retained vanilla lookup consumers use their original coordinate convention.
        uniforms.SkyTextureId = Sky(game);
        uniforms.GlowTextureId = Glow(game);
        uniforms.SunsetMod = api.World.Calendar.SunsetMod;
    }
    #endregion
}
