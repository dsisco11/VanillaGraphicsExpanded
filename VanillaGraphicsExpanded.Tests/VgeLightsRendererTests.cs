using Moq;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;
using Xunit;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks publication ordering and lifetime independently of LumOn and graphics context availability.</summary>
[Collection("GPU")]
public sealed class VgeLightsRendererTests
{
    #region Public API
    /// <summary>Completed Before-stage positions publish once and remain immutable until the next frame.</summary>
    [Fact]
    public void PublishesAfterCollectionAndWithdrawsAtFrameAndWorldBoundaries()
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var source = new DefaultShaderUniforms { PointLightsCount = 1 };
        source.PointLights3[0] = 3; source.PointLightColors3[0] = 4;
        api.SetupGet(a => a.Render.ShaderUniforms).Returns(source);
        using var owner = new VgeLightsRenderer(api.Object);
        Assert.Equal(-10000, owner.RenderOrder);
        Assert.Throws<InvalidOperationException>(() => VgeLightsRenderer.Current);
        owner.OnRenderFrame(.016f, EnumRenderStage.Before);
        // Simulate the attachment resolver's final change after collection; capture cannot run during Before.
        source.PointLights3[0] = -7;
        Assert.Throws<InvalidOperationException>(() => VgeLightsRenderer.Current);
        owner.OnRenderFrame(.016f, EnumRenderStage.Opaque);
        byte[] first = VgeLightsRenderer.Current.Bytes.ToArray();
        Assert.Equal(-7f, MemoryMarshal.Read<float>(first.AsSpan(16)));
        source.PointLights3[0] = 19; source.PointLightColors3[0] = 23;
        owner.OnRenderFrame(.016f, EnumRenderStage.Opaque);
        Assert.Equal(first, VgeLightsRenderer.Current.Bytes.ToArray());
        owner.OnRenderFrame(.016f, EnumRenderStage.Before);
        Assert.Throws<InvalidOperationException>(() => VgeLightsRenderer.Current);
        owner.OnRenderFrame(.016f, EnumRenderStage.Opaque);
        Assert.Equal(19f, MemoryMarshal.Read<float>(VgeLightsRenderer.Current.Bytes.Slice(16)));
        Assert.Equal(23f, MemoryMarshal.Read<float>(VgeLightsRenderer.Current.Bytes.Slice(1616)));
        Mock.Get(api.Object.Event).Raise(e => e.LeaveWorld += null);
        Assert.Throws<InvalidOperationException>(() => VgeLightsRenderer.Current);
        owner.Dispose();
        owner.OnRenderFrame(.016f, EnumRenderStage.Opaque);
        Assert.Throws<InvalidOperationException>(() => VgeLightsRenderer.Current);
        api.Verify(a => a.Event.UnregisterRenderer(owner, EnumRenderStage.Before), Times.Once);
        api.Verify(a => a.Event.UnregisterRenderer(owner, EnumRenderStage.Opaque), Times.Once);
    }
    #endregion
}
