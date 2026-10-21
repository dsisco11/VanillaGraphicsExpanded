using VanillaGraphicsExpanded.PBR.Clouds.Volume;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks that resampled observation metadata follows its physical footprint without renewing observations.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudTemporalFreshnessTests(HeadlessGLFixture fixture):LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Weights freshness and confidence with admitted transport, then expires repeated carries without fresh rays.</summary>
    [Fact]
    public void ResampledFreshnessUsesPhysicalWeightsAndExpiresWithoutObservations()
    {
        EnsureShaderTestAvailable();using var scene=new CloudStoredVolumeViewScene(Programs,16,16);
        scene.SetDepths(1000,1000,1000);
        scene.Draw(CloudVolumeViewSettings.Default with{Detail=false,TraceDivisor=2});
        var cloud=new float[64*4];var metadata=new float[cloud.Length];var diagnostic=new float[cloud.Length];
        var bins=new float[64*3];
        for(int y=0;y<8;y++)for(int x=0;x<8;x++)
        {
            int p=y*8+x;bool old=x%2==0;
            cloud[p*4]=2;cloud[p*4+3]=.5f;
            metadata[p*4]=1;metadata[p*4+1]=1;metadata[p*4+2]=10;metadata[p*4+3]=old?1:4;
            diagnostic[p*4+3]=old?6:0;
            for(int c=0;c<3;c++)bins[p*3+c]=.5f;
        }
        scene.SeedHistory(cloud,metadata,diagnostic,bins);
        // No current ray is valid: only actual retained observations may supply a carry.
        var rays=new float[16*4];var rayBins=Enumerable.Repeat(1f,16*3).ToArray();
        const float yaw=.02f;float cosine=MathF.Cos(yaw),sine=MathF.Sin(yaw);
        var result=scene.FilterAuthored(rays,rayBins,rays,previousYaw:yaw);int checkedFootprints=0;
        for(int y=1;y<7;y++)for(int x=1;x<7;x++)
        {
            int p=y*8+x,i=p*4;if(result[3][i+1]!=2||result[3][i+2]!=0)continue;
            // Analytic projection of the unit current ray into the previous yawed camera.
            float directionX=(x+.5f)/4-1;
            float coordinate=(cosine*directionX-sine)/(sine*directionX+cosine)*4+3.5f;
            int left=(int)MathF.Floor(coordinate);float fraction=coordinate-left;
            float oldWeight=left%2==0?1-fraction:fraction;
            Assert.Equal(2,result[0][i]);Assert.Equal(.5f,result[0][i+3]);
            Assert.Equal(.5f,result[1][p*3]);
            // Required R16F age storage rounds the float32 weighted sum to half precision.
            Assert.InRange(Math.Abs(result[4][p]-(1+6*oldWeight)),0,.004f);
            Assert.Equal(1/(oldWeight+(1-oldWeight)/4),result[2][i+3],4);
            Assert.InRange(result[2][i+3],1,4);checkedFootprints++;
        }
        Assert.True(checkedFootprints>0);
        // Feed only completed output back as history. Resampling must not reset age or create
        // confidence: once even the youngest source is seven frames old, nothing can be carried.
        for(int frame=1;frame<8;frame++)
        {
            var ages=new float[64*4];for(int p=0;p<64;p++)ages[p*4+3]=result[4][p];
            scene.SeedHistory(result[0],result[2],ages,result[1]);
            result=scene.FilterAuthored(rays,rayBins,rays,previousYaw:yaw);
        }
        Assert.DoesNotContain(2,result[3].Where((_,i)=>i%4==1));
    }
    #endregion
}
