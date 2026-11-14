using System;
using System.Collections.Immutable;
using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Shadows;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Shadows.Demand;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Encloses every clipped surface layer before drawing, including the separately projected first-person hands.</summary>
internal sealed class NativeShadowSurfaceReceivers : IShadowSurfaceReceiverSource
{
    private static readonly AccessTools.FieldRef<ModSystemFpHands,IShaderProgram> ItemProgram=
        AccessTools.FieldRefAccess<ModSystemFpHands,IShaderProgram>("fpModeItemShader");
    private static readonly AccessTools.FieldRef<ModSystemFpHands,IShaderProgram> HandProgram=
        AccessTools.FieldRefAccess<ModSystemFpHands,IShaderProgram>("fpModeHandShader");
    private static readonly AccessTools.FieldRef<ClientMain,ClientPlatformAbstract> Platform=
        AccessTools.FieldRefAccess<ClientMain,ClientPlatformAbstract>("Platform");
    private readonly ICoreClientAPI api;

    #region Public API
    /// <summary>Uses the native camera and existing shader owners without enumerating or classifying mesh pools.</summary>
    internal NativeShadowSurfaceReceivers(ICoreClientAPI api)
    {
        this.api=api;
        // Native standard callers use zero or negative extraZOffset. They can
        // narrow clipping, while fragment depthOffset leaves primitive clipping intact.
        if(typeof(ClientMain).Assembly.ManifestModule.ModuleVersionId!=new Guid("8a3ad3aa-2940-4c39-ae1b-881de9e65346") ||
            typeof(ModSystemFpHands).Assembly.ManifestModule.ModuleVersionId!=new Guid("0d8db7e9-fd74-4129-bbe8-1ff82baf23fc"))
            throw new NotSupportedException("Surface receiving bounds require the qualified native projection modules.");
    }

    /// <summary>Charges conservative early bounds for all layers; transparent surfaces never write a demand depth buffer.</summary>
    public ShadowSurfacePublication Capture(ShadowLightSnapshot lights,ShadowFrameBudget budget)
    {
        // Account both inverse matrix pairs, copied matrix inputs, projection
        // arrays, receiving records and immutable program references before any
        // allocation. The fixed reserve includes managed array/object headers.
        if(!budget.TryCharge(new(Inputs:32,Bytes:4096)))throw new InvalidOperationException("Early surface receiving bounds exceed the frame allowance.");
        if(lights.WorldToView.Length!=16)throw new InvalidOperationException("Current source coordinate convention is unavailable.");
        var frame=VgeFrameRenderer.Current;
        var projection=new double[16];for(int i=0;i<16;i++)projection[i]=frame.Projection[i];
        var view=lights.WorldToView.AsSpan();
        // The owned liquid vertex adds .0008/max(.1,clipZ) to clip W.
        // Enclose its positive maximum before inversion; native standard's
        // zero/negative offset alone does not describe all surface clipping.
        var main=Enclose(projection,view,lights,.0008/.1);
        float near=frame.ClipPlanes.X,far=frame.ClipPlanes.Y;
        if(api.World is not ClientMain game)throw new InvalidOperationException("Native receiving world is unavailable.");
        var window=Platform(game).WindowSize;
        int width=window.Width,height=window.Height;
        float fov=api.Settings.Int["fpHandsFoV"]*(float)(Math.PI/180);
        if(width<=0 || height<=0 || !float.IsFinite(fov) || fov<=0 || fov>=Math.PI ||
            !float.IsFinite(near) || !float.IsFinite(far) || near<=0 || far<=near)
            throw new InvalidOperationException("Current first-person receiving projection is unavailable.");
        // ClientMain.Set3DProjection uses the native window aspect and camera
        // near/far planes with this independent hand FOV. Its receiver position
        // remains unmodified by the fragment's visibility-depth offset.
        var handProjection=Mat4d.Perspective(new double[16],fov,width/(double)height,near,far);
        var hand=Enclose(handProjection,view,lights);
        var programs=ImmutableArray.CreateBuilder<ShaderProgramBase>(8);
        Add(ShaderPrograms.Chunkopaque,false);Add(ShaderPrograms.Chunktopsoil,false);
        Add(ShaderPrograms.Standard,true);Add(ShaderPrograms.Entityanimated,true);
        Add(ShaderPrograms.Instanced,true);Add(ShaderPrograms.Chunktransparent,true);
        var firstPerson=api.ModLoader.GetModSystem<ModSystemFpHands>();
        if(firstPerson is null)throw new InvalidOperationException("The native first-person surface owner is unavailable.");
        Add(ItemProgram(firstPerson),true);Add(HandProgram(firstPerson),true);
        return new(lights.Frame,lights.World,lights.Dimension,ShadowSurfaceCategory.All,
            [new(ShadowSurfaceCategory.All,main),new(ShadowSurfaceCategory.All,hand)],programs.ToImmutable());

        /// <summary>Requires each actual executable, including separately registered first-person instances.</summary>
        void Add(IShaderProgram? program,bool forward)
        {
            if(program is not ShaderProgramBase native || !ShaderCapabilities.Has(native,ShaderCapability.SharedVertexDeformation) ||
                (forward?!SurfaceShadowPrograms.Has(native):!ShaderCapabilities.Has(native,ShaderCapability.SceneMaterialCapture)))
                throw new InvalidOperationException("A participating native surface interface is unavailable.");
            if(!programs.Contains(native))programs.Add(native);
        }
    }

