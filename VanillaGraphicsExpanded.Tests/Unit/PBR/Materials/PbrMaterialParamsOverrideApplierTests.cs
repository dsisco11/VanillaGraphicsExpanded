using VanillaGraphicsExpanded.PBR.Materials;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

[Trait("Category", "Unit")]
public sealed class PbrMaterialParamsOverrideApplierTests
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

        PbrMaterialParamsOverrideApplier.ApplyRgbOverride(
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

    private static void AssertPixelRgb(float[] pixels, int width, int x, int y, float r, float g, float b)
    {
        int idx = (y * width + x) * 3;
        Assert.Equal(r, pixels[idx + 0], 6);
        Assert.Equal(g, pixels[idx + 1], 6);
        Assert.Equal(b, pixels[idx + 2], 6);
    }
}
