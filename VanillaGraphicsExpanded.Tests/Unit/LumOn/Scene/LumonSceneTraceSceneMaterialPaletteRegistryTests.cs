using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.Serialization;

using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.PBR.Materials;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

public sealed class LumonSceneTraceSceneMaterialPaletteRegistryTests
{
    [Fact]
    public void SurfaceLut_NormalizesTextureKeys_ToBeExtensionlessAndTexturesPrefixed()
    {
        PbrMaterialRegistry.Instance.Clear();

        try
        {
            AssetLocation normalizedKey = new("game", "textures/block/test_norm");

            var surface = new PbrMaterialSurface(
                Roughness: 0.1f,
                Metallic: 0f,
                Emissive: 0f,
                DiffuseAlbedo: new Vector3(0.1f, 0.2f, 0.3f),
                SpecularF0: new Vector3(0.04f));

            var surfaceMap = (Dictionary<AssetLocation, PbrMaterialSurface>)PbrMaterialRegistry.Instance.SurfaceByTexture;
            surfaceMap[normalizedKey] = surface;

            var surfaceLut = new LumonScenePbrSurfaceLutRegistry();

            ushort idA = surfaceLut.GetOrAssignSurfaceId(new AssetLocation("GAME", "block/test_norm.png"));
            ushort idB = surfaceLut.GetOrAssignSurfaceId(new AssetLocation("game", "textures/block/test_norm"));

            Assert.Equal(idA, idB);

            uint[] surfaceBuf = new uint[LumonScenePbrSurfaceLutRegistry.MaxSurfaceEntries * 4];
            Assert.True(surfaceLut.TryCopyAndClearDirtyLut(surfaceBuf));

            int o = idA * 4;
            Assert.Equal(26u, surfaceBuf[o + 0]);  // 0.1 * 255
            Assert.Equal(51u, surfaceBuf[o + 1]);  // 0.2 * 255
            Assert.Equal(77u, surfaceBuf[o + 2]);  // 0.3 * 255
            Assert.Equal(26u, surfaceBuf[o + 3]);  // 0.1 * 255
        }
        finally
        {
            PbrMaterialRegistry.Instance.Clear();
        }
    }

