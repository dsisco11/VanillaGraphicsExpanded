using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Resolves external authored assignments against one canonical interface without per-field readback.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public GpuProgramMetadata ApplyProgramBindings(int program, IReadOnlyList<ProgramBindingAssignment> assignments, Action<string>? warn)
    {
        RequireContext();
        var snapshot = AcquireProgramInterface(program);
        if (!snapshot.ExecutableAvailable || snapshot.Resources.IsDefault || snapshot.TextureUnits.IsDefault)
            throw new InvalidOperationException("A complete executable snapshot is required for assignments.");
        var manager = RHIResourceManagerSystem.CurrentManager;
        var original = manager?.FindObservedResource(GpuResourceKind.Program, program);
        ulong expectedRevision = original?.MetadataRevision ?? 0;
        // Native callbacks may retire or replace an association. Never let subsequent edits target a reused name.
        void RequireOriginal()
        {
            RequireContext();
            if (original is not null && (!original.IsUsable || original.MetadataRevision != expectedRevision || !ReferenceEquals(original,
                manager!.FindObservedResource(GpuResourceKind.Program, program))))
                throw new InvalidOperationException("The original executable association changed during assignment.");
        }
        var resources = snapshot.Resources.ToBuilder();
        var units = snapshot.TextureUnits.ToBuilder();
        foreach (var assignment in assignments)
        {
            RequireOriginal();
            // Uniform array elements retain compiler locations while authored contracts choose unit values.
            string name = assignment.Name;
            int element = 0;
            int bracket = name.LastIndexOf('[');
            if (assignment.Kind == ProgramResourceKind.Uniform && bracket > 0 && name.EndsWith(']')
                && int.TryParse(name[(bracket + 1)..^1], out int index))
            { element = index; name = name[..bracket]; }
            int resourceIndex = -1;
            for (int i = 0; i < resources.Count; i++)
            {
                var item = resources[i];
                string? baseName = item.Name?.EndsWith("[0]", StringComparison.Ordinal) == true ? item.Name[..^3] : item.Name;
                if (item.Interface == assignment.Kind && baseName == name) { resourceIndex = i; break; }
            }
            if (resourceIndex < 0 || element < 0)
            {
                if (assignment.Required) warn?.Invoke($"Program did not expose required {assignment.Kind} '{assignment.Name}'.");
                continue;
            }
            var resource = resources[resourceIndex];
            if (assignment.Kind == ProgramResourceKind.Uniform)
            {
                if (resource.Location is not { } location || location < 0 || element >= resource.ArraySize
                    || resource.Type is not { } type || !GpuProgramLayout.IsSamplerType(type) && !GpuProgramLayout.IsImageType(type))
                    throw new InvalidOperationException("The assignment does not identify an active texture uniform.");
                location += element;
                int unitIndex = -1;
                for (int i = 0; i < units.Count; i++) if (units[i].Location == location) { unitIndex = i; break; }
                if (unitIndex < 0) throw new InvalidOperationException("The unit snapshot is incomplete.");
                int limit = GpuProgramLayout.IsImageType(type) ? Capabilities.MaxImageUnits : Capabilities.MaxCombinedTextureImageUnits;
                if ((uint)assignment.Slot >= (uint)limit) throw new ArgumentOutOfRangeException(nameof(assignments));
                if (units[unitIndex].Unit == assignment.Slot) continue;
                OpenGLProgramAssignments.SetUnit(program, location, assignment.Slot);
                if (original is not null) expectedRevision++;
                RequireOriginal();
                units[unitIndex] = units[unitIndex] with { Unit = assignment.Slot };
            }
            else
            {
                if (resource.BufferBinding is not { } current) throw new InvalidOperationException("Block binding is unavailable.");
                if (current == assignment.Slot) continue;
                if (assignment.Kind == ProgramResourceKind.UniformBlock)
                    OpenGLProgramAssignments.UniformBlockBinding(program, resource.Index, assignment.Slot);
                else if (assignment.Kind == ProgramResourceKind.ShaderStorageBlock)
                    OpenGLProgramAssignments.ShaderStorageBlockBinding(program, resource.Index, assignment.Slot);
                else throw new ArgumentException("Unsupported assignment category.", nameof(assignments));
                if (original is not null) expectedRevision++;
                RequireOriginal();
                resources[resourceIndex] = resource with { BufferBinding = assignment.Slot };
            }
        }
        RequireOriginal();
        return snapshot with { Resources = resources.ToImmutable(), TextureUnits = units.ToImmutable() };
    }
    #endregion
}
