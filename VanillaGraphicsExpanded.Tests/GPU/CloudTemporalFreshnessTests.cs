using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.PBR.Clouds.Volume;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks that resampled observation metadata follows its physical footprint without renewing observations.</summary>
[Collection("GPU")][Trait("Category", "GpuIntegration")]
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
        var expected=new float[64*4];
        for(int y=1;y<7;y++)for(int x=1;x<7;x++)
        {
            int pixel=y*8+x;
            // Analytic projection of the current unit ray into the independent previous yawed camera.
            float directionX=(x+.5f)/4-1;
            float coordinate=(cosine*directionX-sine)/(sine*directionX+cosine)*4+3.5f;
            int left=(int)MathF.Floor(coordinate);float fraction=coordinate-left;
            float oldWeight=left%2==0?1-fraction:fraction;
            expected[pixel*4]=1+6*oldWeight;
            expected[pixel*4+1]=1/(oldWeight+(1-oldWeight)/4);
        }
        using var oracle=VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.CreateDynamicTexture2D(new()
        {
            Dimension=TextureDimension.Two,Format=TextureFormat.Rgba32f,Width=8,Height=8,MipLevels=1,
            Storage=ResourceStorageMode.Immutable,Filter=TextureFilterMode.Nearest
        }).RequireResource();
        oracle.UploadDataImmediate(expected);
        var result=scene.FilterAuthored(rays,rayBins,rays,previousYaw:yaw);
        try
        {

            // Age and numerical oracle occupy distinct images; keep each original condition explicit.
            ImageAssertions.Predicate(result.Diagnostics!, GpuAssertionPredicate.CloudTemporalFreshnessTests_ResampledFreshnessUsesPhysicalWeightsAndExpiresWithoutObservations,
                result.Cloud,result.Bins).AssertEqual();
            var age=ImageAssertions.Predicate(result.Diagnostics!, GpuAssertionPredicate.CloudTemporalFreshnessTests_ResampledFreshnessUsesPhysicalWeightsAndExpiresWithoutObservations_2,result.Age,oracle);
            age.AssertEqual();Assert.True(age.MatchingTexels>0);
            ImageAssertions.Predicate(result.Diagnostics!, GpuAssertionPredicate.CloudTemporalFreshnessTests_ResampledFreshnessUsesPhysicalWeightsAndExpiresWithoutObservations_3,result.Metadata,oracle).AssertEqual();
            // Reuse completed physical history; the required age image is copied without resetting its clocks.
            for(int frame=1;frame<8;frame++)
            {
                scene.SeedHistory(result);
                var next=scene.FilterAuthored(rays,rayBins,rays,previousYaw:yaw);
                result.Dispose();result=next;
            }
            Assert.Equal(0u,ImageAssertions.Predicate(result.Diagnostics!, GpuAssertionPredicate.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_10).MatchingTexels);
        }
        finally { result.Dispose(); }
    }
    #endregion
}
