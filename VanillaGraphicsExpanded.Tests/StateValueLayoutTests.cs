using System.Runtime.CompilerServices;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Records managed state sizes so storage changes can be assessed against actual layout.</summary>
public sealed class StateValueLayoutTests
{
    private readonly ITestOutputHelper output;

    #region Public API
    /// <summary>Receives the test output destination for managed layout measurements.</summary>
    public StateValueLayoutTests(ITestOutputHelper output) => this.output = output;

    /// <summary>Measures managed state values and the existing category flag representations.</summary>
    [Fact]
    public void ReportManagedSizes()
    {
        Report<DepthState>();
        Report<BlendState>();
        Report<RasterizerState>();
        Report<PrimitiveAssemblyState>();
        Report<DynamicDrawState>();
        Report<SamplingState>();
        Report<StencilState>();
        Report<OutputState>();
        Report<StateCache.PixelPackState>();
        Report<StateCache.PixelUnpackState>();
        Report<LumOnCameraState>();
        Report<GlColorMask>();
        Report<DepthStateKnowledge>();
        Report<BlendStateKnowledge>();
        Report<RasterizerStateKnowledge>();
        Report<PrimitiveAssemblyStateKnowledge>();
        Report<DynamicDrawStateKnowledge>();
        Report<SamplingStateKnowledge>();
        Report<StencilStateKnowledge>();
        Report<OutputStateKnowledge>();
    }
    #endregion

    #region Private
    /// <summary>Reports the actual managed size rather than native marshaling layout.</summary>
    private void Report<T>() where T : struct => output.WriteLine($"{typeof(T).Name}: {Unsafe.SizeOf<T>()} bytes");
    #endregion
}