    /// <summary>Bounds both clip-plane corners and the eye in the exact current-light coordinate convention.</summary>
    internal static ShadowBounds Enclose(ReadOnlySpan<double> projection,ReadOnlySpan<double> view,ShadowLightSnapshot lights,
        double positiveClipWAllowance=0)
    {
        if(projection.Length!=16 || view.Length!=16)throw new ArgumentException("Surface projections require complete matrices.");
        if(!double.IsFinite(positiveClipWAllowance) || positiveClipWAllowance<0)
            throw new ArgumentOutOfRangeException(nameof(positiveClipWAllowance));
        foreach(double value in projection)if(!double.IsFinite(value))throw new InvalidOperationException("Surface projection is not finite.");
        foreach(double value in view)if(!double.IsFinite(value))throw new InvalidOperationException("Surface view is not finite.");
        if(projection[0]<=0 || projection[5]<=0 || projection[11]!=-1 || projection[15]!=0 ||
            view[3]!=0 || view[7]!=0 || view[11]!=0 || view[15]!=1)
            throw new InvalidOperationException("Surface bounds require the native perspective and affine view convention.");
        // The native view is a rigid camera transform. Arbitrary scale/shear
        // needs a different numerical bound and cannot inherit this certificate.
        for(int column=0;column<3;column++)for(int other=column;other<3;other++)
        {
            double dot=0;for(int row=0;row<3;row++)dot+=view[column*4+row]*view[other*4+row];
            if(Math.Abs(dot-(column==other?1:0))>1e-4)
                throw new InvalidOperationException("Surface bounds require a rigid native view transform.");
        }
        var enclosingProjection=projection.ToArray();
        // Replacing W by W+maximumOffset relaxes every homogeneous clip plane,
        // enclosing all smaller positive offsets without an unbounded NDC pad.
        enclosingProjection[15]+=positiveClipWAllowance;
        var inverseProjection=Mat4d.Invert(new double[16],enclosingProjection);
        var inverseView=Mat4d.Invert(new double[16],view.ToArray());
        if(inverseProjection is null || inverseView is null)throw new InvalidOperationException("Surface receiving projection is singular.");
        double ox=lights.OriginX*32.0+lights.OriginRemainder.X,oy=lights.OriginY*32.0+lights.OriginRemainder.Y,
            oz=lights.OriginZ*32.0+lights.OriginRemainder.Z;
        double eyeX=ox+inverseView[12],eyeY=oy+inverseView[13],eyeZ=oz+inverseView[14];
        double minX=eyeX,minY=eyeY,minZ=eyeZ,maxX=eyeX,maxY=eyeY,maxZ=eyeZ;
        // Two four-term float transforms and interpolation can perturb the
        // homogeneous clipping inequalities before division. Expand their NDC
        // enclosure before inversion; a fixed world-space epsilon would miss
        // the amplification near the perspective far-plane asymptote. The
        // angular factor bounds cancellation at the widest admitted ray.
        const double floatUnitRoundoff=1.0/16777216;
        double angularExtent=Math.Max(1,Math.Max((1+Math.Abs(projection[8]))/projection[0],
            (1+Math.Abs(projection[9]))/projection[5]));
        double clipExtent=1+128*floatUnitRoundoff*angularExtent;
        for(int corner=0;corner<8;corner++)
        {
            double x=(corner&1)==0?-clipExtent:clipExtent,y=(corner&2)==0?-clipExtent:clipExtent,
                z=(corner&4)==0?-clipExtent:clipExtent;
            double w=inverseProjection[3]*x+inverseProjection[7]*y+inverseProjection[11]*z+inverseProjection[15];
            // W is affine over the clip box, so checking all corners also
            // rejects an interior zero crossing and an unbounded far extent.
            if(!double.IsFinite(w) || w<=1e-15)throw new InvalidOperationException("Surface receiving extent is unbounded or ill-conditioned.");
            double vx=(inverseProjection[0]*x+inverseProjection[4]*y+inverseProjection[8]*z+inverseProjection[12])/w;
            double vy=(inverseProjection[1]*x+inverseProjection[5]*y+inverseProjection[9]*z+inverseProjection[13])/w;
            double vz=(inverseProjection[2]*x+inverseProjection[6]*y+inverseProjection[10]*z+inverseProjection[14])/w;
            double px=ox+inverseView[0]*vx+inverseView[4]*vy+inverseView[8]*vz+inverseView[12];
            double py=oy+inverseView[1]*vx+inverseView[5]*vy+inverseView[9]*vz+inverseView[13];
            double pz=oz+inverseView[2]*vx+inverseView[6]*vy+inverseView[10]*vz+inverseView[14];
            minX=Math.Min(minX,px);minY=Math.Min(minY,py);minZ=Math.Min(minZ,pz);
            maxX=Math.Max(maxX,px);maxY=Math.Max(maxY,py);maxZ=Math.Max(maxZ,pz);
        }
        // Matrix publication is float precision while source/world positions are
        // doubles. Expand outwards for interpolation and inversion roundoff.
        double padding=Math.Max(1e-4,Math.Max(maxX-minX,Math.Max(maxY-minY,maxZ-minZ))*1e-5);
        var bounds=new ShadowBounds(Math.BitDecrement(minX-padding),Math.BitDecrement(minY-padding),Math.BitDecrement(minZ-padding),
            Math.BitIncrement(maxX+padding),Math.BitIncrement(maxY+padding),Math.BitIncrement(maxZ+padding));
        if(!bounds.IsValid)throw new InvalidOperationException("Surface receiving bounds are not finite.");
        return bounds;
    }
    #endregion
}
