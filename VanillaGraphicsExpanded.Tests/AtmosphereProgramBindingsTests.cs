using System.Reflection;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks explicit atmospheric interface ownership and linked metadata discovery.</summary>
public sealed class AtmosphereProgramBindingsTests
{
    #region Interface ownership
    /// <summary>The solar draw contract is enabled only when all three linked uniforms are present.</summary>
    [Theory]
    [InlineData(null, true)]
    [InlineData("vge_atmosphereSunDraw", false)]
    [InlineData("vge_atmosphereSun", false)]
    [InlineData("vge_atmosphereDisk", false)]
    public void SolarContractRequiresCompleteLinkedInterface(string? missing, bool expected)
    {
        var inputs = AtmosphereProgramBindings.Resolve("standard", name => name != missing);
        Assert.Equal(expected, (inputs & AtmosphereBindings.SunDisk) != 0);
        Assert.Equal(AtmosphereBindings.None, AtmosphereProgramBindings.Resolve("moon", _ => true));
        Assert.Equal(AtmosphereBindings.None, AtmosphereProgramBindings.Resolve("celestialobject", _ => true));
    }

    /// <summary>Only patched engine families declare an atmosphere interface.</summary>
    [Theory]
    [InlineData("sky", AtmosphereBindings.Sky | AtmosphereBindings.SkyMapping)]
    [InlineData("chunkopaque", AtmosphereBindings.Environment)]
    [InlineData("chunktopsoil", AtmosphereBindings.Environment)]
    [InlineData("standard", AtmosphereBindings.Environment | AtmosphereBindings.Solar | AtmosphereBindings.Aerial | AtmosphereBindings.SunDirection | AtmosphereBindings.SunDisk)]
    [InlineData("entityanimated", AtmosphereBindings.Environment | AtmosphereBindings.Solar | AtmosphereBindings.Aerial | AtmosphereBindings.SunDirection)]
    [InlineData("instanced", AtmosphereBindings.Environment | AtmosphereBindings.Solar | AtmosphereBindings.Aerial | AtmosphereBindings.SunDirection)]
    [InlineData("chunktransparent", AtmosphereBindings.Environment | AtmosphereBindings.Solar | AtmosphereBindings.Aerial | AtmosphereBindings.SunDirection)]
    public void PatchedFamiliesDeclareExpectedInputs(string family, object expected)
    {
        Assert.Equal((AtmosphereBindings)expected, AtmosphereProgramBindings.Expected(family));
    }

    /// <summary>Unrelated shader families never trigger interface inspection.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("gui")]
    [InlineData("chunkliquid")]
    [InlineData("pbr_direct_lighting")]
    public void UnrelatedFamiliesSkipDiscovery(string? family)
    {
        Assert.Equal(AtmosphereBindings.None, AtmosphereProgramBindings.Resolve(family, _ => throw new InvalidOperationException("Unexpected discovery")));
    }

    /// <summary>Linker-eliminated inputs are omitted and each expected name is inspected once.</summary>
    [Fact]
    public void ResolveRetainsOnlyLinkedInputs()
    {
        var inspected = new List<string>();
        var active = AtmosphereProgramBindings.Resolve("standard", name =>
        {
            inspected.Add(name);
            return name is "vge_atmosphereSolar";
        });
        Assert.Equal(AtmosphereBindings.Solar, active);
        Assert.Equal(new[] { "vge_atmosphereEnvironment", "vge_atmosphereSolar", "vge_atmosphereAerialParams", "vge_atmosphereAerialRadiance", "vge_atmosphereAerialAttenuation", "vge_atmosphereSunDirection", "vge_atmosphereSunDraw" }, inspected);
    }
    #endregion

    #region Independent aerial discovery
    /// <summary>A missing parameter or sibling sampler cannot suppress another active aerial input.</summary>
    [Theory]
    [InlineData("vge_atmosphereAerialParams", AtmosphereBindings.AerialParams)]
    [InlineData("vge_atmosphereAerialRadiance", AtmosphereBindings.AerialRadiance)]
    [InlineData("vge_atmosphereAerialAttenuation", AtmosphereBindings.AerialAttenuation)]
    public void AerialInputsResolveIndependently(string input, object expected)
    {
        Assert.Equal((AtmosphereBindings)expected, AtmosphereProgramBindings.Resolve("standard", name => name == input));
    }
    #endregion

    #region Cached lifecycle
    /// <summary>A managed program caches its linked inputs until compilation starts, including a failed replacement.</summary>
    [Fact]
    public void CompilationReplacesAndInvalidatesCachedInterface()
    {
        var program = new FixtureProgram();
        program.SetInputs("vge_atmosphereSolar");
        AtmosphereShaderCompilationHook.Postfix(program, true);
        Assert.Equal(AtmosphereBindings.Solar, AtmosphereProgramBindings.Get(program));
        program.SetInputs("vge_atmosphereEnvironment");
        Assert.Equal(AtmosphereBindings.Solar, AtmosphereProgramBindings.Get(program));
        AtmosphereShaderCompilationHook.Prefix(program);
        AtmosphereShaderCompilationHook.Postfix(program, false);
        Assert.Equal(AtmosphereBindings.None, AtmosphereProgramBindings.Get(program));
        AtmosphereShaderCompilationHook.Postfix(program, true);
        Assert.Equal(AtmosphereBindings.Environment, AtmosphereProgramBindings.Get(program));
        AtmosphereProgramBindings.Remove(program);
        Assert.Equal(AtmosphereBindings.None, AtmosphereProgramBindings.Get(program));
    }

    /// <summary>Supplies the engine's cached linked interface without creating GPU objects.</summary>
    private sealed class FixtureProgram : ShaderProgram
    {
        /// <summary>Selects a patched engine family with game-owned assets.</summary>
        internal FixtureProgram() { PassName = "standard"; AssetDomain = "game"; }
        /// <summary>Simulates the linked interface rebuilt by the engine compiler.</summary>
        internal void SetInputs(params string[] names)
        {
            uniformLocations.Clear();
            foreach (string name in names) uniformLocations.Add(name, 1);
        }
    }
    #endregion
    #region Engine declaration evidence
    /// <summary>The intercepted compile target must be the concrete engine implementation with a success result.</summary>
    [Fact]
    public void CompileHookTargetsConcreteEngineImplementation()
    {
        var method = typeof(ShaderProgram).GetMethod("Compile", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        Assert.Equal(typeof(ShaderProgram), method.DeclaringType);
        Assert.False(method.IsAbstract);
        Assert.Equal(typeof(bool), method.ReturnType);
    }
    #endregion
}
