using System.Numerics;
using System.Text.RegularExpressions;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks imported terrain eye vectors, parallax and normal fading under equivalent render origins.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class TerrainEyeRelativeShadingTests : RenderTestBase
{
    /// <summary>Uses the shared graphics context for the linked terrain-style shader.</summary>
    public TerrainEyeRelativeShadingTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Eye-relative material shading
    /// <summary>Rotation and translated render origins preserve eye distance, POM displacement and normal attenuation.</summary>
    [Theory]
    [InlineData(0f, 0f, 0f, 0f)]
    [InlineData(.6f, .35f, 1.6f, -.2f)]
    [InlineData(-.8f, 20f, -9f, 12f)]
    public void ImportedMaterialHelpersPreserveEquivalentEyeGeometry(float angle, float eyeX, float eyeY, float eyeZ)
    {
        EnsureContextValid();
        using var drawing = new ShaderTestFramework();
        using var target = drawing.CreateTestGBuffer(2, 2, PixelInternalFormat.Rgba32f);
        using var texture = drawing.CreateTexture(64, 64, PixelInternalFormat.Rgba32f,
            Enumerable.Range(0,64*64).SelectMany(_ => new[] {.8f,.5f,.9f,.25f}).ToArray());
        using var indices = drawing.CreateTexture(1,1,PixelInternalFormat.R32f,[1f]);
        using var records = drawing.CreateTexture(2,1,PixelInternalFormat.Rgba32f,[0,0,1,1,.04f,0,0,0]);
        using var inputs = new PackedUniformBuffer(96);
        byte[] inputBytes = new byte[96];
        int vs = BuiltShaderFixture.LoadFixture("tests/eye-relative.vsh", ShaderType.VertexShader), fs = BuiltShaderFixture.LoadFixture("tests/eye-relative.fsh", ShaderType.FragmentShader);
        int program = GL.CreateProgram(), vao = GL.GenVertexArray();
        try
        {
            GL.AttachShader(program, vs); GL.AttachShader(program, fs); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            var layout = BuiltShaderFixture.Layout(program, "tests/eye-relative.vsh", "tests/eye-relative.fsh");
            GL.UseProgram(program); GL.BindVertexArray(vao);
            GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, texture.TextureId);
            GL.Uniform1(layout.GetUniformLocation(program,"vge_normalDepthTex"),0);
            indices.Bind(1); records.Bind(2);
            GL.Uniform1(layout.GetUniformLocation(program,"vge_displacementTex"),1);
            GL.Uniform1(layout.GetUniformLocation(program,"vge_displacementRecords"),2);
            GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
            foreach(float distance in new[] {10f,16f})
            {
                Vector3 point = new(2,1,-distance);
                float[] baseline = Render(Vector3.Zero, 0, point, 1);
                Vector3 eye = new(eyeX,eyeY,eyeZ);
                float[] vector = Render(eye, angle, point + eye, 0);
                Assert.InRange(vector[0],-2.00002f,-1.99998f);
                Assert.InRange(vector[1],-1.00002f,-.99998f);
                Assert.InRange(vector[2],distance-.00002f,distance+.00002f);
                Assert.InRange(vector[3],point.Length()-.00002f,point.Length()+.00002f);
                float[] shaded = Render(eye, angle, point + eye, 1);
                for(int i=0;i<4;i++) Assert.InRange(shaded[i],baseline[i]-.00002f,baseline[i]+.00002f);
                if(distance==10) Assert.True(MathF.Abs(baseline[0]-.5f)>.00001f,"POM must displace the authored nonflat surface.");
                float t=Math.Clamp((point.Length()-8)/16,0,1), blend=1-t*t*(3-2*t);
                Vector3 normal=Vector3.Normalize(Vector3.Lerp(Vector3.UnitZ,new(.6f,0,.8f),blend));
                Assert.InRange(shaded[2],normal.X*.5f+.5f-.00002f,normal.X*.5f+.5f+.00002f);
            }

            /// <summary>Uploads the shared linked uniform once and samples the actual material helpers.</summary>
            float[] Render(Vector3 eye, float radians, Vector3 position, int mode)
            {
                float c=MathF.Cos(radians),s=MathF.Sin(radians);
                float[] view=[c,s,0,0,-s,c,0,0,0,0,1,0,-c*eye.X+s*eye.Y,-s*eye.X-c*eye.Y,-eye.Z,1];
                System.Runtime.InteropServices.MemoryMarshal.AsBytes(view.AsSpan()).CopyTo(inputBytes);
                UboPacking.WriteVec3(inputBytes, 64, position.X, position.Y, position.Z);
                UboPacking.WriteInt32(inputBytes, 76, mode);
                inputs.SetBytes(inputBytes);
                Assert.True(inputs.TryBindToSlot(GpuBindingRegistry.Ubo.ShaderInputs));
                target.BindWithViewport(); GL.DrawArrays(PrimitiveType.Triangles,0,3);
                float[] pixel=new float[4]; GL.ReadPixels(0,0,1,1,PixelFormat.Rgba,PixelType.Float,pixel);
                return pixel;
            }
        }
        finally
        {
            GL.UseProgram(0); GL.DeleteVertexArray(vao); GL.DeleteProgram(program); GL.DeleteShader(vs); GL.DeleteShader(fs);
            StateCache.Current.InvalidateAll();
        }
    }
    #endregion

    #region Shader fixtures

    /// <summary>Expands relative production includes without altering their implementations.</summary>
    internal static string Expand(string path) => Regex.Replace(File.ReadAllText(path),"@import\\s+\"([^\"]+)\"",
        match=>Expand(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!,match.Groups[1].Value))));
    #endregion
}
