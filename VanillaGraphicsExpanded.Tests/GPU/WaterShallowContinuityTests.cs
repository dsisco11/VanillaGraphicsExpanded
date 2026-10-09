using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Measures subpixel receiver continuity on independently authored shallow planar and curved walls.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class WaterShallowContinuityTests(HeadlessGLFixture fixture, ITestOutputHelper output)
    : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Smooth wall motion must not acquire abrupt HDR radiance from filtering or receiver selection.</summary>
    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, false)]
    [InlineData(2, false, false)]
    [InlineData(3, false, false)]
    [InlineData(0, true, false)]
    [InlineData(1, true, false)]
    [InlineData(2, true, false)]
    [InlineData(3, true, false)]
    [InlineData(0, false, true)]
    [InlineData(1, false, true)]
    [InlineData(2, false, true)]
    [InlineData(3, false, true)]
    [InlineData(0, true, true)]
    [InlineData(1, true, true)]
    [InlineData(2, true, true)]
    [InlineData(3, true, true)]
    [InlineData(0, false, false, true)]
    [InlineData(1, false, false, true)]
    [InlineData(2, false, false, true)]
    [InlineData(3, false, false, true)]
    [InlineData(0, true, false, true)]
    [InlineData(1, true, false, true)]
    [InlineData(2, true, false, true)]
    [InlineData(3, true, false, true)]
    public void ShallowWallMotionPreservesContinuousRadiance(int quality, bool half, bool shoreline, bool curved = false)
    {
        EnsureShaderTestAvailable();
        const int size = 128;
        const float slope = 2;
        float wallDepth = curved ? 2.12f : shoreline ? 2.03f : 2.2f;
        float motionRange = shoreline ? .006f : .04f;
        using var color = DynamicTexture2D.Create(size,size,PixelInternalFormat.Rgba32f);
        using var depth = DynamicTexture2D.Create(size,size,PixelInternalFormat.R32f);
        var colors = new float[size * size * 4];
        var depths = new float[size * size];
        // Intersect camera rays with z + wallDepth - slope*x = 0, or the
        // independently authored smooth curved depth field. Radiance is affine
        // in physical wall position, so moving samples have no texture edges.
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int pixel = y * size + x;
            float rayX = (2 * (x + .5f) / size - 1) / MathF.Sqrt(3);
            float distance = curved ? wallDepth + .025f * MathF.Sin((x+.5f)/size * 24*MathF.PI)
                : wallDepth / (1 + slope * rayX);
            if (distance is <= .1f or >= 100) { depths[pixel] = 1; continue; }
            depths[pixel] = .5f * (1 + (100.1f - 20 / distance) / 99.9f);
            colors[pixel * 4] = 8 + 4 * rayX * distance;
            colors[pixel * 4 + 1] = 2;
            colors[pixel * 4 + 2] = .5f;
            colors[pixel * 4 + 3] = 1;
        }
        color.UploadDataImmediate(colors); depth.UploadDataImmediate(depths);
        using var halfColor = half ? DynamicTexture2D.Create(size/2,size/2,PixelInternalFormat.Rgba32f) : null;
        using var halfDepth = half ? DynamicTexture2D.Create(size/2,size/2,PixelInternalFormat.Rgba32f) : null;
        using var reduced = half ? GpuFramebuffer.CreateMRT([halfColor!,halfDepth!]) : null;
        if (half)
        {
            var reduction = Programs.Create<WaterRefractionReductionShaderProgram>();
            reduction.SourceColor = color; reduction.SourceDepth = depth;
            TestFramework.RenderQuadTo(reduction,reduced!);
        }
        var program = Programs.Create<WaterRefractionDiagnosticShaderProgram>();
        var inputs = (IWaterRefractionDiagnosticBindings)program;
        inputs.Scenario = 12; inputs.Quality = quality; inputs.Budget = 1 << quality; inputs.SelectReceiver = 1;
        inputs.Normal = Vector3.Normalize(new Vector3(curved ? -.7f : -.4f,0,1));
        inputs.Color = halfColor ?? color; inputs.Depth = halfDepth ?? depth;
        using var target = CreateMRTRenderTarget(1,1,PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f,
            PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f);
        var filter = Programs.Create<WaterReceiverFilterShaderProgram>();
        var filterInputs = (IWaterReceiverFilterBindings)filter;
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/3,1,.1f,100);
        projection.M33 = -100.1f/99.9f; projection.M43 = -20f/99.9f;
        Assert.True(Matrix4x4.Invert(projection,out var inverse));
        using var frameCamera = TestFrameCamera.CreateFromProjection(projection, size, size);
        program.FrameInputs = frameCamera;
        filter.FrameInputs = frameCamera;
        filterInputs.Normal = inputs.Normal; filterInputs.Color = halfColor ?? color; filterInputs.Depth = halfDepth ?? depth;
        using var filterTarget = CreateMRTRenderTarget(1,1,PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f);
        float maximumJump = 0, maximumAssociationError = 0;
        float maximumFilterJump = 0;
        int switches = 0, previousMethod = 0, rays = 0, uv = 0;
        int largestJumpMotion = 0, largestJumpOldMethod = 0, largestJumpNewMethod = 0;
        float? previousRadiance = null;
        float? previousFilterRadiance = null;
        for (int motion = 0; motion <= 200; motion++)
        {
            inputs.Surface = new(-motionRange + motion * motionRange / 100,0,-2);
            if (quality == 0)
            {
                // Isolate direct bilateral sampling from ray/UV selection. The
                // projected seed follows the same surface motion without traversal.
                filterInputs.Surface = inputs.Surface;
                filterInputs.SampleUv = new(inputs.Surface.X/2*MathF.Sqrt(3)*.5f+.5f,.5f);
                TestFramework.RenderQuadTo(filter,filterTarget);
                float filteredRadiance = filterTarget[1].ReadPixels()[0];
                if (previousFilterRadiance is { } previousFilter)
                    maximumFilterJump = MathF.Max(maximumFilterJump,MathF.Abs(filteredRadiance-previousFilter));
                previousFilterRadiance = filteredRadiance;
            }
            TestFramework.RenderQuadTo(program,target);
            var decision = target[0].ReadPixels(); var selection = target[3].ReadPixels();
            var position = target[4].ReadPixels(); var radiance = target[5].ReadPixels();
            Assert.Equal(1,decision[0]); Assert.Equal(1,decision[3]);
            int method = (int)selection[0];
            if (method == 1) rays++; else { Assert.Equal(2,method); uv++; }
            if (!curved) Assert.InRange(MathF.Abs(position[2] + wallDepth - slope * position[0]),0,.0001f);
            if (previousMethod != 0 && previousMethod != method) switches++;
            if (previousRadiance is { } previous)
            {
                float jump = MathF.Abs(radiance[0]-previous);
                if (jump > maximumJump)
                {
                    maximumJump = jump; largestJumpMotion = motion;
                    largestJumpOldMethod = previousMethod; largestJumpNewMethod = method;
                }
            }
            maximumAssociationError = MathF.Max(maximumAssociationError,MathF.Abs(radiance[0]-(8+4*position[0])));
            previousMethod = method; previousRadiance = radiance[0];
        }
        output.WriteLine($"quality={quality} half={half} shoreline={shoreline} curved={curved}: ray={rays}, uv={uv}, switches={switches}, maxAdjacentHdrJump={maximumJump:R}, maxAssociationError={maximumAssociationError:R}, maxDirectFilterJump={maximumFilterJump:R}");
        output.WriteLine($"Largest jump at motion {largestJumpMotion}, method {largestJumpOldMethod}->{largestJumpNewMethod}.");
        Assert.InRange(maximumAssociationError,0,.0001f);
        if (quality == 0) Assert.InRange(maximumFilterJump,0,.01f);
        // A 0.4 mm surface shift on this bounded wall moves radiance by less
        // than .004; .01 permits reconstruction error without accepting texel stairs.
        Assert.InRange(maximumJump,0,.01f);
    }
    #endregion
}
