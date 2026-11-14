using System.Reflection;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.Component.Atmosphere;

/// <summary>Checks explicit atmospheric interface ownership and linked metadata discovery.</summary>
public sealed class AtmosphereProgramBindingsTests
{
    #region Public API
    /// <summary>A managed program caches its linked inputs until compilation starts, including a failed replacement.</summary>
    [Fact]
    public void CompilationReplacesAndInvalidatesCachedInterface()
    {
        // Arrange a fixture program whose linked interface can be changed independently of compilation hooks.
        var program = new FixtureProgram();
        // Act and Assert: publish the atmosphere-owned aerial interface, replace linked inputs, and verify cache changes only at compilation.
        program.SetInputs("vge_atmosphereAerialRadiance");
        AtmosphereShaderCompilationHook.Postfix(program, true);
        Assert.Equal(AtmosphereBindings.AerialRadiance, AtmosphereProgramBindings.Get(program));
        program.SetInputs("vge_atmosphereEnvironment");
        Assert.Equal(AtmosphereBindings.AerialRadiance, AtmosphereProgramBindings.Get(program));
        AtmosphereShaderCompilationHook.Prefix(program);
        AtmosphereShaderCompilationHook.Postfix(program, false);
        Assert.Equal(AtmosphereBindings.None, AtmosphereProgramBindings.Get(program));
        AtmosphereShaderCompilationHook.Postfix(program, true);
        Assert.Equal(AtmosphereBindings.Environment, AtmosphereProgramBindings.Get(program));
        AtmosphereProgramBindings.Remove(program);
        Assert.Equal(AtmosphereBindings.None, AtmosphereProgramBindings.Get(program));
    }
    #endregion

    #region Private
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
}
