using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Readiness;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;

namespace VanillaGraphicsExpanded.PBR.Shadows;

/// <summary>Freezes current direct light, coordinate origin and exact source-to-readiness indices for every surface.</summary>
internal sealed class SurfaceShadowParameters : CpuUniformBuffer
{
    internal const int PackedSize = SourceIndicesOffset + SourceIndexCount * SourceIndexStride;

    #region Public API
    #region Layout
    internal const int ReadyOffset = 0;
    internal const int LightCountOffset = 4;
    internal const int FrameLowOffset = 8;
    internal const int FrameHighOffset = 12;
    internal const int OriginHighOffset = 16;
    internal const int OriginLowOffset = 32;
    internal const int SunDirectionOffset = 48;
    internal const int SolarOffset = 64;
    internal const int MoonDirectionOffset = 80;
    internal const int LunarOffset = 96;
    internal const int BiasOffset = 112;
    internal const int SourceIndicesOffset = 128;
    internal const int SourceIndexCount = 104;
    internal const int SourceIndexStride = sizeof(uint);
    internal const int CelestialSourceCount = 2;
    #endregion

    /// <summary>Declares the immutable byte extent owned by this layout.</summary>
    public override int SizeBytes => PackedSize;

    /// <summary>Allocates the fixed celestial and 102-source interface without changing illumination-list capacity.</summary>
    internal SurfaceShadowParameters(): base() { }

    /// <summary>Publishes illumination even when shadow admission failed; only known exact source keys map to GPU results.</summary>
    internal void Set(ShadowLightSnapshot? lights,ShadowFramePublication? publication,Atmosphere.AtmosphereLighting? atmosphere,
        ShadowPageSettings? settings=null)
    {
        bool ready=publication is {IsCurrent:true} && ReferenceEquals(publication.Lights,lights);
        WriteUInt32(ReadyOffset,ready?1u:0u);WriteUInt32(LightCountOffset,ready?publication!.LightCount:0u);
        WriteUInt32(FrameLowOffset,(uint)(lights?.Frame??0));WriteUInt32(FrameHighOffset,(uint)((lights?.Frame??0)>>32));
        var origin=new Vector3d((lights?.OriginX??0)*32.0+(lights?.OriginRemainder.X??0),
            (lights?.OriginY??0)*32.0+(lights?.OriginRemainder.Y??0), (lights?.OriginZ??0)*32.0+(lights?.OriginRemainder.Z??0));
        ShadowCoordinates.Split(origin,out var high,out var low);
        WriteVector4(OriginHighOffset,new(high,0));WriteVector4(OriginLowOffset,new(low,0));
        WriteVector4(SunDirectionOffset,new(lights?.Sun.Direction??atmosphere?.Sun??Vector3.UnitY,
            lights?.Sun.AngularRadius??Atmosphere.AtmosphereSolarDisk.AngularRadius));
        WriteVector4(SolarOffset,new(lights?.Sun.Irradiance??atmosphere?.Solar??Vector3.Zero,0));
        WriteVector4(MoonDirectionOffset,new(lights?.Moon.Direction??atmosphere?.Moon??Vector3.UnitY,
            lights?.Moon.AngularRadius??atmosphere?.MoonAngularRadius??.03252f));
        WriteVector4(LunarOffset,new(lights?.Moon.Irradiance??atmosphere?.Lunar??Vector3.Zero,0));
        var policy=settings??new ShadowPageSettings();
        WriteVector4(BiasOffset,new((float)policy.DepthBiasTexels,(float)policy.NormalBiasTexels,(float)policy.SlopeBiasTexels,
            (float)ShadowCoordinates.OriginError(origin)));
        for(int i=0;i<SourceIndexCount;i++)WriteUInt32(SourceIndicesOffset+i*SourceIndexStride,uint.MaxValue);
        if(!ready || lights is null)return;
        var indices=publication!.SourceIndices;
        Map(0,lights.Sun.Source);Map(1,lights.Moon.Source);
        for(int i=0;i<Math.Min(VgeLightsUniformBuffer.Capacity,lights.Points.Length);i++)Map(CelestialSourceCount+i,lights.Points[i].Source);

        /// <summary>Identity is established by collection, never by position or list-order similarity.</summary>
        void Map(int slot,ShadowSourceKey source)
        {if(source.IsKnown && indices.TryGetValue(source,out int index))WriteUInt32(SourceIndicesOffset+slot*SourceIndexStride,(uint)index);}
    }
    #endregion

}
