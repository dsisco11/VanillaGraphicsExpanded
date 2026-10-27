using VanillaGraphicsExpanded.Rendering.RHI;
using System.Text.RegularExpressions;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises the production quantity helpers against independent double-precision expectations.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PhotometricLightingShaderTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>An inward solar ray beginning on the planet surface is occluded even when its intersection distance is zero.</summary>
    [Fact]
    public void SurfaceInwardSunlightIsOccluded()
    {
        EnsureContextValid();
        using var parameters = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource();
        parameters.EnsureCapacity(64, growExponentially: false);
        System.Numerics.Vector4[] inputs = [new(0, 1, 0, .001f), new(1, .1f, 32, 24), new(32, 16, 128, 24), new(12, 0, 0, 1)];
        parameters.UploadSubData<System.Numerics.Vector4>(inputs, 0, 64);
        parameters.BindBase(0);
        try
        {
            string include = Expand(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/atmosphere_transport.glsl"));
            float[] pixels = Render("#version 430 core\n" + include + """
                layout(location=0) out vec4 result;
                void main(){
                    vec3 origin=vec3(0,atmGround,0);
                    vec3 sun=normalize(vec3(1,-.002,0));
                    result=vec4(atmTransmission(origin,sun,12),atmGroundDistance(origin,sun));
                }
                """);
            Assert.Equal(new float[] { 0, 0, 0, 0 }, pixels[..4]);
        }
        finally { GpuShaderStorageBuffer.UnbindBase(0); }
    }

    /// <summary>Planet intersections retain metre-scale segments from the actual stored Float32 position.</summary>
    [Fact]
    public void GroundGeometryUsesDeclaredFloatPosition()
    {
        EnsureContextValid();
        string include = Expand(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/atmosphere_transport.glsl"));
        string fragment = "#version 430 core\n" + include + """
            layout(location=0) out vec4 result;
            void main(){
                precise vec3 origin=vec3(0,atmGround+.001,0);
                float ground=atmGroundDistance(origin,vec3(0,-1,0));
                result=vec4(ground,origin.y-atmGround,0,1);
            }
            """;
        float[] pixels = Render(fragment);
        float representedHeight = (float)(6360f + .001f) - 6360f;
        Assert.InRange(pixels[0], representedHeight * .99999, representedHeight * 1.00001);
        Assert.Equal(representedHeight, pixels[1]);
    }

    /// <summary>Distinct installed byte decoders retain equal calibrated torch illumination when a table value crosses their truncation boundary.</summary>
    [Fact]
    public void ActiveBlockDecoderCalibrationMatchesBothPublicationRoutes()
    {
        EnsureContextValid();
        float[] block = Enumerable.Repeat(.03f, 32).ToArray();
        block[13] = .501f; block[14] = .701f;
        float[] sun = Enumerable.Range(0, 32).Select(i => .03f + i * .03f).ToArray();
        LightingCalibration calibration = new EngineLightingCalibration().Capture(block, sun, new byte[8], new byte[8]);
        Assert.NotEqual(calibration.BlockReferenceLux, calibration.AccessorBlockReferenceLux);
        string include = Expand(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/pbr_photometric.glsl"));
        string fragment = "#version 430 core\n" + include + """
            layout(location=0) out vec4 result;
            void main(){bool accessor=int(gl_FragCoord.x)%2==1;
                result=vec4(VgeBlockIlluminance(vec3(accessor?127.0/255.0:128.0/255.0),accessor),1);}
            """;
        float[] pixels = Render(fragment, calibration: calibration);
        for (int pixel = 0; pixel < 16; pixel++)
            for (int channel = 0; channel < 3; channel++) Close(44 / (4 * Math.PI), pixels[pixel * 4 + channel]);
    }

    /// <summary>Both field decoders retain the torch anchor and suppress weak light with a vanishing slope at their neutral floor.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BlockFalloffIsSmoothForBothDecoders(bool accessor)
    {
        EnsureContextValid();
        // Deliberately different floors and coefficients expose accidental use of the terrain route.
        LightingCalibration terrain = LightingCalibration.FromDecodedTorch(new(145f / 255, 99f / 255, 71f / 255),
            new(9, 6, 4), 10f / 255, 7f / 255, 1, .03f, 1);
        LightingCalibration alternate = LightingCalibration.FromDecodedTorch(new(144f / 255, 98f / 255, 70f / 255),
            new(9, 6, 4), 9f / 255, 7f / 255, 1, .03f, 1);
        LightingCalibration calibration = terrain with { AccessorBlockReferenceLux = alternate.BlockReferenceLux,
            AccessorBlockNeutralFloor = alternate.BlockNeutralFloor };
        string include = Expand(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/pbr_photometric.glsl"));
        string fragment = "#version 430 core\n" + include + "\nconst bool accessor=" + (accessor ? "true" : "false") + ";\n" + """
            layout(location=0) out vec4 result;
            void main(){
                int column=int(gl_FragCoord.x);
                float floor=accessor?9.0/255.0:10.0/255.0;
                float epsilon=.0001;
                float peak=column==0?0.0:column==1?floor:column==2?floor+epsilon:
                    column==3?floor+2.0*epsilon:column==6?.8:1.0;
                vec3 raw=column<2?vec3(peak):peak*vec3(1,.6,.3);
                if(column==4)raw=vec3(38,23,14)/255.0;
                if(column==5)raw=(accessor?vec3(144,98,70):vec3(145,99,71))/255.0;
                result=vec4(VgeBlockIlluminance(raw,accessor),1);
            }
            """;
        float[] pixels = Render(fragment, calibration: calibration);
        for (int channel = 0; channel < 3; channel++)
        {
            Assert.Equal(0, pixels[channel]);
            Assert.Equal(0, pixels[4 + channel]);
        }
        // The fourth-power dark-end toe gives sixteen times the light at twice the distance above zero,
        // so the finite-difference slope itself tends to zero rather than retaining a linear edge.
        Assert.InRange(pixels[12] / pixels[8], 15.97f, 16.03f);
        Assert.InRange(pixels[8], 0, .0000000000001f);
        for (int column = 2; column < 8; column++)
        {
            Assert.True(pixels[column * 4] > pixels[(column - 1) * 4]);
            Assert.All(pixels[(column * 4)..(column * 4 + 3)], value => Assert.True(float.IsFinite(value)));
        }
        foreach (int column in new[] { 2, 3, 6, 7 })
        {
            Assert.InRange(pixels[column * 4 + 1] / pixels[column * 4], .59999f, .60001f);
            Assert.InRange(pixels[column * 4 + 2] / pixels[column * 4], .29999f, .30001f);
        }
        double referenceY = .2126 * pixels[20] + .7152 * pixels[21] + .0722 * pixels[22];
        Close(44 / (4 * Math.PI), (float)referenceY);
        if (!accessor)
        {
            double weakestY = .2126 * pixels[16] + .7152 * pixels[17] + .0722 * pixels[18];
            Assert.InRange(weakestY, .019228, .019232);
        }
    }

    /// <summary>The wider fade attenuates intermediate field amplitudes while bounding gain above the torch reference.</summary>
    [Fact]
    public void BlockFadeSpansIntermediateFieldWithoutExcessFullFieldGain()
    {
        EnsureContextValid();
        string include = Expand(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/pbr_photometric.glsl"));
        string fragment = "#version 430 core\n" + include + """
            layout(location=0) out vec4 result;
            const float peaks[8]=float[8](.08,.12,.2,.3,.4,.5,.8,1.0);
            void main(){result=vec4(VgeBlockIlluminance(vec3(peaks[int(gl_FragCoord.x)])),1);}
            """;
        float[] pixels = Render(fragment);
        double[] peaks = [.08, .12, .2, .3, .4, .5, .8, 1];
        for (int column = 0; column < peaks.Length; column++)
        {
            double amplitude = peaks[column] - 10.0 / 255;
            double expected = 12.8574078602199 * Math.Pow(amplitude, 4) / (Math.Pow(amplitude, 3) + .064);
            Close(expected, pixels[column * 4]);
            if (column > 0) Assert.True(pixels[column * 4] > pixels[(column - 1) * 4]);
            // The previous response retained conspicuous outer light. These amplitudes now
            // fade substantially before the existing finite field boundary is reached.
            double previousGentle = 12.3458217833822 * Math.Pow(amplitude, 1.5);
            if (column <= 3) Assert.True(pixels[column * 4] < previousGentle * .5);
            if (column == 4) Assert.True(pixels[column * 4] < previousGentle * .75);
            // A saturating toe returns toward linear at high amplitudes instead of amplifying
            // the full field like an unbounded fourth-power response anchored at a dimmer torch.
            double originalLinear = 8.982904808459303 * amplitude;
            Assert.True(pixels[column * 4] < originalLinear * 1.35);
        }
    }

    /// <summary>Terrain and accessor decoders remove their distinct neutral floors and preserve full availability.</summary>
    [Fact]
    public void SkyAvailabilityUsesDecoderSpecificEndpoints()
    {
        EnsureContextValid();
        string include = Expand(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/pbr_photometric.glsl"));
        string fragment = "#version 430 core\n" + include + """
            layout(location=0) out vec4 result;
            void main(){
                int column=int(gl_FragCoord.x),row=int(gl_FragCoord.y);
                float floor=row==0?7.0/255.0:8.0/255.0;
                float raw=column==0?0.0:column==1?floor:column==2?(floor+1.0)*.5:column==3?1.0:2.0;
                result=vec4(vec3(row==1?VgeStoredSkyAvailability(raw):VgeSkyAvailability(raw,false)),1);
            }
            """;
        float[] pixels = Render(fragment);
        for (int row = 0; row < 2; row++)
        for (int column = 0; column < 8; column++)
            Close(column < 2 ? 0 : column == 2 ? .5 : 1, pixels[(row * 8 + column) * 4]);
    }

    /// <summary>Physical punctual output obeys inverse-square illumination and engine inputs retain an explicit adapter.</summary>
    [Fact]
    public void PointAndEmissionHelpersMatchIndependentUnits()
    {
        EnsureContextValid();
        string include = Expand(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/pbr_photometric.glsl"));
        string fragment = "#version 430 core\n" + include + """
            layout(location=0) out vec4 result;
            void main(){
                int column=int(gl_FragCoord.x);
                if(column<2)result=vec4(VgePointIlluminance(vec4(vec3(1000.0/(4.0*VGE_PHOTOMETRIC_PI)),100),column==0?1.0:4.0),1);
                else if(column==2)result=vec4(VgePointIlluminance(vec4(4,4,4,0),1.0),1);
                else if(column==3)result=vec4(VgePointIlluminance(vec4(4,4,4,0),100.0),1);
                else if(column==4)result=vec4(VgeEmissionLuminance(vec3(.25,.5,1),2),1);
                else result=vec4(VgeEmissionLuminance(vec3(.25,.5,1),-1),1);
            }
            """;
        float[] pixels = Render(fragment);
        for (int channel = 0; channel < 3; channel++)
        {
            Close(1000 / (4 * Math.PI), pixels[channel]);
            Close(1000 / (16 * Math.PI), pixels[4 + channel]);
            Close(4 * LightingCalibration.EnginePointCandelaPerUnit, pixels[8 + channel]);
            Assert.Equal(0, pixels[12 + channel]);
            Close(new[] { 16d, 32d, 64d }[channel], pixels[16 + channel]);
            Assert.Equal(0, pixels[20 + channel]);
        }
    }

    /// <summary>Storage containment keeps exceptional payloads finite without declaring NaN data valid.</summary>
    [Fact]
    public void StorageProtectionPreservesValidityAndDestinationLimits()
    {
        EnsureContextValid();
        string include = Expand(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/pbr_storage_safety.glsl"));
        string fragment = "#version 430 core\n" + include + """
            layout(location=0) out vec4 result;
            void main(){
                int column=int(gl_FragCoord.x),row=int(gl_FragCoord.y);
                float inf=uintBitsToFloat(0x7f800000u),nan=uintBitsToFloat(0x7fc00000u);
                vec3 inputValue=column==0?vec3(1,2,3):column==1?vec3(65504):column==2?vec3(70000):
                    column==3?vec3(inf,-inf,1):column==4?vec3(nan,2,3):column==5?vec3(-70000):vec3(65024);
                float lower=row==0?0.0:-65504.0;
                float upper=column==6?65024.0:column==7?64512.0:65504.0;
                vec3 value;bool clipped;
                bool valid=VgeProtectLightingStorage(inputValue,lower,upper,value,clipped);
                result=vec4(value,valid?(clipped?2.0:1.0):0.0);
            }
            """;
        float[] pixels = Render(fragment);
        Assert.Equal(1, pixels[3]); Assert.Equal(1, pixels[7]);
        Assert.Equal(65504, pixels[8]); Assert.Equal(2, pixels[11]);
        Assert.Equal(65504, pixels[12]); Assert.Equal(0, pixels[13]); Assert.Equal(0, pixels[15]);
        Assert.Equal(0, pixels[19]); Assert.Equal(0, pixels[20]);
        Assert.Equal(-65504, pixels[(8 + 3) * 4 + 1]);
        Assert.Equal(-65504, pixels[(8 + 5) * 4]);
        Assert.Equal(65024, pixels[24]); Assert.Equal(1, pixels[27]);
        Assert.Equal(64512, pixels[28]); Assert.Equal(2, pixels[31]);
        Assert.All(pixels, value => Assert.True(float.IsFinite(value)));
    }

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
                double amplitude = Math.Max(peak - LightingCalibration.Default.BlockNeutralFloor, 0);
                double expected = peak > 0 ? Math.Max(inputs[column][channel], 0)
                    * Math.Pow(amplitude, 4) / (Math.Pow(amplitude, 3) + .064) / peak
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
        float[] limits = [
            AtmosphereSolarDiskStorage.MaximumScale(new(3e38f, 1.5e38f, 0), 1, 1),
            AtmosphereSolarDiskStorage.MaximumScale(new(3e38f, 1.5e38f, 0), 2, 1),
            0, 0, 0, 0,
            AtmosphereSolarDiskStorage.MaximumScale(new(1e-20f), 1, 1),
            AtmosphereSolarDiskStorage.MaximumScale(new(float.MaxValue, float.MaxValue * .5f, 0), 1, 1)];
        string scaleConstants = "const float solarScaleLimits[8]=float[8](" + string.Join(",", limits.Select(value =>
            value.ToString("R", System.Globalization.CultureInfo.InvariantCulture))) + ");\n";
        string fragment = "#version 330 core\n#extension GL_ARB_shading_language_420pack : require\n" + Expand(Path.Combine(directory, "pbr_frame_lighting.glsl"))
            + Expand(Path.Combine(directory, "pbr_photometric.glsl")) + scaleConstants + """
            layout(location=0) out vec4 result;
            void main(){
                int column=int(gl_FragCoord.x), row=int(gl_FragCoord.y);vec3 value=vec3(0);bool valid;
                if(row==0){
                    vec3 physical=column==3?vec3(-1):column==4?vec3(uintBitsToFloat(0x7fc00000u)):
                        column==5?vec3(uintBitsToFloat(0x7f800000u)):column==6?vec3(1e-20):
                        column==7?vec3(uintBitsToFloat(0x7f7fffffu),uintBitsToFloat(0x7f7fffffu)*.5,0):vec3(3e38,1.5e38,0);
                    valid=VgeStoreVisibleSolarDisk(physical,solarScaleLimits[column],value);
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
        Assert.InRange(pixels[28], 47999.9f, 48000); Close(pixels[28] * .5, pixels[29]); Assert.Equal(1, pixels[31]);
        Assert.Equal(1, pixels[8 * 4 + 3]);
        for (int column = 1; column <= 3; column++) Assert.Equal(0, pixels[(8 + column) * 4 + 3]);
        Assert.Equal(1, pixels[(8 + 4) * 4 + 3]);
    }
    #endregion

    #region Private
    /// <summary>Evaluates the current production includes using the real shared frame block layout.</summary>
    private float[] Render(string fragment, byte[]? frozenStorage = null, LightingCalibration? calibration = null)
    {
        int vertex = Compile(ShaderType.VertexShader, "#version 430 core\nlayout(location=0) in vec2 position;void main(){gl_Position=vec4(position,0,1);}");
        int pixel = Compile(ShaderType.FragmentShader, fragment);
        int program = GL.CreateProgram();
        int buffer = GL.GenBuffer();
        int storageBuffer = GL.GenBuffer();
        GL.GetInteger(GetIndexedPName.UniformBufferBinding, 12, out int previous);
        GL.GetInteger((GetIndexedPName)All.ShaderStorageBufferBinding, GpuBindingRegistry.Ssbo.FrameLighting, out int previousStorage);
        try
        {
            GL.AttachShader(program, vertex); GL.AttachShader(program, pixel); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            using var frame = new VgeFrameUniformBuffer();
            if (calibration.HasValue) frame.SetLightingCalibration(calibration.Value);
            byte[] bytes = frame.Bytes.ToArray();
            GL.BindBuffer(BufferTarget.UniformBuffer, buffer);
            GL.BufferData(BufferTarget.UniformBuffer, bytes.Length, bytes, BufferUsageHint.StaticDraw);
            GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 12, buffer);
            if (frozenStorage != null)
            {
                GL.BindBuffer(BufferTarget.ShaderStorageBuffer, storageBuffer);
                GL.BufferData(BufferTarget.ShaderStorageBuffer, frozenStorage.Length, frozenStorage, BufferUsageHint.StaticDraw);
                GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, GpuBindingRegistry.Ssbo.FrameLighting, storageBuffer);
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
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, GpuBindingRegistry.Ssbo.FrameLighting, previousStorage);
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
