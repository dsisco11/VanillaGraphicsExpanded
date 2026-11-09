using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests.Unit.Atmosphere;

/// <summary>Checks explicit atmospheric interface ownership and linked metadata discovery.</summary>
public sealed class AtmosphereLinkedInterfaceRulesTests
{

    #region Public API
    /// <summary>The solar draw contract is enabled only when all four linked uniforms are present.</summary>
    [Theory]
    [InlineData(null, true)]
    [InlineData("vge_atmosphereSunDraw", false)]
    [InlineData("vge_atmosphereSun", false)]
    [InlineData("vge_atmosphereDisk", false)]
    [InlineData("vge_atmosphereDiskStorageScale", false)]
    public void SolarContractRequiresCompleteLinkedInterface(string? missing, bool expected)
    {
        // Arrange: each theory row identifies one missing uniform while all other names remain linkable.
        // Act by resolving linked uniforms while omitting the name selected by this theory row.
        var inputs = AtmosphereProgramBindings.Resolve("standard", name => name != missing);
        // Assert the complete solar contract is required and unrelated families resolve to none.
        Assert.Equal(expected, (inputs & AtmosphereBindings.SunDisk) != 0);
        Assert.Equal(AtmosphereBindings.None, AtmosphereProgramBindings.Resolve("moon", _ => true));
        Assert.Equal(AtmosphereBindings.None, AtmosphereProgramBindings.Resolve("celestialobject", _ => true));
    }

    /// <summary>Only patched engine families declare an atmosphere interface.</summary>
    [Theory]
    [InlineData("sky", AtmosphereBindings.None)]
    [InlineData("chunkopaque", AtmosphereBindings.Environment)]
    [InlineData("chunktopsoil", AtmosphereBindings.Environment)]
    [InlineData("standard", AtmosphereBindings.Environment | AtmosphereBindings.Solar | AtmosphereBindings.Aerial | AtmosphereBindings.SunDirection | AtmosphereBindings.SunDisk | AtmosphereBindings.DirectRadius)]
    [InlineData("entityanimated", AtmosphereBindings.Environment | AtmosphereBindings.Solar | AtmosphereBindings.Aerial | AtmosphereBindings.SunDirection | AtmosphereBindings.DirectRadius)]
    [InlineData("instanced", AtmosphereBindings.Environment | AtmosphereBindings.Solar | AtmosphereBindings.Aerial | AtmosphereBindings.SunDirection | AtmosphereBindings.DirectRadius)]
    [InlineData("chunktransparent", AtmosphereBindings.Environment | AtmosphereBindings.Solar | AtmosphereBindings.Aerial | AtmosphereBindings.SunDirection | AtmosphereBindings.DirectRadius)]
    public void PatchedFamiliesDeclareExpectedInputs(string family, object expected)
    {
        // Arrange: each theory row supplies a patched engine family and its expected linked binding set.
        // Act and assert the expected binding set for this patched family.
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
        // Arrange: each theory row supplies a non-atmosphere family whose discovery callback must remain unused.
        // Act and assert unrelated shader families return without invoking discovery.
        Assert.Equal(AtmosphereBindings.None, AtmosphereProgramBindings.Resolve(family, _ => throw new InvalidOperationException("Unexpected discovery")));
    }

    /// <summary>Linker-eliminated inputs are omitted and each expected name is inspected once.</summary>
    [Fact]
    public void ResolveRetainsOnlyLinkedInputs()
    {
        // Arrange: define one active aerial input and isolate linker discovery to that exact name.
        var inspected = new List<string>();
        // Act by resolving the standard family while reporting only the solar input as linked.
        var active = AtmosphereProgramBindings.Resolve("standard", name =>
        {
            inspected.Add(name);
            return name is "vge_atmosphereSolar";
        });
        // Assert only solar is retained and the resolver inspected each declared input once in order.
        Assert.Equal(AtmosphereBindings.Solar, active);
        Assert.Equal(new[] { "vge_atmosphereEnvironment", "vge_atmosphereSolar", "vge_atmosphereAerialParams", "vge_atmosphereAerialRadiance", "vge_atmosphereAerialAttenuation", "vge_atmosphereSunDirection", "vge_atmosphereDirectRadius", "vge_atmosphereSunDraw", "vge_lightShaftOcclusion", "vge_atmosphereScatteringSun" }, inspected);
    }

    /// <summary>A missing parameter or sibling sampler cannot suppress another active aerial input.</summary>
    [Theory]
    [InlineData("vge_atmosphereAerialParams", AtmosphereBindings.AerialParams)]
    [InlineData("vge_atmosphereAerialRadiance", AtmosphereBindings.AerialRadiance)]
    [InlineData("vge_atmosphereAerialAttenuation", AtmosphereBindings.AerialAttenuation)]
    public void AerialInputsResolveIndependently(string input, object expected)
    {
        // Arrange: limit the linked-name probe to the single aerial input in this theory row.
        // Act and assert the selected aerial name resolves independently of sibling inputs.
        Assert.Equal((AtmosphereBindings)expected, AtmosphereProgramBindings.Resolve("standard", name => name == input));
    }
    #endregion
}
