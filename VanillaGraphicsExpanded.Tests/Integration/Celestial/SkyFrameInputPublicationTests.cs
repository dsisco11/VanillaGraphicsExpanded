using System.Reflection;
using System.Runtime.CompilerServices;
using Moq;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.Integration.Celestial;

/// <summary>Checks shared sky frame inputs advance independently of atmospheric generation and dome submission.</summary>
public sealed class SkyFrameInputPublicationTests
{
    #region Public API
    /// <summary>Noise wraps at screen area and original lookup handles and calendar variation remain valid.</summary>
    [Fact]
    public void PublicationAdvancesOneSeedAndRetainsLegacyResourceConvention()
    {
        // Arrange: provide controlled calendar, frame seed, legacy texture IDs and shader uniforms.
        var game = (ClientMain)RuntimeHelpers.GetUninitializedObject(typeof(ClientMain));
        GC.SuppressFinalize(game);
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var calendarField = typeof(ClientMain).GetField("GameWorldCalendar", flags)!;
        var calendar = RuntimeHelpers.GetUninitializedObject(calendarField.FieldType);
        calendarField.SetValue(game, calendar);
        var sunset = calendarField.FieldType.GetProperty("SunsetMod")!.GetMethod!;
        var sunsetField = sunset.Module.ResolveField(BitConverter.ToInt32(sunset.GetMethodBody()!.GetILAsByteArray()!, 2))!;
        sunsetField.SetValue(calendar, .37f);
        typeof(ClientMain).GetField("frameSeed", flags)!.SetValue(game, 3);
        typeof(ClientMain).GetField("skyTextureId", flags)!.SetValue(game, 101);
        typeof(ClientMain).GetField("skyGlowTextureId", flags)!.SetValue(game, 202);
        var uniforms = new DefaultShaderUniforms();
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        api.SetupGet(value => value.World).Returns(game);
        api.SetupGet(value => value.Render.FrameWidth).Returns(2);
        api.SetupGet(value => value.Render.FrameHeight).Returns(2);
        api.SetupGet(value => value.Render.ShaderUniforms).Returns(uniforms);
        // Act: publish two consecutive frames through the shared sky-input boundary.
        EngineSkyFrameInputs.Publish(api.Object);
        // Assert: the first frame wraps the seed and retains the legacy resources and calendar input.
        Assert.Equal(0, uniforms.DitherSeed);
        Assert.Equal(0, typeof(ClientMain).GetField("frameSeed", flags)!.GetValue(game));
        Assert.Equal(101, uniforms.SkyTextureId);
        Assert.Equal(202, uniforms.GlowTextureId);
        Assert.Equal(.37f, uniforms.SunsetMod);
        // Act: publish again to verify frame-to-frame advancement.
        EngineSkyFrameInputs.Publish(api.Object);
        // Assert: both the published seed and the engine-owned seed advance once.
        Assert.Equal(1, uniforms.DitherSeed);
        Assert.Equal(1, typeof(ClientMain).GetField("frameSeed", flags)!.GetValue(game));
    }
    #endregion
}
