using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks engine relief registration and neutral page binding through real linked uniforms.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class TerrainReliefBindingsTests : RenderTestBase
{
    /// <summary>Uses the shared headless context.</summary>
    public TerrainReliefBindingsTests(HeadlessGLFixture fixture):base(fixture) { }

    #region Installed terrain variants
    /// <summary>Relief remains active without normal mapping in both installed terrain interfaces.</summary>
    [Theory]
    [InlineData("chunkopaque",0)]
    [InlineData("chunkopaque",1)]
    [InlineData("chunktopsoil",0)]
    [InlineData("chunktopsoil",1)]
    public void InstalledReliefLinksWithoutNormalMaps(string family,int ssbo)
    {
        EnsureContextValid();
        var config=VanillaGraphicsExpanded.ModSystems.ConfigModSystem.Config.MaterialAtlas;
        var mode=config.TerrainSurfaceDetailMode; bool normals=config.EnableNormalMaps;
        try
        {
            config.TerrainSurfaceDetailMode=1;config.EnableNormalMaps=false;
            using var shaders=new TerrainShaderTestFixture();
            int vs=shaders.Compile(ShaderType.VertexShader,PbrSurfaceInstalledShaderTests.Build(family+".vsh",2,0,1,ssbo,0));
            int fs=shaders.Compile(ShaderType.FragmentShader,PbrSurfaceInstalledShaderTests.Build(family+".fsh",2,0,1,ssbo,0));
            using var program=GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vs,fs));
            Assert.True(GL.GetUniformLocation(program.ProgramId,"vge_displacementRecords")>=0);
        }
        finally { config.TerrainSurfaceDetailMode=mode;config.EnableNormalMaps=normals; }
    }
    #endregion

    #region Binding lifecycle
    /// <summary>Only registered terrain interfaces bind neutral textures; incomplete or replaced interfaces do not.</summary>
    [Theory]
    [InlineData("chunkopaque",true)]
    [InlineData("chunktopsoil",true)]
    [InlineData("standard",false)]
    public void MissingPageBindsCoherentNeutralData(string pass,bool expected)
    {
        EnsureContextValid();
        using var shaders=new TerrainShaderTestFixture();
        int vs=shaders.Compile(ShaderType.VertexShader,"#version 430\nvoid main(){gl_Position=vec4(0);}");
        int fs=shaders.Compile(ShaderType.FragmentShader,"#version 430\nuniform sampler2D vge_displacementTex;uniform sampler2D vge_displacementRecords;uniform sampler2D vge_normalDepthTex;out vec4 result;void main(){result=texture(vge_displacementTex,vec2(.5))+texture(vge_displacementRecords,vec2(.5))+texture(vge_normalDepthTex,vec2(.5));}");
        using var program=GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vs,fs));
        using var store=new MaterialAtlasTextureStore();
        var owner=new LinkedProgram { ProgramId=program.ProgramId,PassName=pass,AssetDomain="game" };
        owner.Populate();
        var cache=StateCache.Current;
        cache.UseProgram(program.ProgramId);
        try
        {
            TerrainReliefBindings.Register(owner);
            if(expected)
            {
                var definition=new PbrMaterialDefinition(.5f,0,0,default,PbrOverrideScale.Identity,0,null,DisplacementAmplitudeMetres:.03f);
                var tile=new AtlasBuildPlan.MaterialParamsTileJob(7,new AtlasRect(0,0,4,4),new Vintagestory.API.Common.AssetLocation("game","textures/block/test.png"),definition,PbrOverrideScale.Identity,0);
                store.SyncToAtlasPages([(7,4,4)],true);
                store.UpdateDisplacement(new AtlasBuildPlan(new AtlasSnapshot([],[],0,0),[new(7,4,4)],[tile],[],[],[],default));
                Assert.True(store.TryGetDisplacementTextures(7,out var page));
                TerrainReliefBindings.Bind(owner,7,store);
                GL.ActiveTexture(TextureUnit.Texture0+TerrainReliefBindings.IndexUnit);
                Assert.Equal(page!.Indices.TextureId,GL.GetInteger(GetPName.TextureBinding2D));
                GL.ActiveTexture(TextureUnit.Texture0+TerrainReliefBindings.RecordUnit);
                Assert.Equal(page.Records.TextureId,GL.GetInteger(GetPName.TextureBinding2D));
                TerrainReliefBindings.Bind(owner,77,store);
                GL.ActiveTexture(TextureUnit.Texture0+TerrainReliefBindings.RecordUnit);
                int neutral=GL.GetInteger(GetPName.TextureBinding2D);
                Assert.NotEqual(page.Records.TextureId,neutral);
                GL.ActiveTexture(TextureUnit.Texture0+TerrainReliefBindings.IndexUnit);
                Assert.Equal(neutral,GL.GetInteger(GetPName.TextureBinding2D));
                GL.ActiveTexture(TextureUnit.Texture0+TerrainReliefBindings.HeightUnit);
                int neutralHeight=GL.GetInteger(GetPName.TextureBinding2D);
                Assert.NotEqual(neutral,neutralHeight);
                float[] neutralPixel=new float[4];
                GL.GetTexImage(TextureTarget.Texture2D,0,PixelFormat.Rgba,PixelType.Float,neutralPixel);
                Assert.Equal(new float[]{.5f,.5f,1f,.5f},neutralPixel);
                owner.ProgramId=0;
                TerrainReliefBindings.Bind(owner,7,store);
                Assert.Equal(neutralHeight,GL.GetInteger(GetPName.TextureBinding2D));
                owner.ProgramId=program.ProgramId;
                cache.InvalidateAll();
            }
            TerrainReliefBindings.Bind(owner,77,store);
            foreach(string name in new[]{"vge_displacementTex","vge_displacementRecords","vge_normalDepthTex"})
            {
                GL.GetUniform(program.ProgramId,GL.GetUniformLocation(program.ProgramId,name),out int unit);
                Assert.Equal(expected ? name=="vge_displacementTex"?TerrainReliefBindings.IndexUnit:name=="vge_displacementRecords"?TerrainReliefBindings.RecordUnit:TerrainReliefBindings.HeightUnit : 0,unit);
            }
        }
        finally { cache.UseProgram(0);TerrainReliefBindings.Reset();cache.InvalidateAll(); }
    }
    #endregion

    #region Engine fixture
    /// <summary>Populates the engine's linked uniform table using the actual executable.</summary>
    private sealed class LinkedProgram:ShaderProgram
    {
        /// <summary>Mirrors the engine compiler's linked-location publication.</summary>
        internal void Populate()
        {
            foreach(string name in new[]{"vge_displacementTex","vge_displacementRecords","vge_normalDepthTex"})
                uniformLocations[name]=GL.GetUniformLocation(ProgramId,name);
        }
    }
    #endregion
}
