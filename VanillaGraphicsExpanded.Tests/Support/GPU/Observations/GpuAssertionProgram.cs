using TestUniformRing = VanillaGraphicsExpanded.Tests.Support.GPU.Native.TestUniformRing;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;

namespace VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

/// <summary>Owns one production compute asset and publishes explicitly initialized observation inputs.</summary>
internal sealed class GpuAssertionProgram : GpuComputeProgram
{
    private readonly (nint Handle, long Generation) origin = RenderContextRegistry.Current();
    private readonly PackedUniformBuffer inputs;
    private (string Name, GpuTexture? Texture)[] samples = [], outputs = [];

    #region Internal API
    /// <summary>Adopts a completed production asset with the exact declared sampler/image interface.</summary>
    internal GpuAssertionProgram(ComputeShaderAsset asset) : base(asset)
    {
        inputs = OwnUniformBuffer(new PackedUniformBuffer(160));
        var active = Shader.SelectedShader.Interface.PreparedBindings.ActiveEntries;
        SamplerUnits = active.Where(entry => entry.Binding.Contract.Kind == ShaderBindingKind.Sampler)
            .Select(entry => entry.Binding.Contract.Binding.Slot + entry.Element).Distinct().ToImmutableArray();
        ImageUnits = active.Where(entry => entry.Binding.Contract.Kind == ShaderBindingKind.Image)
            .Select(entry => entry.Binding.Contract.Binding.Slot + entry.Element).Distinct().ToImmutableArray();

    }

    /// <summary>Retains all active sampler selectors touched by binding publication, including optional-input clearing.</summary>
    internal ImmutableArray<int> SamplerUnits { get; }
    /// <summary>Retains all active image selectors touched by binding publication.</summary>
    internal ImmutableArray<int> ImageUnits { get; }

    /// <summary>Publishes all mutable inputs afresh before each ordinary production compute submission.</summary>
    internal void Execute(byte[] bytes, (string Name, GpuTexture? Texture)[] sources, (string Name, GpuTexture? Texture)[] destinations, int x, int y = 1, int z = 1)
    {
        RequireContext();
        TestUniformRing.EnsureFrame();
        samples = sources; outputs = destinations;
        try { inputs.SetBytes(bytes); Dispatch(x, y, z); }
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
        var samplers = samples.Where(input => input.Texture != null).Select(input => new RHISamplerBinding(
            GpuBindingEntry.Identity(ShaderBindingKind.Sampler, input.Name), input.Texture!));
        var images = outputs.Where(input => input.Texture != null).Select(input => new RHIImageInput(
            GpuBindingEntry.Identity(ShaderBindingKind.Image, input.Name),
            new RHIImageBinding(input.Texture!, ImageAccess.ReadWrite, Layered: input.Texture!.TextureTarget == TextureDimension.TwoArray,
                Format: input.Texture!.InternalFormat)));
        return new(Shader, samplers: samplers, images: images,
            uniforms: [new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "AssertionInputs"), inputs.Capture())]);
    }
    #endregion
}
