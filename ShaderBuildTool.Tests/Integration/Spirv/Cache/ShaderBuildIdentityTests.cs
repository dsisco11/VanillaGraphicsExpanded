using ShaderBuildTool.Spirv;
using ShaderBuildTool.Tests.Support.Generation;

namespace ShaderBuildTool.Tests.Integration.Spirv.Cache;

/// <summary>Verifies build implementation identity changes with implementation content and schema.</summary>
public sealed class ShaderBuildIdentityTests
{
    #region Public API

    #region Processing and compiler identities

    /// <summary>Implementation identities use file content and schema rather than timestamps or input order.</summary>
    [Fact]
    public void ImplementationHashTracksContentsAndSchema()
    {
        using var fixture = new ShaderBuildFixture();
        string a = Path.Combine(fixture.Assets, "a.dll"), b = Path.Combine(fixture.Assets, "b.dll");
        File.WriteAllText(a, "a"); File.WriteAllText(b, "b");
        string before = ShaderBuildIdentities.Files("schema", [a, b]);
        Assert.Equal(before, ShaderBuildIdentities.Files("schema", [b, a]));
        Assert.NotEqual(before, ShaderBuildIdentities.Files("new-schema", [a, b]));
        File.WriteAllText(b, "c");
        Assert.NotEqual(before, ShaderBuildIdentities.Files("schema", [a, b]));
    }

    #endregion
    #endregion

    #region Private
    /// <summary>Constructs a resolved selection with independently mutable source, layout and specialization inputs.</summary>
    #endregion
}
