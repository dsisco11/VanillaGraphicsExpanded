using System;
using System.Runtime.CompilerServices;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Names the atmospheric inputs owned by each patched engine shader family.</summary>
[Flags]
internal enum AtmosphereBindings
{
    None = 0,
    Environment = 1,
    Solar = 2,
    SunDisk = 64,
    SunDirection = 128,
    AerialParams = 256,
    AerialRadiance = 512,
    AerialAttenuation = 1024,
    Aerial = AerialParams | AerialRadiance | AerialAttenuation
}

/// <summary>Stores the active atmospheric interface once per linked engine program.</summary>
internal static class AtmosphereProgramBindings
{
    internal const int AerialRadianceTextureUnit = 11;
    internal const int AerialAttenuationTextureUnit = 12;
    private static readonly ConditionalWeakTable<ShaderProgramBase, LinkedBindings> programs = new();

    /// <summary>Keeps linked metadata tied to the managed program lifetime, not a reusable GL identifier.</summary>
    private sealed record LinkedBindings(AtmosphereBindings Inputs);

    #region Interface ownership
    /// <summary>Allows only shader families whose sources receive atmospheric patches.</summary>
    internal static AtmosphereBindings Expected(string? passName) => passName switch
    {
        "chunkopaque" or "chunktopsoil" => AtmosphereBindings.Environment,
        "standard" => AtmosphereBindings.Environment | AtmosphereBindings.Solar | AtmosphereBindings.Aerial
            | AtmosphereBindings.SunDirection | AtmosphereBindings.SunDisk,
        "entityanimated" or "instanced" or "chunktransparent" =>
            AtmosphereBindings.Environment | AtmosphereBindings.Solar | AtmosphereBindings.Aerial | AtmosphereBindings.SunDirection,
        _ => AtmosphereBindings.None
    };

    /// <summary>Resolves only the allowlisted interface; the linker may remove unused inputs in specializations.</summary>
    internal static AtmosphereBindings Resolve(string? passName, Func<string, bool> hasUniform)
    {
        var expected = Expected(passName);
        var active = AtmosphereBindings.None;
        if ((expected & AtmosphereBindings.Environment) != 0 && hasUniform("vge_atmosphereEnvironment")) active |= AtmosphereBindings.Environment;
        if ((expected & AtmosphereBindings.Solar) != 0 && hasUniform("vge_atmosphereSolar")) active |= AtmosphereBindings.Solar;
        if ((expected & AtmosphereBindings.AerialParams) != 0 && hasUniform("vge_atmosphereAerialParams")) active |= AtmosphereBindings.AerialParams;
        if ((expected & AtmosphereBindings.AerialRadiance) != 0 && hasUniform("vge_atmosphereAerialRadiance")) active |= AtmosphereBindings.AerialRadiance;
        if ((expected & AtmosphereBindings.AerialAttenuation) != 0 && hasUniform("vge_atmosphereAerialAttenuation")) active |= AtmosphereBindings.AerialAttenuation;
        if ((expected & AtmosphereBindings.SunDirection) != 0 && hasUniform("vge_atmosphereSunDirection")) active |= AtmosphereBindings.SunDirection;
        if ((expected & AtmosphereBindings.SunDisk) != 0 && hasUniform("vge_atmosphereSunDraw")
            && hasUniform("vge_atmosphereSun") && hasUniform("vge_atmosphereDisk")) active |= AtmosphereBindings.SunDisk;
        return active;
    }
    #endregion

    #region Program lifetime
    /// <summary>Discards the previous linked interface before recompilation, including failed recompilation.</summary>
    internal static void Remove(ShaderProgramBase program) => programs.Remove(program);

    /// <summary>Publishes an allowlisted linked interface after successful compilation.</summary>
    internal static void Register(ShaderProgramBase program)
    {
        programs.Remove(program);
        // VGE programs own their own contracts and are not patched engine source families.
        if (program is not ShaderProgram source || source.AssetDomain == Constants.ModId) return;
        if (Expected(source.PassName) == AtmosphereBindings.None) return;
        var layout = new GpuProgramLayout();
        // The engine's source-derived uniform table is not the linked interface. In particular,
        // active sampler3D inputs must be registered even when its source scanner omitted them.
        var active = Resolve(source.PassName, name =>
        {
            if (source.ProgramId == 0) return source.HasUniform(name);
            int location = layout.GetUniformLocation(source.ProgramId, name);
            if (location < 0) return false; // Legitimately optimized out in this specialization.
            source.uniformLocations[name] = location;
            return true;
        });
        if (active != AtmosphereBindings.None) programs.Add(program, new(active));
        if (source.ProgramId != 0 && (active & AtmosphereBindings.Aerial) != 0)
        {
            // Initialize immediately after linking, including before the first atmospheric snapshot.
            // The program is linked, not yet active through the engine. Apply its sampler contract
            // through the GPU layout, which restores GL state without changing engine draw ownership.
            if ((active & AtmosphereBindings.AerialRadiance) != 0)
                layout.RegisterSamplerUnit("vge_atmosphereAerialRadiance", AerialRadianceTextureUnit);
            if ((active & AtmosphereBindings.AerialAttenuation) != 0)
                layout.RegisterSamplerUnit("vge_atmosphereAerialAttenuation", AerialAttenuationTextureUnit);
            layout.ApplyContract(source.ProgramId);
        }
    }

    /// <summary>Reads cached metadata without uniform discovery, allocation or GL queries during a draw.</summary>
    internal static AtmosphereBindings Get(ShaderProgramBase program) =>
        programs.TryGetValue(program, out var linked) ? linked.Inputs : AtmosphereBindings.None;
    #endregion
}
