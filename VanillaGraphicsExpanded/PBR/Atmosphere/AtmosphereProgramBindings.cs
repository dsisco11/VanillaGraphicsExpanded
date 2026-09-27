using System;
using System.Runtime.CompilerServices;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Names the atmospheric inputs owned by each patched engine shader family.</summary>
[Flags]
internal enum AtmosphereBindings
{
    None = 0,
    Environment = 1,
    Solar = 2,
    Horizon = 4,
    Extinction = 8,
    Sky = 16,
    SkyMapping = 32
}

/// <summary>Stores the active atmospheric interface once per linked engine program.</summary>
internal static class AtmosphereProgramBindings
{
    private static readonly ConditionalWeakTable<ShaderProgramBase, LinkedBindings> programs = new();

    /// <summary>Keeps linked metadata tied to the managed program lifetime, not a reusable GL identifier.</summary>
    private sealed record LinkedBindings(AtmosphereBindings Inputs);

    #region Interface ownership
    /// <summary>Allows only shader families whose sources receive atmospheric patches.</summary>
    internal static AtmosphereBindings Expected(string? passName) => passName switch
    {
        "sky" => AtmosphereBindings.Sky | AtmosphereBindings.SkyMapping,
        "chunkopaque" or "chunktopsoil" => AtmosphereBindings.Environment,
        "standard" or "entityanimated" or "instanced" or "chunktransparent" =>
            AtmosphereBindings.Environment | AtmosphereBindings.Solar | AtmosphereBindings.Horizon | AtmosphereBindings.Extinction,
        _ => AtmosphereBindings.None
    };

    /// <summary>Resolves only the allowlisted interface; the linker may remove unused inputs in specializations.</summary>
    internal static AtmosphereBindings Resolve(string? passName, Func<string, bool> hasUniform)
    {
        var expected = Expected(passName);
        var active = AtmosphereBindings.None;
        if ((expected & AtmosphereBindings.Environment) != 0 && hasUniform("vge_atmosphereEnvironment")) active |= AtmosphereBindings.Environment;
        if ((expected & AtmosphereBindings.Solar) != 0 && hasUniform("vge_atmosphereSolar")) active |= AtmosphereBindings.Solar;
        if ((expected & AtmosphereBindings.Horizon) != 0 && hasUniform("vge_atmosphereHorizon")) active |= AtmosphereBindings.Horizon;
        if ((expected & AtmosphereBindings.Extinction) != 0 && hasUniform("vge_atmosphereExtinction")) active |= AtmosphereBindings.Extinction;
        if ((expected & AtmosphereBindings.Sky) != 0 && hasUniform("vge_atmosphereSky")) active |= AtmosphereBindings.Sky;
        if ((expected & AtmosphereBindings.SkyMapping) != 0 && hasUniform("vge_atmosphereLutHorizon")) active |= AtmosphereBindings.SkyMapping;
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
        var active = Resolve(source.PassName, source.HasUniform);
        if (active != AtmosphereBindings.None) programs.Add(program, new(active));
    }

    /// <summary>Reads cached metadata without uniform discovery, allocation or GL queries during a draw.</summary>
    internal static AtmosphereBindings Get(ShaderProgramBase program) =>
        programs.TryGetValue(program, out var linked) ? linked.Inputs : AtmosphereBindings.None;
    #endregion
}
