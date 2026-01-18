using VanillaGraphicsExpanded.PBR.Materials;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

[Trait("Category", "Unit")]
public sealed class MaterialAtlasParamsOverrideApplierTests
{
    [Fact]
    public void ApplyRgbOverride_WritesNormalizedRgbIntoRect()
    {
        const int atlasW = 4;
        const int atlasH = 4;

        float[] atlas = new float[atlasW * atlasH * 3];

        // 2x2 override rect at (1,1)
        const int x = 1;
        const int y = 1;
        const int w = 2;
        const int h = 2;

        // 4 pixels RGBA, alpha ignored
        // (R,G,B): (10,20,30), (40,50,60), (70,80,90), (100,110,120)
        byte[] rgba =
        [
            10, 20, 30, 255,
            40, 50, 60, 255,
            70, 80, 90, 255,
            100, 110, 120, 255
        ];

        MaterialAtlasParamsOverrideApplier.ApplyRgbOverride(
            atlasRgbTriplets: atlas,
            atlasWidth: atlasW,
            atlasHeight: atlasH,
            rectX: x,
            rectY: y,
            rectWidth: w,
            rectHeight: h,
            overrideRgba: rgba);

        AssertPixelRgb(atlas, atlasW, x: 1, y: 1, r: 10f / 255f, g: 20f / 255f, b: 30f / 255f);
        AssertPixelRgb(atlas, atlasW, x: 2, y: 1, r: 40f / 255f, g: 50f / 255f, b: 60f / 255f);
        AssertPixelRgb(atlas, atlasW, x: 1, y: 2, r: 70f / 255f, g: 80f / 255f, b: 90f / 255f);
        AssertPixelRgb(atlas, atlasW, x: 2, y: 2, r: 100f / 255f, g: 110f / 255f, b: 120f / 255f);

        // Outside rect remains untouched (default 0)
        AssertPixelRgb(atlas, atlasW, x: 0, y: 0, r: 0f, g: 0f, b: 0f);
        AssertPixelRgb(atlas, atlasW, x: 3, y: 3, r: 0f, g: 0f, b: 0f);
    }

    [Fact]
    public void ApplyRgbOverride_WithScale_MultipliesClampsAndPreservesNaN()
    {
        const int atlasW = 4;
        const int atlasH = 4;

        float[] atlas = new float[atlasW * atlasH * 3];

        // 2x2 override rect at (1,1)
        const int x = 1;
        const int y = 1;
        const int w = 2;
        const int h = 2;

        // 4 pixels RGBA01 (alpha ignored)
        float[] rgba01 =
        [
            float.NaN, 0.25f, 0.10f, 1f,
            0.60f, 0.90f, 0.20f, 1f,
            0.70f, 0.80f, 0.30f, 1f,
            0.40f, 0.50f, 0.99f, 1f,
        ];

        var scale = new PbrOverrideScale(
            Roughness: 2f,
            Metallic: 0.5f,
            Emissive: 10f,
            Normal: 1f,
            Depth: 1f);

        MaterialAtlasParamsOverrideApplier.ApplyRgbOverride(
            atlasRgbTriplets: atlas,
            atlasWidth: atlasW,
            atlasHeight: atlasH,
            rectX: x,
            rectY: y,
            rectWidth: w,
            rectHeight: h,
            overrideRgba01: rgba01,
            scale: scale);

        // Pixel (1,1): roughness is NaN -> stays NaN; metallic 0.25*0.5=0.125; emissive 0.10*10=1 (clamp)
        int idx = ((1 * atlasW) + 1) * 3;
        Assert.True(float.IsNaN(atlas[idx + 0]));
        Assert.Equal(0.125f, atlas[idx + 1], 6);
        Assert.Equal(1f, atlas[idx + 2], 6);

        // Pixel (2,1): 0.60*2=1.2 -> 1; 0.90*0.5=0.45; 0.20*10=2 -> 1
        AssertPixelRgb(atlas, atlasW, x: 2, y: 1, r: 1f, g: 0.45f, b: 1f);

        // Pixel (1,2): 0.70*2=1.4 -> 1; 0.80*0.5=0.4; 0.30*10=3 -> 1
        AssertPixelRgb(atlas, atlasW, x: 1, y: 2, r: 1f, g: 0.4f, b: 1f);

        // Pixel (2,2): 0.40*2=0.8; 0.50*0.5=0.25; 0.99*10=9.9 -> 1
        AssertPixelRgb(atlas, atlasW, x: 2, y: 2, r: 0.8f, g: 0.25f, b: 1f);
    }

    private static void AssertPixelRgb(float[] pixels, int width, int x, int y, float r, float g, float b)
    {
        int idx = (y * width + x) * 3;
        Assert.Equal(r, pixels[idx + 0], 6);
        Assert.Equal(g, pixels[idx + 1], 6);
        Assert.Equal(b, pixels[idx + 2], 6);
    }
}
