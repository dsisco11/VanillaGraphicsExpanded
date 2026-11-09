using ShaderBuildTool.Spirv;
using ShaderBuildTool.Tests.Support.Generation;

namespace ShaderBuildTool.Tests.Integration.Spirv.Build;

/// <summary>Verifies the optimized build path publishes a receipt reused by a later incremental invocation.</summary>
public sealed class BuildReceiptReuseTests
{

    #region Execution and cancellation

    /// <summary>The production entry point compiles isolated stages and reuses a matching compiler-policy receipt.</summary>
    [Fact]
    public void OptimizedConfigurationPublishesReusableBuildReceipt()
    {
        using var fixture = new ShaderBuildFixture();
        Assert.Equal(0, fixture.Build(1));
        var before = fixture.ContentSnapshot();
        Assert.NotEmpty(before);
        Assert.Equal(0, fixture.Build(1, clean: false, incremental: true));
        Assert.Equal(before, fixture.ContentSnapshot());
    }

    #endregion
}
