using System.Numerics;
using Newtonsoft.Json;
using VanillaGraphicsExpanded.PBR.Materials;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Verifies water-medium authoring defaults and validation in isolation.</summary>
[Collection("PbrMaterialRegistry")]
public sealed class WaterMediumAuthoringTests
{
    #region Public API
    /// <summary>Omitted values inherit while explicit zero coefficients remain zero.</summary>
    [Fact]
    public void MembersInheritAndExplicitZeroSurvives()
    {
        // Arrange: provide inherited medium values while explicitly authoring zero for other members.
        var inherited = new WaterMedium(new(.1f, .2f, .3f), new(.4f), 2, .6f);
        var json = JsonConvert.DeserializeObject<WaterMediumJson>("""
            {"density":0,"anisotropy":0,"scatteringPerMetre":[0,0,0]}
            """)!;

        // Act: resolve the authored subset against the inherited medium.
        var actual = WaterMedium.Resolve(json, inherited);

        // Assert: explicit zeros and the independent clear-water absorption constant are preserved.
        Assert.Equal(inherited.AdditionalAbsorptionPerMetre, actual.AdditionalAbsorptionPerMetre);
        Assert.Equal(Vector3.Zero, actual.ScatteringPerMetre);
        Assert.Equal(Vector3.Zero, actual.AbsorptionPerMetre);
        Assert.Equal(0, actual.Anisotropy);
        Assert.Equal(new Vector3(.340f, .0565f, .00922f), WaterMedium.Clear.AbsorptionPerMetre);
    }

    /// <summary>Invalid coefficients inherit defaults and emit a diagnostic for each invalid member.</summary>
    [Fact]
    public void InvalidMembersInheritWithDiagnostics()
    {
        // Arrange: prepare a callback and authored values containing non-finite, negative, and malformed members.
        var messages = new List<string>();
        var json = new WaterMediumJson
        {
            AdditionalAbsorptionPerMetre = [float.NaN, 0, 0],
            ScatteringPerMetre = [-1, 0],
            Density = float.PositiveInfinity,
            Anisotropy = 1
        };

        // Act: resolve invalid members against clear-water defaults.
        var actual = WaterMedium.Resolve(json, WaterMedium.Clear, messages.Add);

        // Assert: invalid authored members fall back and each reports a diagnostic.
        Assert.Equal(WaterMedium.Clear, actual);
        Assert.Equal(4, messages.Count);
    }
    #endregion
}
