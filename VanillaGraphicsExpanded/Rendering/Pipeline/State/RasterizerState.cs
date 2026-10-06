using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.Pipeline.State;
/// <summary>Concrete RasterizerState values independent of cache knowledge and native operations.</summary>
internal struct RasterizerState
{
    // Only boolean value bits are stored here; cache validity remains a separate category mask.
    private RasterizerStateKnowledge booleanValues;

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
    /// <summary>Declared or observed CullFaceMode CullMode value.</summary>
    public CullFaceMode CullMode;
    /// <summary>Declared or observed FrontFaceDirection FrontFace value.</summary>
    public FrontFaceDirection FrontFace;
    /// <summary>Observed PolygonModes value, valid only when corresponding knowledge is established.</summary>
    public (PolygonMode Front, PolygonMode Back) PolygonModes;
    /// <summary>Observed PolygonOffset value, valid only when corresponding knowledge is established.</summary>
    public (float Factor, float Units) PolygonOffset;
    #region Public API
    #region Boolean values
    /// <summary>Declared CullEnabled value.</summary>
    public bool CullEnabled
    {
        readonly get => booleanValues.HasFlag(RasterizerStateKnowledge.CullEnabled);
        set
        {
            if (value) booleanValues |= RasterizerStateKnowledge.CullEnabled;
            else booleanValues &= ~RasterizerStateKnowledge.CullEnabled;
        }
    }

    /// <summary>Declared ScissorEnabled value.</summary>
    public bool ScissorEnabled
    {
        readonly get => booleanValues.HasFlag(RasterizerStateKnowledge.ScissorEnabled);
        set
        {
            if (value) booleanValues |= RasterizerStateKnowledge.ScissorEnabled;
            else booleanValues &= ~RasterizerStateKnowledge.ScissorEnabled;
        }
    }

    /// <summary>Cached native AlphaTest value, meaningful only when its knowledge flag is set.</summary>
    public bool AlphaTest
    {
        readonly get => booleanValues.HasFlag(RasterizerStateKnowledge.AlphaTest);
        set
        {
            if (value) booleanValues |= RasterizerStateKnowledge.AlphaTest;
            else booleanValues &= ~RasterizerStateKnowledge.AlphaTest;
        }
    }

    /// <summary>Cached native PointSmooth value, meaningful only when its knowledge flag is set.</summary>
    public bool PointSmooth
    {
        readonly get => booleanValues.HasFlag(RasterizerStateKnowledge.PointSmooth);
        set
        {
            if (value) booleanValues |= RasterizerStateKnowledge.PointSmooth;
            else booleanValues &= ~RasterizerStateKnowledge.PointSmooth;
        }
    }

    /// <summary>Cached native LineSmooth value, meaningful only when its knowledge flag is set.</summary>
    public bool LineSmooth
    {
        readonly get => booleanValues.HasFlag(RasterizerStateKnowledge.LineSmooth);
        set
        {
            if (value) booleanValues |= RasterizerStateKnowledge.LineSmooth;
            else booleanValues &= ~RasterizerStateKnowledge.LineSmooth;
        }
    }

    /// <summary>Cached native PolygonSmooth value, meaningful only when its knowledge flag is set.</summary>
    public bool PolygonSmooth
    {
        readonly get => booleanValues.HasFlag(RasterizerStateKnowledge.PolygonSmooth);
        set
        {
            if (value) booleanValues |= RasterizerStateKnowledge.PolygonSmooth;
            else booleanValues &= ~RasterizerStateKnowledge.PolygonSmooth;
        }
    }

    /// <summary>Cached native LineStipple value, meaningful only when its knowledge flag is set.</summary>
    public bool LineStipple
    {
        readonly get => booleanValues.HasFlag(RasterizerStateKnowledge.LineStipple);
        set
        {
            if (value) booleanValues |= RasterizerStateKnowledge.LineStipple;
            else booleanValues &= ~RasterizerStateKnowledge.LineStipple;
        }
    }

    /// <summary>Cached native PolygonStipple value, meaningful only when its knowledge flag is set.</summary>
    public bool PolygonStipple
    {
        readonly get => booleanValues.HasFlag(RasterizerStateKnowledge.PolygonStipple);
        set
        {
            if (value) booleanValues |= RasterizerStateKnowledge.PolygonStipple;
            else booleanValues &= ~RasterizerStateKnowledge.PolygonStipple;
        }
    }

    /// <summary>Cached native DepthClamp enable value.</summary>
    public bool DepthClamp
    {
        readonly get => booleanValues.HasFlag(RasterizerStateKnowledge.DepthClamp);
        set
        {
            if (value) booleanValues |= RasterizerStateKnowledge.DepthClamp;
            else booleanValues &= ~RasterizerStateKnowledge.DepthClamp;
        }
    }

    /// <summary>Cached native RasterizerDiscard enable value.</summary>
    public bool RasterizerDiscard
    {
        readonly get => booleanValues.HasFlag(RasterizerStateKnowledge.RasterizerDiscard);
        set
        {
            if (value) booleanValues |= RasterizerStateKnowledge.RasterizerDiscard;
            else booleanValues &= ~RasterizerStateKnowledge.RasterizerDiscard;
        }
    }

    /// <summary>Cached native PolygonOffsetFill enable value.</summary>
    public bool PolygonOffsetFill
    {
        readonly get => booleanValues.HasFlag(RasterizerStateKnowledge.PolygonOffsetFill);
        set
        {
            if (value) booleanValues |= RasterizerStateKnowledge.PolygonOffsetFill;
            else booleanValues &= ~RasterizerStateKnowledge.PolygonOffsetFill;
        }
    }

    /// <summary>Cached native PolygonOffsetLine enable value.</summary>
    public bool PolygonOffsetLine
    {
        readonly get => booleanValues.HasFlag(RasterizerStateKnowledge.PolygonOffsetLine);
        set
        {
            if (value) booleanValues |= RasterizerStateKnowledge.PolygonOffsetLine;
            else booleanValues &= ~RasterizerStateKnowledge.PolygonOffsetLine;
        }
    }

    /// <summary>Cached native PolygonOffsetPoint enable value.</summary>
    public bool PolygonOffsetPoint
    {
        readonly get => booleanValues.HasFlag(RasterizerStateKnowledge.PolygonOffsetPoint);
        set
        {
            if (value) booleanValues |= RasterizerStateKnowledge.PolygonOffsetPoint;
            else booleanValues &= ~RasterizerStateKnowledge.PolygonOffsetPoint;
        }
    }

    /// <summary>Cached native ProgramPointSize enable value.</summary>
    public bool ProgramPointSize
    {
        readonly get => booleanValues.HasFlag(RasterizerStateKnowledge.ProgramPointSize);
        set
        {
            if (value) booleanValues |= RasterizerStateKnowledge.ProgramPointSize;
            else booleanValues &= ~RasterizerStateKnowledge.ProgramPointSize;
        }
    }
    #endregion
    #endregion
}
