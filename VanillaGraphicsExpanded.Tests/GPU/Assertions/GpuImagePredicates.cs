using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Owns predicates observation policy with explicit fixture dependencies.</summary>
internal sealed class GpuImagePredicates : GpuObservationService
{

    #region Internal API
    /// <summary>Counts uploaded integer coordinate records, excluding subject storage.</summary>
    internal long UploadedCoordinateBytes { get; private set; }
    /// <summary>Counts explicitly copied nominated texels when direct sampling cannot preserve stored aspect/encoding.</summary>
    internal long CopiedPointTexels { get; private set; }

    /// <summary>Borrows prepared executables and related observation services from the fixture owner.</summary>
    internal GpuImagePredicates(GpuObservationPrograms programs) : base(programs)
    {
    }

    /// <summary>Checks every independently nominated texel, including explicit array layers, without downloading the enclosing image.</summary>
    internal GpuPredicateResult PredicateAt(GpuTexture image,IReadOnlyList<(int X,int Y,int Layer)> positions,GpuAssertionPredicate body)
    {
        Programs.RequireContext();
        image.RequireOriginalContext();
        ArgumentNullException.ThrowIfNull(positions);
        if (!body.IsDeclared || body.Domain is not ("observation" or "surface"))
            throw new ArgumentException("Select a declared sparse observation domain.", nameof(body));
        if (positions.Count == 0) throw new ArgumentException("A sparse assertion requires an explicit nonempty domain.", nameof(positions));
        int dimension = image.TextureTarget switch
        {
            TextureDimension.TwoArray => 1, TextureDimension.Two => 2, TextureDimension.Three => 3, TextureDimension.Cube => 4,
            _ => throw new NotSupportedException("Point predicates require a 2D, array, 3D or cube source.")
        };
        var encoding = RHICommandContext.Current.GetTextureEncoding(image, PixelComponentType.Float);
        if (encoding.Channels is PixelChannelLayout.RedInteger or PixelChannelLayout.RgInteger or PixelChannelLayout.RgbInteger or PixelChannelLayout.RgbaInteger)
            throw new ArgumentException("Point predicates require floating or normalized source values.", nameof(image));
        int scalars = checked(positions.Count * 3), width = Math.Min(scalars, 4096), height = (scalars + width - 1) / width;
        uint[] coordinates = new uint[checked(width * height)];
        for (int index = 0; index < positions.Count; index++)
        {
            var p = positions[index];
            if ((uint)p.X >= (uint)image.Width || (uint)p.Y >= (uint)image.Height || (uint)p.Layer >= (uint)image.Depth)
                throw new ArgumentOutOfRangeException(nameof(positions));
            coordinates[index * 3] = (uint)p.X;
            coordinates[index * 3 + 1] = (uint)p.Y;
            coordinates[index * 3 + 2] = (uint)p.Layer;
        }
        // Only integer nominated coordinates are uploaded. The completed original source is borrowed
        // synchronously; no enclosing source image or dummy coordinate snapshot is created.
        using var records = CreateArray(TextureFormat.R32ui, width, height, 1);
        records.UploadDataImmediate(coordinates, 0, 0, 0, width, height, 1);
        UploadedCoordinateBytes += checked((long)coordinates.Length * sizeof(uint));
        byte[] inputs = new byte[160];
        GpuAssertionInputs.Integer(inputs, 0, (uint)positions.Count);
        GpuAssertionInputs.Integer(inputs, 4, 1); GpuAssertionInputs.Integer(inputs, 8, 1);
        GpuAssertionInputs.Integer(inputs, 12, 26); GpuAssertionInputs.Integer(inputs, 16, 1);
        GpuAssertionInputs.Integer(inputs, 20, body.Inputs | 2u);
        GpuAssertionInputs.Integer(inputs, 84, 1); GpuAssertionInputs.Integer(inputs, 92, (uint)body.Choice);
        using var interpreted = CreatePointInterpretation(image, positions);
        if (interpreted is not null) dimension = 4;
        var data = Programs.Dispatch.Observe(positions.Count, 1, 1, 0, positions.Count,
            Programs.Get($"predicate-{body.Domain}-points{dimension}"), inputs,
            [("historyImage", interpreted ?? image), ("expectedImage", records)]);
        if (data.Counters[6] != uint.MaxValue)
        {
            var point = positions[checked((int)(data.Counters[6] / 4))];
            data = data with { ExpectedActual = $"nominated source=({point.X},{point.Y},{point.Layer})" };
        }
        return new(data);
    }

