using System;
using System.Collections.Generic;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Legacy = OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes one fully validated captured resource set using existing cache and uniform allocation authority.</summary>
internal sealed partial class OpenGLBackend
{
    private readonly List<GpuResource> executionResources = new();

    #region Public API
    /// <inheritdoc />
    public void BindResources(RHIShaderRef reference, RHIResourceBindings inputs)
    {
        RequireContext();
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Shader != reference) throw new InvalidOperationException("Captured bindings belong to a different executable generation.");
        var shader = reference.RequireShader();
        BeginBindingExecution(shader, inputs);
        try { PublishResourceBindings(shader, inputs, reuseCommittedUniforms: false); }
        finally { ReleaseExecutionResources(); }
    }
    #endregion

    #region Private
    /// <summary>Retains and validates the complete set for the entire binding or rendering command.</summary>
    private void BeginBindingExecution(RHIShader shader, RHIResourceBindings? inputs)
    {
        if (executionResources.Count != 0) throw new InvalidOperationException("Resource execution cannot recurse.");
        try
        {
            RetainExecutionResource(shader.Program);
            if (inputs is null)
            {
                if (shader.Interface.PreparedBindings.ActiveEntries.Count != 0)
                    throw new InvalidOperationException("Rendering requires a captured complete resource table.");
                return;
            }
            if (inputs.Shader != (RHIShaderRef)shader)
                throw new InvalidOperationException("Captured resources belong to a different executable generation.");
            ValidateResourceBindings(shader, inputs);
            foreach (var active in shader.Interface.PreparedBindings.ActiveEntries)
            {
                if (inputs.TrySampler(active.Identity, active.Element, out var sampler))
                {
                    RetainExecutionResource(sampler.Texture.Resource);
                    if (!sampler.Sampler.IsEmpty) RetainExecutionResource(sampler.Sampler.RequireResource());
                }
                if (inputs.TryImage(active.Identity, out var image)) RetainExecutionResource(image.View.Texture.Resource);
                if (inputs.TryBuffer(active.Identity, out var buffer) && !buffer.Buffer.IsEmpty)
                    RetainExecutionResource(buffer.Buffer.RequireResource());
            }
        }
        catch { ReleaseExecutionResources(); throw; }
    }
    /// <summary>Releases the same original wrappers after native callbacks and operation cleanup finish.</summary>
    private void ReleaseExecutionResources()
    {
        for (int index = executionResources.Count - 1; index >= 0; index--) executionResources[index].ReleasePassReference();
        executionResources.Clear();
    }
    /// <summary>Applies executed resources, reusing committed uniform ranges only for a rendering command's state restoration.</summary>
    private void PublishResourceBindings(RHIShader shader, RHIResourceBindings inputs, bool reuseCommittedUniforms)
    {
        ApplyExecutable(shader);
        PublishTextureInputs(shader, inputs);
        foreach (var active in shader.Interface.PreparedBindings.ActiveEntries)
        {
            var binding = active.Binding;
            int slot = binding.Contract.Binding.Slot;
            switch (binding.Contract.Kind)
            {
                case ShaderBindingKind.Image:
                    inputs.TryImage(active.Identity, out var image);
                    var view = ResolveImageView(binding, image);
                    OpenGLImageSlotBindings.BindImageTexture(cache, slot, view);
                    break;
                case ShaderBindingKind.UniformBlock:
                    if (inputs.TryUniform(active.Identity, out var uniform)) PublishUniformSnapshot(slot, uniform.Snapshot, reuseCommittedUniforms);
                    else { inputs.TryBuffer(active.Identity, out var buffer); PublishBufferInput(binding.Contract.Kind, slot, buffer); }
                    break;
                case ShaderBindingKind.StorageBlock:
                case ShaderBindingKind.AtomicCounter:
                    inputs.TryBuffer(active.Identity, out var storage);
                    PublishBufferInput(binding.Contract.Kind, slot, storage);
                    break;
            }
        }
        shader.RequireUsable();
    }
    /// <summary>Marks only active captured blocks consumed before dispatch, preserving pending recordings and inactive inputs.</summary>
    private static void MarkBindingExecution(RHIShader shader, RHIResourceBindings? inputs)
    {
        if (inputs is null) return;
        foreach (var active in shader.Interface.PreparedBindings.ActiveEntries)
            if (active.Binding.Contract.Kind == ShaderBindingKind.UniformBlock
                && inputs.TryUniform(active.Identity, out var uniform) && uniform.Snapshot is { } snapshot)
                snapshot.Publication.MarkExecution(snapshot.ContentRevision);
    }

    /// <summary>Retains each original backing wrapper once until native callbacks and publication cleanup return.</summary>
    private void RetainExecutionResource(GpuResource? resource)
    {
        if (resource is null || executionResources.Contains(resource)) return;
        resource.RetainPassReference();
        executionResources.Add(resource);
        if (resource is GpuTextureView { Source: { } source }) RetainExecutionResource(source);
    }
    #endregion
}
