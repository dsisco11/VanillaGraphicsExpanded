using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Rendering;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.ModSystems;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.PBR.Tessellation;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Validates identity tessellation against installed terrain interfaces and ordinary rasterization.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class TerrainTessellationTests : RenderTestBase
{
    /// <summary>Uses the shared headless context.</summary>
    public TerrainTessellationTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Coherent production publication
    /// <summary>Ordinary modes never replace a successfully linked terrain program with tessellation.</summary>
    [Theory]
    [InlineData(TerrainSurfaceDetailMode.Disabled)]
    [InlineData(TerrainSurfaceDetailMode.Relief)]
    public void OrdinaryModesRetainEngineExecutable(TerrainSurfaceDetailMode mode)
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        int vertex = shaders.Compile(ShaderType.VertexShader, PbrSurfaceInstalledShaderTests.Build("chunkopaque.vsh", 2, 0, 0, 0, 0));
        int fragment = shaders.Compile(ShaderType.FragmentShader, PbrSurfaceInstalledShaderTests.Build("chunkopaque.fsh", 2, 0, 0, 0, 0));
        using var ordinary = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, fragment));
        var owner = new ShaderProgram
        {
            PassName = "chunkopaque", AssetDomain = "game", ProgramId = ordinary.ProgramId,
            VertexShader = new Shader { ShaderId = vertex, Code = shaders.Source(vertex) },
            FragmentShader = new Shader { ShaderId = fragment }
        };
        var previousMode = ConfigModSystem.Config.MaterialAtlas.TerrainSurfaceDetailMode;
        bool previousHook = TerrainTessellationPrograms.DrawHookAvailable;
        bool previousMeshHook = TerrainTessellationPrograms.MeshDrawHookAvailable;
        try
        {
            ConfigModSystem.Config.MaterialAtlas.TerrainSurfaceDetailMode = mode;
            TerrainTessellationPrograms.DrawHookAvailable = true;
            TerrainTessellationPrograms.MeshDrawHookAvailable = true;
            TerrainTessellationTestAssets.Prepare(owner);
            TerrainTessellationPatches.Configure(owner);
            TerrainTessellationPrograms.Prepare(owner);
            Assert.Equal(ordinary.ProgramId, owner.ProgramId);
            Assert.True(GL.IsProgram(ordinary.ProgramId));
            Assert.False(TerrainTessellationPrograms.Adaptive(owner));
        }
        finally
        {
            TerrainTessellationPrograms.Forget(owner);
            ConfigModSystem.Config.MaterialAtlas.TerrainSurfaceDetailMode = previousMode;
            TerrainTessellationPrograms.DrawHookAvailable = previousHook;
            TerrainTessellationPrograms.MeshDrawHookAvailable = previousMeshHook;
        }
    }

    /// <summary>All terrain consumers must publish matching adaptive executables before displacement enables.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdaptiveFamilyPublishesTogetherAndReloadInvalidates(bool lumon)
    {
        EnsureContextValid();
        var config=ConfigModSystem.Config.MaterialAtlas;
        bool previousLumon=ConfigModSystem.Config.LumOn.Enabled;
        ConfigModSystem.Config.LumOn.Enabled=lumon;
        var previousMode=config.TerrainSurfaceDetailMode;bool previousHook=TerrainTessellationPrograms.DrawHookAvailable; bool previousMeshHook=TerrainTessellationPrograms.MeshDrawHookAvailable;
        var owners=new List<ShaderProgram>();
        using var shaders=new TerrainShaderTestFixture();
        try
        {
            config.TerrainSurfaceDetailMode=VanillaGraphicsExpanded.PBR.Materials.TerrainSurfaceDetailMode.Tessellation;
            TerrainTessellationPrograms.DrawHookAvailable=true; TerrainTessellationPrograms.MeshDrawHookAvailable=true;TerrainTessellationPrograms.BeginReload();
            foreach(string family in new[]{"chunkopaque","chunktopsoil","chunkshadowmap"})
            {
                Assert.False(TerrainTessellationPrograms.Complete);
                string source=PbrSurfaceInstalledShaderTests.Build(family+".vsh",2,0,1,0,0);
                int vs=shaders.Compile(ShaderType.VertexShader,source);
                int fs=shaders.Compile(ShaderType.FragmentShader,PbrSurfaceInstalledShaderTests.Build(family+".fsh",2,0,1,0,0));
                var owner=new ShaderProgram{PassName=family,AssetDomain="game",ProgramId=TerrainShaderTestFixture.Link(vs,fs),VertexShader=new Shader{ShaderId=vs,Code=source},FragmentShader=new Shader{ShaderId=fs}};
                owners.Add(owner);TerrainTessellationTestAssets.Prepare(owner);TerrainTessellationPatches.Configure(owner);TerrainTessellationPrograms.Prepare(owner);
                Assert.True(TerrainTessellationPrograms.Adaptive(owner));
            }
            Assert.True(TerrainTessellationPrograms.Complete);
            int actual=owners[0].ProgramId;owners[0].ProgramId=0;Assert.False(TerrainTessellationPrograms.Complete);owners[0].ProgramId=actual;
            Assert.True(TerrainTessellationPrograms.Complete);TerrainTessellationPrograms.BeginReload();Assert.False(TerrainTessellationPrograms.Complete);
        }
        finally
        {
            foreach(var owner in owners){TerrainTessellationPrograms.Forget(owner);GpuProgramObject.Adopt(owner.ProgramId).Dispose();}
            TerrainTessellationPrograms.BeginReload();ConfigModSystem.Config.LumOn.Enabled=previousLumon;config.TerrainSurfaceDetailMode=previousMode;TerrainTessellationPrograms.DrawHookAvailable=previousHook; TerrainTessellationPrograms.MeshDrawHookAvailable=previousMeshHook;
        }
    }
    #endregion

    #region Installed shadow interface
    /// <summary>Demonstrates why an entity triangle draw cannot share a linked terrain tessellation executable.</summary>
    [Fact]
    public void TessellatedShadowExecutableRejectsOrdinaryEntityTriangles()
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        int vertex = shaders.Compile(ShaderType.VertexShader, PbrSurfaceInstalledShaderTests.Build("chunkshadowmap.vsh", 2, 0, 0, 0, 0));
        int fragment = shaders.Compile(ShaderType.FragmentShader, PbrSurfaceInstalledShaderTests.Build("chunkshadowmap.fsh", 2, 0, 0, 0, 0));
        var sources = TerrainTessellationTestAssets.Generate(shaders.Source(vertex));
        Assert.True(TerrainTessellationLinker.TryCreate(vertex, fragment, sources,
            TerrainTessellationPatches.EnabledDefine, 1, out int tessellated, out string error), error);
        using var program = GpuProgramObject.Adopt(tessellated);
        using var ordinary = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, fragment));
        using var vao = GpuVao.Create();
        using var framework = new ShaderTestFramework();
        using var target = framework.CreateTestGBuffer(4, 4, PixelInternalFormat.Rgba32f);
        target.BindWithViewport();
        GlStateCache.Current.BindVertexArray(vao.VertexArrayId);
        GlStateCache.Current.UseProgram(tessellated);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        // This is the primitive submitted by the engine's non-grouped entity shadow path.
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
        GlStateCache.Current.UseProgram(ordinary.ProgramId);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        GlStateCache.Current.UseProgram(0);
    }

    /// <summary>Compiles installed alpha-tested terrain shadow stages with both terrain mesh layouts.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void InstalledShadowInterfaceLinksAdaptive(int ssbo)
    {
        EnsureContextValid();
        using var shaders=new TerrainShaderTestFixture();
        int vertex=shaders.Compile(ShaderType.VertexShader,PbrSurfaceInstalledShaderTests.Build("chunkshadowmap.vsh",2,0,0,ssbo,0));
        int fragment=shaders.Compile(ShaderType.FragmentShader,PbrSurfaceInstalledShaderTests.Build("chunkshadowmap.fsh",2,0,0,ssbo,0));
        var sources=TerrainTessellationTestAssets.Generate(shaders.Source(vertex),adaptiveDisplacement:true);
        Assert.True(TerrainTessellationLinker.TryCreate(vertex,fragment,sources,TerrainTessellationPatches.EnabledDefine,4,out int program,out string error),error);
        GpuProgramObject.Adopt(program).Dispose();
    }
    #endregion

    #region Installed interfaces
    /// <summary>Exercises both terrain families, SSBO layouts, SSAO outputs and both shadow cascades.</summary>
    [Theory]
    [InlineData("chunkopaque", 0, 0)]
    [InlineData("chunkopaque", 1, 1)]
    [InlineData("chunktopsoil", 0, 0)]
    [InlineData("chunktopsoil", 1, 1)]
    public void InstalledTerrainInterfaceLinks(string family, int ssbo, int ssao)
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        int vertex = shaders.Compile(ShaderType.VertexShader, PbrSurfaceInstalledShaderTests.Build(family + ".vsh", 2, 0, ssao, ssbo, 0));
        int fragment = shaders.Compile(ShaderType.FragmentShader, PbrSurfaceInstalledShaderTests.Build(family + ".fsh", 2, 0, ssao, ssbo, 0));
        var sources = TerrainTessellationTestAssets.Generate(shaders.Source(vertex));
        Assert.True(TerrainTessellationLinker.TryCreate(vertex, fragment, sources, TerrainTessellationPatches.EnabledDefine, 4, out int program, out string error), error);
        GpuProgramObject.Adopt(program).Dispose();
        var adaptiveSources = TerrainTessellationTestAssets.Generate(shaders.Source(vertex), adaptiveDisplacement: true);
        Assert.True(TerrainTessellationLinker.TryCreate(vertex, fragment, adaptiveSources, TerrainTessellationPatches.EnabledDefine, 4, out int adaptiveProgram, out string adaptiveError), adaptiveError);
        GpuProgramObject.Adopt(adaptiveProgram).Dispose();

    }
    /// <summary>Installing a tessellated executable preserves engine stage objects and scopes topology to its managed owner.</summary>
    [Fact]
    public void EngineProgramInstallationPreservesStageOwnership()
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        int vertex = shaders.Compile(ShaderType.VertexShader, PbrSurfaceInstalledShaderTests.Build("chunkopaque.vsh", 2, 0, 0, 0, 0));
        int fragment = shaders.Compile(ShaderType.FragmentShader, PbrSurfaceInstalledShaderTests.Build("chunkopaque.fsh", 2, 0, 0, 0, 0));
        int ordinary = TerrainShaderTestFixture.Link(vertex, fragment);
        var vertexObject = new Shader { ShaderId = vertex, Code = shaders.Source(vertex) };
        var fragmentObject = new Shader { ShaderId = fragment };
        var owner = new TerrainLinkedTestProgram { PassName = "chunkopaque", AssetDomain = "game", ProgramId = ordinary, VertexShader = vertexObject, FragmentShader = fragmentObject };
        var previousMode = ConfigModSystem.Config.MaterialAtlas.TerrainSurfaceDetailMode;
        var previousProgram = ShaderProgramBase.CurrentShaderProgram;
        bool previousHook = TerrainTessellationPrograms.DrawHookAvailable; bool previousMeshHook = TerrainTessellationPrograms.MeshDrawHookAvailable;
        var previousLog = TerrainTessellationPrograms.Log;
        try
        {
            ConfigModSystem.Config.MaterialAtlas.TerrainSurfaceDetailMode = TerrainSurfaceDetailMode.Tessellation;
            TerrainTessellationPrograms.DrawHookAvailable = true; TerrainTessellationPrograms.MeshDrawHookAvailable = true;
            string diagnostic = "";
            TerrainTessellationPrograms.Log = message => diagnostic = message;
            TerrainTessellationTestAssets.Prepare(owner);
            TerrainTessellationPatches.Configure(owner);
            TerrainTessellationPrograms.Prepare(owner);
            owner.PopulateDisplacementUniforms();
            Assert.True(ordinary != owner.ProgramId, diagnostic);
            Assert.False(GL.IsProgram(ordinary));
            Assert.Same(vertexObject, owner.VertexShader); Assert.Same(fragmentObject, owner.FragmentShader);
            Assert.Equal("chunkopaque", owner.PassName);
            Assert.True(GL.IsShader(vertex)); Assert.True(GL.IsShader(fragment));
            ShaderProgramBase.CurrentShaderProgram = owner;
            Assert.Equal(PrimitiveType.Patches, TerrainTessellationPrograms.Topology(PrimitiveType.Triangles));
            Assert.Equal(PrimitiveType.Lines, TerrainTessellationPrograms.Topology(PrimitiveType.Lines));
            var cache = VanillaGraphicsExpanded.Rendering.GlStateCache.Current;
            int previousPatchSize = cache.PatchVertices;
            cache.SetPatchVertices(5);
            VanillaGraphicsExpanded.HarmonyPatches.TerrainTessellationDrawHook.Prefix(out int? saved);
            Assert.Equal(3, GL.GetInteger(GetPName.PatchVertices));
            VanillaGraphicsExpanded.HarmonyPatches.TerrainTessellationDrawHook.Finalizer(saved);
            Assert.Equal(5, GL.GetInteger(GetPName.PatchVertices));
            cache.SetPatchVertices(previousPatchSize);
            ShaderProgramBase.CurrentShaderProgram = new ShaderProgram { ProgramId = owner.ProgramId };
            Assert.Equal(PrimitiveType.Triangles, TerrainTessellationPrograms.Topology(PrimitiveType.Triangles));
            ShaderProgramBase.CurrentShaderProgram = owner;
            TerrainTessellationPrograms.Forget(owner);
            Assert.Equal(PrimitiveType.Triangles, TerrainTessellationPrograms.Topology(PrimitiveType.Triangles));
        }
        finally
        {
            TerrainTessellationPrograms.Forget(owner);
            ShaderProgramBase.CurrentShaderProgram = previousProgram;
            ConfigModSystem.Config.MaterialAtlas.TerrainSurfaceDetailMode = previousMode;
            TerrainTessellationPrograms.DrawHookAvailable = previousHook; TerrainTessellationPrograms.MeshDrawHookAvailable = previousMeshHook;
            TerrainTessellationPrograms.Log = previousLog;
            GpuProgramObject.Adopt(owner.ProgramId).Dispose();
        }
    }
    #endregion

    #region Raster equivalence
    /// <summary>Interpolated colors and last-vertex flat data survive subdivision with nonuniform clip W.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void UndisplacedRasterMatchesTriangles(int level)
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        int vertex = shaders.Compile(ShaderType.VertexShader, """
            #version 430 core
            out vec4 rgba;
            out vec2 uv;
            flat out int renderFlags;
            void main() {
                vec2 p[3] = vec2[3](vec2(-1,-1), vec2(3,-1), vec2(-1,3));
                float w = float(gl_VertexID + 1);
                gl_Position = vec4(p[gl_VertexID] * w, (float(gl_VertexID) * 0.2 - 0.2) * w, w);
                uv = p[gl_VertexID] * 0.25 + 0.25;
                rgba = vec4(float(gl_VertexID == 0), float(gl_VertexID == 1), float(gl_VertexID == 2), 1);
                renderFlags = gl_VertexID + 1;
            }
            """);
        int fragment = shaders.Compile(ShaderType.FragmentShader, """
            #version 430 core
            in vec4 rgba;
            in vec2 uv;
            flat in int renderFlags;
            layout(location=0) out vec4 color;
            layout(location=1) out vec4 coordinates;
            void main() { color = vec4(rgba.rgb, float(renderFlags)); coordinates = vec4(uv, gl_FragCoord.z, 1); }
            """);
        using var vertexArray = GpuVao.Create();
        int ordinary = 0, tessellated = 0, vao = vertexArray.VertexArrayId;
        int previousPatchVertices = GlStateCache.Current.PatchVertices;
        try
        {
            ordinary = TerrainShaderTestFixture.Link(vertex, fragment);
            Assert.True(TerrainTessellationLinker.TryCreate(vertex, fragment, TerrainTessellationTestAssets.Generate(shaders.Source(vertex)), TerrainTessellationPatches.EnabledDefine, level, out tessellated, out string error), error);
            using var framework = new ShaderTestFramework();
            using var target = framework.CreateTestGBuffer(16, 16, PixelInternalFormat.Rgba32f, 2);
            target.BindWithViewport();
            GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
            GlStateCache.Current.BindVertexArray(vao);
            GlStateCache.Current.UseProgram(ordinary); GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            var expected = target[0].ReadPixels();
            var expectedCoordinates = target[1].ReadPixels();
            GlStateCache.Current.SetPatchVertices( 3);
            GlStateCache.Current.UseProgram(tessellated); GL.DrawArrays(PrimitiveType.Patches, 0, 3);
            var actual = target[0].ReadPixels();
            var actualCoordinates = target[1].ReadPixels();
            for (int i = 0; i < actualCoordinates.Length; i++) Assert.InRange(actualCoordinates[i], expectedCoordinates[i] - .0001f, expectedCoordinates[i] + .0001f);
            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < actual.Length; i++) Assert.InRange(actual[i], expected[i] - .0001f, expected[i] + .0001f);
            Assert.Equal(3f, actual[3]);
        }
        finally
        {
            GlStateCache.Current.UseProgram(0); GlStateCache.Current.BindVertexArray(0); GlStateCache.Current.SetPatchVertices( previousPatchVertices);
             if (ordinary != 0) GpuProgramObject.Adopt(ordinary).Dispose(); if (tessellated != 0) GpuProgramObject.Adopt(tessellated).Dispose();

        }
    }
    #endregion

}
