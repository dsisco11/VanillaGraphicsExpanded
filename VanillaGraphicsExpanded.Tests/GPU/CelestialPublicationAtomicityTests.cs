using VanillaGraphicsExpanded.Rendering.RHI;
using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises the exact frame publication policy with real texture uploads and deferred completed sources.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class CelestialPublicationAtomicityTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Context namespace withdrawal invalidates the complete shared publication and old pending result before reuse.</summary>
    [Fact]
    public void ContextGenerationWithdrawalClearsPendingAndPublishedBindings()
    {
        EnsureContextValid();
        using var owner=new AtmosphereModSystem();
        var lighting=new AtmosphereLighting(Vector3.UnitY,Vector3.Zero,Vector3.One,Vector3.Zero,Vector3.Zero,
            ImmutableArray.Create(1f,2f,3f,1f)){Width=1,Height=1};
        owner.Publish(lighting);
        var oldTransmission=AtmosphereModSystem.ExternalTransmissionTexture!;
        int oldSky=AtmosphereModSystem.SkyTextureId;
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        typeof(AtmosphereModSystem).GetField("pendingPublication",flags)!.SetValue(owner,lighting);
        var current=RenderContextRegistry.Current();
        typeof(AtmosphereModSystem).GetField("context",flags)!.SetValue(owner,(current.Handle,current.Generation-1));
        owner.PrepareContext();
        Assert.Null(AtmosphereModSystem.Lighting);
        Assert.Null(AtmosphereModSystem.ExternalTransmissionTexture);
        Assert.Null(AtmosphereModSystem.AerialRadianceTexture);
        Assert.Null(AtmosphereModSystem.AerialAttenuationTexture);
        Assert.Equal(0,AtmosphereModSystem.SkyTextureId);
        Assert.Equal(0,AtmosphereModSystem.AerialRadianceTextureId);
        Assert.Equal(0,AtmosphereModSystem.AerialAttenuationTextureId);
        Assert.Null(typeof(AtmosphereModSystem).GetField("pendingPublication",flags)!.GetValue(owner));
        Assert.False(oldTransmission.IsValid); Assert.False(GL.IsTexture(oldSky));
        owner.Publish(lighting);
        Assert.NotNull(AtmosphereModSystem.Lighting);
        Assert.True(AtmosphereModSystem.ExternalTransmissionTexture!.IsValid);
    }
    /// <summary>Failed atmospheric admission retains the complete visible source and light pair until a matching valid candidate commits.</summary>
    [Fact]
    public void FailedUploadRetainsSourceLightingRotationAndPendingCandidate()
    {
        EnsureContextValid();
        using var assets=new BinaryShaderApiFixture();
        using var owner=new AtmosphereModSystem();
        using var lifetime=new CelestialSourceLifetime(assets.Api);
        var ownerType=typeof(AtmosphereModSystem);
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var pending=ownerType.GetField("pendingPublication",flags)!;
        ownerType.GetField("celestial",flags)!.SetValue(owner,lifetime);
        var readyProperty=typeof(CelestialSourceLifetime).GetProperty("Ready",flags)!;
        var source=CelestialSource.Create(new(0,32,1024,4));
        var first=new CelestialResourceSet(source,RHICommandContext.Current.CreateTextureCube(new RHITextureDescription { Dimension = RHITextureDimension.Cube, Format = RHIFormat.R11fG11fB10f, Width = 32, Height = 32, MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource());
        readyProperty.SetValue(lifetime,first);
        var table=ImmutableArray.CreateRange(Enumerable.Range(0,512).SelectMany(_=>new[]{1f,1f,1f,1f}));
        var lighting=new AtmosphereLighting(-Vector3.UnitY,Vector3.Zero,new(.0001f),Vector3.Zero,Vector3.Zero,
            ImmutableArray.Create(.0001f,.0002f,.0003f,1f))
            {Width=1,Height=1,ExternalTransmission=table,CelestialSource=source,CelestialRotation=Matrix4x4.Identity};
        pending.SetValue(owner,lighting); owner.PublishPending();
        Assert.Same(first,owner.CelestialSource); Assert.Null(lifetime.Ready); Assert.Null(pending.GetValue(owner));
        var previous=AtmosphereModSystem.Lighting;
        int previousSky=AtmosphereModSystem.SkyTextureId;
        var previousTransmission=AtmosphereModSystem.ExternalTransmissionTexture;
        var nextSource=source with{GalaxyAmplitude=source.GalaxyAmplitude*2};
        var next=new CelestialResourceSet(nextSource,RHICommandContext.Current.CreateTextureCube(new RHITextureDescription { Dimension = RHITextureDimension.Cube, Format = RHIFormat.R11fG11fB10f, Width = 32, Height = 32, MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource());
        readyProperty.SetValue(lifetime,next);
        var candidate=lighting with{CelestialSource=nextSource,CelestialRotation=Matrix4x4.CreateRotationX(.4f)};
        var invalid=candidate with{ExternalTransmission=table.SetItem(0,float.NaN)};
        pending.SetValue(owner,invalid);
        Assert.Throws<ArgumentException>(owner.PublishPending);
        Assert.Same(previous,AtmosphereModSystem.Lighting);
        Assert.Same(first,owner.CelestialSource); Assert.Same(next,lifetime.Ready);
        Assert.Same(invalid,pending.GetValue(owner));
        Assert.Equal(previousSky,AtmosphereModSystem.SkyTextureId);
        Assert.Same(previousTransmission,AtmosphereModSystem.ExternalTransmissionTexture);
        Assert.True(first.Galaxy.IsValid); Assert.True(next.Galaxy.IsValid);
        // Correct the rejected payload explicitly; it cannot become publishable through
        // clamping or by committing the source independently of atmosphere admission.
        pending.SetValue(owner,candidate); owner.PublishPending();
        Assert.Same(next,owner.CelestialSource); Assert.Null(lifetime.Ready); Assert.Null(pending.GetValue(owner));
        Assert.Same(nextSource,AtmosphereModSystem.Lighting.CelestialSource);
        Assert.Equal(candidate.CelestialRotation,AtmosphereModSystem.Lighting.CelestialRotation);
        Assert.False(first.Galaxy.IsValid);
        previous=AtmosphereModSystem.Lighting; previousSky=AtmosphereModSystem.SkyTextureId;
        pending.SetValue(owner,candidate with{CelestialSource=nextSource with{GalaxyAmplitude=0}});
        owner.PublishPending();
        Assert.Null(pending.GetValue(owner)); Assert.Same(previous,AtmosphereModSystem.Lighting);
        Assert.Equal(previousSky,AtmosphereModSystem.SkyTextureId); Assert.Same(next,owner.CelestialSource);
    }
    #endregion
}
