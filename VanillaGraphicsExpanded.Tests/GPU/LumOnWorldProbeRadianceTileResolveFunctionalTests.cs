using VanillaGraphicsExpanded.LumOn;
using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumOnWorldProbeRadianceTileResolveFunctionalTests : LumOnShaderFunctionalTestBase
{
    public LumOnWorldProbeRadianceTileResolveFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }

    [Fact]
    public void RadianceTileResolve_WritesExpectedTexel()
    {
        EnsureShaderTestAvailable();

        const int width = 32;
        const int height = 16;

        // Pick a texel that is definitely inside bounds.
        const int atlasX = 7;
        const int atlasY = 9;

        // RGBA16F output; keep values representable and stable across drivers.
        const float r = 0.75f;
        const float g = 0.125f;
        const float b = 0.0f;
        float a = (float)Math.Log(3.0); // example encoded hit distance

        var program = Programs.Create<LumOnWorldProbeRadianceTileResolveShaderProgram>();
        int vao = 0;
        int vbo = 0;

        try
        {

            using var output = TestFramework.CreateTestGBuffer(width, height, PixelInternalFormat.Rgba16f);

            output.BindWithViewport();
            GL.ClearColor(0f, 0f, 0f, 0f);
            GL.Clear(ClearBufferMask.ColorBufferBit);

            // Vertex: (inAtlasCoord.xy, inRadiance.rgba)
            float[] vertexData =
            [
                atlasX, atlasY,
                r, g, b, a,
            ];

            vao = GL.GenVertexArray();
            vbo = GL.GenBuffer();

            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);

            GL.BufferData(BufferTarget.ArrayBuffer, vertexData.Length * sizeof(float), vertexData, BufferUsageHint.StaticDraw);

            int stride = 6 * sizeof(float);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, 0);

            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, stride, 2 * sizeof(float));

            using var use = program.UseScope();
            program.AtlasSize = new(width, height);

            GL.DrawArrays(PrimitiveType.Points, 0, 1);

            GL.BindVertexArray(0);

            // Validate output texel.
            var pixels = output[0].ReadPixels();
            int idx = (atlasY * width + atlasX) * 4;

            Assert.Equal(r, pixels[idx + 0], 2);
            Assert.Equal(g, pixels[idx + 1], 2);
            Assert.Equal(b, pixels[idx + 2], 2);
            Assert.Equal(a, pixels[idx + 3], 2);
        }
        finally
        {
            if (vbo != 0) GL.DeleteBuffer(vbo);
            if (vao != 0) GL.DeleteVertexArray(vao);
        }
    }
}
