using System.Numerics;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering.Integration;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.Unit.Water;

/// <summary>Checks the input-only liquid bridge's supported and rejected contracts without native state.</summary>
public sealed class LiquidPoolInputAdapterTests
{
    #region Public API
    /// <summary>The bridge translates supported logical inputs and rejects unknown or executable operations.</summary>
    [Fact]
    public void TranslatorRejectsUnknownInputsAndExecutableOperations()
    {
        // Arrange a typed sink and a logical-input adapter.
        var sink = new Inputs();
        var adapter = new LiquidPoolInputAdapter(sink);

        // Act by staging the supported inputs, then query advertised support.
        adapter.Uniform("origin", new Vec3f(1, 2, 3));
        adapter.UniformMatrix("modelViewMatrix", [1, 2, 3]);
        adapter.Uniform("forcedTransparency", .25f);

        // Assert supported writes reach the sink and unsupported names are not advertised.
        Assert.Equal(new Vector3(1, 2, 3), sink.Origin);
        Assert.Equal(new float[] { 1, 2, 3 }, sink.ModelViewMatrix);
        Assert.Equal(.25f, sink.ForcedTransparency);
        foreach (string name in new[] { "origin", "modelViewMatrix", "forcedTransparency" })
        {
            Assert.True(adapter.HasUniform(name));
        }
        Assert.False(adapter.HasUniform("mvpMatrix"));
        Assert.False(adapter.HasUniform("unknown"));

        // Assert invalid writes and operations requiring an executable remain explicitly unsupported.
        Assert.Throws<NotSupportedException>(() => adapter.Uniform("unknown", new Vec3f()));
        Assert.Throws<NotSupportedException>(() => adapter.UniformMatrix("mvpMatrix", []));
        Assert.Throws<NotSupportedException>(() => adapter.Uniform("origin", 0f));
        Assert.Throws<NotSupportedException>(() => adapter.Uniform("forcedTransparency", 0));
        Assert.Throws<NotSupportedException>(adapter.Use);
        Assert.Throws<NotSupportedException>(adapter.Stop);
        Assert.Throws<NotSupportedException>(() => adapter.Compile());
        Assert.Throws<NotSupportedException>(adapter.Dispose);
        Assert.Throws<NotSupportedException>(() => adapter.ProgramId);
        Assert.Throws<NotSupportedException>(() => adapter.VertexShader);
        Assert.Throws<NotSupportedException>(() => adapter.BindTexture2D("terrainTex", 1, 0));
    }
    #endregion

    #region Private
    /// <summary>Retains typed values so the adapter's writes can be observed without native state.</summary>
    private sealed class Inputs : ILiquidPoolInputs
    {
        #region Public API
        /// <summary>Gets or sets the staged origin.</summary>
        public Vector3 Origin { get; set; }

        /// <summary>Gets or sets the staged transform.</summary>
        public float[] ModelViewMatrix { get; set; } = [];

        /// <summary>Gets or sets preview transparency.</summary>
        public float ForcedTransparency { get; set; }
        #endregion
    }
    #endregion
}
