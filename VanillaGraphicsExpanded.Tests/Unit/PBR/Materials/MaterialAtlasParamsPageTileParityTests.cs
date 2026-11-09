using System;
using System.Collections.Generic;
using System.Threading;
using VanillaGraphicsExpanded.PBR.Materials;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Compares whole-page and per-tile material parameter construction.</summary>
[Trait("Category", "Unit")]
public sealed class MaterialAtlasParamsPageTileParityTests
{
    #region Public API
    /// <summary>Whole-page building matches reversed-order per-tile writes and equivalent overrides.</summary>
    [Fact]
    public void MaterialParams_PageAndReverseOrderTiles_ProduceIdenticalAtlas_ForFixedSnapshot()
    {
        // Arrange: prepare a fixed atlas page with two disjoint material rectangles.
        const int atlasTexId = 101;
        const int atlasWidth = 8;
        const int atlasHeight = 4;
        var snapshot = new AtlasSnapshot(
            Pages: [new AtlasSnapshot.AtlasPage(AtlasTextureId: atlasTexId, Width: atlasWidth, Height: atlasHeight)],
            Positions: Array.Empty<TextureAtlasPosition?>(),
            ReloadIteration: 1,
            NonNullPositionCount: 2);
        var texA = new AssetLocation("game", "textures/block/a.png");
        var texB = new AssetLocation("game", "textures/block/b.png");
        var positions = new Dictionary<AssetLocation, TextureAtlasPosition>
        {
            [texA] = new() { atlasTextureId = atlasTexId, x1 = 0f, y1 = 0f, x2 = 0.5f, y2 = 1f },
            [texB] = new() { atlasTextureId = atlasTexId, x1 = 0.5f, y1 = 0f, x2 = 1f, y2 = 1f }
        };
        PbrMaterialDefinition defA = new(
            Roughness: 0.2f, Metallic: 0.3f, Emissive: 0.1f,
            Noise: new(0.15f, 0.05f, 0f, 0f, 0f),
            Scale: new(1f, 0.9f, 1f, 1f, 1f), Priority: 0, Notes: null);
        PbrMaterialDefinition defB = new(
            Roughness: 0.8f, Metallic: 0f, Emissive: 0f,
            Noise: new(0f, 0f, 0f, 0f, 0f),
            Scale: PbrOverrideScale.Identity, Priority: 0, Notes: null);
        var materials = new Dictionary<AssetLocation, PbrMaterialDefinition> { [texA] = defA, [texB] = defB };
        AtlasSnapshot.AtlasPage page = snapshot.Pages[0];
        float[] tiledAtlas = new float[checked(atlasWidth * atlasHeight * 3)];
        MaterialAtlasParamsBuilder.FillRgbTriplets(tiledAtlas, MaterialAtlasParamsBuilder.DefaultRoughness,
            MaterialAtlasParamsBuilder.DefaultMetallic, MaterialAtlasParamsBuilder.DefaultEmissive);

        // Act: build one whole page, then assemble the same materials from tiles in reverse completion order.
        var syncResult = MaterialAtlasParamsBuilder.BuildRgb16fPixelBuffers(
            [(page.AtlasTextureId, page.Width, page.Height)], positions, materials);
        float[] syncAtlas = syncResult.PixelBuffersByAtlasTexId[atlasTexId];
        BlitTile(tiledAtlas, atlasWidth, atlasHeight, 4, 0, 4, 4,
            MaterialAtlasParamsBuilder.BuildRgb16fTile(texB, defB, 4, 4, CancellationToken.None));
        BlitTile(tiledAtlas, atlasWidth, atlasHeight, 0, 0, 4, 4,
            MaterialAtlasParamsBuilder.BuildRgb16fTile(texA, defA, 4, 4, CancellationToken.None));
        var overrideScale = new PbrOverrideScale(0.8f, 1f, 1f, 1f, 1f);
        float[] overrideRgba01 = new float[checked(2 * 2 * 4)];
        for (int i = 0; i < overrideRgba01.Length; i += 4)
        {
            overrideRgba01[i] = 0.25f;
            overrideRgba01[i + 1] = 0.75f;
            overrideRgba01[i + 2] = 0.10f;
            overrideRgba01[i + 3] = 0f;
        }
        foreach (float[] atlas in new[] { syncAtlas, tiledAtlas })
        {
            MaterialAtlasParamsOverrideApplier.ApplyRgbOverride(atlas, atlasWidth, atlasHeight,
                0, 0, 2, 2, overrideRgba01, overrideScale);
        }

        // Assert: both construction paths produce the same final page payload.
        Assert.Equal(syncAtlas, tiledAtlas);
    }
    #endregion

    #region Private
    /// <summary>Copies a tightly packed RGB tile into its destination rectangle.</summary>
    private static void BlitTile(float[] atlas, int atlasWidth, int atlasHeight, int rectX, int rectY,
        int rectWidth, int rectHeight, float[] tile)
    {
        if (atlas is null) throw new ArgumentNullException(nameof(atlas));
        if (tile is null) throw new ArgumentNullException(nameof(tile));
        if (atlasWidth <= 0) throw new ArgumentOutOfRangeException(nameof(atlasWidth));
        if (atlasHeight <= 0) throw new ArgumentOutOfRangeException(nameof(atlasHeight));
        if (rectX < 0 || rectY < 0) throw new ArgumentOutOfRangeException("rect origin must be non-negative");
        if (rectWidth <= 0 || rectHeight <= 0) throw new ArgumentOutOfRangeException("rect size must be positive");
        if (rectX + rectWidth > atlasWidth || rectY + rectHeight > atlasHeight) throw new ArgumentOutOfRangeException("rect exceeds atlas bounds");
        int expectedAtlasFloats = checked(atlasWidth * atlasHeight * 3);
        if (atlas.Length != expectedAtlasFloats)
        {
            throw new ArgumentException($"atlasRgbTriplets length mismatch (expected={expectedAtlasFloats}, actual={atlas.Length}).", nameof(atlas));
        }
        int expectedTileFloats = checked(rectWidth * rectHeight * 3);
        if (tile.Length != expectedTileFloats)
        {
            throw new ArgumentException($"tileRgb length mismatch (expected={expectedTileFloats}, actual={tile.Length}).", nameof(tile));
        }
        int rowFloats = checked(rectWidth * 3);
        for (int y = 0; y < rectHeight; y++)
        {
            int destination = checked(((rectY + y) * atlasWidth + rectX) * 3);
            int source = checked(y * rowFloats);
            tile.AsSpan(source, rowFloats).CopyTo(atlas.AsSpan(destination, rowFloats));
        }
    }
    #endregion
}




