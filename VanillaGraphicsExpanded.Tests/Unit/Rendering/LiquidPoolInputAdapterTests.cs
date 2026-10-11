using System.Numerics;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering.Integration;
using Vintagestory.API.MathTools;
namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;
/// <summary>Checks the input-only bridge's explicit supported and rejected contracts without native state.</summary>
public sealed class LiquidPoolInputAdapterTests
{
    #region Public API
    /// <summary>The bridge translates only the verified logical inputs and cannot be used as an executable.</summary>
    [Fact]
    public void TranslatorRejectsUnknownInputsAndExecutableOperations()
    {
        var sink = new Inputs(); var adapter = new LiquidPoolInputAdapter(sink);
        adapter.Uniform("origin", new Vec3f(1, 2, 3));
        adapter.UniformMatrix("modelViewMatrix", [1, 2, 3]);
        adapter.Uniform("forcedTransparency", .25f);
        Assert.Equal(new Vector3(1, 2, 3), sink.Origin);
        Assert.Equal(new float[] { 1, 2, 3 }, sink.ModelViewMatrix);
        Assert.Equal(.25f, sink.ForcedTransparency);
        foreach (string name in new[] { "origin", "modelViewMatrix", "forcedTransparency" }) Assert.True(adapter.HasUniform(name));
        Assert.False(adapter.HasUniform("mvpMatrix")); Assert.False(adapter.HasUniform("unknown"));
        Assert.Throws<NotSupportedException>(() => adapter.Uniform("unknown", new Vec3f()));
        Assert.Throws<NotSupportedException>(() => adapter.UniformMatrix("mvpMatrix", []));
        Assert.Throws<NotSupportedException>(() => adapter.Uniform("origin", 0f));
        Assert.Throws<NotSupportedException>(() => adapter.Uniform("forcedTransparency", 0));
        Assert.Throws<NotSupportedException>(adapter.Use); Assert.Throws<NotSupportedException>(adapter.Stop);
        Assert.Throws<NotSupportedException>(() => adapter.Compile()); Assert.Throws<NotSupportedException>(adapter.Dispose);
        Assert.Throws<NotSupportedException>(() => adapter.ProgramId);
        Assert.Throws<NotSupportedException>(() => adapter.VertexShader);
        Assert.Throws<NotSupportedException>(() => adapter.BindTexture2D("terrainTex", 1, 0));
    }
    #endregion
    #region Private
    /// <summary>Retains typed values for contract observation.</summary>
    private sealed class Inputs : ILiquidPoolInputs
    {
        /// <summary>Retains the staged origin.</summary>
        public Vector3 Origin { get; set; }
        /// <summary>Retains the staged transform.</summary>
        public float[] ModelViewMatrix { get; set; } = [];
        /// <summary>Retains preview transparency.</summary>
        public float ForcedTransparency { get; set; }
    }
    #endregion
}
