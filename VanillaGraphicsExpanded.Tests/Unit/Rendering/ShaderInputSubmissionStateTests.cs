using System.Reflection;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks persistent ordinary assignments and the actual publication guard without graphics activation.</summary>
public sealed class ShaderInputSubmissionStateTests
{
    #region Public API
    /// <summary>Direct assignments retain omitted parameters without requiring an edit scope.</summary>
    [Fact]
    public void OrdinaryAssignmentsRetainOtherParameterValues()
    {
        var shader = new LumOnUpsampleShaderProgram();
        shader.UpsampleDepthSigma = 0.5f;
        shader.UpsampleNormalSigma = 8f;
        var parameters = (LumOnUpsampleParamsUbo)typeof(LumOnUpsampleShaderProgram)
            .GetField("paramsUbo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shader)!;
        parameters.UpsampleDepthSigma = 2f;
        Assert.Equal(2f, parameters.UpsampleDepthSigma);
        Assert.Equal(8f, parameters.UpsampleNormalSigma);
        Assert.True(parameters.IsDirty);
    }

    /// <summary>Mutation and recursive activation are rejected only during publication, then edits resume.</summary>
    [Fact]
    public void SubmissionRejectsMutationAndReentrancyThenReleasesGuard()
    {
        var shader = new GuardedShader { Value = 7 };
        shader.Publish();
        Assert.Equal(7, shader.Value);
        shader.Value = 9;
        Assert.Equal(9, shader.Value);
        shader.Fail = true;
        Assert.Throws<TargetInvocationException>(shader.Publish);
        shader.Value = 11;
        Assert.Equal(11, shader.Value);
    }
    #endregion

    #region Private
    /// <summary>Exercises the production submission boundary with a CPU-only implementation.</summary>
    private sealed class GuardedShader : GeneratedAccessorShader
    {
        private int value;
        /// <summary>Retains one guarded runtime value.</summary>
        public int Value { get => value; set { RequireInputMutation(); this.value = value; } }
        /// <summary>Requests an exception after checking the guard.</summary>
        public bool Fail { get; set; }
        /// <summary>Invokes the real publication coordinator without invoking OpenGL activation.</summary>
        public void Publish() => typeof(GpuProgram).GetMethod("SubmitPreparedInputs", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(this, null);
        /// <summary>Verifies both mutation and recursive executable work are forbidden while publishing.</summary>
        protected override void Submit()
        {
            Assert.Throws<InvalidOperationException>(() => Value = 23);
            Assert.Throws<InvalidOperationException>(RequireOutsideSubmission);
            if (Fail) throw new InvalidOperationException("Controlled submission failure.");
        }
    }
    #endregion
}
