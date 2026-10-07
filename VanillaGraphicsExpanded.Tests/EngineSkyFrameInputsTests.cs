using System.Reflection;
using System.Runtime.CompilerServices;
using Moq;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks shared sky frame inputs advance independently of atmospheric generation and dome submission.</summary>
public sealed class EngineSkyFrameInputsTests
{
    #region Public API
    /// <summary>Noise wraps at screen area and original lookup handles and calendar variation remain valid.</summary>
    [Fact]
    public void PublicationAdvancesOneSeedAndRetainsLegacyResourceConvention()
    {
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
        EngineSkyFrameInputs.Publish(api.Object);
        Assert.Equal(0, uniforms.DitherSeed);
        Assert.Equal(0, typeof(ClientMain).GetField("frameSeed", flags)!.GetValue(game));
        Assert.Equal(101, uniforms.SkyTextureId);
        Assert.Equal(202, uniforms.GlowTextureId);
        Assert.Equal(.37f, uniforms.SunsetMod);
        EngineSkyFrameInputs.Publish(api.Object);
        Assert.Equal(1, uniforms.DitherSeed);
        Assert.Equal(1, typeof(ClientMain).GetField("frameSeed", flags)!.GetValue(game));
    }
    #endregion
}