    [Fact]
    public void EnsureEntryForBlockId_WritesFaceSurfaceIds_AndSurfaceLutHasPbrValues()
    {
        PbrMaterialRegistry.Instance.Clear();

        try
        {
            const int blockId = 5;
            const int paletteIndex = 123;

            Block block = CreateBlockWithTexture(faceKey: "up", textureDomain: "game", texturePath: "block/test_albedo");

            AssetLocation surfaceKey = NormalizeTextureKeyForTest(new AssetLocation("game", "block/test_albedo.png"));
            var surface = new PbrMaterialSurface(
                Roughness: 0.5f,
                Metallic: 0f,
                Emissive: 0f,
                DiffuseAlbedo: new Vector3(0.2f, 0.4f, 0.6f),
                SpecularF0: new Vector3(0.04f));

            // Populate the registry surface map directly (test seam).
            var surfaceMap = (Dictionary<AssetLocation, PbrMaterialSurface>)PbrMaterialRegistry.Instance.SurfaceByTexture;
            surfaceMap[surfaceKey] = surface;

            System.Func<int, Block?> getBlock = id => id == blockId ? block : null;
            IClientWorldAccessor world = FunctionalWorldProxy.Create(getBlock);
            ICoreClientAPI capi = FunctionalCapiProxy.Create(world);

            var surfaceLut = new LumonScenePbrSurfaceLutRegistry();
            var palette = new LumonSceneTraceSceneMaterialPaletteRegistry(surfaceLut);

            _ = surfaceLut.TryCopyAndClearDirtyLut(new uint[LumonScenePbrSurfaceLutRegistry.MaxSurfaceEntries * 4]);
            _ = palette.TryCopyAndClearDirtyPalette(new uint[LumonSceneOccupancyClipmapGpuResources.MaxMaterialPaletteEntries * 4]);

            palette.EnsureEntryForBlockId(capi, blockId, paletteIndex);

            uint[] paletteBuf = new uint[LumonSceneOccupancyClipmapGpuResources.MaxMaterialPaletteEntries * 4];
            Assert.True(palette.TryCopyAndClearDirtyPalette(paletteBuf));

            int o = paletteIndex * 4;

            ushort face0 = (ushort)(paletteBuf[o + 0] & 0xFFFFu);
            ushort face1 = (ushort)((paletteBuf[o + 0] >> 16) & 0xFFFFu);
            ushort face2 = (ushort)(paletteBuf[o + 1] & 0xFFFFu);
            ushort face3 = (ushort)((paletteBuf[o + 1] >> 16) & 0xFFFFu);
            ushort face4 = (ushort)(paletteBuf[o + 2] & 0xFFFFu);
            ushort face5 = (ushort)((paletteBuf[o + 2] >> 16) & 0xFFFFu);

            // With only one declared texture ("up"), BlockFaceTextureKeyResolver falls back to the first texture for all faces.
            Assert.Equal((ushort)1, face0);
            Assert.Equal((ushort)1, face1);
            Assert.Equal((ushort)1, face2);
            Assert.Equal((ushort)1, face3);
            Assert.Equal((ushort)1, face4);
            Assert.Equal((ushort)1, face5);

            uint[] surfaceBuf = new uint[LumonScenePbrSurfaceLutRegistry.MaxSurfaceEntries * 4];
            Assert.True(surfaceLut.TryCopyAndClearDirtyLut(surfaceBuf));

            int so = 1 * 4;
            Assert.Equal(51u, surfaceBuf[so + 0]);  // 0.2 * 255
            Assert.Equal(102u, surfaceBuf[so + 1]); // 0.4 * 255
            Assert.Equal(153u, surfaceBuf[so + 2]); // 0.6 * 255
            Assert.Equal(128u, surfaceBuf[so + 3]); // 0.5 * 255
        }
        finally
        {
            PbrMaterialRegistry.Instance.Clear();
        }
    }

    [Fact]
    public void SurfaceLut_UpgradesDefaultEntryOncePbrSurfaceBecomesAvailable()
    {
        PbrMaterialRegistry.Instance.Clear();

        try
        {
            const int blockId = 6;
            const int paletteIndex = 321;

            Block block = CreateBlockWithTexture(faceKey: "up", textureDomain: "game", texturePath: "block/test2");

            // Use registry normalization so the key shape matches production (textures/ prefix + extensionless).
            AssetLocation surfaceKey = NormalizeTextureKeyForTest(new AssetLocation("game", "block/test2.png"));
            var surface = new PbrMaterialSurface(
                Roughness: 0.25f,
                Metallic: 0f,
                Emissive: 0f,
                DiffuseAlbedo: new Vector3(1f, 0f, 0f),
                SpecularF0: new Vector3(0.04f));

            var surfaceMap = (Dictionary<AssetLocation, PbrMaterialSurface>)PbrMaterialRegistry.Instance.SurfaceByTexture;

            System.Func<int, Block?> getBlock = id => id == blockId ? block : null;
            IClientWorldAccessor world = FunctionalWorldProxy.Create(getBlock);
            ICoreClientAPI capi = FunctionalCapiProxy.Create(world);

            var surfaceLut = new LumonScenePbrSurfaceLutRegistry();
            var palette = new LumonSceneTraceSceneMaterialPaletteRegistry(surfaceLut);

            _ = surfaceLut.TryCopyAndClearDirtyLut(new uint[LumonScenePbrSurfaceLutRegistry.MaxSurfaceEntries * 4]);
            _ = palette.TryCopyAndClearDirtyPalette(new uint[LumonSceneOccupancyClipmapGpuResources.MaxMaterialPaletteEntries * 4]);

            // PBR surface missing initially -> surfaceId 1 is assigned but uses default payload.
            palette.EnsureEntryForBlockId(capi, blockId, paletteIndex);

            uint[] surf1 = new uint[LumonScenePbrSurfaceLutRegistry.MaxSurfaceEntries * 4];
            Assert.True(surfaceLut.TryCopyAndClearDirtyLut(surf1));

            int so = 1 * 4;
            Assert.Equal(140u, surf1[so + 0]); // default 0.55 * 255
            Assert.Equal(140u, surf1[so + 1]);
            Assert.Equal(140u, surf1[so + 2]);
            Assert.Equal(217u, surf1[so + 3]); // default 0.85 * 255

            // Now enable PBR resolution and ensure the LUT entry upgrades.
            surfaceMap[surfaceKey] = surface;

            int upgraded = surfaceLut.UpgradeUnresolvedEntries(maxToUpgrade: 8);
            Assert.True(upgraded > 0);

            uint[] surf2 = new uint[LumonScenePbrSurfaceLutRegistry.MaxSurfaceEntries * 4];
            Assert.True(surfaceLut.TryCopyAndClearDirtyLut(surf2));

            Assert.Equal(255u, surf2[so + 0]); // red
            Assert.Equal(0u, surf2[so + 1]);
            Assert.Equal(0u, surf2[so + 2]);
            Assert.Equal(64u, surf2[so + 3]); // 0.25 * 255
        }
        finally
        {
            PbrMaterialRegistry.Instance.Clear();
        }
    }

