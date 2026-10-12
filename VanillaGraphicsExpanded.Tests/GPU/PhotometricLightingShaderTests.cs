using System.Text.RegularExpressions;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises the production quantity helpers against independent double-precision expectations.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PhotometricLightingShaderTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Block conversion retains chroma, neutral zero and monotonic intensity without camera exposure.</summary>
    [Fact]
    public void BlockAndPersistentHelpersMatchPhysicalContracts()
    {
        EnsureContextValid();
        string include = Expand(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/pbr_photometric.glsl"));
        string fragment = "#version 430 core\n" + include + """
            layout(location=0) out vec4 result;
            void main(){
                int column=int(gl_FragCoord.x), row=int(gl_FragCoord.y);
                vec3 raw=column==0?vec3(0):column==1?vec3(10.0/255.0):
                    column==2?vec3(.2,.1,.05):column==3?vec3(.4,.2,.1):
                    column==4?vec3(145,99,71)/255.0:column==5?vec3(-1,.4,.2):
                    column==6?vec3(1):vec3(1048064);
                if(row==0)result=vec4(VgeBlockIlluminance(raw),1);
                else if(column<6)result=vec4(VgeDiffuseIncident(VgeBlockIlluminance(raw)),1);
                else {vec3 physical=column==6?vec3(-32,131072,1048064):vec3(1048065);
                    result=vec4(VgeDecodePersistentLight(VgeEncodePersistentLight(physical)),
                        VgePersistentLightAdmitted(physical,true)?1:0);}
            }
            """;
        float[] pixels = Render(fragment);
        double[][] inputs = [ [0,0,0], [10.0/255,10.0/255,10.0/255], [.2,.1,.05], [.4,.2,.1],
            [145.0/255,99.0/255,71.0/255], [-1,.4,.2], [1,1,1], [1048064,1048064,1048064] ];
        for (int column = 0; column < inputs.Length; column++)
        {
            double peak = inputs[column].Select(x => Math.Max(x, 0)).Max();
            for (int channel = 0; channel < 3; channel++)
            {
                double expected = peak > 0 ? Math.Max(inputs[column][channel], 0)
                    * Math.Max(peak - LightingCalibration.Default.BlockNeutralFloor, 0) / peak
                    * LightingCalibration.Default.BlockReferenceLux : 0;
                Close(expected, pixels[column * 4 + channel]);
                if (column < 6) Close(expected / Math.PI, pixels[(8 + column) * 4 + channel]);
            }
        }
        double torchY = .2126 * pixels[16] + .7152 * pixels[17] + .0722 * pixels[18];
        Close(44 / (4 * Math.PI), (float)torchY);
        Assert.True(pixels[12] > pixels[8]);
        Assert.Equal(-32, pixels[(8 + 6) * 4]);
        Assert.Equal(131072, pixels[(8 + 6) * 4 + 1]);
        Assert.Equal(1048064, pixels[(8 + 6) * 4 + 2]);
        Assert.Equal(1, pixels[(8 + 6) * 4 + 3]);
        Assert.Equal(0, pixels[(8 + 7) * 4 + 3]);
    }

    /// <summary>Signed source overshoot is bounded without changing homogeneous scattering or transmission.</summary>
    [Fact]
    public void BoundarySourceBoundPreservesHomogeneousTransport()
    {
        EnsureContextValid();
        string include = Expand(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/liquids/boundary_transport.glsl"));
        string fragment = "#version 430 core\n" + include + """
            layout(location=0) out vec4 result;
            void main(){
                int column=int(gl_FragCoord.x), row=int(gl_FragCoord.y);
                float tau=column==0?0.0:column==1?.0001:column==2?.5:column==3?10.0:1.0;
                vec3 upper=vec3(.2,.3,.4), q=upper*tau;
                if(column==4)q*=100.0;
                if(column==5)q=-q;
                if(column==6)upper.x=-1.0;
                if(column==7){upper=vec3(3e38);tau=100.0;}
                vec3 transmission,scatter;float length;
                bool valid=VgeWaterBoundaryTransport(vec4(vec3(tau),1),vec4(q,0),vec3(0),upper,
                    false,1000.0,transmission,scatter,length);
                result=vec4(row==0?scatter:transmission,valid?1:0);
            }
            """;
        float[] pixels = Render(fragment);
        double[] depths = [0, .0001, .5, 10, 1, 1];
        double[] source = [.2, .3, .4];
        for (int column = 0; column < 6; column++)
        for (int channel = 0; channel < 3; channel++)
        {
            double transmission = Math.Exp(-depths[column]);
            Close(column == 5 ? 0 : source[channel] * (1 - transmission), pixels[column * 4 + channel]);
            Close(transmission, pixels[(8 + column) * 4 + channel]);
            Assert.Equal(1, pixels[column * 4 + 3]);
        }
        Assert.Equal(0, pixels[6 * 4 + 3]);
        Assert.Equal(0, pixels[7 * 4 + 3]);
    }

    /// <summary>Frame helpers decode physical values once and reject unsupported temporal or generation inputs.</summary>
    [Fact]
    public void FrozenStorageHelpersKeepHistoryAndPhysicalDomainsSeparate()
    {
        EnsureContextValid();
        string include = Expand(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/pbr_frame_lighting.glsl"));
        string fragment = "#version 430 core\n" + include + """
            layout(location=0) out vec4 result;
            void main(){
                int column=int(gl_FragCoord.x), row=int(gl_FragCoord.y);vec3 value=vec3(0);bool valid=true;
                if(column==0)value=VgeStoreFrameLight(vec3(32));
                else if(column==1)value=VgePhysicalFrameLight(vec3(8));
                else if(column==2)value=VgePersistentToFrameLight(vec3(2));
                else if(column<6)valid=VgeRescaleFrameHistory(vec3(2),column==3?.0625:column==4?0.0:row==1?3e38:.00001,32.0,value);
                else valid=VgeFrameLightingValid(42u,column==6?7u:8u);
                result=vec4(value,valid?1:0);
            }
            """;
        byte[] publication = new byte[32];
        System.Buffer.BlockCopy(new float[] { .25f, 4, 100, 1 }, 0, publication, 0, 16);
        System.Buffer.BlockCopy(new uint[] { 42, 7, 0, 0 }, 0, publication, 16, 16);
        float[] pixels = Render(fragment, publication);
        Assert.Equal(8, pixels[0]); Assert.Equal(32, pixels[4]); Assert.Equal(8, pixels[8]);
        Assert.Equal(8, pixels[12]); Assert.Equal(1, pixels[15]);
        Assert.Equal(0, pixels[19]); Assert.Equal(0, pixels[23]);
        Assert.Equal(1, pixels[27]); Assert.Equal(0, pixels[31]);
        Assert.Equal(0, pixels[(8 + 5) * 4 + 3]);
    }

    /// <summary>Visible disk storage respects glare headroom before multiplication and rejects unsupported source metadata.</summary>
    [Fact]
    public void VisibleDiskAndDirectionalAdmissionRespectDeclaredRanges()
    {
        EnsureContextValid();
        string directory = Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes");
        string fragment = "#version 430 core\n" + Expand(Path.Combine(directory, "pbr_frame_lighting.glsl"))
            + Expand(Path.Combine(directory, "pbr_photometric.glsl")) + """
            layout(location=0) out vec4 result;
            void main(){
                int column=int(gl_FragCoord.x), row=int(gl_FragCoord.y);vec3 value=vec3(0);bool valid;
                if(row==0){
                    vec3 physical=column==3?vec3(-1):column==4?vec3(uintBitsToFloat(0x7fc00000u)):
                        column==5?vec3(uintBitsToFloat(0x7f800000u)):column==6?vec3(1e-20):vec3(3e38,1.5e38,0);
                    valid=VgeStoreVisibleSolarDisk(physical,column==1?2.0:column==2?3.0:1.0,1.0,value);
                }else{
                    vec3 physical=column==0?vec3(131072):column==1?vec3(131073):column==2?vec3(-1):
                        column==3?vec3(uintBitsToFloat(0x7fc00000u)):vec3(0);
                    valid=VgeDirectionalLightAdmitted(physical);
                }
                result=vec4(value,valid?1:0);
            }
            """;
        byte[] publication = new byte[32];
        System.Buffer.BlockCopy(new float[] { 4, .25f, 100, 1 }, 0, publication, 0, 16);
        System.Buffer.BlockCopy(new uint[] { 42, 7, 0, 0 }, 0, publication, 16, 16);
        float[] pixels = Render(fragment, publication);
        Assert.InRange(pixels[0], 47999.9f, 48000); Assert.InRange(pixels[4], 15999.9f, 16000);
        Close(pixels[0] * .5, pixels[1]); Close(pixels[4] * .5, pixels[5]);
        Assert.Equal(1, pixels[3]); Assert.Equal(1, pixels[7]);
        for (int column = 2; column <= 5; column++) Assert.Equal(0, pixels[column * 4 + 3]);
        Assert.InRange(pixels[24], 3.999e-20f, 4.001e-20f); Assert.Equal(1, pixels[27]);
        Assert.Equal(1, pixels[8 * 4 + 3]);
        for (int column = 1; column <= 3; column++) Assert.Equal(0, pixels[(8 + column) * 4 + 3]);
        Assert.Equal(1, pixels[(8 + 4) * 4 + 3]);
    }
    #endregion

    #region Private
    /// <summary>Evaluates the current production includes using the real shared frame block layout.</summary>
    private float[] Render(string fragment, byte[]? frozenStorage = null)
    {
        int vertex = Compile(ShaderType.VertexShader, "#version 430 core\nlayout(location=0) in vec2 position;void main(){gl_Position=vec4(position,0,1);}");
        int pixel = Compile(ShaderType.FragmentShader, fragment);
        int program = GL.CreateProgram();
        int buffer = GL.GenBuffer();
        int storageBuffer = GL.GenBuffer();
        GL.GetInteger(GetIndexedPName.UniformBufferBinding, 12, out int previous);
        GL.GetInteger((GetIndexedPName)All.ShaderStorageBufferBinding, 3, out int previousStorage);
        try
        {
            GL.AttachShader(program, vertex); GL.AttachShader(program, pixel); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            using var frame = new VgeFrameUniformBuffer();
            byte[] bytes = frame.Bytes.ToArray();
            GL.BindBuffer(BufferTarget.UniformBuffer, buffer);
            GL.BufferData(BufferTarget.UniformBuffer, bytes.Length, bytes, BufferUsageHint.StaticDraw);
            GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 12, buffer);
            if (frozenStorage != null)
            {
                GL.BindBuffer(BufferTarget.ShaderStorageBuffer, storageBuffer);
                GL.BufferData(BufferTarget.ShaderStorageBuffer, frozenStorage.Length, frozenStorage, BufferUsageHint.StaticDraw);
                GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 3, storageBuffer);
            }
            using var target = CreateRenderTarget(8, 2, PixelInternalFormat.Rgba32f);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, target.FboId); GL.Viewport(0, 0, 8, 2);
            GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.ScissorTest); GL.ColorMask(true, true, true, true); GL.UseProgram(program);
            RenderFullscreenQuad(program);
            return ReadPixelsFloat(target);
        }
        finally
        {
            GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 12, previous);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 3, previousStorage);
            GL.DeleteBuffer(storageBuffer); GL.DeleteBuffer(buffer); GL.DeleteProgram(program); GL.DeleteShader(vertex); GL.DeleteShader(pixel);
            StateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Expands local guarded production includes without replacing their quantity equations.</summary>
    private static string Expand(string path) => Regex.Replace(File.ReadAllText(path), "@import \\\"([^\\\"]+)\\\"",
        match => Expand(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, match.Groups[1].Value))));

    /// <summary>Compiles a diagnostic stage and includes the driver's explanation on failure.</summary>
    private static int Compile(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type); GL.ShaderSource(shader, source); GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
        Assert.True(compiled != 0, GL.GetShaderInfoLog(shader));
        return shader;
    }

    /// <summary>Bounds arithmetic error without accepting nonfinite physical quantities.</summary>
    private static void Close(double expected, float actual)
    {
        Assert.True(float.IsFinite(actual));
        double tolerance = 2e-6 * Math.Max(1, Math.Abs(expected));
        Assert.InRange(actual, expected - tolerance, expected + tolerance);
    }
    #endregion
}
