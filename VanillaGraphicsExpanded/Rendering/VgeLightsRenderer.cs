using System;
using Vintagestory.API.Client;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering.Integration.Shadows;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Publishes one engine dynamic-light snapshot after collection and held attachment resolution.</summary>
internal sealed class VgeLightsRenderer : IRenderer
{
    private static VgeLightsRenderer? active;
    private readonly ICoreClientAPI api;
    private readonly VgeLightsUniformBuffer inputs = new();
    private bool published, disposed;
    private long frame;
    private int previousUnknown = -1;
    /// <summary>Retains both celestial emitters and the exact local collection coordinate convention.</summary>
    internal static ShadowLightSnapshot? ShadowInputs => active is { published: true } owner ? owner.snapshot : null;
    private ShadowLightSnapshot? snapshot;

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
        api.Event.LeaveWorld += NativeShadowLightSources.Clear;
    }

    /// <summary>Copies the completed source list once; all consumers reuse the same retained publication.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (disposed) return;
        if (stage == EnumRenderStage.Before) { Reset(); return; }
        if (stage != EnumRenderStage.Opaque || published) return;
        var source = api.Render.ShaderUniforms;
        // Native collection identity is available only from its owning client world.
        // Other API world providers still publish illumination, with unknown shadow identity.
        var captured = api.World is ClientMain game
            ? NativeShadowLightSources.For(game).Capture(source.PointLightsCount) : default;
        inputs.Capture(source.PointLightsCount, source.PointLights3, source.PointLightColors3, captured.Keys.Span);
        var atmosphere = ModSystems.AtmosphereModSystem.Lighting;
        var origin = captured.Origin;
        if (!captured.Complete || !ValidOrigin(origin.X) || !ValidOrigin(origin.Y) || !ValidOrigin(origin.Z))
        {
            snapshot = null;
            published = true;
            if (previousUnknown != source.PointLightsCount + 1)
                api.Logger.Warning("[VGE Shadows] Native collection coordinates unavailable; illumination retained without shadow inputs.");
            previousUnknown = source.PointLightsCount + 1;
            return;
        }
        var split = FrameWorldSpaceBridge.Compute(origin.X, origin.Y, origin.Z);
        int dimension = captured.Dimension;
        snapshot = new(checked(++frame), captured.World, dimension, inputs.Lights,
            new(new(captured.World, dimension, ShadowSourceKind.Sun, 0, 1, 0), atmosphere?.Sun ?? System.Numerics.Vector3.UnitY, atmosphere?.Solar ?? default),
            new(new(captured.World, dimension, ShadowSourceKind.Moon, 0, 1, 1), atmosphere?.Moon ?? System.Numerics.Vector3.UnitY, atmosphere?.Lunar ?? default),
            ImmutableArray.Create(captured.View.ToArray()), split.ChunkOffset.X, split.ChunkOffset.Y, split.ChunkOffset.Z,
            new((float)split.BlockOffsetRemainder.X, (float)split.BlockOffsetRemainder.Y, (float)split.BlockOffsetRemainder.Z));
        if (snapshot.UnknownSources != previousUnknown)
        {
            previousUnknown = snapshot.UnknownSources;
            if (previousUnknown != 0) api.Logger.Warning("[VGE Shadows] {0} illuminated sources lack stable identity; cached shadows unavailable.", previousUnknown);
        }
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
        api.Event.LeaveWorld -= NativeShadowLightSources.Clear;
        Reset();
        inputs.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Prevents a light generation from surviving into another frame or world.</summary>
    private void Reset() { published = false; snapshot = null; }

    /// <summary>Rejects origins outside the integer grid instead of allowing float/integer overflow.</summary>
    private static bool ValidOrigin(double coordinate) => double.IsFinite(coordinate)
        && Math.Floor(coordinate / 32) >= int.MinValue && Math.Floor(coordinate / 32) <= int.MaxValue;
    #endregion
}
