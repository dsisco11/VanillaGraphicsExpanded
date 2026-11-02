using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Shares numeric input packing and synchronous execution mechanics without selecting assertion policy.</summary>
internal abstract class GpuObservationService(GpuObservationPrograms programs)
{
    /// <summary>Borrows executable and context authority from the explicit fixture owner.</summary>
    protected GpuObservationPrograms Programs { get; } = programs;

    #region Protected API
    /// <summary>Packs the shared image extent and numeric contract without narrowing doubles.</summary>
    protected static byte[] DomainInputs(GpuImageSnapshot actual, uint? channelMask, double tolerance, double minimum, double maximum)
    {
        if (!double.IsFinite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        GpuObservationLimits.RequireDomain(actual.Texture.Width, actual.Texture.Height, actual.Texture.Depth);
        uint mask = channelMask ?? (1u << actual.Channels) - 1;
        if (mask == 0 || (mask & ~((1u << actual.Channels) - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(channelMask));
        byte[] bytes = new byte[160];
        GpuAssertionInputs.Integer(bytes, 0, (uint)actual.Texture.Width); GpuAssertionInputs.Integer(bytes, 4, (uint)actual.Texture.Height); GpuAssertionInputs.Integer(bytes, 8, (uint)actual.Texture.Depth);
        GpuAssertionInputs.Integer(bytes, 16, mask); GpuAssertionInputs.Integer(bytes, 24, 1); GpuAssertionInputs.Integer(bytes, 28, 1); GpuAssertionInputs.Integer(bytes, 84, 1);
        GpuAssertionInputs.Double(bytes, 32, tolerance); GpuAssertionInputs.Double(bytes, 40, minimum); GpuAssertionInputs.Double(bytes, 48, maximum);
        GpuAssertionInputs.Vector(bytes, 128, new(1, 1, 1, 0));
        return bytes;
    }

    /// <summary>Executes one prepared whole-domain observation with only its declared input bindings.</summary>
    protected GpuObservationData Observe(GpuImageSnapshot actual, string family, byte[] inputs, (string Name, GpuTexture? Texture)[] sources)
    {
        var program = Programs.Get(family, family.StartsWith("predicate-") ? 0 : actual.Kind);
        return Programs.Dispatch.Observe(actual, program, inputs, sources);
    }

    /// <summary>Rejects mismatched domains before a relational oracle reads corresponding GPU texels.</summary>
    protected static void RequireMatchingFloating(GpuImageSnapshot first, GpuImageSnapshot second)
    {
        if (first.Kind != 0 || second.Kind != 0 || first.Texture.Width != second.Texture.Width ||
            first.Texture.Height != second.Texture.Height || first.Texture.Depth != second.Texture.Depth || first.Channels != second.Channels)
            throw new ArgumentException("Relational observations require matching floating domains and channels.");
    }

    /// <summary>Creates only context-owned fixture storage for expected data.</summary>
    protected static Texture3D CreateArray(TextureFormat format, int width, int height, int depth)
    {
        return RHICommandContext.Current.CreateTexture3D(new RHITextureDescription
        { Dimension = TextureDimension.TwoArray, Format = format, Width = width, Height = height, Depth = depth,
            MipLevels = 1, Storage = ResourceStorageMode.Immutable, Filter = TextureFilterMode.Nearest, Label = "Tests.Assertion.Expected" }).RequireResource();
    }

    #endregion
}
