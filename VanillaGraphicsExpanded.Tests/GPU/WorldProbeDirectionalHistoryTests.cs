using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies ring-slot history retirement without erasing overlapping probes or leaking GL state.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class WorldProbeDirectionalHistoryTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Physical slot invalidation
    /// <summary>A wrapped inclusive local box clears every directional and scalar target only in the selected level.</summary>
    [Fact]
    public void WrappedLocalBoxPreservesOtherSlotsAndRenderState()
    {
        EnsureContextValid();
        using var resources=new LumOnWorldProbeClipmapGpuResources(4,2,4);
        GL.Disable(EnableCap.ScissorTest);GL.ColorMask(true,true,true,true);
        resources.GetRadianceFbo().BindWithViewport();GL.ClearBuffer(ClearBuffer.Color,0,new[]{1f,1f,1f,1f});
        resources.GetFbo().BindWithViewport();
        for(int attachment=0;attachment<3;attachment++) GL.ClearBuffer(ClearBuffer.Color,attachment,new[]{1f,1f,1f,1f});
        GL.Enable(EnableCap.ScissorTest);GL.Scissor(3,4,5,6);GL.ColorMask(false,true,false,true);
        try
        {
            resources.ClearLocalBox(1,new VectorInt3(3,2,1),new VectorInt3(),new VectorInt3(2,1,1));
            int[] scissor=new int[4];bool[] mask=new bool[4];
            GL.GetInteger(GetPName.ScissorBox,scissor);GL.GetBoolean(GetPName.ColorWritemask,mask);
            Assert.True(GL.IsEnabled(EnableCap.ScissorTest));
            Assert.Equal(new[]{3,4,5,6},scissor);Assert.Equal(new[]{false,true,false,true},mask);
        }
        finally { GL.Disable(EnableCap.ScissorTest);GL.ColorMask(true,true,true,true); }
        var radiance=resources.ProbeRadianceAtlas.ReadPixels();
        var metadata=resources.ProbeMeta0.ReadPixels();
        var visibility=resources.ProbeVis0.ReadPixels();
        var distance=resources.ProbeDist0.ReadPixels();
        for(int level=0;level<2;level++)for(int z=0;z<4;z++)for(int y=0;y<4;y++)for(int x=0;x<4;x++)
        {
            bool cleared=level==1 && x!=2 && y>=2 && z>=1 && z<=2;
            float expected=cleared?0:1;
            int scalar=((level*4+y)*16+z*4+x)*4;
            Assert.Equal(expected,metadata[scalar>>1]);Assert.Equal(expected,visibility[scalar]);Assert.Equal(expected,distance[scalar>>1]);
            for(int v=0;v<4;v++)for(int u=0;u<4;u++)
            {
                int pixel=(((level*4+y)*4+v)*64+(z*4+x)*4+u)*4;
                for(int c=0;c<4;c++) Assert.Equal(expected,radiance[pixel+c]);
            }
        }
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
}
