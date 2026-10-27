using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>Immutable rasterization policy with neutral defaults for fullscreen and geometry draws.</summary>
public sealed record RasterizerDesc
{
    public bool Cull { get; init; }
    public RasterizerCullMode CullMode { get; init; } = RasterizerCullMode.Back;
    public FrontFaceWinding FrontFace { get; init; } = FrontFaceWinding.CounterClockwise;
    public RasterizerFillMode PolygonMode { get; init; } = RasterizerFillMode.Fill;
    public bool OffsetFill { get; init; }
    public bool OffsetLine { get; init; }
    public bool OffsetPoint { get; init; }
    public float DepthBiasFactor { get; init; }
    public float DepthBiasUnits { get; init; }
    public bool DepthClamp { get; init; }
    public bool Discard { get; init; }
    public bool Scissor { get; init; }
    public ProvokingVertexConvention ProvokingVertex { get; init; } = ProvokingVertexConvention.Last;
    public float LineWidth { get; init; } = 1;
    public float PointSize { get; init; } = 1;
    public bool ProgramPointSize { get; init; }

    public ClipCoordinateOrigin ClipOrigin { get; init; } = ClipCoordinateOrigin.LowerLeft;
    public ClipDepthRange ClipDepth { get; init; } = ClipDepthRange.NegativeOneToOne;
    public PointSpriteOrigin PointSpriteOrigin { get; init; } = PointSpriteOrigin.UpperLeft;
    public bool LineSmooth { get; init; }
    public bool PolygonSmooth { get; init; }
}
