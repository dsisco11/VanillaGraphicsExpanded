using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using Moq;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VanillaGraphicsExpanded.Tests.Unit.Atmosphere;

/// <summary>Checks physical lighting projection and reversible legacy ambient publication.</summary>
public sealed class AtmosphereAmbientPublicationTests
{
    #region Public API
    /// <summary>Solar and diffuse responses both contribute without a synthetic night-light floor.</summary>
    [Fact]
    public void LegacyResponseCombinesSolarAndDiffuseWithoutNightFloor()
    {
        // Arrange dark, solar-only, diffuse-only and combined snapshots as independent legacy response inputs.
        var dark = Snapshot(Vector3.Zero, Vector3.Zero);
        // Act by projecting each snapshot; assert darkness stays dark and both physical sources combine without a night floor.
        Assert.Equal(new AtmosphereLegacyLighting(Vector3.Zero, Vector3.Zero, 0), AtmosphereLegacyLighting.From(dark));
        var solar = AtmosphereLegacyLighting.From(Snapshot(new(MathF.PI), Vector3.Zero));
        var diffuse = AtmosphereLegacyLighting.From(Snapshot(Vector3.Zero, Vector3.One));
        Assert.Equal(solar, diffuse);
        Assert.InRange(solar.Daylight, .73f, .74f);
        var combined = AtmosphereLegacyLighting.From(Snapshot(new(MathF.PI), Vector3.One));
        Assert.True(combined.Daylight > solar.Daylight);
        Assert.InRange(combined.Daylight, 0, 1);
        Assert.Equal(combined, AtmosphereLegacyLighting.From(Snapshot(new(MathF.PI), Vector3.One)));
    }
    #endregion

    #region Private
    /// <summary>Creates a deterministic complete lighting value without GPU resources.</summary>
    private static AtmosphereLighting Snapshot(Vector3 solar, Vector3 environment) =>
        new(Vector3.UnitY, solar, environment, Vector3.Zero, Vector3.Zero, ImmutableArray<float>.Empty);
    #endregion
}
