using HarmonyLib;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Binds the published atmosphere to patched engine programs without replacing engine-owned scene uniforms.</summary>
[HarmonyPatch(typeof(ShaderProgramBase), nameof(ShaderProgramBase.Use))]
internal static class AtmosphereShaderBindingHook
{
    private const int SkyTextureUnit = 13;

    #region Binding
    /// <summary>Shares one snapshot across sky, terrain and forward draws; initialization completes before the first scene draw.</summary>
    [HarmonyPostfix]
    internal static void Postfix(ShaderProgramBase __instance)
    {
        var bindings = AtmosphereProgramBindings.Get(__instance);
        if (bindings == AtmosphereBindings.None) return;
        var lighting = AtmosphereModSystem.Lighting;
        if ((bindings & AtmosphereBindings.SunDisk) != 0)
        {
            // Standard also renders GUI/items and terrain objects. Reset on every Use,
            // including startup before a snapshot exists, so solar state cannot leak.
            bool solarDraw = AtmosphereSunDrawHook.Active && lighting is not null;
            __instance.Uniform("vge_atmosphereSunDraw", solarDraw ? 1 : 0);
            if (solarDraw)
            {
                var disk = AtmosphereSolarDisk.Radiance(lighting!);
                __instance.Uniform("vge_atmosphereSun", lighting!.Sun.X, lighting.Sun.Y, lighting.Sun.Z, lighting.HorizonElevation);
                __instance.Uniform("vge_atmosphereDisk", disk.X, disk.Y, disk.Z, AtmosphereSolarDisk.AngularRadius);
            }
        }
        // Resource initialization is owned by the Before renderer.
        if (lighting is null) return;
        if ((bindings & AtmosphereBindings.Environment) != 0)
            __instance.Uniform("vge_atmosphereEnvironment", lighting.Environment.X, lighting.Environment.Y, lighting.Environment.Z);
        if ((bindings & AtmosphereBindings.Solar) != 0)
            __instance.Uniform("vge_atmosphereSolar", lighting.Solar.X, lighting.Solar.Y, lighting.Solar.Z);
        if ((bindings & AtmosphereBindings.SunDirection) != 0)
            __instance.Uniform("vge_atmosphereSunDirection", lighting.Sun.X, lighting.Sun.Y, lighting.Sun.Z);
        if ((bindings & AtmosphereBindings.Horizon) != 0)
            __instance.Uniform("vge_atmosphereHorizon", lighting.Horizon.X, lighting.Horizon.Y, lighting.Horizon.Z);
        if ((bindings & AtmosphereBindings.Extinction) != 0)
            __instance.Uniform("vge_atmosphereExtinction", lighting.Extinction.X, lighting.Extinction.Y, lighting.Extinction.Z);
        if ((bindings & AtmosphereBindings.SkyMapping) != 0)
            __instance.Uniform("vge_atmosphereLutHorizon", lighting.HorizonElevation);
        if ((bindings & AtmosphereBindings.Sky) != 0)
        {
            __instance.BindTexture2D("vge_atmosphereSky", AtmosphereModSystem.SkyTextureId, SkyTextureUnit);
            Rendering.GlStateCache.Current.UnbindSampler(SkyTextureUnit);
        }
    }
    #endregion
}
