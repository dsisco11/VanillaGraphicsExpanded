using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Text.RegularExpressions;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Assertions;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises the production quantity helpers against independent double-precision expectations.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
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
            var image = Render("#version 430 core\n" + include + """
                layout(location=0) out vec4 result;
                void main(){
                    vec3 origin=vec3(0,atmGround,0);
                    vec3 sun=normalize(vec3(1,-.002,0));
                    result=vec4(atmTransmission(origin,sun,12),atmGroundDistance(origin,sun));
                }
                """);
            Assert.Equal(new float[] { 0, 0, 0, 0 }, GpuLocalRead.NativeColorTuple(image, 0, 0));
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
        var image = Render(fragment);
        var pixels = GpuLocalRead.NativeColorTuple(image, 0, 0);
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
        var image = Render(fragment, calibration: calibration);
        double expected = 44 / (4 * Math.PI), tolerance = 2e-6 * Math.Max(1, Math.Abs(expected));
        ImageAssertions.Statistics.Inspect(image, expected - tolerance, expected + tolerance, channelMask: 7).AssertFiniteRange();
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
        var image = Render(fragment, calibration: calibration);
        ImageAssertions.Predicates.Predicate(image, PhotometricPredicates.BlockFalloff,
            parameters: new(accessor ? 1 : 0, 0, 0, 0)).AssertEqual();
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
        var image = Render(fragment);
        double[] peaks = [.08, .12, .2, .3, .4, .5, .8, 1];
        List<GpuScalarInterval> bounds = [];
        for (int column = 0; column < peaks.Length; column++)
        {
            double amplitude = peaks[column] - 10.0 / 255;
            double expected = 12.8574078602199 * Math.Pow(amplitude, 4) / (Math.Pow(amplitude, 3) + .064);
            bounds.Add(Near(column * 4, expected));
            // Preserve strict upper bounds independently of the shader's response equation.
            double previousGentle = 12.3458217833822 * Math.Pow(amplitude, 1.5);
            if (column <= 3) bounds.Add(new(column, 0, double.NegativeInfinity, Math.BitDecrement(previousGentle * .5)));
            if (column == 4) bounds.Add(new(column, 0, double.NegativeInfinity, Math.BitDecrement(previousGentle * .75)));
            double originalLinear = 8.982904808459303 * amplitude;
            bounds.Add(new(column, 0, double.NegativeInfinity, Math.BitDecrement(originalLinear * 1.35)));
        }
        ImageAssertions.Comparisons.CompareIntervals(image, bounds).AssertEqual();
        ImageAssertions.Predicates.Predicate(image, PhotometricPredicates.IncreasingRed).AssertEqual();
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
        var image = Render(fragment);
        ImageAssertions.Comparisons.CompareRegions(image,
            [new(new(default, 0, 0, 0, 2, 2, 1), new(0), 1),
             new(new(default, 2, 0, 0, 1, 2, 1), new(.5f), 1),
             new(new(default, 3, 0, 0, 5, 2, 1), new(1), 1)], tolerance: 2e-6).AssertEqual();
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
        var image = Render(fragment);
        List<GpuScalarInterval> bounds = [];
        for (int channel = 0; channel < 3; channel++)
        {
            bounds.Add(Near(channel, 1000 / (4 * Math.PI)));
            bounds.Add(Near(4 + channel, 1000 / (16 * Math.PI)));
            bounds.Add(Near(8 + channel, 4 * LightingCalibration.EnginePointCandelaPerUnit));
            bounds.Add(new(3, channel, 0, 0));
            bounds.Add(Near(16 + channel, new[] { 16d, 32d, 64d }[channel]));
            bounds.Add(new(5, channel, 0, 0));
        }
        ImageAssertions.Comparisons.CompareIntervals(image, bounds).AssertEqual();
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
        var image = Render(fragment);
        ImageAssertions.Comparisons.CompareIntervals(image,
            [Exact(3, 1), Exact(7, 1), Exact(8, 65504), Exact(11, 2), Exact(12, 65504),
             Exact(13, 0), Exact(15, 0), Exact(19, 0), Exact(20, 0), Exact((8 + 3) * 4 + 1, -65504),
             Exact((8 + 5) * 4, -65504), Exact(24, 65024), Exact(27, 1), Exact(28, 64512), Exact(31, 2)]).AssertEqual();
        ImageAssertions.Statistics.Inspect(image).AssertFiniteRange();
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
        var image = Render(fragment);
        double[][] inputs = [ [0,0,0], [10.0/255,10.0/255,10.0/255], [.2,.1,.05], [.4,.2,.1],
            [145.0/255,99.0/255,71.0/255], [-1,.4,.2], [1,1,1], [1048064,1048064,1048064] ];
        List<GpuScalarInterval> bounds = [];
        for (int column = 0; column < inputs.Length; column++)
        {
            double peak = inputs[column].Select(x => Math.Max(x, 0)).Max();
            for (int channel = 0; channel < 3; channel++)
            {
                double amplitude = Math.Max(peak - LightingCalibration.Default.BlockNeutralFloor, 0);
                double expected = peak > 0 ? Math.Max(inputs[column][channel], 0)
                    * Math.Pow(amplitude, 4) / (Math.Pow(amplitude, 3) + .064) / peak
                    * LightingCalibration.Default.BlockReferenceLux : 0;
                bounds.Add(Near(column * 4 + channel, expected));
                if (column < 6) bounds.Add(Near((8 + column) * 4 + channel, expected / Math.PI));
            }
        }
        bounds.AddRange([Exact((8 + 6) * 4, -32), Exact((8 + 6) * 4 + 1, 131072),
            Exact((8 + 6) * 4 + 2, 1048064), Exact((8 + 6) * 4 + 3, 1), Exact((8 + 7) * 4 + 3, 0)]);
        ImageAssertions.Comparisons.CompareIntervals(image, bounds).AssertEqual();
        var torch = GpuLocalRead.NativeColorTuple(image, 4, 0);
        double torchY = .2126 * torch[0] + .7152 * torch[1] + .0722 * torch[2];
        Close(44 / (4 * Math.PI), (float)torchY);
        Assert.True(GpuLocalRead.NativeColorTuple(image, 3, 0)[0] > GpuLocalRead.NativeColorTuple(image, 2, 0)[0]);
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
        var image = Render(fragment);
        double[] depths = [0, .0001, .5, 10, 1, 1];
        double[] source = [.2, .3, .4];
        List<GpuScalarInterval> bounds = [];
        for (int column = 0; column < 6; column++)
        for (int channel = 0; channel < 3; channel++)
        {
            double transmission = Math.Exp(-depths[column]);
            bounds.Add(Near(column * 4 + channel, column == 5 ? 0 : source[channel] * (1 - transmission)));
            bounds.Add(Near((8 + column) * 4 + channel, transmission));
            bounds.Add(Exact(column * 4 + 3, 1));
        }
        bounds.Add(Exact(6 * 4 + 3, 0));
        bounds.Add(Exact(7 * 4 + 3, 0));
        ImageAssertions.Comparisons.CompareIntervals(image, bounds).AssertEqual();
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
        var image = Render(fragment, publication);
        ImageAssertions.Comparisons.CompareIntervals(image,
            [Exact(0, 8), Exact(4, 32), Exact(8, 8), Exact(12, 8), Exact(15, 1),
             Exact(19, 0), Exact(23, 0), Exact(27, 1), Exact(31, 0), Exact((8 + 5) * 4 + 3, 0)]).AssertEqual();
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
        var image = Render(fragment, publication);
        List<GpuScalarInterval> bounds = [new(0, 0, 47999.9f, 48000), new(1, 0, 15999.9f, 16000),
            Exact(3, 1), Exact(7, 1), new(6, 0, 3.999e-20f, 4.001e-20f), Exact(27, 1),
            new(7, 0, 47999.9f, 48000), Exact(31, 1), Exact(8 * 4 + 3, 1), Exact((8 + 4) * 4 + 3, 1)];
        for (int column = 2; column <= 5; column++) bounds.Add(Exact(column * 4 + 3, 0));
        for (int column = 1; column <= 3; column++) bounds.Add(Exact((8 + column) * 4 + 3, 0));
        ImageAssertions.Comparisons.CompareIntervals(image, bounds).AssertEqual();
        // These three correlated tuples retain their original CPU double arithmetic.
        foreach (int column in new[] { 0, 1, 7 })
        {
            var tuple = GpuLocalRead.NativeColorTuple(image, column, 0);
            Close(tuple[0] * .5, tuple[1]);
        }
    }
    #endregion

    #region Private
    /// <summary>Prepares only the compact comparisons and correlated photometric checks used by this suite.</summary>
    private protected override void PrepareObservations(GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare");
        programs.Prepare("inspection");
        programs.Prepare("predicate-photometric");
    }

    /// <summary>Maps a formerly flat scalar coordinate to an exact nominated GPU interval.</summary>
    private static GpuScalarInterval Exact(int component, double expected) => new(component / 4, component % 4, expected, expected);

    /// <summary>Retains the original independent double relative bound at one authored scalar coordinate.</summary>
    private static GpuScalarInterval Near(int component, double expected)
    {
        double tolerance = 2e-6 * Math.Max(1, Math.Abs(expected));
        return new(component / 4, component % 4, expected - tolerance, expected + tolerance);
    }

    /// <summary>Evaluates the current production includes using the real shared frame block layout.</summary>
    private GpuTexture Render(string fragment, byte[]? frozenStorage = null, LightingCalibration? calibration = null)
    {
        int program = TestShaderCache.CreateGraphics("#version 430 core\nlayout(location=0) in vec2 position;void main(){gl_Position=vec4(position,0,1);}", fragment);
        int buffer = GL.GenBuffer();
        int storageBuffer = GL.GenBuffer();
        GL.GetInteger(GetIndexedPName.UniformBufferBinding, 12, out int previous);
        GL.GetInteger((GetIndexedPName)All.ShaderStorageBufferBinding, GpuBindingRegistry.Ssbo.FrameLighting, out int previousStorage);
        try
        {
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
            using var target = CreateRenderTarget(8, 2, TextureFormat.Rgba32f);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, target.FboId); GL.Viewport(0, 0, 8, 2);
            GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.ScissorTest); GL.ColorMask(true, true, true, true); GL.UseProgram(program);
            RenderFullscreenQuad(program);
            return target[0];
        }
        finally
        {
            GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 12, previous);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, GpuBindingRegistry.Ssbo.FrameLighting, previousStorage);
            GL.DeleteBuffer(storageBuffer); GL.DeleteBuffer(buffer); GL.DeleteProgram(program);
            OpenGlStateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Expands local guarded production includes without replacing their quantity equations.</summary>
    private static string Expand(string path) => Regex.Replace(TestAssetSnapshot.ReadSourceText(path), "@import \\\"([^\\\"]+)\\\"",
        match => Expand(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, match.Groups[1].Value))));


    /// <summary>Bounds arithmetic error without accepting nonfinite physical quantities.</summary>
    private static void Close(double expected, float actual)
    {
        Assert.True(float.IsFinite(actual));
        double tolerance = 2e-6 * Math.Max(1, Math.Abs(expected));
        Assert.InRange(actual, expected - tolerance, expected + tolerance);
    }
    #endregion
}
