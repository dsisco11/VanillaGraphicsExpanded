using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.SceneColor;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Compares storage envelopes to arithmetic extracted verbatim from the installed native shaders.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SceneCompatibilityEnvelopeTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Signed vertex gains and the unclamped distant edge feed the real cloud perception and color adapter.</summary>
    [Fact]
    public void InstalledSignedCloudVertexAndFragmentFitJointEnvelope()
    {
        EnsureContextValid();
        string vertex = Native("clouds.vsh"), fragment = Native("clouds.fsh");
        string edge = Slice(vertex, "float distToEdge =", "float undulate =");
        string undulation = Slice(vertex, "float undulate =", "vexPos.y += undulate*50;");
        string color = Slice(vertex, "rgbaCloud = vec4(rgbaBase.rgb", "// Blend to fog color");
        string modulation = Slice(vertex, "rgbaCloud.rgb *= mix(1, 0.2", "rgbaCloud.a *= max");
        string body = Slice(fragment, "vec4 col = rgbaCloud;", "// fake depth for better blending")
            .Replace("texture(liquidDepth, gl_FragCoord.xy/frameSize.xy).r", ".25", StringComparison.Ordinal);
        string fog = File.ReadAllText(Path.Combine(GameRoot(), "assets/game/shaderincludes/fogandlight.fsh"));
        string water = File.ReadAllText(Path.Combine(GameRoot(), "assets/game/shaderincludes/underwatereffects.fsh"));
        using var shaders = new TerrainShaderTestFixture();
        using var program = Link(shaders, """
            #define BLOOM 1
            const float Epsilon=.00001;
            uniform float cloudsLength;uniform float positionRadius;uniform float noiseValue;uniform float cloudBrightness;uniform float globalCloudBrightness;uniform float undulatingModeness;
            uniform float windWaveCounter;float psychedelicStrength=2,dayLight=1.2,cloudCounter=0;
            vec2 tileOffset=vec2(0);float gnoise(vec3 p){return noiseValue;}
            vec4 waterMurkColor=vec4(7,2,3,1),rgbaBase=vec4(1,.5,.25,1),rgbaFog=vec4(4,2,3,1);
            vec3 plightrgb=vec3(2,5,3),vertexPos=vec3(1,2,3);float fogDensityIn=.02,nightVisionStrengthv=2,fogAmountf=.2;
            float getSkyMurkiness(){return 0;}
            """ + Slice(fog, "vec3 palette(", "vec4 applyRustEffect(") + Function(water, "vec3 applyUnderwaterEffects") +
            "\nvoid main(){vec3 vexPos=vec3(positionRadius,0,0);vec4 rgbaCloud;float cloudOpaqueness=1,selfThickness=1;" + edge + undulation + color + modulation +
            "vec4 skyGlow=vec4(3,1,2,1);" + body + "outColor=vec4(VgeCompatibilityLuminance(col.rgb),col.a);}");
        using var framework = new ShaderTestFramework();
        using var target = CreateMRTRenderTarget(1, 1, PixelInternalFormat.Rgba32f);
        foreach (float radius in new[] { 0f, 1000f, 10000f })
        foreach (float brightness in new[] { -1f, 1f })
        foreach (float global in new[] { -2f, 2f })
        foreach (float mode in new[] { -1f, 0f, 1f })
        foreach (float noise in new[] { -3.6f, 3.6f })
        foreach (float animation in new[] { 0f, 100f })
        {
            float vertexMaximum = SceneColorCompatibilityBounds.CloudVertexColor(1.2f, 1, 2, Math.Abs(mode), radius, 1000);
            float bound = SceneColorCompatibilityBounds.Clouds(vertexMaximum, 3, 2, 4, 5, 2, 7, 0, 1);
            Set(program, "positionRadius", radius); Set(program, "cloudsLength", 1000);
            Set(program, "cloudBrightness", brightness); Set(program, "globalCloudBrightness", global);
            Set(program, "undulatingModeness", mode); Set(program, "noiseValue", noise); Set(program, "windWaveCounter", animation);
            framework.RenderQuadTo(program.ProgramId, target);
            Assert.All(target[0].ReadPixels()[..3], value => Assert.InRange(value, 0, bound));
        }
    }

    /// <summary>Exact native particle normalization and subsequent shadow/fog/underwater equations fit an admitted source envelope.</summary>
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)]
    public void InstalledCubeParticleLightingFitsEnvelope(bool pointSources, bool staticBranch)
    {
        EnsureContextValid();
        string include = File.ReadAllText(Path.Combine(GameRoot(), "assets/game/shaderincludes/fogandlight.vsh"));
        string fragment = File.ReadAllText(Path.Combine(GameRoot(), "assets/game/shaderincludes/fogandlight.fsh"));
        string underwater = File.ReadAllText(Path.Combine(GameRoot(), "assets/game/shaderincludes/underwatereffects.fsh"));
        using var shaders = new TerrainShaderTestFixture();
        using var program = Link(shaders, "#define DYNLIGHTS " + (staticBranch ? 0 : 2) + "\n" + """
            #define SHADOWQUALITY 1
            #define MINBRIGHT 4.0
            const int GlowLevelBitMask=255;
            float glowLevel,blockBrightness;vec3 blockLight;
            uniform vec3 pointLights[2];uniform vec3 pointLightColors[2];uniform int pointLightQuantity;
            uniform float nightVisionStrength;uniform float sunField;uniform float blockField;uniform float fixtureMurk;uniform float fixtureFog;
            float glitchStrengthFL=0,shadowIntensity=3;
            vec3 lightPosition=vec3(0,1,0);vec4 rgbaFog=vec4(6,2,4,1),waterMurkColor=vec4(7,2,3,1);
            float getBrightnessFromShadowMap(){return 1.0;}
            vec4 applySpheresFog(vec4 c,float f,vec3 p){return c;}
            """ + Function(include, "vec4 applyLightWithoutPointLight") + Function(include, "vec4 getPointLightRgbv") + Function(include, "vec4 applyLight(") +
            Function(fragment, "vec4 applyFog(") + Function(fragment, "float getBrightnessFromNormal") + Function(fragment, "vec4 applyFogAndShadowWithNormal") +
            Function(underwater, "vec3 applyUnderwaterEffects") + """
            void main(){vec4 color=applyLight(vec3(1,3,2),vec4(vec3(blockField),sunField),255,vec4(1,0,0,1))*vec4(1,.5,.25,1);
            vec4 c=applyFogAndShadowWithNormal(color,fixtureMurk>0.0?0.0:fixtureFog,vec3(0,1,0),1.0,.34,vec3(1,0,0));
            if(fixtureMurk>0.0)c.rgb=applyUnderwaterEffects(c.rgb,fixtureMurk);
            outColor=vec4(VgeCompatibilityLuminance(c.rgb),c.a);}
            """);
        using var framework = new ShaderTestFramework();
        using var target = CreateMRTRenderTarget(1, 1, PixelInternalFormat.Rgba32f);
        float[] colors = [1, 2, 3, 200, 400, 100];
        int count = pointSources ? 2 : 0;
        var pointBounds = SceneColorPointLightBounds.FromEngine(count, colors);
        GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "pointLightQuantity"), count);
        GL.ProgramUniform3(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "pointLightColors"), 2, colors);
        GL.ProgramUniform3(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "pointLights"), 2, new float[6]);
        float peak = 0;
        foreach (float night in new[] { 0f, 1f })
        foreach (float sun in new[] { 0f, .001f, 1f })
        foreach (float block in new[] { 0f, 1f })
        foreach (float fog in new[] { 0f, .5f, 1f })
        foreach (float murk in new[] { 0f, .5f, 1f })
        {
            float bound = SceneColorCompatibilityBounds.CubeParticles(new(1, 3, 2), night, 4, 1, 3, 6, 7, pointBounds, 0, 1);
            Set(program, "nightVisionStrength", night); Set(program, "sunField", sun); Set(program, "blockField", block);
            Set(program, "fixtureFog", fog); Set(program, "fixtureMurk", murk);
            framework.RenderQuadTo(program.ProgramId, target);
            float[] actual = target[0].ReadPixels();
            Assert.All(actual[..3], value => Assert.InRange(value, 0, bound)); peak = Math.Max(peak, actual[..3].Max());
        }
        Assert.True(peak > 32);
    }

    /// <summary>Native cloud glow, animated perception, fog, point light and depth-qualified underwater mixing retain the envelope.</summary>
    [Fact]
    public void InstalledCloudExpressionFitsAdmittedMurkEnvelope()
    {
        EnsureContextValid();
        string native = Native("clouds.fsh");
        string body = Slice(native, "vec4 col = rgbaCloud;", "// fake depth for better blending")
            .Replace("texture(liquidDepth, gl_FragCoord.xy/frameSize.xy).r", ".25", StringComparison.Ordinal);
        string fog = File.ReadAllText(Path.Combine(GameRoot(), "assets/game/shaderincludes/fogandlight.fsh"));
        string perception = Slice(fog, "vec3 palette(", "vec4 applyRustEffect(");
        string underwater = Slice(File.ReadAllText(Path.Combine(GameRoot(), "assets/game/shaderincludes/underwatereffects.fsh")), "vec3 applyUnderwaterEffects", "\n}") + "\n}";
        using var shaders = new TerrainShaderTestFixture();
        using var program = Link(shaders, """
            #define BLOOM 1
            const float Epsilon=.00001;
            uniform float psychedelicStrength;uniform float windWaveCounter;uniform float fixtureMurk;uniform float fogAmountf;uniform float glowAlpha;
            vec4 waterMurkColor=vec4(7,2,3,1),rgbaCloud=vec4(3,1,2,1),rgbaFog=vec4(4,2,3,1);
            vec3 plightrgb=vec3(2,5,3),vertexPos=vec3(1,2,3);float fogDensityIn=.02,nightVisionStrengthv=2;
            float getSkyMurkiness(){return fixtureMurk;}
            """ + perception + underwater + "\nvoid main(){vec4 skyGlow=vec4(3,1,2,glowAlpha);" + body + "outColor=vec4(VgeCompatibilityLuminance(col.rgb),col.a);}");
        using var framework = new ShaderTestFramework();
        using var target = CreateMRTRenderTarget(1, 1, PixelInternalFormat.Rgba32f);
        float bound = SceneColorCompatibilityBounds.Clouds(3, 3, 2, 4, 5, 2, 7, 0, 1);
        foreach (float glow in new[] { 0f, .5f, 1f })
        foreach (float fogAmount in new[] { 0f, .5f, 1f })
        foreach (float murk in new[] { 0f, .5f, 1f })
        foreach (float animation in new[] { 0f, 100f, 1000f })
        {
            Set(program, "glowAlpha", glow); Set(program, "fogAmountf", fogAmount); Set(program, "fixtureMurk", murk);
            Set(program, "psychedelicStrength", 2); Set(program, "windWaveCounter", animation);
            framework.RenderQuadTo(program.ProgramId, target);
            Assert.All(target[0].ReadPixels()[..3], value => Assert.InRange(value, 0, bound));
        }
    }

    /// <summary>Actual native weighted color averages remain bounded at nearby, distant and mixed lightning sources.</summary>
    [Theory]
    [InlineData("clouds.vsh", false)]
    [InlineData("cloudmap.fsh", true)]
    public void InstalledNativePointAverageFitsCertificate(string name, bool map)
    {
        EnsureContextValid();
        string native = Native(name);
        int start = native.IndexOf("vec4 getPointLightRgbvl", StringComparison.Ordinal);
        int body = native.IndexOf('{', start), end = body + 1, depth = 1;
        while (depth != 0)
        { char c = native[end++]; if (c == '{') depth++; else if (c == '}') depth--; }
        string function = native[start..end];
        float[] colors = [200, 400, 100, 2, 9, 3, .02f, .05f, .01f];
        var bounds = SceneColorPointLightBounds.FromEngine(3, colors);
        Assert.True(bounds.Supported);
        using var shaders = new TerrainShaderTestFixture();
        using var program = Link(shaders, "#define DYNLIGHTS 1\n" +
            "uniform vec3 pointLights[3];uniform vec3 pointLightColors[3];uniform int pointLightQuantity;uniform vec3 samplePosition;float glitchStrengthFL=0;\n" +
            function + "\nvoid main(){outColor=getPointLightRgbvl(samplePosition);}");
        using var framework = new ShaderTestFramework();
        using var target = CreateMRTRenderTarget(1, 1, PixelInternalFormat.Rgba32f);
        GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "pointLightQuantity"), 3);
        GL.ProgramUniform3(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "pointLightColors"), 3, colors);
        GL.ProgramUniform3(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "pointLights"), 3, new float[] { 0, 0, 0, 4, 2, -1, -8, 2, 1 });
        foreach (float distance in new[] { 0f, 1f, 10f, 100f })
        {
            GL.ProgramUniform3(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "samplePosition"), distance, 0f, 0f);
            framework.RenderQuadTo(program.ProgramId, target);
            float[] actual = target[0].ReadPixels();
            Assert.All(actual[..3], value => Assert.InRange(value, 0, map ? bounds.CloudMapMaximum : bounds.CloudMaximum));
            Assert.True(actual[1] > 0);
        }
    }

    /// <summary>Signed normalized cloud-map endpoints, HDR gains, fog and native point additions fit the certified envelope.</summary>
    [Fact]
    public void InstalledCloudMapSignedExtremaFitEnvelope()
    {
        EnsureContextValid();
        string native = Native("cloudmap.fsh");
        string grey = Slice(native, "float greyscale =", "float height =");
        string color = Slice(native, "colour = vec4(vec3(greyscale)", "tile.r = opaque;");
        using var shaders = new TerrainShaderTestFixture();
        using var program = Link(shaders, """
            uniform float cloudBrightness;uniform float undulatingModeness;uniform float noiseValue;uniform float globalCloudBrightness;
            uniform float fogValue;uniform float glowAlpha;uniform float nightVisionStrength;
            vec3 tilePosition=vec3(0),viewSpace=vec3(0),sunPosition=vec3(0);float fogMinIn=0,fogDensityIn=0;
            vec4 rgbaFogIn=vec4(4,2,3,1);
            vec4 getSkyGlowAt(vec3 a,vec3 b,float c,float d,float e,float f){return vec4(3,1,2,glowAlpha);}
            float getFogLevel(vec4 a,float b,float c){return fogValue;}
            vec4 getPointLightRgbvl(vec3 a){return vec4(2,5,3,1);}
            void main(){vec4 colour;float dayLight=1.1;float undulate=noiseValue*undulatingModeness;
            """ + grey + "{\n" + color + "}\noutColor=vec4(VgeCompatibilityLuminance(colour.rgb),colour.a);}");
        using var framework = new ShaderTestFramework();
        using var target = CreateMRTRenderTarget(1, 1, PixelInternalFormat.Rgba32f);
        float bound = SceneColorCompatibilityBounds.CloudMap(1, 1, 2, 3, 4, 5, 2);
        float peak = 0;
        foreach (float brightness in new[] { -1f, 0f, 1f })
        foreach (float undulation in new[] { -1f, 0f, 1f })
        foreach (float noise in new[] { -3.6f, 0f, 3.6f })
        foreach (float global in new[] { -2f, 0f, 2f })
        foreach (float fog in new[] { 0f, .5f, 1f })
        foreach (float glow in new[] { 0f, .5f, 1f })
        {
            Set(program, "cloudBrightness", brightness); Set(program, "undulatingModeness", undulation);
            Set(program, "noiseValue", noise); Set(program, "globalCloudBrightness", global);
            Set(program, "fogValue", fog); Set(program, "glowAlpha", glow); Set(program, "nightVisionStrength", 2);
            framework.RenderQuadTo(program.ProgramId, target);
            float[] actual = target[0].ReadPixels();
            Assert.All(actual[..3], value => Assert.InRange(value, 0, bound));
            Assert.Equal(1, actual[3]); peak = Math.Max(peak, actual[..3].Max());
        }
        Assert.True(peak > 32);
    }

    /// <summary>Native fog restoration precedes the lunar power response, including values above the authored unit range.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void InstalledMoonFogRestorationFitsEnvelope(int nonlinear)
    {
        EnsureContextValid();
        string body = Slice(Native("celestialobject.fsh"), "texColor = applyFog(texColor, fogAmount);", "#if SSAOLEVEL > 0");
        string underwater = Slice(File.ReadAllText(Path.Combine(GameRoot(), "assets/game/shaderincludes/underwatereffects.fsh")), "vec3 applyUnderwaterEffects", "\n}") + "\n}";
        using var shaders = new TerrainShaderTestFixture();
        using var program = Link(shaders, "uniform float fixtureMurk;vec4 waterMurkColor=vec4(7,2,3,1);float getSkyMurkiness(){return fixtureMurk;}\n" + underwater + "\n" +
            "vec4 applyFog(vec4 c,float f){return vec4(mix(c.rgb,vec3(6,4,2),f),c.a);}" +
            "void main(){vec4 texColor=vec4(0,0,0,1),texGlow=vec4(0),skyColor=vec4(3,2,1,1),skyGlow=vec4(0),outGlow;float fogAmount=1,dayLight=0;int weirdMathToMakeMoonLookNicer=" + nonlinear + ";" + body + "outColor.rgb=VgeCompatibilityLuminance(outColor.rgb);}");
        using var framework = new ShaderTestFramework();
        using var target = CreateMRTRenderTarget(1, 1, PixelInternalFormat.Rgba32f);
        float bound = SceneColorCompatibilityBounds.Moon(0, 1, 6, 3, 7, nonlinear != 0, 0, 1);
        foreach (float murk in new[] { 0f, .5f, 1f })
        {
            Set(program, "fixtureMurk", murk); framework.RenderQuadTo(program.ProgramId, target);
            float[] actual = target[0].ReadPixels();
            Assert.All(actual[..3], value => Assert.InRange(value, 0, bound));
            Assert.True(actual[0] > 32);
        }
    }
    #endregion

    #region Private
    /// <summary>Extracts the installed function through its balanced body without rewriting its expressions.</summary>
    private static string Function(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0);
        int end = source.IndexOf('{', start) + 1, depth = 1;
        while (depth != 0)
        { char c = source[end++]; if (c == '{') depth++; else if (c == '}') depth--; }
        return source[start..end] + "\n";
    }

    /// <summary>Loads native code from the same installation used by the shader catalog.</summary>
    private static string Native(string name) => File.ReadAllText(Path.Combine(GameRoot(), "assets/game/shaders", name));
    /// <summary>Locates the configured installation.</summary>
    private static string GameRoot() => Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? "G:/Vintagestory";
    /// <summary>Selects a verbatim expression region, failing if its installed contract moved.</summary>
    private static string Slice(string source, string start, string end)
    {
        int first = source.IndexOf(start, StringComparison.Ordinal), last = source.IndexOf(end, first + start.Length, StringComparison.Ordinal);
        Assert.True(first >= 0 && last > first); return source[first..last];
    }
    /// <summary>Links exact native expression regions to production authored-color decoding.</summary>
    private static GpuProgramObject Link(TerrainShaderTestFixture shaders, string body)
    {
        string helper = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/pbr_color.glsl"));
        int vertex = shaders.Compile(ShaderType.VertexShader, "#version 330 core\nlayout(location=0)in vec2 position;void main(){gl_Position=vec4(position,0,1);}");
        int fragment = shaders.Compile(ShaderType.FragmentShader, "#version 330 core\nlayout(location=0)out vec4 outColor;\n" + helper + "\n" + body);
        return GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, fragment));
    }
    /// <summary>Changes one controlled native expression input without replacing its arithmetic.</summary>
    private static void Set(GpuProgramObject program, string name, float value) => GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, name), value);
    #endregion
}
