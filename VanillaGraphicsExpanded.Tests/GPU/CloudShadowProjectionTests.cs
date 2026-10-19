using System.Collections.Immutable;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks the production frame's full native coverage under current map and light publications.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudShadowProjectionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Both sources cover all native box corners through poles, recentering, wind and changed heights.</summary>
    [Fact]
    public void CurrentProjectionCoversTheCommittedNativeDomain()
    {
        EnsureContextValid();
        using var programs=new ComponentShaderPrograms();
        using var probe=new CloudTraversalProbe(programs.Api);
        using var native=DynamicTexture2D.Create(21,21,PixelInternalFormat.Rgba32f);
        using var hierarchy=new CloudHierarchy(()=>CloudHierarchyComputeShader.Create(programs.Api));
        using var volume=new CloudVolumeParameters();
        var input=new CloudMapInput(this,1,1,1,1,1,1,new(native.TextureId,2,3,4,21,50,5),default);
        foreach(int revision in new[]{0,1,2})
        {
            float lower=-25-revision*25,upper=75+revision*50;
            var columns=new float[21*21*4];
            for(int i=0;i<21*21;i++){columns[i*4]=.125f;columns[i*4+2]=lower/50;columns[i*4+3]=upper/50;}
            native.UploadDataImmediate(columns);
            var placement=new CloudPlacement(revision,revision,0,0,revision,0,-.25*revision,0,new(50*revision,-3,0),0);
            input=input with {Generation=revision+1,Placement=placement};
            Assert.True(hierarchy.TryBuild(input,()=>true),hierarchy.Failure);
            Assert.True(hierarchy.TryGetCurrent(input,out var bounds));
            volume.CaptureMap(21,placement,1,CloudVolumeSettings.Default);
            foreach(float angle in new[]{-.16f,-.14f,0f,.14f,.16f,MathF.PI,0f})
            {
                Vector3 sun=Vector3.Normalize(new(MathF.Sin(angle),MathF.Cos(angle),0)),moon=-sun;
                var lighting=new AtmosphereLighting(sun,new(100),Vector3.One,Vector3.Zero,Vector3.Zero,ImmutableArray<float>.Empty)
                    {Moon=moon,Lunar=new(.2f),CalibrationGeneration=1,LookupStorageScale=LightingStorage.PersistentScale};
                volume.CaptureLighting(lighting,0,Vector4.Zero,Vector3.Zero);
                for(int source=0;source<2;source++)
                {
                    Vector4[] records=probe.Run(volume,native,Vector3.Zero,Vector3.UnitX,source,19,bounds:bounds,root:bounds!.MipLevels-1);
                    Assert.Equal(1,records[402].X);
                    Vector3 center=V(records[0]),u=V(records[1]),v=V(records[2]),radius=V(records[3]),light=source==0?sun:moon;
                    Assert.InRange(Math.Abs(Vector3.Dot(u,v)),0,2e-6f);
                    Assert.InRange(Math.Abs(Vector3.Dot(u,light)),0,2e-6f);
                    foreach(float x in new[]{0f,1050f})foreach(float z in new[]{0f,1050f})foreach(float y in new[]{lower,upper})
                    {
                        Vector3 point=new Vector3(x,y,z)-center;
                        // Dot products and conservative box extents are binary32 arithmetic.
                        Assert.InRange(Math.Abs(Vector3.Dot(point,u)),0,radius.X+.001f);
                        Assert.InRange(Math.Abs(Vector3.Dot(point,v)),0,radius.Y+.001f);
                        Assert.InRange(Math.Abs(Vector3.Dot(point,light)),0,radius.Z+.001f);
                    }
                }
            }
            // Geometric orientation has no authority to admit a withdrawn root certificate.
            Assert.Equal(0,probe.Run(volume,native,Vector3.Zero,Vector3.UnitX,0,19,bounds:bounds)[402].X);
        }
        GlDebug.ThrowIfErrors("Cloud projection coverage");
    }
    #endregion
    #region Private
    /// <summary>Reads the geometric portion of a diagnostic record.</summary>
    private static Vector3 V(Vector4 value)=>new(value.X,value.Y,value.Z);
    #endregion
}
