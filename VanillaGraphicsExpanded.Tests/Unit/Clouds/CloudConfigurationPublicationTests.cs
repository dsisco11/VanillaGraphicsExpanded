using Newtonsoft.Json;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.PBR.Clouds.Volume;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Tests.Unit.Clouds;

/// <summary>Checks cloud configuration clamping and queued profile publication.</summary>
public sealed class CloudConfigurationPublicationTests
{
    #region Public API
    /// <summary>Applied features and resolution remain pending until early preparation.</summary>
    [Fact]
    public void ConfigurationPublishesOnlyAtPreparation()
    {
        // Arrange: keep requested cloud settings separate from the currently staged profile.
        var config=new CloudRenderingSettings();var staged=new CloudVolumeConfiguration();
        Assert.Equal(CloudVolumeViewSettings.Default,config.Capture());
        foreach(var (reference,resolution,divisor) in new[]{(false,CloudTraceResolution.Half,2),(true,CloudTraceResolution.Quarter,1),(false,CloudTraceResolution.Quarter,4),(false,CloudTraceResolution.Full,1)})
        {
            var previous=staged.Settings;config.TraceResolution=resolution;config.FullResolutionReference=reference;
            config.Detail=false;config.SelfShadowing=false;config.Diffuse=false;
            // Act: queue settings at request time.
            staged.Request(config);
            // Assert: a request alone does not publish the new view.
            Assert.Equal(previous,staged.Settings);
            // Act: apply the queued profile at the preparation boundary.
            staged.Apply();
            // Assert: preparation publishes the corresponding trace divisor and features.
            Assert.Equal(new CloudVolumeViewSettings(false,false,false,.005f,divisor),staged.Settings);
        }
        // Act/Assert: invalid enum values and ray budgets clamp to their supported ranges.
        config.TraceResolution=(CloudTraceResolution)99;
        Assert.Equal(4,config.Capture().TraceDivisor);
        config.LightingRaysPerUpdate=int.MaxValue;Assert.Equal(327680,config.Capture().LightingRaysPerUpdate);
        config.LightingRaysPerUpdate=-1;Assert.Equal(2560,config.Capture().LightingRaysPerUpdate);
        config.LightingRaysPerUpdate=9000;Assert.Equal(7680,config.Capture().LightingRaysPerUpdate);
        config.LightingRaysPerUpdate=0;
        Assert.Equal(2,config.Quality);
        // Act/Assert: walk every authored quality profile and the minimum, profile, and maximum budgets.
        foreach(var (level,shape,directions,distance,stop,transport,rays) in new[]{
            (0,1,2,100,.02f,.02f,20480),(1,1,4,200,.01f,.005f,40960),
            (2,2,8,400,.005f,0f,81920),(3,3,16,600,.0025f,0f,163840),(4,4,32,800,.00125f,0f,327680)})
        {
            config.Quality=level;var captured=config.Capture();
            // Assert: quality resolution and captured stop/budget values match the profile.
            Assert.Equal(new CloudVolumeQuality(level,shape,directions,distance,stop,transport,rays),CloudVolumeQuality.Resolve(level));
            Assert.Equal(level,captured.Quality);Assert.Equal(stop,captured.Stop);Assert.Equal(rays,captured.LightingRaysPerUpdate);
            // Act: queue a profile, mutate the request, and apply the staged snapshot.
            staged.Request(config);config.Quality=(level+1)%5;staged.Apply();
            // Assert: the queued snapshot remains unchanged beneath the in-flight update.
            Assert.Equal(captured,staged.Settings); // A queued profile cannot change beneath a retained update.
            foreach(int budget in new[]{2560,rays,327680})
            {
                config.Quality=level;config.LightingRaysPerUpdate=budget;
                // Assert: candidate counts stay inside the documented bounds at each boundary budget.
                Assert.Equal(budget,config.Capture().LightingRaysPerUpdate);
                Assert.InRange(CloudVolumeLightingBudget.Candidates(budget,true,directions),1,256);
                Assert.InRange(CloudVolumeLightingBudget.Candidates(budget,false,directions),1,128);
            }
            config.LightingRaysPerUpdate=0;
        }
        // Act/Assert: quality bounds, serialization, and view-setting validation remain stable.
        config.Quality=-1;Assert.Equal(0,config.Capture().Quality);
        config.Quality=99;Assert.Equal(4,config.Capture().Quality);
        config.LightingRaysPerUpdate=9000;Assert.Equal(7680,config.Capture().LightingRaysPerUpdate);
        var copy=JsonConvert.DeserializeObject<CloudRenderingSettings>(JsonConvert.SerializeObject(config))!;
        Assert.Equal(config.Capture(),copy.Capture());
        Assert.Throws<ArgumentOutOfRangeException>(()=>(CloudVolumeViewSettings.Default with{TraceDivisor=3}).Validate());
    }
    #endregion
}
