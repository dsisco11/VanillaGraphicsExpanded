using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Unit;

public sealed class NormalDepthAtlasPlumbingTests
{
    [Fact]
    public void PbrMaterialAtlasTextures_PageKeyMapping_IsConsistent()
    {
        // Arrange: inject a synthetic atlas-page bundle keyed by base atlas texture id.
        const int baseAtlasTexId = 1234;
        const int normalDepthTexId = 5678;

        var instance = MaterialAtlasSystem.Instance;

        MaterialAtlasTextureStore store = GetPrivateInstanceField<MaterialAtlasTextureStore>(instance, "textureStore");
        IDictionary pagesByAtlasTexId = GetPrivateInstanceField<IDictionary>(store, "pagesByAtlasTexId");

        pagesByAtlasTexId.Clear();

        DynamicTexture2D materialParams = CreateFakeDynamicTexture(textureId: 111, internalFormat: PixelInternalFormat.Rgb16f);
        DynamicTexture2D normalDepth = CreateFakeDynamicTexture(textureId: normalDepthTexId, internalFormat: PixelInternalFormat.Rgba16f);

        var pageTextures = new MaterialAtlasPageTextures(materialParams, normalDepth);

        Type entryType = typeof(MaterialAtlasTextureStore).GetNestedType("PageEntry", BindingFlags.NonPublic)!;
        object entry = Activator.CreateInstance(
            entryType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: new object[] { 1, 1, pageTextures },
            culture: null)!;

        pagesByAtlasTexId[baseAtlasTexId] = entry;

        // Act + Assert
        Assert.True(instance.TryGetNormalDepthTextureId(baseAtlasTexId, out int got));
        Assert.Equal(normalDepthTexId, got);

        Assert.False(instance.TryGetNormalDepthTextureId(baseAtlasTexId + 1, out _));

        // Cleanup (avoid disposing fake textures)
        pagesByAtlasTexId.Clear();
    }

    [Fact]
    public void TerrainMaterialParamsTextureBindingHook_ClearUniformCache_EmptiesBothCaches()
    {
        // Arrange: populate both caches via reflection.
        var materialCache = GetPrivateStaticField<Dictionary<int, int>>(typeof(TerrainMaterialParamsTextureBindingHook), "uniformLocationCache");
        var normalDepthCache = GetPrivateStaticField<Dictionary<int, int>>(typeof(TerrainMaterialParamsTextureBindingHook), "normalDepthUniformLocationCache");

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

    private static T GetPrivateInstanceField<T>(object instance, string fieldName)
    {
        FieldInfo? field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);

        object? value = field!.GetValue(instance);
        Assert.NotNull(value);
        Assert.IsAssignableFrom<T>(value);
        return (T)value!;
    }

    private static T GetPrivateStaticField<T>(Type type, string fieldName)
    {
        FieldInfo? field = type.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field);

        object? value = field!.GetValue(null);
        Assert.NotNull(value);
        Assert.IsType<T>(value);
        return (T)value;
    }

    private static DynamicTexture2D CreateFakeDynamicTexture(int textureId, PixelInternalFormat internalFormat)
    {
        // Tests should not require a live GL context; we create an uninitialized instance and
        // populate the minimum fields used by IsValid/TextureId.
#pragma warning disable SYSLIB0050
        var tex = (DynamicTexture2D)FormatterServices.GetUninitializedObject(typeof(DynamicTexture2D));
#pragma warning restore SYSLIB0050

        SetPrivateInstanceField(tex, "textureId", textureId);
        SetPrivateInstanceField(tex, "width", 1);
        SetPrivateInstanceField(tex, "height", 1);
        SetPrivateInstanceField(tex, "mipLevels", 1);
        SetPrivateInstanceField(tex, "internalFormat", internalFormat);
        SetPrivateInstanceField(tex, "filterMode", TextureFilterMode.Nearest);
        SetPrivateInstanceField(tex, "isDisposed", false);
        return tex;
    }

    private static void SetPrivateInstanceField<T>(object instance, string fieldName, T value)
    {
        FieldInfo? field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(instance, value);
    }
}
