using System;
using System.Collections.Generic;
using System.Threading;

using VanillaGraphicsExpanded.PBR.Materials;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

[Trait("Category", "Unit")]
public sealed class MaterialAtlasParamsSyncAsyncParityTests
{
    [Fact]
    public void MaterialParams_SyncVsAsyncTileBuild_ProducesIdenticalAtlas_ForFixedSnapshot()
    {
        const int atlasTexId = 101;
        const int atlasWidth = 8;
        const int atlasHeight = 4;

        // Fixed atlas snapshot (one page, two disjoint rects).
        var snapshot = new AtlasSnapshot(
            Pages: new[] { new AtlasSnapshot.AtlasPage(AtlasTextureId: atlasTexId, Width: atlasWidth, Height: atlasHeight) },
            Positions: Array.Empty<TextureAtlasPosition?>(),
            ReloadIteration: 1,
            NonNullPositionCount: 2);

        var texA = new AssetLocation("game", "textures/block/a.png");
        var texB = new AssetLocation("game", "textures/block/b.png");

        var posA = new TextureAtlasPosition { atlasTextureId = atlasTexId, x1 = 0f, y1 = 0f, x2 = 0.5f, y2 = 1f };
        var posB = new TextureAtlasPosition { atlasTextureId = atlasTexId, x1 = 0.5f, y1 = 0f, x2 = 1f, y2 = 1f };

        // Note: BuildRgb16fPixelBuffers expects positions keyed by AssetLocation.
        var positions = new Dictionary<AssetLocation, TextureAtlasPosition>
        {
            [texA] = posA,
            [texB] = posB
        };

        PbrMaterialDefinition defA = new(
            Roughness: 0.2f,
            Metallic: 0.3f,
            Emissive: 0.1f,
            Noise: new PbrMaterialNoise(Roughness: 0.15f, Metallic: 0.05f, Emissive: 0.0f, Reflectivity: 0f, Normals: 0f),
            Scale: new PbrOverrideScale(Roughness: 1.0f, Metallic: 0.9f, Emissive: 1.0f, Normal: 1f, Depth: 1f),
            Priority: 0,
            Notes: null);

        PbrMaterialDefinition defB = new(
            Roughness: 0.8f,
            Metallic: 0.0f,
            Emissive: 0.0f,
            Noise: new PbrMaterialNoise(Roughness: 0.0f, Metallic: 0.0f, Emissive: 0.0f, Reflectivity: 0f, Normals: 0f),
            Scale: PbrOverrideScale.Identity,
            Priority: 0,
            Notes: null);

        var materials = new Dictionary<AssetLocation, PbrMaterialDefinition>
        {
            [texA] = defA,
            [texB] = defB
        };

        AtlasSnapshot.AtlasPage page = snapshot.Pages[0];

        // Sync-style: build entire page buffer.
        MaterialAtlasParamsBuilder.Result syncResult = MaterialAtlasParamsBuilder.BuildRgb16fPixelBuffers(
            atlasPages: new[] { (page.AtlasTextureId, page.Width, page.Height) },
            texturePositions: positions,
            materialsByTexture: materials);

        float[] syncAtlas = syncResult.PixelBuffersByAtlasTexId[atlasTexId];

        // Async-style: build per-tile and blit into an initially-default atlas.
        float[] asyncAtlas = new float[checked(atlasWidth * atlasHeight * 3)];
        MaterialAtlasParamsBuilder.FillRgbTriplets(asyncAtlas, MaterialAtlasParamsBuilder.DefaultRoughness, MaterialAtlasParamsBuilder.DefaultMetallic, MaterialAtlasParamsBuilder.DefaultEmissive);

        // Simulate different job completion order (B then A).
        BlitTile(asyncAtlas, atlasWidth, atlasHeight, rectX: 4, rectY: 0, rectWidth: 4, rectHeight: 4, tileRgb: MaterialAtlasParamsBuilder.BuildRgb16fTile(texB, defB, 4, 4, CancellationToken.None));
        BlitTile(asyncAtlas, atlasWidth, atlasHeight, rectX: 0, rectY: 0, rectWidth: 4, rectHeight: 4, tileRgb: MaterialAtlasParamsBuilder.BuildRgb16fTile(texA, defA, 4, 4, CancellationToken.None));

        // Apply an override to a fixed rect inside texA (top-left 2x2).
        var overrideScale = new PbrOverrideScale(Roughness: 0.8f, Metallic: 1.0f, Emissive: 1.0f, Normal: 1f, Depth: 1f);
        float[] overrideRgba01 = new float[checked(2 * 2 * 4)];
        for (int i = 0; i < overrideRgba01.Length; i += 4)
        {
            // roughness, metallic, emissive, alpha (ignored)
            overrideRgba01[i + 0] = 0.25f;
            overrideRgba01[i + 1] = 0.75f;
            overrideRgba01[i + 2] = 0.10f;
            overrideRgba01[i + 3] = 0.00f;
        }

        MaterialAtlasParamsOverrideApplier.ApplyRgbOverride(
            atlasRgbTriplets: syncAtlas,
            atlasWidth: atlasWidth,
            atlasHeight: atlasHeight,
            rectX: 0,
            rectY: 0,
            rectWidth: 2,
            rectHeight: 2,
            overrideRgba01: overrideRgba01,
            scale: overrideScale);

        MaterialAtlasParamsOverrideApplier.ApplyRgbOverride(
            atlasRgbTriplets: asyncAtlas,
            atlasWidth: atlasWidth,
            atlasHeight: atlasHeight,
            rectX: 0,
            rectY: 0,
            rectWidth: 2,
            rectHeight: 2,
            overrideRgba01: overrideRgba01,
            scale: overrideScale);

        Assert.Equal(syncAtlas, asyncAtlas);
    }

