// Shared GL 4.5 demand/addressing and explicit comparison filter. Hosts provide sampleInputs[],
// entries[] and four statically named sampler2DArray bindings. No implicit derivatives or optional features.
#define S(i) sampleInputs[uint(i)]
#define PE(s,i) entries[(s)*64u+uint(i)]
/// Decodes world/projection doubles without optional integer-64 operations.
double shadowNumber(uint i) { return packDouble2x32(uvec2(S(i),S(i+1u))); }
/// Reads a precise world vector before any float depth conversion.
dvec3 shadowVector(uint i) { return dvec3(shadowNumber(i),shadowNumber(i+2u),shadowNumber(i+4u)); }
/// Reconstructs precise world positions from native integer chunk origin and bounded relative doubles.
dvec3 shadowWorld(uint i) { return dvec3(ivec3(S(120),S(121),S(122)))*32.0lf+shadowVector(i); }
/// Resolves cube ties deterministically in X, Y, Z order.
int shadowFace(dvec3 d) {
    dvec3 a=abs(d); return a.x>=a.y && a.x>=a.z ? (d.x>=0.0lf?0:1) : a.y>=a.z ? (d.y>=0.0lf?2:3) : (d.z>=0.0lf?4:5);
}
/// Reconstructs an unbounded face-plane ray so edge taps route to adjacent faces.
dvec3 shadowDirection(int face,dvec2 uv) {
    if(face==0) return dvec3(1,-uv.y,-uv.x); if(face==1) return dvec3(-1,-uv.y,uv.x);
    if(face==2) return dvec3(uv.x,1,uv.y); if(face==3) return dvec3(uv.x,-1,-uv.y);
    if(face==4) return dvec3(uv.x,-uv.y,1); return dvec3(-uv.x,-uv.y,-1);
}
/// Returns face numerator XY and positive major-axis depth.
dvec3 shadowFaceCoordinates(dvec3 d,int face) {
    if(face==0) return dvec3(-d.z,-d.y,d.x); if(face==1) return dvec3(d.z,-d.y,-d.x);
    if(face==2) return dvec3(d.x,d.z,d.y); if(face==3) return dvec3(d.x,-d.z,-d.y);
    if(face==4) return dvec3(d.x,-d.y,d.z); return dvec3(-d.x,-d.y,-d.z);
}
/// Uses one mapping for demand and filtering; world position stays consistent at directional level transitions.
bool shadowTap(ivec2 offset,out int face,out dvec2 texel,out double reference) {
    face=0; texel=dvec2(0); reference=0.0lf;
    dvec3 receiver=shadowWorld(98u),normal=shadowVector(104u);
    double width=shadowNumber(90u),nearDepth=shadowNumber(86u),farDepth=shadowNumber(88u);
    dvec3 shifted=receiver+normal*(shadowNumber(112u)*width);
    dvec3 towardLight=S(64)==0u?-shadowVector(80u):normalize(shadowWorld(92u)-shifted);
    double cosine=clamp(abs(dot(normal,towardLight)),0.0lf,1.0lf);
    double slope=min(4.0lf,sqrt(max(0.0lf,1.0lf-cosine*cosine))/max(0.1lf,cosine));
    double bias=min(4.0lf,shadowNumber(110u)+shadowNumber(114u)*slope)*width;
    dvec2 t;
    if(S(64)==0u) {
        face=int(S(14));
        t=floor(dvec2(dot(shifted,shadowVector(68u)),dot(shifted,shadowVector(74u)))/width)+dvec2(offset);
        reference=(dot(shifted,shadowVector(80u))-nearDepth-bias)/(farDepth-nearDepth);
    } else {
        dvec3 d=shifted-shadowWorld(92u); double radius=length(d); if(radius<=0.0lf) return false;
        int original=shadowFace(d); dvec3 initial=shadowFaceCoordinates(d,original);
        dvec2 pixel=floor((initial.xy/initial.z+1.0lf)*0.5lf*double(S(66)))+dvec2(offset)+0.5lf;
        dvec3 ray=shadowDirection(original,pixel*(2.0lf/double(S(66)))-1.0lf);
        face=shadowFace(ray);
        dvec3 target=shadowFaceCoordinates(normalize(ray)*radius,face);
        t=floor((target.xy/target.z+1.0lf)*0.5lf*double(S(66)));
        t=clamp(t,dvec2(0),dvec2(double(S(66)-1u)));
        reference=farDepth/(farDepth-nearDepth)-farDepth*nearDepth/((farDepth-nearDepth)*target.z);
        reference-=bias*farDepth*nearDepth/((farDepth-nearDepth)*target.z*target.z);
    }
    if(any(isnan(t))||any(isinf(t))||any(lessThan(t,dvec2(-2147483648.0lf*double(S(65)))))||any(greaterThan(t,dvec2(2147483647.0lf*double(S(65)))))
        ||isnan(reference)||isinf(reference)||reference<0.0lf||reference>1.0lf) return false;
    texel=t; return true;
}
/// Checks a bounded receiving enclosure and exact current dependency stamp for every tap.
bool shadowCoverage(uint slot) {
    for(uint i=0u;i<3u;i++) {
        double lo=packDouble2x32(uvec2(PE(slot,28u+2u*i),PE(slot,29u+2u*i)));
        double hi=packDouble2x32(uvec2(PE(slot,34u+2u*i),PE(slot,35u+2u*i)));
        if(shadowNumber(28u+2u*i)<lo || shadowNumber(34u+2u*i)>hi) return false;
    }
    return PE(slot,40)==S(40) && PE(slot,41)==S(41) && PE(slot,42)==S(42)
        && PE(slot,43)==S(43) && PE(slot,44)==S(44) && PE(slot,45)==S(45);
}
/// Looks up full source/projection/face/page identity; probe exhaustion cannot alias a different page.
bool shadowLookup(int face,dvec2 texel,out uint slot) {
    ivec2 page=ivec2(floor(dvec2(texel)/double(S(65)))); uint hash=2166136261u;
    for(uint i=0u;i<17u;i++) { uint v=i==14u?uint(face):i==15u?uint(page.x):i==16u?uint(page.y):S(i); hash=(hash^v)*16777619u; }
    for(uint probe=0u;probe<min(S(117),S(116));probe++) {
        uint s=(hash+probe)%S(116); if(PE(s,20)!=2u) continue;
        bool match=true;
        for(uint i=0u;i<17u;i++) { uint v=i==14u?uint(face):i==15u?uint(page.x):i==16u?uint(page.y):S(i); if(PE(s,i)!=v) match=false; }
        if(match && shadowCoverage(s) && s/S(118)<S(119)) { slot=s; return true; }
    }
    return false;
}
/// Uses static sampler expressions and point fetches, legal under divergent receiver selection on GL 4.5.
float shadowFetch(uint slot,dvec2 texel) {
    ivec2 local=ivec2(mod(dvec2(texel),double(S(65)))); ivec3 at=ivec3(local,int(slot%S(118)));
    uint bank=slot/S(118);
    if(bank==0u) return texelFetch(shadowDepth0,at,0).r;
    if(bank==1u) return texelFetch(shadowDepth1,at,0).r;
    if(bank==2u) return texelFetch(shadowDepth2,at,0).r;
    return texelFetch(shadowDepth3,at,0).r;
}
/// Evaluates all taps; any invalid lookup withdraws the whole query and returns diagnosed illumination.
float shadowPcf(out uint failureMask,out ivec3 addresses[9],out float references[9]) {
    failureMask=0u; float visibility=0.0; int tap=0;
    for(int y=-1;y<=1;y++) for(int x=-1;x<=1;x++) {
        int face=0; dvec2 texel=dvec2(0); double reference=0.0lf; uint slot=0u;
        bool valid=shadowTap(ivec2(x,y),face,texel,reference);
        addresses[tap]=ivec3(face,ivec2(floor(texel/double(S(65))))); references[tap]=float(reference);
        if(!valid || !shadowLookup(face,texel,slot)) failureMask|=1u<<uint(tap);
        else { float depth=shadowFetch(slot,texel); if(isnan(depth)||isinf(depth)) failureMask|=1u<<uint(tap); else visibility+=reference<=double(depth)?1.0:0.0; }
        tap++;
    }
    return failureMask==0u?visibility/9.0:1.0;
}

