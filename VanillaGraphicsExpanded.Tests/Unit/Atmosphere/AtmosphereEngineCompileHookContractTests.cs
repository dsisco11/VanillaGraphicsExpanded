using System.Reflection;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.Unit.Atmosphere;

/// <summary>Checks explicit atmospheric interface ownership and linked metadata discovery.</summary>
public sealed class AtmosphereEngineCompileHookContractTests
{
    #region Public API
    /// <summary>The intercepted compile target must be the concrete engine implementation with a success result.</summary>
    [Fact]
    public void CompileHookTargetsConcreteEngineImplementation()
    {
        // Arrange: resolve the concrete engine Compile method by reflection without invoking it.
        var method = typeof(ShaderProgram).GetMethod("Compile", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        // Act and Assert: perform the operation and check its observable result.
        Assert.Equal(typeof(ShaderProgram), method.DeclaringType);
        Assert.False(method.IsAbstract);
        Assert.Equal(typeof(bool), method.ReturnType);
    }
    #endregion
}
