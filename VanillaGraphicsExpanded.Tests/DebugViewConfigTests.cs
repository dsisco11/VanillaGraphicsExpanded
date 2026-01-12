using Newtonsoft.Json;

using VanillaGraphicsExpanded.LumOn;

namespace VanillaGraphicsExpanded.Tests;

public sealed class DebugViewConfigTests
{
    [Fact]
    public void LumOnConfig_DoesNotSerialize_DebugMode()
    {
        var cfg = new LumOnConfig
        {
            Enabled = true,
            DebugMode = LumOnDebugMode.ProbeAtlasMetaFlags
        };

        string json = JsonConvert.SerializeObject(cfg);

        Assert.DoesNotContain("\"DebugMode\"", json);
    }
}