    /// <summary>
    /// Evaluates every original texel using GLSL variables a, b, c, d, coordinate, index and multipliers.
    /// The body returns bvec2(contractSatisfied, witnessSelected); witnesses are counted independently
    /// of failures so conditional assertions cannot pass without exercising their selected branch.
    /// Explicit mappedSources permits different source dimensions: b/c/d clamp to their final texel,
    /// while the authored body can select independent coordinates through the named sampler uniforms.
    /// </summary>
    internal GpuPredicateResult Predicate(GpuTexture image, GpuAssertionPredicate body, GpuTexture? second = null,
        GpuTexture? third = null, GpuTexture? fourth = null, Vector4 parameters = default,bool mappedSources=false, Vector4? evaluation=null)
    {
        Programs.RequireContext();
        if (!body.IsDeclared)
            throw new ArgumentOutOfRangeException(nameof(body), "Select a declared independently authored predicate.");
        using var actual = new GpuImageSnapshot(image);
        using var reference = second is null ? null : new GpuImageSnapshot(second);
        using var additional = third is null ? null : new GpuImageSnapshot(third);
        using var last = fourth is null ? null : new GpuImageSnapshot(fourth);
        foreach (var candidate in new[] { actual, reference, additional, last })
        {
            // Different channel counts are intentional for radiance/metadata pairs; dimensions must agree.
            if (candidate is null) continue;
            if (candidate.Kind != 0 || (!mappedSources && (candidate.Texture.Width != actual.Texture.Width ||
                candidate.Texture.Height != actual.Texture.Height || candidate.Texture.Depth != actual.Texture.Depth)))
                throw new ArgumentException("Authored predicates require corresponding floating image domains.");
        }
        var inputs = DomainInputs(actual, 1, 0, double.NegativeInfinity, double.PositiveInfinity);
        GpuAssertionInputs.Integer(inputs, 12, (uint)PredicateOperation.Authored);
        GpuAssertionInputs.Vector(inputs, 64, parameters);
        GpuAssertionInputs.Integer(inputs, 92, (uint)body.Choice);
        GpuAssertionInputs.Integer(inputs, 20, body.Inputs | (evaluation?.W == 1 ? 2u : 0u));
        GpuAssertionInputs.Vector(inputs, 128, evaluation ?? new(1, 1, 1, 0));
        return new(Observe(actual, "predicate-" + body.Domain, inputs, [("actualImage", actual.Texture), ("historyImage", (body.Inputs & 2) != 0 || evaluation?.W == 1 ? reference?.Texture ?? actual.Texture : null), ("additionalImage", (body.Inputs & 8) != 0 ? additional?.Texture ?? actual.Texture : null), ("fourthImage", (body.Inputs & 16) != 0 ? last?.Texture ?? actual.Texture : null)]));
    }

    #endregion

    #region Private
    /// <summary>Copies only nominated tuples when stored sRGB bytes, depth aspect or cube interpretation requires owned selectors.</summary>
    private Texture3D? CreatePointInterpretation(GpuTexture source, IReadOnlyList<(int X, int Y, int Layer)> points)
    {
        var format = source.InternalFormat switch
        {
            TextureFormat.Srgb8 => TextureFormat.Rgb8, TextureFormat.Srgb8Alpha8 => TextureFormat.Rgba8,
            _ => source.InternalFormat
        };
        if (format == source.InternalFormat && source.InternalFormat is not (TextureFormat.Depth24Stencil8 or TextureFormat.Depth32fStencil8)
            && source.TextureTarget != TextureDimension.Cube) return null;
        int width = Math.Min(points.Count, 4096), height = (points.Count + width - 1) / width;
        var copy = CreateArray(format, width, height, 1);
        try
        {
            // CopyImage preserves the stored bits independently of the source's sRGB decode or depth/stencil sampling selector.
            // The copy owns its default depth aspect. Work scales with nominated records, including authored duplicates.
            for (int index = 0; index < points.Count; index++)
            {
                var point = points[index];
                RHICommandContext.Current.CopyTexture(source,
                    source.TextureTarget == TextureDimension.Three
                        ? new(new(0,0),point.X,point.Y,point.Layer,1,1,1)
                        : new(new(0,point.Layer),point.X,point.Y,0,1,1,1),
                    copy,new(new(0,0),index%width,index/width,0,1,1,1));
                CopiedPointTexels++;
            }
            return copy;
        }
        catch { copy.Dispose(); throw; }
    }

    /// <summary>Identifies only the wire operations implemented by this focused observation service.</summary>
    private enum PredicateOperation
    {
        /// <summary>Selects the Authored kernel operation.</summary>
        Authored = 21,
    }
    #endregion
}
