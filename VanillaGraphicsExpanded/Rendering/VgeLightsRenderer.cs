using System;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Publishes one engine dynamic-light snapshot after collection and held attachment resolution.</summary>
internal sealed class VgeLightsRenderer : IRenderer
{
    private static VgeLightsRenderer? active;
    private readonly ICoreClientAPI api;
    private readonly VgeLightsUniformBuffer inputs = new();
    private bool published, disposed;

    #region Public API
    /// <summary>Runs after camera publication and before the first world shading consumer.</summary>
    public double RenderOrder => -10000;
    /// <summary>Light publication is independent of visible chunk range.</summary>
    public int RenderRange => int.MaxValue;
    /// <summary>Rejects access outside the current published world frame.</summary>
    internal static VgeLightsUniformBuffer Current => active is { published: true, disposed: false } owner
        ? owner.inputs : throw new InvalidOperationException("The shared light list has not been published.");

    /// <summary>Registers independent light ownership without depending on LumOn or a particular shading pass.</summary>
    internal VgeLightsRenderer(ICoreClientAPI api)
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
        active = this;
        api.Event.RegisterRenderer(this, EnumRenderStage.Before, "vge_lights_reset");
        api.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "vge_lights_snapshot");
        api.Event.LeaveWorld += Reset;
    }

    /// <summary>Copies the completed source list once; all consumers reuse the same retained publication.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (disposed) return;
        if (stage == EnumRenderStage.Before) { Reset(); return; }
        if (stage != EnumRenderStage.Opaque || published) return;
        var source = api.Render.ShaderUniforms;
        inputs.Capture(source.PointLightsCount, source.PointLights3, source.PointLightColors3);
        published = true;
    }

    /// <summary>Withdraws publication before retiring its storage and registered callbacks.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (ReferenceEquals(active, this)) active = null;
        api.Event.UnregisterRenderer(this, EnumRenderStage.Before);
        api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        api.Event.LeaveWorld -= Reset;
        Reset();
        inputs.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Prevents a light generation from surviving into another frame or world.</summary>
    private void Reset() => published = false;
    #endregion
}
