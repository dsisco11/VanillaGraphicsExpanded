using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Independent validity flags for cached RasterizerState values.</summary>
[Flags]
internal enum RasterizerStateKnowledge : uint
{
    /// <summary>No fields are known.</summary>
    None = 0,
    /// <summary>The cached CullEnabled value is known.</summary>
    CullEnabled = 1 << 0,
    /// <summary>The cached ScissorEnabled value is known.</summary>
    ScissorEnabled = 1 << 1,
    /// <summary>The cached LineWidth value is known.</summary>
    LineWidth = 1 << 2,
    /// <summary>The cached PointSize value is known.</summary>
    PointSize = 1 << 3,
    /// <summary>The cached ProvokingVertex value is known.</summary>
    ProvokingVertex = 1 << 4,
    /// <summary>The cached ClipDistances state is known.</summary>
    ClipDistances = 1u << 5,
    /// <summary>The cached ClipControl state is known.</summary>
    ClipControl = 1u << 6,
    /// <summary>The cached PointSpriteOrigin state is known.</summary>
    PointSpriteOrigin = 1u << 7,
    /// <summary>The cached AlphaTest state is known.</summary>
    AlphaTest = 1u << 8,
    /// <summary>The cached PointSmooth state is known.</summary>
    PointSmooth = 1u << 9,
    /// <summary>The cached LineSmooth state is known.</summary>
    LineSmooth = 1u << 10,
    /// <summary>The cached PolygonSmooth state is known.</summary>
    PolygonSmooth = 1u << 11,
    /// <summary>The cached LineStipple state is known.</summary>
    LineStipple = 1u << 12,
    /// <summary>The cached PolygonStipple state is known.</summary>
    PolygonStipple = 1u << 13,
    /// <summary>The cached AlphaFunction state is known.</summary>
    AlphaFunction = 1u << 14,
    /// <summary>The cached LineStippleParameters state is known.</summary>
    LineStippleParameters = 1u << 15,
    /// <summary>The cached PolygonStipplePattern state is known.</summary>
    PolygonStipplePattern = 1u << 16,
    /// <summary>All configurable clipping and compatibility fields.</summary>
    ConfigurableRaster = ClipDistances | ClipControl | PointSpriteOrigin | AlphaTest | PointSmooth | LineSmooth | PolygonSmooth | LineStipple | PolygonStipple | AlphaFunction | LineStippleParameters | PolygonStipplePattern,
    /// <summary>Every field in this category is known.</summary>
    All = ConfigurableRaster | CullEnabled | ScissorEnabled | LineWidth | PointSize | ProvokingVertex
}
