using System.Runtime.InteropServices;
using ShaderBuildTool.Spirv;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Terrain;

/// <summary>Executes the authored shared deformation kernel with nonzero material motion inputs.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class SharedVertexDeformationGpuTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Entity, static-item and liquid material orders produce finite, bounded motion on the GPU.</summary>
    [Fact]
    public void SharedKernelProducesBoundedMaterialSpecificMotion()
    {
        // Arrange
        EnsureContextValid();
        var context = RHICommandContext.Current;
        using var shader = BuiltShaderFixture.LoadProgram(new(new ShaderSettings(
            TestShaderPrograms.Create().Programs["tests/shared-deformation-numeric"])));
        using var pipeline = context.CreateComputePipeline(new(shader)).RequireResource();
        byte[] inputs = Inputs();
        using var inputBuffer = context.CreateUniformBuffer(new RHIBufferDescription
        {
            Role = BufferRole.Uniform,
            Mode = ResourceStorageMode.Mutable,
            Capacity = inputs.Length,
            Usage = BufferUsage.DynamicDraw,
            Label = "SharedDeformation.Inputs"
        }, new(inputs)).RequireResource();
        using var outputBuffer = context.CreateBuffer(new RHIBufferDescription
        {
            Role = BufferRole.ShaderStorage,
            Mode = ResourceStorageMode.Mutable,
            Capacity = 64,
            Usage = BufferUsage.DynamicRead,
            Label = "SharedDeformation.Results"
        }).RequireResource();

        var bindings = new RHIResourceBindings(shader,
            buffers:
            [
                new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "DeformationInputs"), BufferRole.Uniform, inputBuffer),
                new(GpuBindingEntry.Identity(ShaderBindingKind.StorageBlock, "DeformationResults"), BufferRole.ShaderStorage, outputBuffer)
            ]);

        // Act
        context.SetPipeline(pipeline);
        context.BindResources(bindings);
        context.Dispatch(new RHIDispatch(1));
        context.Barrier(new RHIBarrierDescription(ResourceAccess.BufferUpdate));
        using var readback = context.EnqueueReadback(new RHIReadbackDescription(
            new RHIBufferRange(outputBuffer, 0, 64)));
        Assert.Equal(CompletionStatus.Ready, readback.Wait());
        float[] result;
        using (var mapped = readback.MapReadyReadback())
            result = MemoryMarshal.Cast<byte, float>(mapped.Bytes).ToArray();

        // Assert
        Assert.All(result, value => Assert.True(float.IsFinite(value)));
        Assert.True(Distance(result, 0) > 0.01f, "The full static warp mode should move a placed vertex.");
        Assert.Equal(0f, Distance(result, 4));
        Assert.True(Distance(result, 8) > 0.01f, "Quarter-strength static warp should move a placed vertex.");
        Assert.True(Distance(result, 12) > 0.01f, "The entity liquid-mask path should move a placed vertex.");
        Assert.InRange(Distance(result, 0), 0.01f, 0.5f);
        Assert.InRange(Distance(result, 12), 0.01f, 0.5f);
        // Expected samples independently evaluate the specified local, global, and perception equations
        // for this nonzero world origin, frame phases, material flags, and strengths.
        AssertVector(result, 0, [3.17723f, -1.97531f, 6.76453f, 1f]);
        AssertVector(result, 4, [3.25f, -2f, 6.75f, 1f]);
        AssertVector(result, 8, [3.26877f, -1.97529f, 6.78765f, 1f]);
        AssertVector(result, 12, [3.29480f, -1.99761f, 6.84027f, 1f]);
    }
    #endregion

    #region Private
    /// <summary>Packs explicit shared-kernel inputs into the uniform block's std140 layout.</summary>
    private static byte[] Inputs()
    {
        byte[] bytes = new byte[64];
        WriteVec3(bytes, 0, .4f, -.2f, .7f);
        WriteFloat(bytes, 12, 1.2f);
        WriteFloat(bytes, 16, .8f);
        WriteFloat(bytes, 20, 2.1f);
        WriteFloat(bytes, 24, .6f);
        WriteFloat(bytes, 28, 1.1f);
        WriteFloat(bytes, 32, .8f);
        WriteFloat(bytes, 36, .25f);
        WriteFloat(bytes, 40, .7f);
        WriteFloat(bytes, 44, .9f);
        WriteInt(bytes, 48, 2);
        WriteFloat(bytes, 52, .5f);
        return bytes;
    }

    /// <summary>Writes one std140 float at its declared scalar offset.</summary>
    private static void WriteFloat(byte[] destination, int offset, float value) =>
        System.Buffer.BlockCopy(BitConverter.GetBytes(value), 0, destination, offset, sizeof(float));

    /// <summary>Writes one std140 integer at its declared scalar offset.</summary>
    private static void WriteInt(byte[] destination, int offset, int value) =>
        System.Buffer.BlockCopy(BitConverter.GetBytes(value), 0, destination, offset, sizeof(int));

    /// <summary>Writes the three vector lanes at their std140 field offset.</summary>
    private static void WriteVec3(byte[] destination, int offset, float x, float y, float z)
    {
        WriteFloat(destination, offset, x);
        WriteFloat(destination, offset + sizeof(float), y);
        WriteFloat(destination, offset + sizeof(float) * 2, z);
    }

    /// <summary>Measures displacement from the authored nonzero source coordinate.</summary>
    private static float Distance(float[] values, int offset)
    {
        float x = values[offset] - 3.25f;
        float y = values[offset + 1] + 2f;
        float z = values[offset + 2] - 6.75f;
        return MathF.Sqrt(x * x + y * y + z * z);
    }

    /// <summary>Compares GPU results against independently evaluated deformation samples.</summary>
    private static void AssertVector(float[] actual, int offset, float[] expected)
    {
        for (int component = 0; component < expected.Length; component++)
            Assert.InRange(actual[offset + component], expected[component] - 0.0002f, expected[component] + 0.0002f);
    }
    #endregion
}