    private static void BlitTile(
        float[] atlasRgbTriplets,
        int atlasWidth,
        int atlasHeight,
        int rectX,
        int rectY,
        int rectWidth,
        int rectHeight,
        float[] tileRgb)
    {
        if (atlasRgbTriplets is null) throw new ArgumentNullException(nameof(atlasRgbTriplets));
        if (tileRgb is null) throw new ArgumentNullException(nameof(tileRgb));
        if (atlasWidth <= 0) throw new ArgumentOutOfRangeException(nameof(atlasWidth));
        if (atlasHeight <= 0) throw new ArgumentOutOfRangeException(nameof(atlasHeight));
        if (rectX < 0 || rectY < 0) throw new ArgumentOutOfRangeException("rect origin must be non-negative");
        if (rectWidth <= 0 || rectHeight <= 0) throw new ArgumentOutOfRangeException("rect size must be positive");
        if (rectX + rectWidth > atlasWidth || rectY + rectHeight > atlasHeight) throw new ArgumentOutOfRangeException("rect exceeds atlas bounds");

        int expectedAtlasFloats = checked(atlasWidth * atlasHeight * 3);
        if (atlasRgbTriplets.Length != expectedAtlasFloats)
        {
            throw new ArgumentException($"atlasRgbTriplets length mismatch (expected={expectedAtlasFloats}, actual={atlasRgbTriplets.Length}).", nameof(atlasRgbTriplets));
        }

        int expectedTileFloats = checked(rectWidth * rectHeight * 3);
        if (tileRgb.Length != expectedTileFloats)
        {
            throw new ArgumentException($"tileRgb length mismatch (expected={expectedTileFloats}, actual={tileRgb.Length}).", nameof(tileRgb));
        }

        int tileRowFloats = checked(rectWidth * 3);

        for (int y = 0; y < rectHeight; y++)
        {
            int dstRow = checked(((rectY + y) * atlasWidth + rectX) * 3);
            int srcRow = checked((y * rectWidth) * 3);

            tileRgb.AsSpan(srcRow, tileRowFloats).CopyTo(atlasRgbTriplets.AsSpan(dstRow, tileRowFloats));
        }
    }
}
