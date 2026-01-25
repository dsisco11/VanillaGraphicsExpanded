using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Integration tests for specialized query wrappers built on <see cref="GpuQuery"/>.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public class GpuSpecializedQueryIntegrationTests
{
    private readonly HeadlessGLFixture fixture;

    public GpuSpecializedQueryIntegrationTests(HeadlessGLFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public void TimestampQuery_Issue_ProducesResult()
    {
        fixture.MakeCurrent();

        using var q = GpuTimestampQuery.Create("Test.TimestampQuery");
        Assert.True(q.IsValid);

        q.Issue();

        GL.Finish();

        Assert.True(WaitForAvailable(q.IsResultAvailable));
        long ns = q.GetResultNanoseconds();
        Assert.True(ns >= 0);
    }

    [Fact]
    public void TimerQuery_BeginEnd_ProducesElapsedResult()
    {
        fixture.MakeCurrent();

        using var q = GpuTimerQuery.Create("Test.TimerQuery");
        Assert.True(q.IsValid);

        using (q.BeginScope())
        {
            GL.ClearColor(0.1f, 0.2f, 0.3f, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit);
        }

        GL.Finish();

        Assert.True(WaitForAvailable(q.IsResultAvailable));
        long ns = q.GetResultNanoseconds();
        Assert.True(ns >= 0);
    }

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
}

