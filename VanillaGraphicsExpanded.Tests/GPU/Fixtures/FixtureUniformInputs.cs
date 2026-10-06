using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Publishes test-owned controls alongside runtime engine GLSL compatibility interfaces.</summary>
internal sealed class FixtureUniformInputs : CpuUniformBuffer
{
    #region Public API
    #region Interface setup
    /// <summary>Creates inputs for a binary fixture with its block binding authored in source.</summary>
    internal FixtureUniformInputs(int size) : base(size) { }

    /// <summary>Assigns the linked test block once without changing engine-owned numeric inputs.</summary>
    internal FixtureUniformInputs(int program, int size) : this(size)
    {
        var layout = new GpuProgramLayout();
        layout.RegisterUniformBlockBinding("TestInputs", GpuBindingRegistry.Ubo.ShaderInputs);
        layout.ApplyContract(program);
    }
    #endregion
    #region Typed packing
    /// <summary>Writes a float control at its declared std140 offset.</summary>
    internal void Float(int offset, float value) => WriteFloat(offset, value);
    /// <summary>Writes an integer or 32-bit boolean control at its declared offset.</summary>
    internal void Integer(int offset, int value) => WriteInt32(offset, value);
    /// <summary>Writes vector components without occupying the following scalar's bytes.</summary>
    internal void Vector(int offset, float x, float y, float z) => WriteVector3(offset, new Vector3(x, y, z));
    /// <summary>Copies a matrix in the existing column ordering.</summary>
    internal void Matrix(int offset, ReadOnlySpan<float> columns) => WriteMatrix4(offset, columns);
    #endregion
    #region Publication
    /// <summary>Establishes the immutable block range through the current test frame's allocator.</summary>
    internal void Publish()
    {
        if (!TryBindToSlot(GpuBindingRegistry.Ubo.ShaderInputs))
            throw new InvalidOperationException("Fixture inputs require an open uniform publication frame.");
    }
    #endregion
    #endregion
}
