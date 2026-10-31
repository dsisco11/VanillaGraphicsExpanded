using System;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Realizes prepared semantic selections without duplicating canonical metadata or executed slot knowledge.</summary>
internal sealed class OpenGLBindingPlan
{
    /// <summary>Retains native selectors only for the owning structure's established configuration.</summary>
    internal sealed class Input
    {
        internal readonly RHIResourceBindingStructure.Input Selection;
        internal int Texture;
        internal int Sampler;
        internal int Buffer;
        internal TextureFormat Format;

        /// <summary>Captures one active semantic input for native realization.</summary>
        internal Input(RHIResourceBindingStructure.Input selection) { Selection = selection; }
    }

    internal readonly Input[] Textures;
    internal readonly Input[] Other;
    internal readonly Input[] Uniforms;
    internal readonly RHIResourceBindingStructure Structure;
    internal ulong Revision = ulong.MaxValue;

    #region Internal API
    /// <summary>Partitions active work once; uniform versions do not allocate another structural plan.</summary>
    internal OpenGLBindingPlan(RHIResourceBindingStructure structure)
    {
        Structure = structure;
        var inputs = structure.Inputs.Select(value => new Input(value)).ToArray();
        Textures = inputs.Where(value => value.Selection.Binding.Contract.Kind == ShaderBindingKind.Sampler)
            .OrderBy(value => value.Selection.Binding.Contract.Binding.Slot).ToArray();
        Other = inputs.Where(value => value.Selection.Binding.Contract.Kind != ShaderBindingKind.Sampler).ToArray();
        Uniforms = inputs.Where(value => value.Selection.HasUniform).ToArray();
    }
    #endregion
}
