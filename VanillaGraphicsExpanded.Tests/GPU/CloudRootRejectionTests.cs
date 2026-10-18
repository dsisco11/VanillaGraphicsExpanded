using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Protects inclusive native support from conservative root rejection at fixed shadow positions.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudRootRejectionTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Checks boundary, near-parallel, thin, tiny-positive and empty roots without grid-crossing clocks.</summary>
    [Fact]
    public void RootClippingPreservesScheduledOpticalDepthAtSupportBoundaries()
    {
        EnsureShaderTestAvailable();
        using var native=DynamicTexture2D.Create(21,21,PixelInternalFormat.Rgba32f);
        using var hierarchy=new CloudHierarchy(()=>CloudHierarchyComputeShader.Create(Programs.Api));
        using var probe=new CloudTraversalProbe(Programs.Api);
        using var volume=new CloudVolumeParameters();
        volume.CaptureMap(21,default,1,CloudVolumeSettings.Default with {Detail=false});
        var input=new CloudMapInput(this,1,1,1,1,1,1,new(native.TextureId,2,3,4,21,50,5),default);
        foreach(int pattern in new[]{0,1,2,3,4})
        {
            var column=pattern switch
            {
                0=>new Vector4(.125f,0,-.5f,.5f),
                1=>new Vector4(1e-20f,0,-2,2),
                // Place the first midpoint inside this thin interval; exact inclusive endpoints
                // are independently exercised by the horizontal upper/lower boundary rays.
                2=>new Vector4(.125f,0,.14999f,.15001f),
                4=>new Vector4(.125f,0,-.0002f,0),
                _=>Vector4.Zero
            };
            float[] data=new float[21*21*4];
            for(int cell=0;cell<21*21;cell++)
            {data[cell*4]=column.X;data[cell*4+2]=column.Z;data[cell*4+3]=column.W;}
            native.UploadDataImmediate(data);
            input=input with {Generation=input.Generation+1};
            Assert.True(hierarchy.TryBuild(input,()=>true),hierarchy.Failure);
            Assert.True(hierarchy.TryGetCurrent(input,out var bounds));
            var rays=pattern==0
                ?new (Vector3 Origin,Vector3 Direction)[]{(new(525,25,525),Vector3.UnitX),
                    (new(525,MathF.BitIncrement(25),525),new(1,-9e-9f,0)),
                    (new(525,-25,525),-Vector3.UnitX)}
                :pattern==4?new (Vector3 Origin,Vector3 Direction)[]{(new(25,5e-6f,525),new(1,-9e-9f,0))}
                :new (Vector3 Origin,Vector3 Direction)[]{(new(525,0,525),pattern==2?Vector3.UnitY:Vector3.UnitX)};
            foreach(var (origin,direction) in rays)
            {
                // Compare the complete physical tau, not counters substituted for shader radiance.
                // Both executions retain the same origin-anchored interval positions and weights.
                float direct=probe.Run(volume,native,origin,direction,0,7)[402].Z;
                float accelerated=probe.Run(volume,native,origin,direction,0,7,bounds:bounds,root:bounds!.MipLevels-1)[402].Z;
                Assert.True(float.IsFinite(direct)&&float.IsFinite(accelerated));
                Assert.InRange(Math.Abs(accelerated-direct),0,Math.Abs(direct)*3e-6f);
                if(pattern!=3) Assert.True(direct>0,$"Positive support required: pattern={pattern}, origin={origin}, direction={direction}.");
                else Assert.Equal(0,direct);
            }
        }
        GlDebug.ThrowIfErrors("Cloud root boundary checks");
    }
    #endregion
}
