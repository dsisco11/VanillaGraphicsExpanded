using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises production liquid refraction with immutable scene radiance and depth.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class WaterRefractionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Accepted opaque hits replace OIT transmission while invalid sources retain straight-through behavior.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    public void OpaqueHitsAndUnavailableSourcesHaveDefinedComposition(int scenario)
    {
        EnsureContextValid();
        int frameSize = scenario >= 14 ? 128 : 16;
        int center = frameSize / 2;
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        var program = GpuShaderPrograms.Declare(assets.Api, new LiquidShaderProgram());
        Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
        using var target = CreateMRTRenderTarget(frameSize, frameSize, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f);
        using var terrain = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var material = Texture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var depth = DynamicTexture2D.Create(frameSize, frameSize, PixelInternalFormat.R32f);
        using var aerial = DynamicTexture3D.Create(1, 1, 1, PixelInternalFormat.Rgba32f, textureTarget: TextureTarget.Texture3D);
        // Allocation contents are undefined; numerical optics require an explicitly empty atmosphere.
        aerial.UploadDataImmediate(new float[4], 0, 0, 0, 1, 1, 1, 0);
        terrain.UploadDataImmediate(new float[] { 1, 1, 1, 1 });
        material.UploadDataImmediate([.1f, 0, 0, 1]);
        const float near = .1f, far = 100;
        float receiver = scenario == 2 ? 1 : scenario == 5 ? 80 : 10;
        float deviceDepth = .5f * (1 + (far + near - 2 * far * near / receiver) / (far - near));
        depth.UploadDataImmediate(Enumerable.Repeat(deviceDepth, frameSize * frameSize).ToArray());
        using var sceneColor = DynamicTexture2D.Create(frameSize, frameSize, PixelInternalFormat.Rgba32f);
        using var sceneDepth = DynamicTexture2D.Create(frameSize, frameSize, PixelInternalFormat.R32f);
        sceneColor.UploadDataImmediate(Enumerable.Range(0, frameSize * frameSize).SelectMany(pixel => scenario == 9
            ? new float[] { 1f + (pixel % 16) * .2f, .2f, .1f, 1f }
            : new float[] { 4f, 2f, 1f, scenario == 3 ? 0f : 1f }).ToArray());
        var sceneDepthValues = Enumerable.Repeat(scenario == 4 ? float.NaN : deviceDepth, frameSize * frameSize).ToArray();
        if (scenario == 10) sceneDepthValues[8 * 16 + 9] = .5f * (1 + (far + near - 2 * far * near) / (far - near));
        sceneDepth.UploadDataImmediate(sceneDepthValues);
        using var mediumIndex = Texture2D.Create(1, 1, PixelInternalFormat.R32f);
        using var mediumRecord = Texture2D.Create(2, 1, PixelInternalFormat.Rgba32f);
        mediumIndex.UploadDataImmediate([1f]);
        mediumRecord.UploadDataImmediate(new float[8]);
        program.WaterMediumIndicesTexture = mediumIndex;
        program.WaterMediumRecordsTexture = mediumRecord;
        program.RefractionColorTexture = sceneColor;
        program.RefractionDepthTexture = sceneDepth;
        program.TerrainTexture = terrain.TextureId;
        program.MaterialParamsTexture = material;
        program.DepthTexture = depth.TextureId;
        program.AerialRadianceTexture = aerial;
        program.AerialAttenuationTexture = aerial;
        program.ShadowRanges = Vector4.Zero;
        program.SunDirection = new(Vector3.UnitY, 0);
        program.SolarIrradiance = Vector4.Zero;
        program.EnvironmentIrradiance = Vector4.Zero;
        program.DepthRangeAndFrameSize = new(near, far, frameSize, frameSize);
        program.AtlasMetrics = Vector4.One;
        program.SetCounts(0, 0);
        program.MediumLookupEnabled = scenario == 9 || scenario >= 14;
        program.AerialParameters = new(0, 0, scenario is >= 6 and <= 8 ? 1 : 0, 0);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1, near, far);
        projection.M33 = -(far + near) / (far - near);
        projection.M43 = -2 * far * near / (far - near);
        program.ProjectionMatrix = Flatten(projection);
        Matrix4x4 modelView = scenario switch
        {
            11 => Matrix4x4.CreateRotationY(1.2f),
            12 => Matrix4x4.CreateRotationX(.9f),
            13 => Matrix4x4.CreateRotationZ(.7f) * Matrix4x4.CreateRotationY(-.8f),
            _ => Matrix4x4.Identity
        };
        Matrix4x4.Invert(modelView, out Matrix4x4 worldFromView);
        Vector3 camera = new(0, 2, 0);
        if (scenario >= 14)
        {
            float pitch = new[] { 15, 45, 75 }[(scenario - 14) % 3] * MathF.PI / 180;
            float yaw = scenario >= 17 ? MathF.PI / 2 : 0;
            Vector3 forward = new(MathF.Sin(yaw) * MathF.Cos(pitch), -MathF.Sin(pitch), -MathF.Cos(yaw) * MathF.Cos(pitch));
            modelView = Matrix4x4.CreateLookAt(camera, camera + forward, Vector3.UnitY);
            Matrix4x4.Invert(modelView, out worldFromView);
            var floorDepth = new float[frameSize * frameSize];
            var floorColor = new float[frameSize * frameSize * 4];
            // Generate the opaque floor independently from camera rays, keeping its world position fixed.
            for (int pixel = 0; pixel < frameSize * frameSize; ++pixel)
            {
                Vector3 ray = Vector3.TransformNormal(new((2f * (pixel % frameSize + .5f) / frameSize - 1) / MathF.Sqrt(3),
                    (2f * (pixel / frameSize + .5f) / frameSize - 1) / MathF.Sqrt(3), -1), worldFromView);
                if (ray.Y >= 0)
                {
                    floorDepth[pixel] = 1;
                    continue;
                }
                Vector3 floor = camera + ray * (-5 / ray.Y);
                Vector4 clip = Vector4.Transform(new Vector4(floor, 1), modelView * projection);
                floorDepth[pixel] = (clip.Z / clip.W + 1) * .5f;
                floorColor[pixel * 4] = MathF.Max(.1f, 2 + floor.X * .02f);
                floorColor[pixel * 4 + 1] = .2f;
                floorColor[pixel * 4 + 2] = MathF.Max(.1f, 1 + floor.Z * .02f);
                floorColor[pixel * 4 + 3] = 1;
            }
            depth.UploadDataImmediate(floorDepth);
            sceneDepth.UploadDataImmediate(floorDepth);
            sceneColor.UploadDataImmediate(floorColor);
        }
        program.ModelViewMatrix = Flatten(modelView);
        var state = StateCache.Current;
        using var fixedFunction = state.CaptureLegacyFixedFunctionState();
        using var framebuffer = state.BindFramebufferScope(FramebufferTarget.Framebuffer, target.FboId);
        target.BindWithViewport();

        state.Apply(new GlPipelineDesc(defaultMask: GlPipelineStateMask.From(GlPipelineStateId.DepthTestEnable)
            .With(GlPipelineStateId.CullFaceEnable).With(GlPipelineStateId.ScissorTestEnable).With(GlPipelineStateId.ColorMask)
            .With(GlPipelineStateId.BlendEnable), nonDefaultMask: default));
        using var vao = GpuVao.Create("Test.WaterCapture");
        using var vertices = GpuVbo.Create(debugName: "Test.WaterCapture.Vertices");
        using var flags = GpuVbo.Create(debugName: "Test.WaterCapture.Flags");
        using var binding = vao.BindScope();
        using var indices = GpuEbo.Create(debugName: "Test.WaterCapture.Indices");
        indices.UploadIndices(new uint[] { 0, 1, 2, 3, 4, 5 });
        using (vertices.BindScope())
        {
            vertices.UploadData(new float[6 * 11]);
            vao.AttribPointer(0, 3, VertexAttribPointerType.Float, false, 44, 0);
            vao.AttribPointer(1, 2, VertexAttribPointerType.Float, false, 44, 12);
            vao.AttribPointer(2, 4, VertexAttribPointerType.Float, false, 44, 20);
            vao.AttribPointer(4, 2, VertexAttribPointerType.Float, false, 44, 36);
        }
        using (flags.BindScope())
        {
            flags.UploadData(new int[18]);
            vao.AttribIPointer(3, 1, VertexAttribIntegerType.Int, 12, 0);
            vao.AttribIPointer(5, 1, VertexAttribIntegerType.Int, 12, 4);
            vao.AttribIPointer(6, 1, VertexAttribIntegerType.Int, 12, 8);
        }
        program.RefractionEnabled = false;
        target.Clear(0, 0, 0, 0);
        DrawBoundary(2, true);
        var baseline = target[1].ReadPixelsRegion(center,center,1,1);
        var baselineAccumulation = target[3].ReadPixelsRegion(center,center,1,1);
        var edgeBaseline = target[1].ReadPixelsRegion(8,0,1,8);
        program.RefractionEnabled = true;
        Assert.Equal(1f, BitConverter.ToSingle(((ILiquidShaderProgramBindings)program).FrameParameters.Bytes.Slice(4632, 4)));
        target.Clear(0, 0, 0, 0);
        DrawBoundary(2, true);
        var actual = target[1].ReadPixelsRegion(center,center,1,1);
        Assert.All(actual, value => Assert.True(float.IsFinite(value)));
        if (scenario >= 14)
        {
            Assert.True(actual[0] < baseline[0], $"Fixed-world camera scenario {scenario} must transmit a refracted floor sample.");
            AssertFixedWorldSnellGradient(target[3].ReadPixelsRegion(center,center,1,1), baselineAccumulation,
                actual[0], baseline[0], frameSize, camera, worldFromView, modelView * projection);
        }
        else if (scenario < 2 || scenario == 6 || scenario == 9 || scenario >= 11)
        {
            // Zero revealage proves scene radiance replaces rather than re-blends the original background.
            Assert.InRange(actual[0], 0, .00001f);
            var accumulation = target[3].ReadPixelsRegion(center,center,1,1);
            Assert.True(accumulation[0] > 0);
            Assert.True(baseline[0] > .001f);
            if (scenario == 9) AssertSnellGradient(accumulation);

        }
        else if (scenario == 7)
        {
            Assert.InRange(actual[0], 0, .00001f);
            var accumulated = target[3].ReadPixelsRegion(center,center,1,1);
            Assert.All(accumulated.Take(3), value => Assert.InRange(MathF.Abs(value), 0, .00001f));
        }
        else
            for (int channel = 0; channel < 4; channel++)
                Assert.InRange(MathF.Abs(actual[channel] - baseline[channel]), 0, .00001f);
        if (scenario == 0)
        {
            // The bottom image boundary must approach the original transmission continuously.
            var edgeRevealage = target[1].ReadPixelsRegion(8,0,1,8);
            Assert.InRange(MathF.Abs(edgeRevealage[0] - edgeBaseline[0]), 0, .00001f);
            Assert.True(edgeRevealage[3 * 4] > 0);
            Assert.True(edgeRevealage[3 * 4] < edgeBaseline[3 * 4]);
            for (int row = 1; row < 8; ++row)
                Assert.True(edgeRevealage[row * 4] / edgeBaseline[row * 4]
                    <= edgeRevealage[(row - 1) * 4] / edgeBaseline[(row - 1) * 4] + .00001f);
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());

        /// <summary>Submits a sloped quad with an explicitly encoded outward normal and no wave animation.</summary>
        void DrawBoundary(float distance, bool entry)
        {
            float[] points = [-10,-10, 10,-10, 10,10, -10,-10, 10,10, -10,10];
            var data = new float[66];
            var packed = new int[18];
            for (int vertex = 0; vertex < 6; vertex++)
            {
                float x = points[vertex * 2];
                int offset = vertex * 11;
                data[offset] = x;
                data[offset + 1] = points[vertex * 2 + 1];
                data[offset + 2] = -distance + (scenario == 1 ? .05f * x : scenario == 7 ? 2f * x : scenario == 8 ? 1.2f * x : scenario == 9 ? .4f * x : 0);
                // Rotating the world and camera together preserves the same optical geometry.
                Vector3 worldPosition = Vector3.Transform(new(data[offset], data[offset + 1], data[offset + 2]), worldFromView);
                data[offset] = worldPosition.X;
                data[offset + 1] = worldPosition.Y;
                data[offset + 2] = worldPosition.Z;
                if (scenario >= 14)
                {
                    data[offset] = points[vertex * 2] * 10;
                    data[offset + 1] = 0;
                    data[offset + 2] = -points[vertex * 2 + 1] * 10;
                }
                data[offset + 3] = data[offset + 4] = .5f;
                data[offset + 8] = 1;
                packed[vertex * 3] = (7 << 22) | (entry ? 0 : 1 << 21);
                if (scenario >= 14) packed[vertex * 3] = 7 << 18;
            }
            using (vertices.BindScope()) vertices.UploadData(data);
            using (flags.BindScope()) flags.UploadData(packed);
            target.BindWithViewport();
            using var shader = program.UseScope();
            vao.DrawElements(PrimitiveType.Triangles, indices);
        }
    }
    #endregion

    #region Private
    /// <summary>Checks the independently predicted refracted floor sample and premultiplied fallback mixture.</summary>
    private static void AssertFixedWorldSnellGradient(float[] accumulation, float[] fallback, float revealage,
        float fallbackRevealage, int frameSize, Vector3 camera, Matrix4x4 worldFromView, Matrix4x4 viewProjection)
    {
        // Intersect the camera ray with the interface, apply Snell's law, then intersect the fixed floor.
        Vector3 incident = Vector3.Normalize(Vector3.TransformNormal(new(1 / ((float)frameSize * MathF.Sqrt(3)),
            1 / ((float)frameSize * MathF.Sqrt(3)), -1), worldFromView));
        Vector3 surface = camera + incident * (-camera.Y / incident.Y);
        const float eta = 1 / 1.333f;
        float cosine = -incident.Y;
        float transmittedCosine = MathF.Sqrt(1 - eta * eta * (1 - cosine * cosine));
        Vector3 direction = eta * incident + (eta * cosine - transmittedCosine) * Vector3.UnitY;
        Vector3 receiver = surface + direction * (-3 / direction.Y);
        Vector4 clip = Vector4.Transform(new Vector4(receiver, 1), viewProjection);
        int x = (int)((clip.X / clip.W * .5f + .5f) * frameSize);
        int y = (int)((clip.Y / clip.W * .5f + .5f) * frameSize);
        Assert.InRange(x, 0, frameSize - 1);
        Assert.InRange(y, 0, frameSize - 1);
        Vector3 sampleRay = Vector3.TransformNormal(new((2f * (x + .5f) / frameSize - 1) / MathF.Sqrt(3),
            (2f * (y + .5f) / frameSize - 1) / MathF.Sqrt(3), -1), worldFromView);
        Vector3 sampledFloor = camera + sampleRay * (-5 / sampleRay.Y);
        float rs = (eta * cosine - transmittedCosine) / (eta * cosine + transmittedCosine);
        float rp = (cosine - eta * transmittedCosine) / (cosine + eta * transmittedCosine);
        float rawRed = MathF.Max(.1f, 2 + sampledFloor.X * .02f) * (1 - .5f * (rs * rs + rp * rp));
        float linearRed = rawRed / (1 + rawRed);
        float encodedRed = 1.055f * MathF.Pow(linearRed, 1 / 2.4f) - .055f + (.5f / 64 - .5f) / 255;
        float alpha = 1 - revealage;
        float fallbackAlpha = 1 - fallbackRevealage;
        // Revealage identifies the confidence weight; compare the independently composed display source.
        float confidence = (alpha - fallbackAlpha) / (1 - fallbackAlpha);
        float expectedRed = (fallback[0] / fallback[3] * fallbackAlpha * (1 - confidence) + encodedRed * confidence) / alpha;
        Assert.InRange(accumulation[0] / accumulation[3], expectedRed - .003f, expectedRed + .003f);
    }

    /// <summary>Predicts the refracted receiver texel using independent vector Snell optics and display transfer.</summary>
    private static void AssertSnellGradient(float[] accumulation)
    {
        float raySlope = 1f / (16f * MathF.Sqrt(3));
        float z = -2f / (1f + .4f * raySlope);
        Vector3 surface = new(-z * raySlope, -z * raySlope, z);
        Vector3 incident = Vector3.Normalize(surface);
        Vector3 normal = Vector3.Normalize(new(-.4f, 0, 1));
        float cosine = -Vector3.Dot(normal, incident);
        const float eta = 1f / 1.333f;
        float transmittedCosine = MathF.Sqrt(1f - eta * eta * (1f - cosine * cosine));
        Vector3 direction = eta * incident + (eta * cosine - transmittedCosine) * normal;
        Vector3 receiver = surface + direction * ((-10f - z) / direction.Z);
        int pixel = (int)((receiver.X / 10f * MathF.Sqrt(3f) * .5f + .5f) * 16f);
        Assert.NotEqual(8, pixel);
        float rs = (eta * cosine - transmittedCosine) / (eta * cosine + transmittedCosine);
        float rp = (cosine - eta * transmittedCosine) / (cosine + eta * transmittedCosine);
        float transmission = 1f - .5f * (rs * rs + rp * rp);
        float rawRed = (1f + pixel * .2f) * transmission;
        float linearRed = rawRed / (1f + rawRed);
        float encodedRed = 1.055f * MathF.Pow(linearRed, 1f / 2.4f) - .055f;
        float dither = (.5f / 64f - .5f) / 255f;
        Assert.InRange(accumulation[0] / accumulation[3], encodedRed + dither - .002f, encodedRed + dither + .002f);
    }

    /// <summary>Publishes row-vector Numerics storage as engine column-major matrix bytes.</summary>
    private static float[] Flatten(Matrix4x4 value) => [value.M11,value.M12,value.M13,value.M14,
        value.M21,value.M22,value.M23,value.M24,value.M31,value.M32,value.M33,value.M34,value.M41,value.M42,value.M43,value.M44];
    #endregion
}











