using System.Reflection;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks persistent ordinary assignments and the actual publication guard without graphics activation.</summary>
public sealed class ShaderInputSubmissionStateTests
{
    #region Public API
    /// <summary>Every concrete assembly owner has generator-owned state with exactly its non-UBO binding values.</summary>
    [Fact]
    public void EveryConcreteOwnerHasCompleteGeneratedState()
    {
        var owners = typeof(GpuProgram).Assembly.GetTypes().Where(type => !type.IsAbstract && !type.IsNested &&
            (type.IsSubclassOf(typeof(GpuProgram)) || type.IsSubclassOf(typeof(GpuComputeProgram)))).ToArray();
        Assert.NotEmpty(owners);
        foreach (var owner in owners)
        {
            var active = owner.GetField("__activeState", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Assert.NotNull(active);
            Assert.Equal(owner.Name + "State", active.FieldType.Name);
            Assert.True(active.FieldType.IsValueType);
            var expected = owner.GetInterfaces().SelectMany(type => type.GetProperties())
                .Where(property => property.CustomAttributes.Any(attribute => attribute.AttributeType == typeof(ShaderBindingAttribute)))
                .Where(property => !property.PropertyType.Namespace!.EndsWith(".Contracts", StringComparison.Ordinal))
                .Where(property => !typeof(CpuUniformBuffer).IsAssignableFrom(property.PropertyType) && property.PropertyType != typeof(GpuUniformBuffer))
                .GroupBy(property => property.Name).Select(group => group.First()).OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
            var fields = active.FieldType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .OrderBy(field => field.Name, StringComparer.Ordinal).ToArray();
            Assert.Equal(expected.Select(property => property.Name), fields.Select(field => field.Name));
            Assert.Equal(expected.Select(property => property.PropertyType), fields.Select(field => field.FieldType));
        }
    }
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
        Assert.Throws<InvalidOperationException>(shader.Publish);
        shader.Value = 11;
        Assert.Equal(11, shader.Value);
    }
    #endregion

    #region Private
    /// <summary>Exercises the production submission boundary with a CPU-only implementation.</summary>
    private sealed class GuardedShader : GeneratedAccessorShader, IGpuProgram
    {
        private int value;
        /// <summary>Retains one guarded runtime value.</summary>
        public int Value { get => value; set { RequireInputMutation(); this.value = value; } }
        /// <summary>Requests an exception after checking the guard.</summary>
        public bool Fail { get; set; }
        /// <summary>Invokes the real publication coordinator without invoking OpenGL activation.</summary>
        public void Publish() => ((IGpuProgram)this).Activate();
        /// <summary>Supplies ready CPU-only preparation for workflow guard verification.</summary>
        bool IGpuProgram.PrepareExecutable() => true;
        /// <summary>Avoids native binding in the CPU-only guard fixture.</summary>
        void IGpuProgram.BindExecutable() { }
        /// <summary>Owns no native activation to clear in the CPU-only fixture.</summary>
        void IGpuProgram.ClearActivation(bool bindingEntered) { }
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
