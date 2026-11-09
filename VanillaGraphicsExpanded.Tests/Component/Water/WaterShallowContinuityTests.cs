using VanillaGraphicsExpanded.Tests.Component.Water.Fixtures;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Component.Water;


/// <summary>Measures subpixel receiver continuity on independently authored shallow planar and curved walls.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class WaterShallowContinuityTests(HeadlessGLFixture fixture, ITestOutputHelper output,
    WaterShallowContinuityProgramFixture sharedPrograms)
    : LumOnShaderFunctionalTestBase(fixture), IClassFixture<WaterShallowContinuityProgramFixture>
{
    #region Public API
    /// <summary>Smooth wall motion must not acquire abrupt HDR radiance from filtering or receiver selection.</summary>
    [Theory]
    [InlineData(0, true, false, false)]
    [InlineData(0, true, true, false)]
    [InlineData(0, true, false, true)]
    [InlineData(1, true, false, false)]
    [InlineData(1, false, false, true)]
    [InlineData(1, true, false, true)]
    [InlineData(3, true, false, true)]
    public void ShallowWallMotionPreservesContinuousRadiance(int quality, bool half, bool shoreline, bool curved = false)
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureShaderTestAvailable();
        const int size = 128;
        const float slope = 2;
        float wallDepth = curved ? 2.12f : shoreline ? 2.03f : 2.2f;
        float motionRange = shoreline ? .006f : .04f;
        using var color = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, size), Height = System.Math.Max(1, size), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var depth = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, size), Height = System.Math.Max(1, size), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
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
        using var halfColor = half ? RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, size/2), Height = System.Math.Max(1, size/2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource() : null;
        using var halfDepth = half ? RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, size/2), Height = System.Math.Max(1, size/2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource() : null;
        using var reduced = half ? DynamicGpuFramebuffer.CreateMRT([halfColor!,halfDepth!]) : null;
        if (half)
        {
            var reduction = Programs.Create<WaterRefractionReductionShaderProgram>();
            reduction.SourceColor = color; reduction.SourceDepth = depth;
            // Arrange: reduce the authored receiver only when exercising half-resolution sampling.
            TestFramework.RenderQuadTo(reduction,reduced!);
        }
        using var lease = sharedPrograms.Acquire();
        var programSet = lease.ProgramSet;
        var program = programSet.Diagnostic;
        var inputs = (IWaterRefractionDiagnosticBindings)program;
        inputs.Scenario = 12; inputs.Quality = quality; inputs.Budget = 1 << quality; inputs.SelectReceiver = 1;
        inputs.Normal = Vector3.Normalize(new Vector3(curved ? -.7f : -.4f,0,1));
        inputs.Color = halfColor ?? color; inputs.Depth = halfDepth ?? depth;
        using var target = CreateMRTRenderTarget(64,1,TextureFormat.Rgba32f,TextureFormat.Rgba32f,
            TextureFormat.Rgba32f,TextureFormat.Rgba32f,TextureFormat.Rgba32f,TextureFormat.Rgba32f,TextureFormat.Rgba32f);
        var filter = programSet.Filter;
        var filterInputs = (IWaterReceiverFilterBindings)filter;
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/3,1,.1f,100);
        projection.M33 = -100.1f/99.9f; projection.M43 = -20f/99.9f;
        // Arrange: verify the authored camera projection is invertible before binding the rendering inputs.
        Assert.True(Matrix4x4.Invert(projection,out var inverse));
        using var frameCamera = TestFrameCamera.CreateFromProjection(projection, size, size);
        program.FrameInputs = frameCamera;
        filter.FrameInputs = frameCamera;
        filterInputs.Normal = inputs.Normal; filterInputs.Color = halfColor ?? color; filterInputs.Depth = halfDepth ?? depth;
        using var filterTarget = CreateMRTRenderTarget(64,1,TextureFormat.Rgba32f,TextureFormat.Rgba32f,TextureFormat.Rgba32f);
        float maximumJump = 0, maximumAssociationError = 0;
        float maximumFilterJump = 0;
        int switches = 0, previousMethod = 0, rays = 0, uv = 0;
        int largestJumpMotion = 0, largestJumpOldMethod = 0, largestJumpNewMethod = 0;
        float? previousRadiance = null;
        float? previousFilterRadiance = null;
        // Act and assert: submit each motion batch and validate its per-record receiver, position, and radiance evidence.
        for (int batchStart = 0; batchStart <= 200; batchStart += 64)
        {
            // Author each original motion input once in sweep order; accumulated adjacency metrics remain outside the chunk loop.
            int batchCount = Math.Min(64, 201 - batchStart);
            var surfaces = new Vector4[batchCount];
            var sampleUvs = quality == 0 ? new Vector4[batchCount] : Array.Empty<Vector4>();
            for (int local = 0; local < batchCount; local++)
            {
                int motion = batchStart + local;
                Vector3 surface = new(-motionRange + motion * motionRange / 100,0,-2);
                surfaces[local] = new(surface, 0);
                if (quality == 0) sampleUvs[local] = new(surface.X/2*MathF.Sqrt(3)*.5f+.5f,.5f,0,0);
            }
            program.SetMotionBatch(surfaces);
            TestFramework.RenderQuadTo(program,target,scissor: (0,0,batchCount,1));
            // Scissor and region readback use only populated records, including the nine-sample tail.
            float[] decisions = target[0].ReadPixelsRegion(0,0,batchCount,1);
            float[] selections = target[3].ReadPixelsRegion(0,0,batchCount,1);
            float[] positions = target[4].ReadPixelsRegion(0,0,batchCount,1);
            float[] radiances = target[5].ReadPixelsRegion(0,0,batchCount,1);
            float[]? operations = quality == 0 ? target[6].ReadPixelsRegion(0,0,batchCount,1) : null;
            float[]? filterRadiances = null;
            if (quality == 0)
            {
                // Isolate direct bilateral sampling from ray/UV selection using the same CPU-authored records.
                filter.SetMotionBatch(surfaces, sampleUvs);
                TestFramework.RenderQuadTo(filter,filterTarget,scissor: (0,0,batchCount,1));
                filterRadiances = filterTarget[1].ReadPixelsRegion(0,0,batchCount,1);
            }
            for (int local = 0; local < batchCount; local++)
            {
                int motion = batchStart + local;
                int offset = local * 4;
                Vector4 decision = ReadRecord(decisions,offset);
                Vector4 selection = ReadRecord(selections,offset);
                Vector4 position = ReadRecord(positions,offset);
                Vector4 radiance = ReadRecord(radiances,offset);
                Vector3 surface = new(surfaces[local].X,surfaces[local].Y,surfaces[local].Z);
                Vector4 work = operations is null ? new(float.NaN) : ReadRecord(operations,offset);
                float filteredRadiance = filterRadiances is null ? float.NaN : filterRadiances[offset];
                try
                {
                    Assert.Equal(1,decision.X); Assert.Equal(1,decision.W);
                    int method = (int)selection.X;
                    if (quality == 0)
                    {
                        Vector3 selectedPosition=new(position.X,position.Y,position.Z);
                        Assert.True(Vector3.Dot(selectedPosition-surface,inputs.Normal)<0,
                            "Bounded continuation must retain submerged receiver coverage.");
                        // Two bounded footprints plus reduced-source coverage recovery never become a ray search.
                        Assert.InRange(selection.Y, 0, 2);
                        Assert.InRange(work.X, 0, 15);
                        Assert.InRange(work.Y, 0, 4);
                        if (previousFilterRadiance is { } previousFilter)
                            maximumFilterJump = MathF.Max(maximumFilterJump,MathF.Abs(filteredRadiance-previousFilter));
                        previousFilterRadiance = filteredRadiance;
                    }
                    if (method == 1) rays++; else { Assert.Equal(2,method); uv++; }
                    if (!curved) Assert.InRange(MathF.Abs(position.Z + wallDepth - slope * position.X),0,.0001f);
                    if (previousMethod != 0 && previousMethod != method) switches++;
                    if (previousRadiance is { } previous)
                    {
                        float jump = MathF.Abs(radiance.X-previous);
                        if (jump > maximumJump)
                        {
                            maximumJump = jump; largestJumpMotion = motion;
                            largestJumpOldMethod = previousMethod; largestJumpNewMethod = method;
                        }
                    }
                    maximumAssociationError = MathF.Max(maximumAssociationError,MathF.Abs(radiance.X-(8+4*position.X)));
                    previousMethod = method; previousRadiance = radiance.X;
                }
                catch (Exception)
                {
                    output.WriteLine($"water-motion-failure quality={quality} half={half} shoreline={shoreline} curved={curved} motion={motion} batchStart={batchStart} local={local} surface={surface} decision={decision} selection={selection} position={position} radiance={radiance} operations={work} filterRadiance={filteredRadiance:R}");
                    throw;
                }
            }
        }
        program.SetMotionBatch(ReadOnlySpan<Vector4>.Empty);
        if (quality == 0) filter.SetMotionBatch(ReadOnlySpan<Vector4>.Empty,ReadOnlySpan<Vector4>.Empty);
        output.WriteLine($"quality={quality} half={half} shoreline={shoreline} curved={curved}: ray={rays}, uv={uv}, switches={switches}, maxAdjacentHdrJump={maximumJump:R}, maxAssociationError={maximumAssociationError:R}, maxDirectFilterJump={maximumFilterJump:R}");
        output.WriteLine($"Largest jump at motion {largestJumpMotion}, method {largestJumpOldMethod}->{largestJumpNewMethod}.");
        if (maximumJump > .01f)
        {
            // Replay only the failing adjacent pair to expose the existing kernel's triangle/fallback
            // work and selected UV. The measured sweep and its numerical assertions remain unchanged.
            for (int motion = Math.Max(0, largestJumpMotion - 1); motion <= largestJumpMotion; motion++)
            {
                inputs.Surface = new(-motionRange + motion * motionRange / 100, 0, -2);
                TestFramework.RenderQuadTo(program, target, scissor: (0,0,1,1));
                output.WriteLine($"receiver-replay motion={motion} selection={string.Join(',', target[3].ReadPixelsRegion(0,0,1,1))} "
                    + $"position={string.Join(',', target[4].ReadPixelsRegion(0,0,1,1))} radiance={string.Join(',', target[5].ReadPixelsRegion(0,0,1,1))} "
                    + $"operations={string.Join(',', target[6].ReadPixelsRegion(0,0,1,1))}");
            }
        }
        // Assert the full motion sweep stays continuous in receiver association and radiance.
        Assert.InRange(maximumAssociationError,0,.0001f);
        if (quality == 0) Assert.InRange(maximumFilterJump,0,.01f);
        // A 0.4 mm surface shift on this bounded wall moves radiance by less
        // than .004; .01 permits reconstruction error without accepting texel stairs.
        Assert.InRange(maximumJump,0,.01f);
    }
    #endregion

    #region Private
    /// <summary>Reads one four-channel attachment record without allocating a per-record array.</summary>
    private static Vector4 ReadRecord(float[] records,int offset)
        => new(records[offset],records[offset+1],records[offset+2],records[offset+3]);
    #endregion
}
