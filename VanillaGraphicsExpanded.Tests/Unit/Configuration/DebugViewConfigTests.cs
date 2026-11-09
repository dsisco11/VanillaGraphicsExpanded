using Newtonsoft.Json;

using VanillaGraphicsExpanded.LumOn;

namespace VanillaGraphicsExpanded.Tests.Unit.Configuration;

/// <summary>Verifies transient debug selection does not enter the persisted configuration contract.</summary>
public sealed class DebugViewConfigTests
{
    #region Public API
    /// <summary>Serialization omits the runtime-only LumOn debug mode.</summary>
    [Fact]
    public void LumOnConfig_DoesNotSerialize_DebugMode()
    {
        // Arrange
        var cfg = new VgeConfig
        {
            LumOn =
            {
                Enabled = true,
                DebugMode = LumOnDebugMode.ProbeAtlasMetaFlags
            }
        };

        // Act
        string json = JsonConvert.SerializeObject(cfg);

        // Assert
        Assert.DoesNotContain("\"DebugMode\"", json);
    }
    #endregion
}
