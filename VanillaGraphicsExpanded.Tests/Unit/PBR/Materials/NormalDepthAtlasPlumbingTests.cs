using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Serialization;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Checks CPU-side material atlas lookup and uniform-cache plumbing without a live GL context.</summary>
[Collection("CPU state")]
public sealed class NormalDepthAtlasPlumbingTests
{
    #region Public API
    /// <summary>A registered atlas page returns its normal-depth texture for the matching base texture key.</summary>
    [Fact]
    public void PbrMaterialAtlasTextures_PageKeyMapping_IsConsistent()
    {
        // Arrange
        // Arrange: inject a synthetic atlas-page bundle keyed by base atlas texture id.
        const int baseAtlasTexId = 1234;
        const int normalDepthTexId = 5678;

        var instance = MaterialAtlasSystem.Instance;

        MaterialAtlasTextureStore store = GetPrivateInstanceField<MaterialAtlasTextureStore>(instance, "textureStore");
        IDictionary pagesByAtlasTexId = GetPrivateInstanceField<IDictionary>(store, "pagesByAtlasTexId");
        var previousPages = new List<DictionaryEntry>();
        foreach (DictionaryEntry page in pagesByAtlasTexId)
            previousPages.Add(page);

        try
        {
            pagesByAtlasTexId.Clear();
            Texture2D materialParams = CreateFakeTexture2D(textureId: 111, internalFormat: TextureFormat.Rgb16f);
            Texture2D normalDepth = CreateFakeTexture2D(textureId: normalDepthTexId, internalFormat: TextureFormat.Rgba16f);
            var pageTextures = new MaterialAtlasPageTextures(materialParams, normalDepth);

            Type entryType = typeof(MaterialAtlasTextureStore).GetNestedType("PageEntry", BindingFlags.NonPublic)!;
            object entry = Activator.CreateInstance(
                entryType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: new object[] { 1, 1, pageTextures },
                culture: null)!;

            pagesByAtlasTexId[baseAtlasTexId] = entry;

            // Act
            Assert.True(instance.TryGetNormalDepthTextureId(baseAtlasTexId, out int got));

            // Assert
            Assert.Equal(normalDepthTexId, got);
            Assert.False(instance.TryGetNormalDepthTextureId(baseAtlasTexId + 1, out _));
        }
        finally
        {
            pagesByAtlasTexId.Clear();
            foreach (DictionaryEntry page in previousPages)
                pagesByAtlasTexId[page.Key] = page.Value;
        }
    }

    /// <summary>Clearing material texture binding state empties both cached uniform-location maps.</summary>
    [Fact]
    public void TerrainMaterialParamsTextureBindingHook_ClearUniformCache_EmptiesBothCaches()
    {
        // Arrange
        // Arrange: populate both caches via reflection.
        var materialCache = GetPrivateStaticField<Dictionary<int, int>>(typeof(TerrainMaterialParamsTextureBindingHook), "uniformLocationCache");
        var normalDepthCache = GetPrivateStaticField<Dictionary<int, int>>(typeof(TerrainMaterialParamsTextureBindingHook), "normalDepthUniformLocationCache");

        var previousMaterialCache = new Dictionary<int, int>(materialCache);
        var previousNormalDepthCache = new Dictionary<int, int>(normalDepthCache);

        try
        {
            materialCache.Clear();
            normalDepthCache.Clear();
            materialCache[1] = 10;
            normalDepthCache[1] = 11;
            Assert.NotEmpty(materialCache);
            Assert.NotEmpty(normalDepthCache);

            // Act
            TerrainMaterialParamsTextureBindingHook.ClearUniformCache();

            // Assert
            Assert.Empty(materialCache);
            Assert.Empty(normalDepthCache);
        }
        finally
        {
            RestoreCache(materialCache, previousMaterialCache);
            RestoreCache(normalDepthCache, previousNormalDepthCache);
        }
    }
    #endregion

    #region Private helpers
    /// <summary>Restores a shared uniform cache after the test mutates its contents.</summary>
    private static void RestoreCache(Dictionary<int, int> cache, Dictionary<int, int> previous)
    {
        cache.Clear();
        foreach ((int program, int location) in previous)
            cache.Add(program, location);
    }

    /// <summary>Reads and type-checks a private instance field used to assemble context-free GPU fixtures.</summary>
    private static T GetPrivateInstanceField<T>(object instance, string fieldName)
    {
        FieldInfo? field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);

        object? value = field!.GetValue(instance);
        Assert.NotNull(value);
        Assert.IsAssignableFrom<T>(value);
        return (T)value!;
    }

    /// <summary>Reads and type-checks a private static field used by cache assertions.</summary>
    private static T GetPrivateStaticField<T>(Type type, string fieldName)
    {
        FieldInfo? field = type.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field);

        object? value = field!.GetValue(null);
        Assert.NotNull(value);
        Assert.IsType<T>(value);
        return (T)value;
    }

    /// <summary>Creates a minimal texture object with metadata needed by the CPU lookup path.</summary>
    private static Texture2D CreateFakeTexture2D(int textureId, TextureFormat internalFormat)
    {
        // Tests should not require a live GL context; we create an uninitialized instance and
        // populate the minimum fields used by IsValid/TextureId.
#pragma warning disable SYSLIB0050
        var tex = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
#pragma warning restore SYSLIB0050

        SetPrivateInstanceField(tex, "textureId", textureId);
        tex.AssignMetadataSnapshot(new GpuTextureMetadata(TextureDimension.Two,
            ImmutableArray.Create(new GpuTextureLevelMetadata(0, 0, 1, 1, 1, internalFormat, 0, true)),
            false, 0, 0, 0));
        SetPrivateInstanceField(tex, "filterMode", TextureFilterMode.Nearest);
        // GpuResource uses an int disposal sentinel.
        SetPrivateInstanceField(tex, "disposed", 0);
        return tex;
    }

    /// <summary>Sets a private instance field by searching the object's inheritance chain.</summary>
    private static void SetPrivateInstanceField<T>(object instance, string fieldName, T value)
    {
        Type? type = instance.GetType();
        FieldInfo? field = null;

        while (type is not null)
        {
            field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field is not null)
            {
                break;
            }

            type = type.BaseType;
        }

        Assert.NotNull(field);
        field!.SetValue(instance, value);
    }
    #endregion
}
