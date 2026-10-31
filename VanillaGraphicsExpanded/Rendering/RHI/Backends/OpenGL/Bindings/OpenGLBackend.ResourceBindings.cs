using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes owner-prepared bindings through the context's existing slot cache and uniform allocator.</summary>
internal sealed partial class OpenGLBackend
{
    private readonly ConditionalWeakTable<RHIResourceBindingStructure, OpenGLBindingPlan> bindingPlans = new();
    private OpenGLBindingPlan? executionBindingPlan;
    private RHIResourceBindings? executionBindings;
#if DEBUG
    /// <summary>Counts native structural plans independently of uniform versions and executed-state comparisons.</summary>
    internal long BindingPlansPrepared { get; private set; }
#endif

    #region Public API
    /// <inheritdoc />
    public void BindResources(RHIShaderRef reference, RHIResourceBindings inputs)
    {
        RequireContext();
        ArgumentNullException.ThrowIfNull(inputs);
        var shader = reference.SelectedShader;
        SelectResourceBindings(shader, inputs);
        try { PublishResourceBindings(shader, reuseCommittedUniforms: false); }
        finally { ClearResourceBindings(); }
    }
    #endregion

    #region Private
    /// <summary>Selects cached structural work, refreshing only after a relevant committed resource change.</summary>
    private void SelectResourceBindings(RHIShader shader, RHIResourceBindings? inputs)
    {
        if (executionBindingPlan is not null) throw new InvalidOperationException("Resource publication cannot recurse.");
        shader.RequirePrepared();
        if (inputs is null)
        {
            if (shader.Interface.PreparedBindings.ActiveEntries.Count != 0)
                throw new InvalidOperationException("Rendering requires a captured resource table.");
            return;
        }
        if (inputs.Shader != (RHIShaderRef)shader)
            throw new InvalidOperationException("Captured resources belong to a different executable generation.");
        inputs.Prepare(Capabilities, this);
        if (!bindingPlans.TryGetValue(inputs.Structure, out var plan))
        {
            plan = new(inputs.Structure);
            bindingPlans.Add(inputs.Structure, plan);
#if DEBUG
            BindingPlansPrepared++;
#endif
        }
        if (plan.Revision != inputs.Structure.Revision) RealizeResourceBindings(plan);
        foreach (var input in plan.Uniforms)
        {
            inputs.TryUniform(input.Selection.Identity, out var uniform);
            if (uniform.Snapshot is not { } snapshot) continue;
            snapshot.RequireUsable();
            if (!RHIUniformRingSystem.TryGetCurrent(out var ring))
                throw new InvalidOperationException("Captured uniform publication requires an open allocation epoch.");
            ring.RequireOpenPublication();
        }
        // Attachment overlap belongs to the current pass, not the reusable shader/resource structure.
        foreach (var input in plan.Other)
            if (input.Selection.Binding.Contract.Kind == ShaderBindingKind.Image && !input.Selection.Image.Texture.IsEmpty)
                Pipeline.Passes.RenderPass.ValidateImageTexture(input.Selection.Image);
        executionBindings = inputs;
        executionBindingPlan = plan;
    }

    /// <summary>Refreshes backend selectors after semantic preparation has succeeded for the complete structure.</summary>
    private void RealizeResourceBindings(OpenGLBindingPlan plan)
    {
        foreach (var input in plan.Textures)
        {
            var selected = input.Selection.Sampler;
            input.Texture = selected.Texture.Resource is { } texture ? checked((int)texture.ResourceId) : 0;
            input.Sampler = input.Texture == 0 ? 0 : selected.Sampler.IsEmpty
                ? ResolvePolicySampler((ShaderSamplerPolicy)input.Selection.Binding.Contract.Binding.Sampler, plan.Structure)
                : checked((int)selected.Sampler.RequireResource().ResourceId);
        }
        foreach (var input in plan.Other)
        {
            var selected = input.Selection;
            if (selected.Binding.Contract.Kind == ShaderBindingKind.Image)
            {
                input.Texture = selected.Image.Texture.Resource is { } image ? checked((int)image.ResourceId) : 0;
                input.Format = input.Texture == 0 ? TextureFormat.Rgba8 : selected.Image.Format ?? selected.Image.Texture.RequireImage(selected.Image.Level).Format;
            }
            else if (!selected.Buffer.Buffer.IsEmpty) input.Buffer = checked((int)selected.Buffer.Buffer.RequireResource().ResourceId);
        }
        plan.Revision = plan.Structure.Revision;
    }

    /// <summary>Clears command-local selections without owning or guarding their native resources.</summary>
    private void ClearResourceBindings() { executionBindingPlan = null; executionBindings = null; }

    /// <summary>Applies active slots using actual executed knowledge; external changes require no compatibility rebuild.</summary>
    private void PublishResourceBindings(RHIShader shader, bool reuseCommittedUniforms)
    {
        ApplyExecutable(shader);
        var plan = executionBindingPlan ?? throw new InvalidOperationException("Binding execution was not prepared.");
        PublishTextureInputs(plan);
        foreach (var input in plan.Other)
        {
            var selection = input.Selection;
            var binding = selection.Binding;
            int slot = binding.Contract.Binding.Slot;
            switch (binding.Contract.Kind)
            {
                case ShaderBindingKind.Image:
                    var image = selection.Image;
                    OpenGLImageSlotBindings.BindImageTexture(cache, slot, input.Texture, input.Texture == 0 ? 0 : image.Level,
                        input.Texture != 0 && image.Layered, input.Texture == 0 ? 0 : image.Layer,
                        image.Access switch { ImageAccess.ReadOnly => OpenTK.Graphics.OpenGL.TextureAccess.ReadOnly, ImageAccess.WriteOnly => OpenTK.Graphics.OpenGL.TextureAccess.WriteOnly, _ => OpenTK.Graphics.OpenGL.TextureAccess.ReadWrite }, (OpenTK.Graphics.OpenGL.SizedInternalFormat)OpenGLImageValues.Format(input.Format));
                    break;
                case ShaderBindingKind.UniformBlock when selection.HasUniform:
                    executionBindings!.TryUniform(selection.Identity, out var uniform);
                    PublishUniformSnapshot(slot, uniform.Snapshot, reuseCommittedUniforms);
                    break;
                default:
                    PublishBufferInput(binding.Contract.Kind, slot, selection.Buffer, input.Buffer);
                    break;
            }
        }
    }

    /// <summary>Consumes only the active captured versions actually selected for the impending draw or dispatch.</summary>
    private void MarkBindingExecution()
    {
        if (executionBindingPlan is not { } plan) return;
        foreach (var input in plan.Uniforms)
        {
            executionBindings!.TryUniform(input.Selection.Identity, out var uniform);
            if (uniform.Snapshot is { } snapshot) snapshot.Publication.MarkExecution(snapshot.ContentRevision);
        }
    }

    #endregion
}
