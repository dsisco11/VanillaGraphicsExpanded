using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>Immutable rasterization policy with neutral defaults for fullscreen and geometry draws.</summary>
internal sealed record RasterizerDesc
{
    public bool Cull { get; init; }
    public CullFaceMode CullMode { get; init; } = CullFaceMode.Back;
    public FrontFaceDirection FrontFace { get; init; } = FrontFaceDirection.Ccw;
    public PolygonMode PolygonMode { get; init; } = PolygonMode.Fill;
    public bool OffsetFill { get; init; }
    public bool OffsetLine { get; init; }
    public bool OffsetPoint { get; init; }
    public float DepthBiasFactor { get; init; }
    public float DepthBiasUnits { get; init; }
    public bool DepthClamp { get; init; }
    public bool Discard { get; init; }
    public bool Scissor { get; init; }
    public ProvokingVertexMode ProvokingVertex { get; init; } = ProvokingVertexMode.LastVertexConvention;
    public float LineWidth { get; init; } = 1;
    public float PointSize { get; init; } = 1;
    public bool ProgramPointSize { get; init; }

    // Unsupported compatibility settings are fixed policy, not omitted ambient state.
    public bool UserClipDistances => false;
    public bool AlphaTest => false;
    public bool PointSmooth => false;
    public bool LineSmooth => false;
    public bool PolygonSmooth => false;
    public bool LineStipple => false;
    public bool PolygonStipple => false;
    public bool LowerLeftClipOrigin => true;
    public bool NegativeOneToOneClipDepth => true;
    public bool UpperLeftPointSpriteOrigin => true;
}
