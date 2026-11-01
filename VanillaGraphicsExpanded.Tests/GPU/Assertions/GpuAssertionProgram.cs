using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Owns one production compute asset and publishes explicitly initialized observation inputs.</summary>
internal sealed class GpuAssertionProgram : GpuComputeProgram
{
    private readonly (nint Handle, long Generation) origin = RenderContextRegistry.Current();
    private readonly PackedUniformBuffer inputs;
    private readonly string[] samplerNames, imageNames;
    private readonly ImageAccess imageAccess;
    private GpuTexture[] samples = [], outputs = [];

    #region Internal API
    /// <summary>Adopts a completed production asset with the exact declared sampler/image interface.</summary>
    internal GpuAssertionProgram(ComputeShaderAsset asset, string family) : base(asset)
    {
        inputs = OwnUniformBuffer(new PackedUniformBuffer(160));
        imageAccess = family == "check" ? ImageAccess.ReadWrite : ImageAccess.WriteOnly;
        samplerNames = family == "check" ? ["actualImage", "historyImage", "expectedImage", "additionalImage", "fourthImage"]
            : ["sourceImage", "referenceImage"];
        imageNames = family == "check" ? ["resultImage", "histogram"]
            : family == "layer" ? ["destination"] : ["destinationImage"];
    }

    /// <summary>Publishes all mutable inputs afresh before each ordinary production compute submission.</summary>
    internal void Execute(byte[] bytes, GpuTexture[] sources, GpuTexture[] destinations, int x, int y = 1, int z = 1)
    {
        RequireContext();
        Fixtures.TestUniformRing.EnsureFrame();
        if (sources.Length != samplerNames.Length || destinations.Length != imageNames.Length)
            throw new ArgumentException("Observation resources must match the declared interface.");
        samples = sources; outputs = destinations;
        inputs.SetBytes(bytes);
        try { Dispatch(x, y, z); }
        finally { samples = []; outputs = []; }
    }

    /// <summary>Rejects retired owners and foreign physical context generations before observation.</summary>
    internal void RequireContext()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (!RenderContextRegistry.IsCurrentResourceContext(origin))
            throw new InvalidOperationException("The checker requires its original physical context.");
    }
    #endregion

    #region Protected API
    /// <summary>Captures the exact shader generation, numeric block and borrowed resources for this submission.</summary>
    protected override RHIResourceBindings CaptureBindingsCore()
    {
        var samplers = samples.Select((texture, index) => new RHISamplerBinding(
            GpuBindingEntry.Identity(ShaderBindingKind.Sampler, samplerNames[index]), texture));
        var images = outputs.Select((texture, index) => new RHIImageInput(
            GpuBindingEntry.Identity(ShaderBindingKind.Image, imageNames[index]),
            new RHIImageBinding(texture, imageAccess, Layered: texture.TextureTarget == TextureDimension.TwoArray,
                Format: texture.InternalFormat)));
        return new(Shader, samplers: samplers, images: images,
            uniforms: [new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "AssertionInputs"), inputs.Capture())]);
    }
    #endregion
}
