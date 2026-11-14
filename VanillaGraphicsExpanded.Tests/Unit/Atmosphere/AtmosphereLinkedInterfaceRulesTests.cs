using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests.Unit.Atmosphere;

/// <summary>Checks explicit atmospheric interface ownership and linked metadata discovery.</summary>
public sealed class AtmosphereLinkedInterfaceRulesTests
{

    #region Public API
    /// <summary>The atmosphere-owned sun-disk contract requires every linked draw uniform.</summary>
    [Theory]
    [InlineData(null, true)]
    [InlineData("vge_atmosphereSunDraw", false)]
    [InlineData("vge_atmosphereSun", false)]
    [InlineData("vge_atmosphereDisk", false)]
    [InlineData("vge_atmosphereDiskStorageScale", false)]
    public void SunDiskContractRequiresCompleteLinkedInterface(string? missing, bool expected)
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
    [InlineData("standard", AtmosphereBindings.Environment | AtmosphereBindings.Aerial | AtmosphereBindings.SunDisk)]
    [InlineData("entityanimated", AtmosphereBindings.Environment | AtmosphereBindings.Aerial)]
    [InlineData("instanced", AtmosphereBindings.Environment | AtmosphereBindings.Aerial)]
    [InlineData("chunktransparent", AtmosphereBindings.Environment | AtmosphereBindings.Aerial)]
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

    /// <summary>Linker-eliminated inputs are omitted while discovery stays within the owned interface.</summary>
    [Fact]
    public void ResolveRetainsOnlyLinkedInputs()
    {
        // Arrange: link only aerial radiance and record the allowlisted names inspected by the resolver.
        var inspected = new List<string>();
        // Act by resolving the standard family while reporting only aerial radiance as linked.
        var active = AtmosphereProgramBindings.Resolve("standard", name =>
        {
            inspected.Add(name);
            return name is "vge_atmosphereAerialRadiance";
        });
        // Assert only linked bits survive, names belong to this family's interface, and no name is queried twice.
        // Complete sun-disk and scattering groups may stop discovery at their first inactive sibling.
        Assert.Equal(AtmosphereBindings.AerialRadiance, active);
        var standardInputs = new HashSet<string>(StringComparer.Ordinal)
        {
            "vge_atmosphereEnvironment", "vge_atmosphereAerialParams", "vge_atmosphereAerialRadiance",
            "vge_atmosphereAerialAttenuation", "vge_atmosphereSunDraw", "vge_atmosphereSun",
            "vge_atmosphereDisk", "vge_atmosphereDiskStorageScale", "vge_lightShaftOcclusion",
            "vge_atmosphereScatteringSun", "vge_atmosphereScatteringMoon"
        };
        Assert.Contains("vge_atmosphereAerialRadiance", inspected);
        Assert.Equal(inspected.Count, inspected.Distinct(StringComparer.Ordinal).Count());
        Assert.All(inspected, name => Assert.Contains(name, standardInputs));
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
