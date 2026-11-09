using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using Moq;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VanillaGraphicsExpanded.Tests.Component.Atmosphere;

/// <summary>Checks physical lighting projection and reversible legacy ambient publication.</summary>
public sealed class AtmosphereAmbientPublicationTests
{
    #region Public API
    /// <summary>Republication replaces one ordered slot and restores it without disturbing weather.</summary>
    [Fact]
    public void PublicationPreservesOrderAndRestoresOriginalModifier()
    {
        // Arrange ordered original-sunglow and weather modifiers behind a mocked client API.
        var original = new AmbientModifier().EnsurePopulated();
        var weather = new AmbientModifier().EnsurePopulated();
        var modifiers = new Vintagestory.API.Datastructures.OrderedDictionary<string, AmbientModifier> { ["sunglow"] = original, ["weather"] = weather };
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        api.SetupGet(value => value.Ambient.CurrentModifiers).Returns(modifiers);
        api.SetupGet(value => value.Render.ShaderUniforms).Returns(new DefaultShaderUniforms());
        using var publication = new AtmosphereAmbientPublication(api.Object);
        // Act by publishing twice and disposing; assert slot order/weather preservation and restoration of the original modifier.
        Assert.True(publication.Publish(Snapshot(Vector3.One, Vector3.One)));
        var owned = modifiers["sunglow"];
        Assert.NotSame(original, owned);
        Assert.True(publication.Publish(Snapshot(Vector3.Zero, Vector3.Zero)));
        Assert.Same(owned, modifiers["sunglow"]);
        Assert.Equal(new[] { "sunglow", "weather" }, modifiers.Keys);
        Assert.Same(weather, modifiers["weather"]);
        Assert.Equal(0, owned.AmbientColor.Value[0]);
        publication.Dispose();
        Assert.Same(original, modifiers["sunglow"]);
        Assert.Same(weather, modifiers["weather"]);
    }

    /// <summary>Foreign ownership changes are neither overwritten nor removed on teardown.</summary>
    [Fact]
    public void ForeignReplacementRejectsPublicationAndSurvivesDisposal()
    {
        // Arrange an empty ordered modifier collection and mocked API for publication ownership checks.
        var modifiers = new Vintagestory.API.Datastructures.OrderedDictionary<string, AmbientModifier>();
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        api.SetupGet(value => value.Ambient.CurrentModifiers).Returns(modifiers);
        api.SetupGet(value => value.Render.ShaderUniforms).Returns(new DefaultShaderUniforms());
        using var publication = new AtmosphereAmbientPublication(api.Object);
        // Act through initial, owned, and foreign replacement states; assert publication and teardown preserve foreign ownership.
        Assert.False(publication.Publish(Snapshot(Vector3.One, Vector3.One)));
        modifiers["sunglow"] = new AmbientModifier().EnsurePopulated();
        Assert.True(publication.Publish(Snapshot(Vector3.One, Vector3.One)));
        var foreign = new AmbientModifier().EnsurePopulated();
        modifiers["sunglow"] = foreign;
        Assert.False(publication.Publish(Snapshot(Vector3.Zero, Vector3.Zero)));
        Assert.Equal(0f, (float)typeof(DefaultShaderUniforms).GetField("SkyDaylight",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(api.Object.Render.ShaderUniforms)!);
        Assert.Same(foreign, modifiers["sunglow"]);
        publication.Dispose();
        Assert.Same(foreign, modifiers["sunglow"]);
        Assert.False(publication.Publish(Snapshot(Vector3.One, Vector3.One)));
        Assert.Same(foreign, modifiers["sunglow"]);
    }
    #endregion

    #region Private
    /// <summary>Creates a deterministic complete lighting value without GPU resources.</summary>
    private static AtmosphereLighting Snapshot(Vector3 solar, Vector3 environment) =>
        new(Vector3.UnitY, solar, environment, Vector3.Zero, Vector3.Zero, ImmutableArray<float>.Empty);
    #endregion
}
