using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>
/// Integration tests for specialized query wrappers built on <see cref="GpuQuery"/>.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public class GpuSpecializedQueryIntegrationTests
{
    private readonly HeadlessGLFixture fixture;

    #region Public API

    /// <summary>Uses the shared headless OpenGL fixture for query execution.</summary>
    public GpuSpecializedQueryIntegrationTests(HeadlessGLFixture fixture)
    {
        this.fixture = fixture;
    }

    /// <summary>Issues a timestamp query and verifies its completed result is nonnegative.</summary>
    [Fact]
    public void TimestampQuery_Issue_ProducesResult()
    {
        // Arrange
        fixture.MakeCurrent();

        using var q = GpuTimestampQuery.Create("Test.TimestampQuery");
        Assert.True(q.IsValid);

        // Act
        q.Issue();

        GL.Finish();

        // Assert
        Assert.True(WaitForAvailable(q.IsResultAvailable));
        long ns = q.GetResultNanoseconds();
        Assert.True(ns >= 0);
    }

    /// <summary>Measures a scoped GPU operation and verifies the elapsed result is nonnegative.</summary>
    [Fact]
    public void TimerQuery_BeginEnd_ProducesElapsedResult()
    {
        // Arrange
        fixture.MakeCurrent();

        using var q = GpuTimerQuery.Create("Test.TimerQuery");
        Assert.True(q.IsValid);

        // Act
        using (q.BeginScope())
        {
            GL.ClearColor(0.1f, 0.2f, 0.3f, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit);
        }

        GL.Finish();

        // Assert
        Assert.True(WaitForAvailable(q.IsResultAvailable));
        long ns = q.GetResultNanoseconds();
        Assert.True(ns >= 0);
    }

    #endregion
    #region Private

    /// <summary>Polls query availability until completion or the bounded timeout.</summary>
    private static bool WaitForAvailable(Func<bool> isAvailable, int timeoutMs = 250)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (isAvailable())
            {
                return true;
            }
        }

        return false;
    }
    #endregion
}

