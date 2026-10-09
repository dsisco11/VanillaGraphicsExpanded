using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Owns the pbr_bloom executable and its typed staged inputs.</summary>
[ShaderProgram("Contract", "pbr_bloom", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_postprocess.vsh", Identity = "pbr_bloom.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_bloom.fsh")]
internal sealed partial class BloomShaderProgram : GpuProgram, IBloomShaderProgramBindings
{
    private readonly PostprocessInputs inputs;
    private static readonly GaussianKernel[] kernels = CreateGaussianKernels();
    #region Public API
    /// <summary>Declares compilation and reload identity.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    /// <summary>Creates the retained parameter block and generated binding layout.</summary>
    public BloomShaderProgram() {
        inputs = OwnUniformBuffer(new PostprocessInputs());
        ProgramLayout.RegisterContract(Contract.Stages[0].Bindings);
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }
    /// <summary>Stages the SourceImage image.</summary>
    public partial GpuTexture? SourceImage { set; }
    /// <summary>Stages the SecondaryImage image.</summary>
    public partial GpuTexture? SecondaryImage { set; }
    /// <summary>Captures the complete draw parameters for the shader contract.</summary>
    internal void Capture(Vector4 pass, Vector4 effect, Vector4 sun = default, Vector4 solar = default)
        => inputs.Capture(pass, effect, sun, solar);
    /// <summary>Stages a dense Gaussian as paired linear samples; source and destination must have matching dimensions.</summary>
    internal void CaptureGaussian(int radius, bool vertical)
    {
        if (radius < 1 || radius > 8) throw new ArgumentOutOfRangeException(nameof(radius));
        var kernel = kernels[radius];
        inputs.Capture(new(radius, kernel.Center, vertical ? 3 : 2, 0), kernel.Weights, kernel.Offsets);
    }
    /// <summary>Publishes the retained input block.</summary>
    CpuUniformBuffer IBloomShaderProgramBindings.Inputs => inputs;
    #endregion

    #region Private
    /// <summary>Stores the center coefficient and four symmetric bilinear tap pairs.</summary>
    private readonly record struct GaussianKernel(float Center, Vector4 Weights, Vector4 Offsets);

    /// <summary>Precomputes normalized, contiguous Gaussian coefficients and combines adjacent samples exactly.</summary>
    private static GaussianKernel[] CreateGaussianKernels()
    {
        var result = new GaussianKernel[9];
        for (int radius = 1; radius <= 8; radius++)
        {
            var coefficients = new double[radius + 1];
            double sum = 0;
            for (int tap = 0; tap <= radius; tap++)
            {
                // The support reaches negligible tail energy rather than cutting a visible Gaussian shoulder.
                coefficients[tap] = Math.Exp(-16.7 * tap * tap / (radius * radius));
                sum += coefficients[tap] * (tap == 0 ? 1 : 2);
            }
            var weights = new float[4];
            var offsets = new float[4];
            for (int pair = 0; pair < 4; pair++)
            {
                int first = pair * 2 + 1;
                if (first > radius) break;
                double a = coefficients[first], b = first < radius ? coefficients[first + 1] : 0;
                // Linear interpolation at this offset reproduces both adjacent weighted texels.
                weights[pair] = (float)((a + b) / sum);
                offsets[pair] = first + (float)(b / (a + b));
            }
            result[radius] = new((float)(coefficients[0] / sum),
                new(weights[0], weights[1], weights[2], weights[3]),
                new(offsets[0], offsets[1], offsets[2], offsets[3]));
        }
        return result;
    }
    #endregion
}
