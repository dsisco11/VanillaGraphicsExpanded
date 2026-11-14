// FP32 translated receivers, integer virtual pages and explicit depth comparisons.
#ifndef S
#define S(i) sampleInputs[uint(i)]
#endif
#ifndef PE
#define PE(s,i) entries[(s)*128u+uint(i)]
#endif
#ifndef VGE_SHADOW_SURFACE_RECEIVER
@import "./coverage.glsl"
#endif
/// Reads a scalar in the frozen rendered projection's sampling frame.
float shadowFrame(uint i) { return uintBitsToFloat(S(160u+i)); }
/// Reads a vector in the translated sampling frame.
vec3 shadowFrameVector(uint i) { return vec3(shadowFrame(i),shadowFrame(i+1u),shadowFrame(i+2u)); }
/// Returns a float sum together with the rounding it would otherwise discard.
vec3 shadowSum(vec3 a,vec3 b,out vec3 error) {
    precise vec3 sum=a+b;
    precise vec3 bVirtual=sum-a;
    precise vec3 aVirtual=sum-bVirtual;
    precise vec3 bError=b-bVirtual;
    precise vec3 aError=a-aVirtual;
    precise vec3 roundoff=aError+bError;
    error=roundoff;
    return sum;
}
/// Cancels origins before demotion, retaining low-part rounding across high-part boundaries.
vec3 shadowOriginOffset(vec3 aHigh,vec3 aLow,vec3 bHigh,vec3 bLow) {
    precise vec3 highError,lowError,sumError;
    precise vec3 high=shadowSum(aHigh,-bHigh,highError);
    precise vec3 low=shadowSum(aLow,-bLow,lowError);
    precise vec3 sum=shadowSum(high,low,sumError);
    precise vec3 residual=(highError+lowError)+sumError;
    precise vec3 offset=sum+residual;
    return offset;
}
/// Returns a bounded receiver without assembling an absolute world position.
vec3 shadowReceiver() {
#ifdef VGE_SHADOW_SURFACE_RECEIVER
    return vgeShadowReceiver;
#else
    return uintBitsToFloat(uvec3(S(98),S(99),S(100)));
#endif
}
/// Returns the owning camera/query origin high part.
vec3 shadowOriginHigh() {
#ifdef VGE_SHADOW_SURFACE_RECEIVER
    return vgeShadowOriginHigh.xyz;
#else
    return uintBitsToFloat(uvec3(S(104),S(105),S(106)));
#endif
}
/// Returns the owning camera/query origin low part.
vec3 shadowOriginLow() {
#ifdef VGE_SHADOW_SURFACE_RECEIVER
    return vgeShadowOriginLow.xyz;
#else
    return uintBitsToFloat(uvec3(S(107),S(108),S(109)));
#endif
}
/// Returns the world-axis receiver normal.
vec3 shadowNormal() {
#ifdef VGE_SHADOW_SURFACE_RECEIVER
    return vgeShadowNormal;
#else
    return uintBitsToFloat(uvec3(S(101),S(102),S(103)));
#endif
}
/// Returns the float texel-bias policy.
vec3 shadowBias() {
#ifdef VGE_SHADOW_SURFACE_RECEIVER
    return vgeShadowBias.xyz;
#else
    return uintBitsToFloat(uvec3(S(110),S(111),S(112)));
#endif
}
/// Converts the receiver directly into the selected rendered light frame.
vec3 shadowTranslatedReceiver() {
    return shadowReceiver()+shadowOriginOffset(shadowOriginHigh(),shadowOriginLow(),shadowFrameVector(0u),shadowFrameVector(3u));
}
/// Preserves deterministic X/Y/Z tie ownership.
int shadowFace(vec3 d) {
    vec3 a=abs(d);int axis=0;if(a.y>a.x)axis=1;if(a.z>a[axis])axis=2;
    return 2*axis+(d[axis]<0.0?1:0);
}
/// Reconstructs an unbounded face ray for seam traversal.
vec3 shadowDirection(int face,vec2 uv) {
    if(face==0)return vec3(1,-uv.y,-uv.x);if(face==1)return vec3(-1,-uv.y,uv.x);
    if(face==2)return vec3(uv.x,1,uv.y);if(face==3)return vec3(uv.x,-1,-uv.y);
    if(face==4)return vec3(uv.x,-uv.y,1);return vec3(-uv.x,-uv.y,-1);
}
/// Returns face numerators and positive major-axis depth.
vec3 shadowFaceCoordinates(vec3 d,int face) {
    if(face==0)return vec3(-d.z,-d.y,d.x);if(face==1)return vec3(d.z,-d.y,-d.x);
    if(face==2)return vec3(d.x,d.z,d.y);if(face==3)return vec3(d.x,-d.z,-d.y);
    if(face==4)return vec3(d.x,-d.y,d.z);return vec3(-d.x,-d.y,-d.z);
}
/// Resolves receiver-dependent texel scale in the rendered light frame.
float shadowWidth(vec3 receiver) { return S(64)==0u?shadowFrame(15u):2.0*length(receiver)/float(S(66)); }
/// Adds local displacement without overflowing signed global page identity.
bool shadowAddPage(int anchor,int delta,out int page) {
    page=0;
    if((delta>0&&anchor>2147483647-delta)||(delta<0&&anchor<(-2147483647-1)-delta))return false;
    page=anchor+delta;return true;
}
/// Produces integer pages and bounded local texels without large float texel addresses.
bool shadowTap(ivec2 offset,out int face,out ivec2 page,out ivec2 local,out float reference) {
    face=0;page=ivec2(0);local=ivec2(0);reference=0;
    if(S(191)==0u)return false;
    vec3 receiver=shadowTranslatedReceiver(),normal=shadowNormal(),biasPolicy=shadowBias();
    float width=shadowWidth(receiver),nearDepth=shadowFrame(16u),farDepth=shadowFrame(17u);
    vec3 shifted=receiver+normal*(biasPolicy.y*width);
    vec3 towardLight=S(64)==0u?-shadowFrameVector(12u):normalize(-shifted);
    float cosine=clamp(abs(dot(normal,towardLight)),0.0,1.0);
    float slope=min(4.0,sqrt(max(0.0,1.0-cosine*cosine))/max(.1,cosine));
    float bias=min(4.0,biasPolicy.x+biasPolicy.z*slope)*width;
    vec2 t;
    if(S(64)==0u) {
        face=int(S(14));
        t=floor(vec2(dot(shifted,shadowFrameVector(6u)),dot(shifted,shadowFrameVector(9u)))/width+
            vec2(shadowFrame(20u),shadowFrame(21u)))+vec2(offset);
        reference=(dot(shifted,shadowFrameVector(12u))-nearDepth-bias)/(farDepth-nearDepth);
    } else {
        float radius=length(shifted);if(radius<=0.0)return false;
        int original=shadowFace(shifted);vec3 initial=shadowFaceCoordinates(shifted,original);
        vec2 pixel=floor((initial.xy/initial.z+1.0)*.5*float(S(66)))+vec2(offset)+.5;
        vec3 ray=shadowDirection(original,pixel*(2.0/float(S(66)))-1.0);
        face=shadowFace(ray);vec3 target=shadowFaceCoordinates(normalize(ray)*radius,face);
        t=clamp(floor((target.xy/target.z+1.0)*.5*float(S(66))),vec2(0),vec2(float(S(66)-1u)));
        reference=farDepth/(farDepth-nearDepth)-farDepth*nearDepth/((farDepth-nearDepth)*target.z);
        reference-=bias*farDepth*nearDepth/((farDepth-nearDepth)*target.z*target.z);
    }
    if(any(isnan(t))||any(isinf(t))||isnan(reference)||isinf(reference)||reference<0.0||reference>1.0)return false;
    vec2 delta=floor(t/float(S(65)));
    if(any(lessThan(delta,vec2(-2147483648.0)))||any(greaterThanEqual(delta,vec2(2147483648.0))))return false;
    ivec2 anchor=S(64)==0u?ivec2(S(178),S(179)):ivec2(0);
    if(!shadowAddPage(anchor.x,int(delta.x),page.x)||!shadowAddPage(anchor.y,int(delta.y),page.y))return false;
    local=ivec2(t-delta*float(S(65)));return true;
}
/// Checks the complete fragment footprint in the page entry's independent coverage frame.
bool shadowFloatCoverage(uint slot) {
    vec3 high=uintBitsToFloat(uvec3(PE(slot,104),PE(slot,105),PE(slot,106)));
    vec3 low=uintBitsToFloat(uvec3(PE(slot,107),PE(slot,108),PE(slot,109)));
    vec3 coverageOffset=shadowOriginOffset(shadowOriginHigh(),shadowOriginLow(),high,low);
    vec3 receiver=shadowReceiver()+coverageOffset;
    float width=shadowWidth(shadowTranslatedReceiver());
    vec3 originOffset=shadowOriginOffset(shadowOriginHigh(),shadowOriginLow(),shadowFrameVector(0u),shadowFrameVector(3u));
    vec3 magnitude=abs(shadowReceiver())+abs(originOffset);
    float rounding=4.76837158203125e-7*max(1.0,max(magnitude.x,max(magnitude.y,magnitude.z)));
    float radius=(3.0*(S(64)==1u?1.0+8.0/float(S(66)):1.0)+shadowBias().y)*width+rounding+shadowFrame(22u);
#ifdef VGE_SHADOW_SURFACE_RECEIVER
    radius+=vgeShadowBias.w;
#else
    radius+=uintBitsToFloat(S(123));
#endif
    vec3 minimum=uintBitsToFloat(uvec3(PE(slot,98),PE(slot,99),PE(slot,100)));
    vec3 maximum=uintBitsToFloat(uvec3(PE(slot,101),PE(slot,102),PE(slot,103)));
    return all(greaterThanEqual(receiver-vec3(radius),minimum))&&all(lessThanEqual(receiver+vec3(radius),maximum));
}
/// Resolves exact source, rendered generation, destination face and integer page.
bool shadowLookup(int face,ivec2 page,out uint slot,out bool stale) {
    slot=0u;stale=false;uint hash=2166136261u;
    for(uint i=0u;i<17u;i++){uint v=i==14u?uint(face):i==15u?uint(page.x):i==16u?uint(page.y):S(i);hash=(hash^v)*16777619u;}
    for(uint probe=0u;probe<min(S(117),S(116));probe++) {
        uint s=(hash+probe)%S(116);if(PE(s,20)!=2u)continue;
        bool match=true;
        for(uint i=0u;i<17u;i++){uint v=i==14u?uint(face):i==15u?uint(page.x):i==16u?uint(page.y):S(i);if(PE(s,i)!=v)match=false;}
        if(!match)continue;
        for(uint i=64u;i<98u;i++){if(S(64)==1u&&(i==90u||i==91u))continue;if(PE(s,i)!=S(i))match=false;}
#ifndef VGE_SHADOW_SURFACE_RECEIVER
        if(shadowCoverageReason(s,stale)!=0u)match=false;
#else
        // The immutable gate interval already certified temporal eligibility for this selected mapping.
        stale=PE(s,110)==1u;
#endif
        ivec3 dimensions=textureSize(shadowDepthPool,0);
        bool address=S(65)>0u&&PE(s,17)<S(119)&&PE(s,17)<uint(dimensions.z)&&
            all(lessThan(uvec2(PE(s,18),PE(s,19)),uvec2(dimensions.xy)/S(65)));
        if(match&&address&&shadowFloatCoverage(s)){slot=s;return true;}
    }
    return false;
}
/// Evaluates nine taps, withdrawing the whole query on any invalid lookup.
float shadowPcf(out uint failureMask,out uint staleMask,out ivec3 addresses[9],out float references[9]) {
    failureMask=0u;staleMask=0u;float visibility=0.0;int tap=0;
    for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++) {
        int face;ivec2 page,local;float reference;uint slot;bool stale=false;
        bool valid=shadowTap(ivec2(x,y),face,page,local,reference);
        addresses[tap]=ivec3(face,page);references[tap]=reference;
        if(!valid||!shadowLookup(face,page,slot,stale))failureMask|=1u<<uint(tap);
        else {
            ivec2 at=ivec2(PE(slot,18),PE(slot,19))*int(S(65))+local;
            float depth=uintBitsToFloat(texelFetch(shadowDepthPool,ivec3(at,int(PE(slot,17))),0).r);
            if(isnan(depth)||isinf(depth)||depth<0.0||depth>1.0)failureMask|=1u<<uint(tap);
            else visibility+=reference<=depth?1.0:0.0;
        }
        if(stale)staleMask|=1u<<uint(tap);tap++;
    }
    return failureMask==0u?visibility/9.0:1.0;
}