    private static AssetLocation NormalizeTextureKeyForTest(AssetLocation input)
    {
        var mi = typeof(PbrMaterialRegistry).GetMethod(
            "NormalizeTextureLocation",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(mi);

        object? result = mi!.Invoke(null, new object?[] { input });
        Assert.IsType<AssetLocation>(result);
        return (AssetLocation)result!;
    }

    private static Block CreateBlockWithTexture(string faceKey, string textureDomain, string texturePath)
    {
        var block = (Block)FormatterServices.GetUninitializedObject(typeof(Block));

        var textures = new Dictionary<string, CompositeTexture>(StringComparer.Ordinal)
        {
            [faceKey] = new CompositeTexture
            {
                Base = new AssetLocation(textureDomain, texturePath)
            }
        };

        // Fill common fields/properties used by the resolver.
        TrySetMember(block, "Textures", textures);
        TrySetMember(block, "Code", new AssetLocation(textureDomain, "testblock"));

        return block;
    }

    private static void TrySetMember(object target, string name, object? value)
    {
        Type t = target.GetType();

        PropertyInfo? prop = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop?.CanWrite == true)
        {
            prop.SetValue(target, value);
            return;
        }

        FieldInfo? field = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                           ?? t.GetField($"<{name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        field?.SetValue(target, value);
    }

    private class FunctionalWorldProxy : DispatchProxy
    {
        private System.Func<int, Block?>? getBlock;

        public static IClientWorldAccessor Create(System.Func<int, Block?> getBlock)
        {
            object proxy = Create<IClientWorldAccessor, FunctionalWorldProxy>();
            var typed = (FunctionalWorldProxy)proxy;
            typed.getBlock = getBlock;
            return (IClientWorldAccessor)proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null) return null;
            args ??= Array.Empty<object?>();

            if (targetMethod.Name == nameof(IWorldAccessor.GetBlock) && args.Length == 1 && args[0] is int id)
            {
                return getBlock!(id);
            }

            Type returnType = targetMethod.ReturnType;
            if (returnType == typeof(void)) return null;
            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }

    private class FunctionalCapiProxy : DispatchProxy
    {
        private IClientWorldAccessor? world;

        public static ICoreClientAPI Create(IClientWorldAccessor world)
        {
            object proxy = Create<ICoreClientAPI, FunctionalCapiProxy>();
            var typed = (FunctionalCapiProxy)proxy;
            typed.world = world;
            return (ICoreClientAPI)proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null) return null;

            if (targetMethod.Name == "get_World")
            {
                return world;
            }

            Type returnType = targetMethod.ReturnType;
            if (returnType == typeof(void)) return null;
            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }
}
