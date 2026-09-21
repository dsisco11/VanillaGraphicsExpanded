using System.Numerics;
using System.Reflection;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.WorldProbes;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;

/// <summary>Controls material readiness while restoring the original singleton data after an exclusive test.</summary>
internal sealed class ScopedPbrMaterialFixture : IDisposable
{
    private readonly PbrMaterialRegistry registry = PbrMaterialRegistry.Instance;
    private readonly FieldInfo derivedField = typeof(PbrMaterialRegistry).GetField("derivedSurfaceByBlockFace", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private readonly Dictionary<AssetLocation, PbrMaterialSurface> surfaces;
    private readonly DerivedSurface[] originalDerived;
    private readonly AssetLocation texture = new("vgetest", "textures/block/local_capture_readiness");
    private readonly bool hadSurface;
    private readonly PbrMaterialSurface originalSurface;
    public Block Cube { get; }

    #region Scoped State
    /// <summary>Creates a textured opaque cube and saves the exact state that the fixture will replace.</summary>
    public ScopedPbrMaterialFixture()
    {
        surfaces = (Dictionary<AssetLocation, PbrMaterialSurface>)registry.SurfaceByTexture;
        hadSurface = surfaces.TryGetValue(texture, out originalSurface);
        originalDerived = (DerivedSurface[])derivedField.GetValue(registry)!;
        Cube = new Block
        {
            BlockId = 7, Code = new AssetLocation("vgetest", "local_capture_cube"),
            CollisionBoxes = Block.DefaultCollisionSelectionBoxes,
            Textures = new Dictionary<string, CompositeTexture> { ["all"] = new CompositeTexture(texture) }
        };
    }

    /// <summary>Publishes or withholds the surface and derived lookup independently, using the production lookup builder.</summary>
    public void SetReadiness(bool surfaceReady, bool derivedReady)
    {
        var surface = new PbrMaterialSurface(0.5f, 0f, 0f, Vector3.One, new Vector3(0.04f));
        if (surfaceReady) surfaces[texture] = surface;
        else surfaces.Remove(texture);
        // Build complete face data from the same material even when only its surface publication is withheld.
        var source = new Dictionary<AssetLocation, PbrMaterialSurface> { [texture] = surface };
        var derived = derivedReady ? BlockFaceDerivedSurfaceLookupBuilder.Build([Cube], source, out _) : Array.Empty<DerivedSurface>();
        derivedField.SetValue(registry, derived);
    }

    /// <summary>Restores original objects and the one surface entry without clearing unrelated global state.</summary>
    public void Dispose()
    {
        derivedField.SetValue(registry, originalDerived);
        if (hadSurface) surfaces[texture] = originalSurface;
        else surfaces.Remove(texture);
    }
    #endregion
}