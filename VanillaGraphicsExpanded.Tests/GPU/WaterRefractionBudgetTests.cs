using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Compares bounded production selection with a dense independent visible-geometry reference.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class WaterRefractionBudgetTests(HeadlessGLFixture fixture, ITestOutputHelper output)
    : LumOnShaderFunctionalTestBase(fixture)
{
    private const int Size = 128;

    #region Public API
    /// <summary>Actual ray/UV switches retain associated HDR radiance and bounded receiver motion on one smooth plane.</summary>
    [Theory]
    [InlineData(false,4f)]
    [InlineData(true,4f)]
    [InlineData(false,1f)]
    [InlineData(true,1f)]
    public void RayAndUvTransitionsStayWithinReceiverFootprint(bool half, float slope)
    {
        EnsureShaderTestAvailable();
        using var color = DynamicTexture2D.Create(Size,Size,PixelInternalFormat.Rgba32f);
        using var depth = DynamicTexture2D.Create(Size,Size,PixelInternalFormat.R32f);
        var colors = new float[Size * Size * 4]; var depths = new float[Size * Size];
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            int pixel = y * Size + x;
            Vector2 uv = new((x + .5f) / Size,(y + .5f) / Size);
            float distance = 10 / (1 + slope * (uv.X * 2 - 1) / MathF.Sqrt(3));
            if (distance is <= .1f or >= 100) { depths[pixel] = 1; continue; }
            Vector3 position = AtDepth(uv,distance);
            depths[pixel] = .5f * (1 + (100.1f - 20 / distance) / 99.9f);
            colors[pixel * 4] = 8 + .1f * position.X;
            colors[pixel * 4 + 1] = 4 + .1f * position.Y;
            colors[pixel * 4 + 2] = 8 + .05f * position.Z; colors[pixel * 4 + 3] = 1;
        }
        color.UploadDataImmediate(colors); depth.UploadDataImmediate(depths);
        using var halfColor = half ? DynamicTexture2D.Create(Size / 2,Size / 2,PixelInternalFormat.Rgba32f) : null;
        using var halfDepth = half ? DynamicTexture2D.Create(Size / 2,Size / 2,PixelInternalFormat.Rgba32f) : null;
        using var reduced = half ? GpuFramebuffer.CreateMRT([halfColor!,halfDepth!]) : null;
        if (half)
        {
            var reduction = Programs.Create<WaterRefractionReductionShaderProgram>();
            reduction.SourceColor = color; reduction.SourceDepth = depth;
            TestFramework.RenderQuadTo(reduction,reduced!);
        }
        var program = Programs.Create<WaterRefractionDiagnosticShaderProgram>();
        var inputs = (IWaterRefractionDiagnosticBindings)program;
        SetProjection(inputs);
        inputs.Scenario = 12; inputs.Budget = 2; inputs.Quality = 1; inputs.SelectReceiver = 1;
        inputs.FrameSize = new(Size); inputs.Normal = Vector3.Normalize(new Vector3(-.4f,0,1));
        inputs.Color = halfColor ?? color; inputs.Depth = halfDepth ?? depth;
        using var target = CreateMRTRenderTarget(1,1,PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f,
            PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f,
            PixelInternalFormat.Rgba32f);
        int switches = 0, rayCount = 0, uvCount = 0, previousMethod = 0;
        Vector3 previousPosition = default, previousReference = default, previousColor = default;
        float maximumPositionError = 0, maximumColorError = 0;
        for (int motion = 0; motion <= 60; motion++)
        {
            Vector3 surface = new(slope == 4 ? .3f + motion * .008f : -.4f + motion * .02f,0,-2);
            inputs.Surface = surface;
            TestFramework.RenderQuadTo(program,target);
            float[] decision = target[0].ReadPixels(), selection = target[3].ReadPixels();
            float[] receiver = target[4].ReadPixels(), radiance = target[5].ReadPixels();
            Assert.Equal(1,decision[0]); Assert.Equal(1,decision[3]);
            Assert.InRange(decision[2],0,2); Assert.InRange(selection[1],0,2);
            int method = (int)selection[0];
            float[] work = target[6].ReadPixels();
            Assert.InRange(work[1], 0, work[0]);
            Assert.Equal(1, work[2] + work[3]);
            Assert.InRange(work[1], 1, 4);
            if (method == 1) Assert.Equal(0, work[2]);
            if (method == 1) rayCount++; else { Assert.Equal(2,method); uvCount++; }
            Vector3 position = new(receiver[0],receiver[1],receiver[2]);
            Vector3 rgb = new(radiance[0],radiance[1],radiance[2]);
            Assert.True(MathF.Abs(position.Z + 10 - slope * position.X) <= .001f,$"half={half} motion={motion} method={method} point={position} rgb={rgb} planeError={position.Z + 10 - slope * position.X:R} rayCalls={decision[2]}");
            Assert.InRange(Vector3.Distance(rgb,new(8 + .1f * position.X,4 + .1f * position.Y,8 + .05f * position.Z)),0,.0001f);
            Vector3 incident = Vector3.Normalize(surface);
            float cosine = -Vector3.Dot(incident,inputs.Normal), eta = 1 / 1.333f;
            Vector3 direction = eta * incident + (eta * cosine - MathF.Sqrt(1 - eta * eta * (1 - cosine * cosine))) * inputs.Normal;
            Vector3 reference = surface + direction * ((-10 + slope * surface.X - surface.Z) / (direction.Z - slope * direction.X));
            if (previousMethod != 0 && previousMethod != method)
            {
                switches++;
                Vector3 movementError = position - previousPosition - (reference - previousReference);
                float positionError = movementError.Length();
                Vector3 expectedColorMotion = (reference - previousReference) * new Vector3(.1f,.1f,.05f);
                float colorError = Vector3.Distance(rgb - previousColor,expectedColorMotion);
                maximumPositionError = MathF.Max(maximumPositionError,positionError);
                maximumColorError = MathF.Max(maximumColorError,colorError);
                float footprint = 4 * MathF.Max(-position.Z,-previousPosition.Z) / (MathF.Sqrt(3) * (half ? Size / 2 : Size));
                Assert.True(positionError <= footprint,$"half={half} motion={motion} method={previousMethod}->{method} receiver jump={positionError:R}m exceeds two-texel footprint={footprint:R}m");
                Assert.True(colorError <= .1f * footprint,$"half={half} method={previousMethod}->{method} HDR jump={colorError:R}");
            }
            previousMethod = method; previousPosition = position; previousReference = reference; previousColor = rgb;
        }
        output.WriteLine($"half={half}: ray={rayCount}, uv={uvCount}, switches={switches}, maxTransitionPositionError={maximumPositionError:R}m, maxTransitionHdrError={maximumColorError:R}");
        // Independent neighboring geometry now establishes steep continuous
        // receiver coverage. Full-size planes must keep geometric intersections;
        // reduced source hulls still exercise genuine ray/UV transition bounds.
        if (!half) Assert.Equal(61,rayCount);
        else Assert.True(switches > 0,"Reduced original-source hulls must exercise an actual RAY/UV transition.");
    }

    /// <summary>Cached reuse respects an irregular source hull and associates color with the corrected hit.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CachedPatchRequiresRepresentedCoverage(bool irregular)
    {
        EnsureShaderTestAvailable();
        int size = irregular ? Size / 2 : Size;
        using var color = DynamicTexture2D.Create(size,size,PixelInternalFormat.Rgba32f);
        using var depth = DynamicTexture2D.Create(size,size,irregular ? PixelInternalFormat.Rgba32f : PixelInternalFormat.R32f);
        var colors = new float[size * size * 4]; var depths = new float[size * size * (irregular ? 4 : 1)];
        float hardware = .5f * (1 + (100.1f - 2) / 99.9f);
        if (irregular)
        {
            // Four half-resolution taps select an irregular quadrilateral inside
            // their original 4x4 footprint. (2.8,2.8) is outside its convex hull.
            Vector2[] originals = [new(0,0),new(3,1),new(1,3),new(2,2)];
            for (int tap = 0; tap < 4; tap++)
            {
                int pixel = (32 + (tap >> 1)) * size + 32 + (tap & 1);
                Vector2 uv = (originals[tap] + new Vector2(64.5f)) / Size;
                depths[pixel * 4] = hardware; depths[pixel * 4 + 1] = uv.X;
                depths[pixel * 4 + 2] = uv.Y; depths[pixel * 4 + 3] = 1;
                colors[pixel * 4] = 4 + 16 * uv.X; colors[pixel * 4 + 3] = 1;
            }
        }
        else
        {
            Array.Fill(depths,hardware);
            for (int pixel = 0; pixel < size * size; pixel++)
            { colors[pixel * 4] = 4 + 16 * (pixel % size + .5f) / size; colors[pixel * 4 + 3] = 1; }
        }
        color.UploadDataImmediate(colors); depth.UploadDataImmediate(depths);
        Vector2 seedUv = irregular ? new(65.5f / Size) : new(.5f);
        Vector2 hitUv = irregular ? new(67.3f / Size) : new(.502f,.5f);
        Vector3 surface = AtDepth(seedUv,2), hit = AtDepth(hitUv,10);
        Vector3 direction = Vector3.Normalize(hit - surface);
        // Invert Snell's vector relation to author the normal for this exact ray.
        Vector3 normal = Vector3.Normalize(Vector3.Normalize(surface) / 1.333f - direction);
        var program = Programs.Create<WaterRefractionDiagnosticShaderProgram>();
        var inputs = (IWaterRefractionDiagnosticBindings)program;
        SetProjection(inputs);
        inputs.Scenario = 12; inputs.Budget = 2; inputs.Surface = surface; inputs.Normal = normal;
        inputs.FrameSize = new(Size); inputs.Color = color; inputs.Depth = depth;
        using var target = CreateMRTRenderTarget(1,1,PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f,
            PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f);
        TestFramework.RenderQuadTo(program,target);
        float[] decision = target[0].ReadPixels();
        Assert.Equal(irregular ? 2 : 1,decision[2]);
        if (!irregular)
        {
            Assert.Equal(1,decision[0]); Assert.Equal(1,target[3].ReadPixels()[0]);
            Assert.InRange(MathF.Abs(target[5].ReadPixels()[0] - (4 + 16 * hitUv.X)),0,.0001f);
        }
    }

    /// <summary>Exhaustion reaches the exact ceiling; TIR performs neither ray nor UV receiver reads.</summary>
    [Theory]
    [InlineData(1,false)]
    [InlineData(2,false)]
    [InlineData(3,false)]
    [InlineData(1,true)]
    [InlineData(2,true)]
    [InlineData(3,true)]
    public void ExhaustionAndTirAccountForEveryLookup(int quality, bool tir)
    {
        EnsureShaderTestAvailable();
        using var color = DynamicTexture2D.Create(Size,Size,PixelInternalFormat.Rgba32f);
        using var depth = DynamicTexture2D.Create(Size,Size,PixelInternalFormat.R32f);
        color.UploadDataImmediate(new float[Size * Size * 4]);
        depth.UploadDataImmediate(Enumerable.Repeat(1f,Size * Size).ToArray());
        var program = Programs.Create<WaterRefractionDiagnosticShaderProgram>();
        var inputs = (IWaterRefractionDiagnosticBindings)program;
        SetProjection(inputs);
        inputs.Scenario = 12; inputs.Budget = 1 << quality; inputs.Quality = quality; inputs.SelectReceiver = 1;
        inputs.Surface = new(0,0,-2); inputs.FrameSize = new(Size); inputs.Underwater = tir ? 1 : 0;
        inputs.Normal = tir ? Vector3.Normalize(new Vector3(.9f,0,.3f)) : Vector3.UnitZ;
        inputs.Color = color; inputs.Depth = depth;
        using var target = CreateMRTRenderTarget(1,1,PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f,
            PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f);
        TestFramework.RenderQuadTo(program,target);
        var decision = target[0].ReadPixels(); var selected = target[3].ReadPixels();
        Assert.Equal(0,decision[0]); Assert.Equal(0,selected[0]);
        Assert.Equal(tir ? 0 : 1 << quality,decision[2]);
        Assert.Equal(tir ? 0 : 1,selected[1]);
    }

    /// <summary>Motion sweeps report geometric error, missed thin receivers and explicit UV fallback without hiding work.</summary>
    [Theory]
    [InlineData(1,false)]
    [InlineData(1,true)]
    [InlineData(2,false)]
    [InlineData(2,true)]
    [InlineData(3,false)]
    [InlineData(3,true)]
    public void TierSelectionTracksDenseReference(int quality, bool half)
    {
        EnsureShaderTestAvailable();
        int budget = 1 << quality;
        using var color = DynamicTexture2D.Create(Size,Size,PixelInternalFormat.Rgba32f);
        using var depth = DynamicTexture2D.Create(Size,Size,PixelInternalFormat.R32f);
        using var halfColor = half ? DynamicTexture2D.Create(Size / 2,Size / 2,PixelInternalFormat.Rgba32f) : null;
        using var halfDepth = half ? DynamicTexture2D.Create(Size / 2,Size / 2,PixelInternalFormat.Rgba32f) : null;
        using var reduced = half ? GpuFramebuffer.CreateMRT([halfColor!,halfDepth!]) : null;
        var reduction = half ? Programs.Create<WaterRefractionReductionShaderProgram>() : null;
        var program = Programs.Create<WaterRefractionDiagnosticShaderProgram>();
        var inputs = (IWaterRefractionDiagnosticBindings)program;
        SetProjection(inputs);
        inputs.Scenario = 12; inputs.Budget = budget; inputs.Quality = quality; inputs.SelectReceiver = 1;
        inputs.FrameSize = new(Size); inputs.Normal = Vector3.Normalize(new Vector3(-.4f,0,1));
        inputs.Color = halfColor ?? color; inputs.Depth = halfDepth ?? depth;
        using var target = CreateMRTRenderTarget(1,1,PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f,
            PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f);
        foreach (string scene in new[] { "floor", "thin", "gap", "discontinuity" })
        {
            FillScene(color,depth,scene);
            if (half)
            {
                reduction!.SourceColor = color; reduction.SourceDepth = depth;
                TestFramework.RenderQuadTo(reduction,reduced!);
            }
            int geometric = 0, fallback = 0, unavailable = 0, referenceThin = 0, missedThin = 0, maximumCalls = 0;
            float largestError = 0, largestMotionError = 0, maximumFloorErrorBound = .05f;
            Vector3? previousActual = null, previousReference = null;
            for (int motion = -3; motion <= 3; motion++)
            {
                Vector3 surface = new(motion * .04f,0,-2);
                inputs.Surface = surface;
                TestFramework.RenderQuadTo(program,target);
                float[] decision = target[0].ReadPixels(), selection = target[3].ReadPixels();
                float[] position = target[4].ReadPixels(), radiance = target[5].ReadPixels();
                Assert.InRange(decision[2],0,budget); Assert.InRange(selection[1],0,2);
                maximumCalls = Math.Max(maximumCalls,(int)decision[2]);
                var reference = DenseReference(surface,inputs.Normal,scene);
                if (reference is { } expected && MathF.Abs(expected.Z + 4) < .001f) referenceThin++;
                if (decision[0] == 0)
                {
                    unavailable++;
                    if (reference is { } missing && MathF.Abs(missing.Z + 4) < .001f) missedThin++;
                    previousActual = null; previousReference = null; continue;
                }
                Assert.Equal(1,decision[3]); Assert.True(radiance[0] > 1);
                Vector3 actual = new(position[0],position[1],position[2]);
                if (selection[0] == 2) fallback++;
                else
                {
                    Assert.Equal(1,selection[0]); geometric++;
                    // Every authored visible layer is planar. A depth jump must not
                    // manufacture a marched receiver floating between those layers.
                    float nearestLayer = MathF.Min(MathF.Abs(actual.Z + 10),MathF.Abs(actual.Z + 4));
                    Assert.InRange(nearestLayer,0,.05f);
                }
                if (reference is { } referencePoint)
                {
                    float error = Vector3.Distance(actual,referencePoint);
                    largestError = MathF.Max(largestError,error);
                    if (MathF.Abs(referencePoint.Z + 4) < .001f && MathF.Abs(actual.Z + 4) > .05f) missedThin++;
                    if (scene == "floor")
                    {
                        float errorBound = .05f;
                        if (half && selection[0] == 2)
                        {
                            // Separate planar UV approximation from the original-source
                            // displacement bounded by one reduced texel diagonal.
                            Vector3 incident = Vector3.Normalize(surface);
                            float eta = 1 / 1.333f, cosine = -Vector3.Dot(incident,inputs.Normal);
                            Vector3 direction = eta * incident + (eta * cosine - MathF.Sqrt(1 - eta * eta * (1 - cosine * cosine))) * inputs.Normal;
                            Vector3 seed = AtDepth(Project(surface),10);
                            float path = -Vector3.Dot(seed - surface,inputs.Normal) / -Vector3.Dot(direction,inputs.Normal);
                            Vector3 approximate = AtDepth(Project(surface + direction * path),10);
                            float footprint = MathF.Sqrt(2) * 20 / (MathF.Sqrt(3) * (Size / 2));
                            Assert.InRange(Vector3.Distance(actual,approximate),0,footprint);
                            errorBound = Vector3.Distance(approximate,referencePoint) + footprint;
                        }
                        else Assert.Equal(1,selection[0]);
                        maximumFloorErrorBound = MathF.Max(maximumFloorErrorBound,errorBound);
                        Assert.InRange(error,0,errorBound);
                        if (previousActual is { } pa && previousReference is { } pr)
                            largestMotionError = MathF.Max(largestMotionError,Vector3.Distance(actual - pa,referencePoint - pr));
                        previousActual = actual; previousReference = referencePoint;
                    }
                }
            }
            if (scene == "floor")
            {
                Assert.Equal(7,geometric + fallback);
                Assert.InRange(largestMotionError,0,half ? 2 * maximumFloorErrorBound : .05f);
            }
            output.WriteLine($"quality={quality} half={half} scene={scene}: ray={geometric}, uv={fallback}, unavailable={unavailable}, rayCallsMax={maximumCalls}/{budget}, referenceThin={referenceThin}, missedThin={missedThin}, maxPositionError={largestError:R}m, maxMotionError={largestMotionError:R}m");
        }
    }
    #endregion

    #region Private
    /// <summary>Supplies the conventional OpenGL camera and its CPU inverse for authored depth fields.</summary>
    private static void SetProjection(IWaterRefractionDiagnosticBindings inputs)
    {
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1, .1f, 100);
        projection.M33 = -100.1f / 99.9f; projection.M43 = -20f / 99.9f;
        Assert.True(Matrix4x4.Invert(projection, out var inverse));
        inputs.Projection = projection; inputs.InverseProjection = inverse;
    }
    /// <summary>Authors depth from visible axial planes, including coverage absent from the immutable snapshot.</summary>
    private static void FillScene(DynamicTexture2D color, DynamicTexture2D depth, string scene)
    {
        var colors = new float[Size * Size * 4]; var depths = new float[Size * Size];
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            int pixel = y * Size + x;
            float metres = SceneDepth((x + .5f) / Size,scene);
            depths[pixel] = metres > 0 ? .5f * (1 + (100.1f - 20 / metres) / 99.9f) : 1;
            colors[pixel * 4] = 4 + x / (float)Size;
            colors[pixel * 4 + 1] = 2; colors[pixel * 4 + 2] = .5f;
            colors[pixel * 4 + 3] = metres > 0 ? 1 : 0;
        }
        color.UploadDataImmediate(colors); depth.UploadDataImmediate(depths);
    }

    /// <summary>Defines actual visible layers independently from GPU filtering and traversal implementation.</summary>
    private static float SceneDepth(float u, string scene)
    {
        int x = (int)MathF.Floor(u * Size);
        return scene switch
        {
            "thin" when x is >= 68 and <= 69 => 4,
            "gap" when x is >= 62 and <= 66 => 0,
            "discontinuity" when x >= 69 => 4,
            _ => 10
        };
    }

    /// <summary>Uses 1024 independent ray intervals and exact plane crossings to reject depth-jump pseudo intersections.</summary>
    private static Vector3? DenseReference(Vector3 surface, Vector3 normal, string scene)
    {
        const float eta = 1 / 1.333f;
        Vector3 incident = Vector3.Normalize(surface);
        float cosine = -Vector3.Dot(incident,normal);
        Vector3 direction = eta * incident + (eta * cosine - MathF.Sqrt(1 - eta * eta * (1 - cosine * cosine))) * normal;
        for (int step = 1; step <= 1024; step++)
        {
            float high = step / 32f, low = (step - 1) / 32f;
            Vector3 point = surface + direction * high;
            Vector2 uv = Project(point);
            if (uv.X is < 0 or > 1 || uv.Y is < 0 or > 1) continue;
            float depth = SceneDepth(uv.X,scene);
            if (depth <= 0) continue;
            float crossing = (-depth - surface.Z) / direction.Z;
            if (crossing < low || crossing > high) continue;
            Vector3 exact = surface + direction * crossing;
            Vector2 exactUv = Project(exact);
            if (MathF.Abs(SceneDepth(exactUv.X,scene) - depth) < .001f) return exact;
        }
        return null;
    }

    /// <summary>Projects into the diagnostic fixture's independently specified sixty-degree square camera.</summary>
    private static Vector2 Project(Vector3 point) => new Vector2(point.X,point.Y) / -point.Z * (MathF.Sqrt(3) * .5f) + new Vector2(.5f);

    /// <summary>Places an original camera sample on a known axial plane without using GPU reconstruction.</summary>
    private static Vector3 AtDepth(Vector2 uv, float depth) => new((uv.X * 2 - 1) * depth / MathF.Sqrt(3),
        (uv.Y * 2 - 1) * depth / MathF.Sqrt(3),-depth);
    #endregion
}
