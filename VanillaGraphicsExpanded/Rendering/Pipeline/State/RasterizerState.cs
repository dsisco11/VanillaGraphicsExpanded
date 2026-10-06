using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.Pipeline.State;
/// <summary>Concrete RasterizerState values independent of cache knowledge and native operations.</summary>
internal struct RasterizerState
{
    /// <summary>Declared CullEnabled value.</summary>
    public bool CullEnabled;
    /// <summary>Declared ScissorEnabled value.</summary>
    public bool ScissorEnabled;
    /// <summary>Declared LineWidth value.</summary>
    public float LineWidth;
    /// <summary>Declared PointSize value.</summary>
    public float PointSize;
    /// <summary>Declared ProvokingVertex value.</summary>
    public ProvokingVertexMode ProvokingVertex;
    /// <summary>Cached native ClipDistances value, meaningful only when its knowledge flag is set.</summary>
    public uint ClipDistances;
    /// <summary>Cached native ClipOrigin value, meaningful only when its knowledge flag is set.</summary>
    public ClipOrigin ClipOrigin;
    /// <summary>Cached native ClipDepth value, meaningful only when its knowledge flag is set.</summary>
    public ClipDepthMode ClipDepth;
    /// <summary>Cached native PointSpriteOrigin value, meaningful only when its knowledge flag is set.</summary>
    public PointSpriteCoordOriginParameter PointSpriteOrigin;
    /// <summary>Cached native AlphaTest value, meaningful only when its knowledge flag is set.</summary>
    public bool AlphaTest;
    /// <summary>Cached native PointSmooth value, meaningful only when its knowledge flag is set.</summary>
    public bool PointSmooth;
    /// <summary>Cached native LineSmooth value, meaningful only when its knowledge flag is set.</summary>
    public bool LineSmooth;
    /// <summary>Cached native PolygonSmooth value, meaningful only when its knowledge flag is set.</summary>
    public bool PolygonSmooth;
    /// <summary>Cached native LineStipple value, meaningful only when its knowledge flag is set.</summary>
    public bool LineStipple;
    /// <summary>Cached native PolygonStipple value, meaningful only when its knowledge flag is set.</summary>
    public bool PolygonStipple;
    /// <summary>Cached native AlphaComparison value, meaningful only when its knowledge flag is set.</summary>
    public AlphaFunction AlphaComparison;
    /// <summary>Cached native AlphaReference value, meaningful only when its knowledge flag is set.</summary>
    public float AlphaReference;
    /// <summary>Cached native LineStippleFactor value, meaningful only when its knowledge flag is set.</summary>
    public int LineStippleFactor;
    /// <summary>Cached native LineStipplePattern value, meaningful only when its knowledge flag is set.</summary>
    public ushort LineStipplePattern;
    /// <summary>Cached native PolygonStipplePattern value, meaningful only when its knowledge flag is set.</summary>
    public Pipeline.Descriptions.PipelineValues<byte>? PolygonStipplePattern;
    /// <summary>Independent knowledge for observed native values.</summary>
    public CompleteRasterKnowledge SupplementalKnown;
    /// <summary>Supplemental native enable values and their validity.</summary>
    public CompleteEnableState SupplementalEnables;
    /// <summary>Declared or observed CullFaceMode CullMode value.</summary>
    public CullFaceMode CullMode;
    /// <summary>Declared or observed FrontFaceDirection FrontFace value.</summary>
    public FrontFaceDirection FrontFace;
    /// <summary>Observed PolygonModes value, valid only when corresponding knowledge is established.</summary>
    public (PolygonMode Front, PolygonMode Back) PolygonModes;
    /// <summary>Observed PolygonOffset value, valid only when corresponding knowledge is established.</summary>
    public (float Factor, float Units) PolygonOffset;
}
