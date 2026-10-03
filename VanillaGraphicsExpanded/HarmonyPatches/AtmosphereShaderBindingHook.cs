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
    private const int AerialRadianceTextureUnit = AtmosphereProgramBindings.AerialRadianceTextureUnit;
    private const int AerialAttenuationTextureUnit = AtmosphereProgramBindings.AerialAttenuationTextureUnit;

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
        if ((bindings & AtmosphereBindings.AerialRadiance) != 0)
        {
            // Standard also draws before world initialization. Assign distinct 3D
            // sampler units even then, so they cannot alias engine 2D samplers at unit zero.
            __instance.Uniform("vge_atmosphereAerialRadiance", AerialRadianceTextureUnit);
            var cache = Rendering.StateCache.Current;
            cache.BindTexture(OpenTK.Graphics.OpenGL.TextureTarget.Texture3D, AerialRadianceTextureUnit, AtmosphereModSystem.AerialRadianceTextureId);
            cache.UnbindSampler(AerialRadianceTextureUnit);
        }
        if ((bindings & AtmosphereBindings.AerialAttenuation) != 0)
        {
            __instance.Uniform("vge_atmosphereAerialAttenuation", AerialAttenuationTextureUnit);
            var cache = Rendering.StateCache.Current;
            cache.BindTexture(OpenTK.Graphics.OpenGL.TextureTarget.Texture3D, AerialAttenuationTextureUnit, AtmosphereModSystem.AerialAttenuationTextureId);
            cache.UnbindSampler(AerialAttenuationTextureUnit);
        }
        // Resource initialization is owned by the Before renderer.
        if (lighting is null) return;
        if ((bindings & AtmosphereBindings.Environment) != 0)
            __instance.Uniform("vge_atmosphereEnvironment", lighting.Environment.X, lighting.Environment.Y, lighting.Environment.Z);
        if ((bindings & AtmosphereBindings.Solar) != 0)
            __instance.Uniform("vge_atmosphereSolar", lighting.Solar.X, lighting.Solar.Y, lighting.Solar.Z);
        if ((bindings & AtmosphereBindings.SunDirection) != 0)
            __instance.Uniform("vge_atmosphereSunDirection", lighting.Sun.X, lighting.Sun.Y, lighting.Sun.Z);
        if ((bindings & AtmosphereBindings.AerialParams) != 0)
        {
            __instance.Uniform("vge_atmosphereAerialParams", lighting.Altitude, lighting.HorizonElevation,
                PbrDrawRouteHook.Api?.Render.ShaderUniforms.CameraUnderwater ?? 0f);
        }
        if ((bindings & AtmosphereBindings.SkyMapping) != 0)
            __instance.Uniform("vge_atmosphereLutHorizon", lighting.HorizonElevation);
        if ((bindings & AtmosphereBindings.Sky) != 0)
        {
            __instance.BindTexture2D("vge_atmosphereSky", AtmosphereModSystem.SkyTextureId, SkyTextureUnit);
            Rendering.StateCache.Current.UnbindSampler(SkyTextureUnit);
        }
    }
    #endregion
}
