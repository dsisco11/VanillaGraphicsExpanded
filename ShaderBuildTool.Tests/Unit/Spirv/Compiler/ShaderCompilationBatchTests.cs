using ShaderBuildTool.Spirv;

namespace ShaderBuildTool.Tests.Unit.Spirv.Compiler;

/// <summary>Checks invalid batch bounds and duplicate jobs are rejected before work starts.</summary>
public sealed class ShaderCompilationBatchTests
{

    #region Failure and cancellation

    /// <summary>Invalid bounds and duplicate work fail before any compiler invocation.</summary>
    [Fact]
    public async Task RejectsInvalidBatchBeforeStarting()
    {
        bool started = false;
        var job = new ShaderCompilationJob("same", token =>
        {
            started = true;
            return Task.FromResult(new ShaderCompilerResult(0, "", ""));
        });
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ShaderCompilationBatch.RunAsync([job], 0, TextWriter.Null, TextWriter.Null, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => ShaderCompilationBatch.RunAsync([job, job], 2, TextWriter.Null, TextWriter.Null, TestContext.Current.CancellationToken));
        Assert.False(started);
    }
    #endregion
}
