using System;
using System.Collections.Immutable;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Identifies resolved original attachments and routing, excluding executed clear/load/store intentions.</summary>
internal sealed class OpenGLPassTargetKey : IEquatable<OpenGLPassTargetKey>
{
    internal readonly ImmutableArray<(int Slot, OpenGLAttachmentIdentity Image)> Colors;
    internal readonly OpenGLAttachmentIdentity? DepthStencil;
    internal readonly ImmutableArray<int> Outputs;
    private readonly int hash;

    #region Internal API
    /// <summary>Reports original dependency usability without retaining a second canonical metadata snapshot.</summary>
    internal bool IsCurrent
    {
        get
        {
            foreach (var color in Colors) if (!color.Image.IsCurrent) return false;
            return DepthStencil is not { } depth || depth.IsCurrent;
        }
    }
    /// <summary>Copies execution-resolved configuration after published resource validation.</summary>
    internal OpenGLPassTargetKey(ImmutableArray<(int Slot, OpenGLAttachmentIdentity Image)> colors,
        OpenGLAttachmentIdentity? depthStencil, ImmutableArray<int> outputs)
    {
        Colors = colors; DepthStencil = depthStencil; Outputs = outputs;
        var digest = new HashCode();
        foreach (var image in colors) digest.Add(image);
        digest.Add(depthStencil);
        foreach (int output in outputs) digest.Add(output);
        hash = digest.ToHashCode();
    }
    /// <summary>Checks a repeatedly executed frozen description without allocating another key or reading native state.</summary>
    internal bool Matches(RHIRenderPassDescription description)
    {
        int imageIndex = 0;
        for (int output = 0; output < description.Colors.Length; output++)
            if (description.Colors[output].Image is { } image)
            {
                if (imageIndex >= Colors.Length || Colors[imageIndex++].Image != OpenGLAttachmentIdentity.Resolve(image)) return false;
            }
        return imageIndex == Colors.Length && (description.DepthStencil is { } depth
            ? DepthStencil == OpenGLAttachmentIdentity.Resolve(depth) : !DepthStencil.HasValue);
    }
    #endregion

    #region Public API
    /// <summary>Compares every original association and route; neither native-name nor descriptor-object equality is sufficient.</summary>
    public bool Equals(OpenGLPassTargetKey? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null || hash != other.hash || DepthStencil != other.DepthStencil
            || Colors.Length != other.Colors.Length || Outputs.Length != other.Outputs.Length) return false;
        for (int i = 0; i < Colors.Length; i++) if (Colors[i] != other.Colors[i]) return false;
        for (int i = 0; i < Outputs.Length; i++) if (Outputs[i] != other.Outputs[i]) return false;
        return true;
    }
    /// <inheritdoc />
    public override bool Equals(object? value) => value is OpenGLPassTargetKey key && Equals(key);
    /// <inheritdoc />
    public override int GetHashCode() => hash;
    #endregion
}
