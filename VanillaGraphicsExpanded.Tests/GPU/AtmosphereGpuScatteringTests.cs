using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks bounded samples of every production angular and path integration budget.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class AtmosphereGpuScatteringTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Source transport
    /// <summary>Uses real SPIR-V and SSBO owners to compare selected cells at every quality without building enormous CPU tables.</summary>
    [Theory]
    [InlineData(0, .1f)] [InlineData(1, .1f)] [InlineData(2, .1f)] [InlineData(3, .1f)]
    [InlineData(0, 0f)] [InlineData(0, 1f)]
    public void SelectedCellsMatchScalarReference(int quality, float groundAlbedo)
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        Assert.True(GpuComputePipeline.TryCreateFromAssets(assets.Api, new ShaderSettings(AtmosphereComputePrograms.Scattering), out var program, out string log), log);
        using var ownedProgram = program!;
        using var parameters = GpuShaderStorageBuffer.Create();
        parameters.EnsureCapacity(64, growExponentially: false);
        using var source = GpuShaderStorageBuffer.Create();
        var budget = AtmosphereScatteringBudget.FromQuality(quality);
        source.EnsureCapacity(budget.Width * budget.Height * 16, growExponentially: false);
        foreach (int cell in new[] { budget.Width - 1, (budget.Height / 2) * budget.Width + budget.Width / 2, budget.Width * budget.Height - 1 })
        {
            Vector4[] inputs = [new(0, 1, 0, .001f), new(2.4f, groundAlbedo, 32, 24),
                new(budget.Width, budget.Height, budget.DirectionSamples, budget.RaySamples), new(budget.LightSamples, cell, 0, 0)];
            parameters.UploadSubData<Vector4>(inputs, 0, 64);
            parameters.BindBase(0); source.BindBase(1);
            try
            {
                ownedProgram.Dispatch(1);
                GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.BufferUpdateBarrierBit);
                using var fence = GpuFence.Insert();
                Assert.Contains(fence.Wait(TimeSpan.FromSeconds(5)), new[] { WaitSyncStatus.AlreadySignaled, WaitSyncStatus.ConditionSatisfied });
                using var mapped = source.MapRange<Vector4>(cell * 16, 1, MapBufferAccessMask.MapReadBit);
                Assert.True(mapped.IsMapped);
                Vector3 expected = Reference(cell, budget, groundAlbedo);
                Vector4 actual = mapped.Span[0];
                Assert.True(Vector3.Distance(expected, new(actual.X, actual.Y, actual.Z)) <= 1e-4f + .01f * expected.Length(), $"quality={quality}, cell={cell}, expected={expected}, actual={actual}");
            }
            finally { GpuShaderStorageBuffer.UnbindBase(0); GpuShaderStorageBuffer.UnbindBase(1); }
        }
    }

    /// <summary>Evaluates only the requested source cell with the retained scalar numerical oracle.</summary>
    private static Vector3 Reference(int cell, AtmosphereScatteringBudget budget, float groundAlbedo)
    {
        float v = (float)(cell / budget.Width) / (budget.Height - 1);
        float altitude = .001f + 99 * v * v;
        float cosine = 2f * (cell % budget.Width) / (budget.Width - 1) - 1;
        Vector3 sun = new(MathF.Sqrt(MathF.Max(0, 1 - cosine * cosine)), cosine, 0);
        Vector3 source = default, feedback = default;
        for (int d = 0; d < budget.DirectionSamples; d++)
        {
            float up = 1 - 2f * (d + .5f) / budget.DirectionSamples, azimuth = d * 2.39996323f;
            float horizontal = MathF.Sqrt(1 - up * up);
            Vector3 direction = new(horizontal * MathF.Cos(azimuth), up, horizontal * MathF.Sin(azimuth));
            var transfer = AtmosphereModel.MultipleScatteringTransfer(direction, sun, altitude, 2.4f, groundAlbedo, budget.RaySamples, budget.LightSamples);
            source += transfer.Source; feedback += transfer.Feedback;
        }
        source /= budget.DirectionSamples; feedback /= budget.DirectionSamples;
        return source / Vector3.Max(new(1e-5f), Vector3.One - feedback);
    }
    #endregion
}
