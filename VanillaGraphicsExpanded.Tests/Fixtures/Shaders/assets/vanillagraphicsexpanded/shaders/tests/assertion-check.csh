#version 450 core
layout(std140,binding=28) uniform AssertionInputs { ivec4 domain; uvec4 selectors; dvec4 limits; vec4 multipliers; ivec4 controls; uvec4 channelMinimum; uvec4 channelMaximum; vec4 evaluation; };
#define extent domain.xyz
#define mode domain.w
#define channels selectors.x
#define constantValue selectors.y
#define rankSide int(selectors.z)
#define rankSlices int(selectors.w)
#define tolerance limits.x
#define bounds limits.yz
#define referenceChannel controls.x
#define pixelStride controls.y
#define pixelOffset controls.z
#define predicateChoice controls.w
#define parameters multipliers
layout(local_size_x=64) in;
#if KIND == 0
layout(binding=0) uniform sampler2DArray actualImage;
layout(binding=1) uniform sampler2DArray historyImage;
layout(binding=3) uniform sampler2DArray additionalImage;
layout(binding=4) uniform sampler2DArray fourthImage;
#elif KIND == 1
layout(binding=0) uniform isampler2DArray actualImage;
layout(binding=1) uniform isampler2DArray historyImage;
#else
layout(binding=0) uniform usampler2DArray actualImage;
layout(binding=1) uniform usampler2DArray historyImage;
#endif
layout(binding=2) uniform usampler2DArray expectedImage;
layout(r32ui,binding=0) uniform uimage2D resultImage;
layout(r32ui,binding=1) uniform uimage2D histogram;
void statistics();

/// Decodes finite IEEE float values exactly in double precision, including subnormals.
double number(uint bits) {
#if KIND == 1
    return double(int(bits));
#elif KIND == 2
    return double(bits);
#else
    uint exponent=(bits>>23)&255u, fraction=bits&0x7fffffu;
    double value=exponent==0u ? ldexp(double(fraction),-149)
        : ldexp(double(fraction|0x800000u),int(exponent)-150);
    return (bits&0x80000000u)!=0u ? -value : value;
#endif
}
/// Classifies nonfinite IEEE values without arithmetic or integer coercion.
bool finiteBits(uint bits) {
#if KIND == 0
    return (bits&0x7f800000u)!=0x7f800000u;
#else
    return true;
#endif
}
/// Compares represented values with explicit nonfinite, signed-zero and tolerance semantics.
bool equalBits(uint a,uint b) {
    // Integer masks preserve explicit Boolean combination without nested early-return merges.
    uint exact=uint(a==b);
    uint numeric=uint(tolerance>0.0)&uint(abs(number(a)-number(b))<=tolerance);
#if KIND == 0
    exact|=uint(((a|b)&0x7fffffffu)==0u);
    exact|=uint((a&0x7fffffffu)>0x7f800000u)&uint((b&0x7fffffffu)>0x7f800000u);
    numeric&=uint(finiteBits(a))&uint(finiteBits(b));
#endif
    return (exact|numeric)!=0u;
}
/// Orders finite bit patterns for atomic extrema while preserving integer precision.
uint ordered(uint bits) {
#if KIND == 0
    return (bits&0x80000000u)!=0u ? ~bits : bits^0x80000000u;
#elif KIND == 1
    return bits^0x80000000u;
#else
    return bits;
#endif
}
/// Reads original components without changing the represented numeric kind.
uvec4 fetchBits(ivec3 p,bool history) {
#if KIND == 0
    return floatBitsToUint(history?texelFetch(historyImage,p,0):texelFetch(actualImage,p,0));
#else
    return uvec4(history?texelFetch(historyImage,p,0):texelFetch(actualImage,p,0));
#endif
}
/// Accumulates a predicate failure and the earliest actual source component.
void fail(uint index,int counter) {
    imageAtomicAdd(resultImage,ivec2(counter,0),1u);
    imageAtomicMin(resultImage,ivec2(6,0),index);
}
#if KIND == 0 && AUTHORED_PROGRAM
/// Preserves .NET numeric equality for selected lanes, including subnormal metadata, signed zero and NaN.
bool same(vec4 a,vec4 b,uint mask) {
    uvec4 first=floatBitsToUint(a),second=floatBitsToUint(b);
    for(int channel=0;channel<4;channel++)if((mask&(1u<<channel))!=0u && !equalBits(first[channel],second[channel]))return false;
    return true;
}
/// Evaluates the original independent predicate 1.
bvec2 predicate1(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=b.r>multipliers.x;
return bvec2(!selected || (a.r>=0.995 && a.r<=1.001),false);

}
}
/// Evaluates the original independent predicate 2.
bvec2 predicate2(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=coordinate.x%37==0 && coordinate.y%31==0;
precise float hardware=b.r*2.0-1.0;
precise float scaled=hardware*99.9;
precise float denominator=100.1-scaled;
precise float expected=20.0/denominator;
precise float lower=expected-0.04,upper=expected+0.04;
return bvec2(!selected || (a.g>=lower && a.g<=upper),selected);

}
}
/// Evaluates the original independent predicate 3.
bvec2 predicate3(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(coordinate.x>=int(multipliers.x) || (a.r>=0.995 && a.r<=1.001),false);

}
}
/// Evaluates the original independent predicate 4.
bvec2 predicate4(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise vec3 lower=b.rgb-vec3(0.00001),upper=b.rgb+vec3(0.00001),combinedUpper=d.rgb+vec3(0.0001);
bool valid=all(greaterThanEqual(a.rgb,lower)) && all(lessThanEqual(a.rgb,upper))
    && !any(isnan(c.rgb)) && !any(isinf(c.rgb))
    && all(greaterThanEqual(c.rgb,vec3(-0.00001))) && all(lessThanEqual(c.rgb,combinedUpper));
return bvec2(valid,false);

}
}
/// Evaluates the original independent predicate 5.
bvec2 predicate5(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(coordinate.x!=30 || (a.r>=0.995 && a.r<=1.001),false);

}
}
/// Evaluates the original independent predicate 6.
bvec2 predicate6(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(coordinate.x%4>=2 || a.r==1.0,false);

}
}
/// Evaluates the original independent predicate 7.
bvec2 predicate7(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
ivec2 childSize=ivec2(multipliers.xy),parentSize=ivec2(multipliers.zw);
ivec2 first=coordinate.xy*childSize/parentSize;
ivec2 end=((coordinate.xy+1)*childSize+parentSize-1)/parentSize;
uint expected=0u;
for(int dy=0;dy<3;dy++)for(int dx=0;dx<3;dx++)
    if(first.x+dx<end.x && first.y+dy<end.y &&
        texelFetch(additionalImage,ivec3(first+ivec2(dx,dy),0),0).r==b.r)
        expected|=1u<<uint(dy*3+dx);
return bvec2(uint(a.r)==expected,false);

}
}
/// Evaluates the original independent predicate 8.
bvec2 predicate8(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,equalBits(floatBitsToUint(a[int(multipliers.y)]),floatBitsToUint(multipliers.x)));

}
}
/// Evaluates the original independent predicate 9.
bvec2 predicate9(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.r==multipliers.x);

}
}
/// Evaluates the original independent predicate 10.
bvec2 predicate10(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise float reconstructed=a.r+b.r*d.g;
reconstructed=reconstructed+c.r*d.b;
reconstructed=reconstructed/multipliers.x;
return bvec2(a.r>0.0 && abs(reconstructed-d.r)<=2e-6,false);

}
}
/// Evaluates the original independent predicate 11.
bvec2 predicate11(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r>=multipliers.x && a.r<=multipliers.y,false);

}
}
/// Evaluates the original independent predicate 12.
bvec2 predicate12(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise vec3 value=a.rgb/multipliers.x;
precise vec3 error=abs(value/b.rgb-vec3(1));
bool valid=!any(isnan(value)) && !any(isinf(value)) && all(greaterThanEqual(value,vec3(0)))
    && all(lessThanEqual(value,vec3(multipliers.y))) && error.r<=0.0101 && error.g<=0.0101 && error.b<=0.0201;
return bvec2(valid,false);

}
}
/// Evaluates the original independent predicate 13.
bvec2 predicate13(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool fresh=a.g==1.0;
vec4 raw=texelFetch(historyImage,ivec3(coordinate.xy/2,0),0);
bool valid=raw.g>=1.0 && roundEven(double(raw.r)*1e5)==roundEven(double(c.r)*1e5);
if(d.r>=0.0)valid=valid && multipliers.x-d.r>=1.0 && multipliers.x-d.r<=7.0;
return bvec2(!fresh || valid,fresh);

}
}
/// Evaluates the original independent predicate 14.
bvec2 predicate14(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(coordinate.x!=0 || a.r>texelFetch(actualImage,ivec3(4,coordinate.y,0),0).r,false);

}
}
/// Evaluates the original independent predicate 15.
bvec2 predicate15(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.g>=1.0);

}
}
/// Evaluates the original independent predicate 16.
bvec2 predicate16(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.a>1.0);

}
}
/// Evaluates the original independent predicate 17.
bvec2 predicate17(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool carried=a.g==2.0;
bool valid=a.r==1.0 && b.a==4.0 && a.a==1.0 && all(lessThanEqual(abs(c-d),vec4(2e-5)));
return bvec2(!carried || valid,carried);

}
}
/// Evaluates the original independent predicate 18.
bvec2 predicate18(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool fresh=a.g==1.0;
bool valid=a.a==0.0 && abs(b.a-1.0/(1.0-a.r))<=0.02 && (a.r!=0.0 || b.a==1.0);
return bvec2(!fresh || valid,fresh);

}
}
/// Evaluates the original independent predicate 19.
bvec2 predicate19(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(same(a,b,15u),false);

}
}
/// Evaluates the original independent predicate 20.
bvec2 predicate20(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(same(a,b,7u),false);

}
}
/// Evaluates the original independent predicate 21.
bvec2 predicate21(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r==b.r,false);

}
}
/// Evaluates the original independent predicate 22.
bvec2 predicate22(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool fresh=a.g==1.0;
return bvec2(!fresh || (roundEven(double(b.r)*1e5)==120000.0 && roundEven(double(c.a)*1e5)==500000.0),fresh);

}
}
/// Evaluates the original independent predicate 23.
bvec2 predicate23(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool carry=a.g==2.0;return bvec2(!carry || (b.r==1.0 && c.a==4.0),carry);

}
}
/// Evaluates the original independent predicate 24.
bvec2 predicate24(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.g==2.0);

}
}
/// Evaluates the original independent predicate 25.
bvec2 predicate25(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.b==8.0);

}
}
/// Evaluates the original independent predicate 26.
bvec2 predicate26(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool carry=a.g==2.0;
return bvec2(!carry || (a.a==(index%2u==0u?1.0:7.0) && b.a==4.0),carry);

}
}
/// Evaluates the original independent predicate 27.
bvec2 predicate27(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=coordinate.x==3 && coordinate.y>=1 && coordinate.y<7;
return bvec2(!selected || (a.b==12.0 && b.r==1.0 && b.a==0.5 && c.r==0.5 && d.a>=4.0),false);

}
}
/// Evaluates the original independent predicate 28.
bvec2 predicate28(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=coordinate.x==3 && coordinate.y>=1 && coordinate.y<7 && a.g==2.0;
return bvec2(!selected || b.r==3.0,false);

}
}
/// Evaluates the original independent predicate 29.
bvec2 predicate29(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.b==9.0);

}
}
/// Evaluates the original independent predicate 30.
bvec2 predicate30(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.b!=9.0 || b.g==0.0,false);

}
}
/// Evaluates the original independent predicate 31.
bvec2 predicate31(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=coordinate.x>=4 && coordinate.x<7 && coordinate.y>=1 && coordinate.y<7 && a.g==2.0 && b.g==1.0; bool valid=b.a==4.0 && all(equal(roundEven(dvec3(c.rgb)*1e5),dvec3(200000,300000,400000)))
    && roundEven(double(c.a)*1e5)==50000.0
    && roundEven(double(b.b)*1e4)==roundEven(double(d.r)*1e4);
return bvec2(!selected || valid,selected);

}
}
/// Evaluates the original independent predicate 32.
bvec2 predicate32(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=coordinate.x>=4 && coordinate.x<7 && coordinate.y>=1 && coordinate.y<7 && a.g==2.0 && b.g==1.0; bool valid=d.r==1.0 && all(equal(roundEven(dvec3(c.rgb)*1e5),dvec3(20000,40000,60000)));
return bvec2(!selected || valid,false);

}
}
/// Evaluates the original independent predicate 33.
bvec2 predicate33(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=coordinate.x>=4 && coordinate.x<7 && coordinate.y>=1 && coordinate.y<7 && a.g==2.0 && b.g==1.0; return bvec2(true,selected && coordinate.x%2==0);

}
}
/// Evaluates the original independent predicate 34.
bvec2 predicate34(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=coordinate.x>=4 && coordinate.x<7 && coordinate.y>=1 && coordinate.y<7 && a.g==2.0 && b.g==1.0; return bvec2(true,selected && coordinate.x%2!=0);

}
}
/// Evaluates the original independent predicate 35.
bvec2 predicate35(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=coordinate.x>=1 && coordinate.x<7 && coordinate.y>=1 && coordinate.y<7 && a.g==2.0 && a.b==0.0; return bvec2(!selected || (b.r==2.0 && b.a==0.5 && c.r==0.5),false);

}
}
/// Evaluates the original independent predicate 36.
bvec2 predicate36(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=coordinate.x>=1 && coordinate.x<7 && coordinate.y>=1 && coordinate.y<7 && a.g==2.0 && a.b==0.0; return bvec2(!selected || abs(b.r-c.r)<=0.004,selected);

}
}
/// Evaluates the original independent predicate 37.
bvec2 predicate37(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=coordinate.x>=1 && coordinate.x<7 && coordinate.y>=1 && coordinate.y<7 && a.g==2.0 && a.b==0.0; return bvec2(!selected || (roundEven(double(b.a)*1e4)==roundEven(double(c.g)*1e4)
    && b.a>=1.0 && b.a<=4.0),false);

}
}
/// Evaluates the original independent predicate 38.
bvec2 predicate38(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool monotonic=coordinate.x==0 || a.r+1.0/255.0>=texelFetch(actualImage,ivec3(coordinate.x-1,0,0),0).r;
return bvec2(monotonic && abs(a.g-a.r)<=multipliers.x && abs(a.b-a.r)<=multipliers.x,false);

}
}
/// Evaluates the original independent predicate 39.
bvec2 predicate39(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,any(greaterThan(a.rgb,vec3(0))));

}
}
/// Evaluates the original independent predicate 40.
bvec2 predicate40(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.r!=0);

}
}
/// Evaluates the original independent predicate 41.
bvec2 predicate41(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.b>.1);

}
}
/// Evaluates the original independent predicate 42.
bvec2 predicate42(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,any(greaterThan(a,b+vec4(.001))));

}
}
/// Evaluates the original independent predicate 43.
bvec2 predicate43(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise vec4 difference=abs(a-b);
precise vec4 allowed=vec4(.001)+.002*abs(b);
bool finite=!any(isnan(a)) && !any(isinf(a)) && !any(isnan(b)) && !any(isinf(b));
return bvec2(finite && all(lessThanEqual(difference,allowed)) && (multipliers.x==0 || (all(equal(a,vec4(0))) && all(equal(b,vec4(0))))),false);

}
}
/// Evaluates the original independent predicate 44.
bvec2 predicate44(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise float difference=abs(a.r-multipliers.x);return bvec2(difference<=.004,false);

}
}
/// Evaluates the original independent predicate 45.
bvec2 predicate45(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,any(greaterThan(abs(a.rgb-b.rgb),vec3(.02))));

}
}
/// Evaluates the original independent predicate 46.
bvec2 predicate46(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=b.r==1;
bool color=all(greaterThanEqual(a.rgb,vec3(0))) && all(lessThanEqual(a.rgb,vec3(65504)));
return bvec2(!selected || (a.a==1 && color),selected);

}
}
/// Evaluates the original independent predicate 47.
bvec2 predicate47(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(value.r==1,true);

}
}
/// Evaluates the original independent predicate 48.
bvec2 predicate48(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,false);

}
}
/// Evaluates the original independent predicate 49.
bvec2 predicate49(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(bvec4(b.r==a.r*2.0,c.r==a.r,d.r==a.r,b.g==1.0)), index%2u==0u);

}
}
/// Evaluates the original independent predicate 50.
bvec2 predicate50(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(texelFetch(historyImage,ivec3(0),0).r==7.0,b.r==7.0);

}
}
/// Evaluates the original independent predicate 51.
bvec2 predicate51(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,index==194u);

}
}
/// Evaluates the original independent predicate 52.
bvec2 predicate52(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(same(a,b,3u),false);

}
}
/// Evaluates the original independent predicate 53.
bvec2 predicate53(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
float horizontal=texelFetch(actualImage,ivec3(64-coordinate.x,coordinate.y,0),0).r;
float vertical=texelFetch(actualImage,ivec3(coordinate.x,32-coordinate.y,0),0).r;
return bvec2(a.r>=0 && a.r<=8 && horizontal>=a.r-.0001 && horizontal<=a.r+.0001
    && vertical>=a.r-.0001 && vertical<=a.r+.0001,false);

}
}
/// Evaluates the original independent predicate 54.
bvec2 predicate54(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool eligible=all(greaterThanEqual(coordinate.xy,ivec2(64)))&&all(lessThan(coordinate.xy,ivec2(192)))&&!(a.a<=0||b.a<=0);return bvec2(!eligible||!isnan(abs(a.r/a.a-b.r/b.a)),eligible);

}
}
/// Evaluates the original independent predicate 55.
bvec2 predicate55(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool eligible=all(greaterThanEqual(coordinate.xy,ivec2(64)))&&all(lessThan(coordinate.xy,ivec2(192)))&&!(a.a<=0||b.a<=0);return bvec2(true,eligible&&abs(a.r/a.a-b.r/b.a)>.01);

}
}
/// Evaluates the original independent predicate 56.
bvec2 predicate56(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r==-16 && a.g==(coordinate.x==1?1:0),false);

}
}
/// Evaluates the original independent predicate 57.
bvec2 predicate57(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise float detail=a.b-a.g;return bvec2(abs(double(detail)-.25lf)<=.00001lf,false);

}
}
/// Evaluates the original independent predicate 58.
bvec2 predicate58(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise vec3 difference=abs(a.rgb-multipliers.rgb);return bvec2(all(lessThan(difference,vec3(multipliers.w))),false);

}
}
/// Evaluates the original independent predicate 59.
bvec2 predicate59(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool green=a.g>0.8 && a.r<0.2 && a.b<0.2;
bool yellow=a.r>0.8 && a.g>0.8 && a.b<0.2;
bool red=a.r>0.8 && a.g<0.2 && a.b<0.2;
return bvec2(true,green || yellow || red);

}
}
/// Evaluates the original independent predicate 60.
bvec2 predicate60(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise float brightness=a.r+a.g;brightness=brightness+a.b;return bvec2(true,brightness>0.1);

}
}
/// Evaluates the original independent predicate 61.
bvec2 predicate61(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise vec3 difference=abs(a.rgb-vec3(0.5));return bvec2(all(lessThan(difference,vec3(0.2))),false);

}
}
/// Evaluates the original independent predicate 62.
bvec2 predicate62(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.r<0.01 && a.g<0.01 && a.b<0.01);

}
}
/// Evaluates the original independent predicate 63.
bvec2 predicate63(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.r>0.01 || a.g>0.01 || a.b>0.01);

}
}
/// Evaluates the original independent predicate 64.
bvec2 predicate64(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
ivec2 origin=coordinate.xy*2;
vec4 block=vec4(texelFetch(historyImage,ivec3(origin,0),0).r,
    texelFetch(historyImage,ivec3(origin+ivec2(1,0),0),0).r,
    texelFetch(historyImage,ivec3(origin+ivec2(0,1),0),0).r,
    texelFetch(historyImage,ivec3(origin+ivec2(1,1),0),0).r);
float expected=min(min(block.x,block.y),min(block.z,block.w));
precise float difference=abs(a.r-expected);
return bvec2(!any(isnan(block)) && difference<multipliers.x,false);

}
}
/// Evaluates the original independent predicate 65.
bvec2 predicate65(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r>=9.99 && a.r<=10.01 && b.r==1.0,false);

}
}
/// Evaluates the original independent predicate 66.
bvec2 predicate66(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(b.r==1.0 && a.g>=9.99 && a.g<=10.01,abs(c.r-a.r)>0.03);

}
}
/// Evaluates the original independent predicate 67.
bvec2 predicate67(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(equal(a.rgb,vec3(0))) && same(a,c,7u) && b.r==0.0 && (flags&1u)==1u && (flags&32u)==0u,false);

}
}
/// Evaluates the original independent predicate 68.
bvec2 predicate68(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r==0.0 && b.r==0.0 && (flags&34u)==0u,false);

}
}
/// Evaluates the original independent predicate 69.
bvec2 predicate69(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r>=9.9 && a.r<=10.1 && c.r==0.0 && b.r==1.0 && (flags&32u)!=0u,false);

}
}
/// Evaluates the original independent predicate 70.
bvec2 predicate70(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.r>9.0);

}
}
/// Evaluates the original independent predicate 71.
bvec2 predicate71(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.r==0.0);

}
}
/// Evaluates the original independent predicate 72.
bvec2 predicate72(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r==0.0 && b.r==0.0 && (flags&1u)==1u,false);

}
}
/// Evaluates the original independent predicate 73.
bvec2 predicate73(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r==0.0 && a.a==0.0 && b.r==0.0 && (flags&1u)==1u,false);

}
}
/// Evaluates the original independent predicate 74.
bvec2 predicate74(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(equal(a.rgb,vec3(0))) && b.r==0.0,false);

}
}
/// Evaluates the original independent predicate 75.
bvec2 predicate75(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2((flags&1u)!=0u && (flags&32u)==0u,false);

}
}
/// Evaluates the original independent predicate 76.
bvec2 predicate76(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool hit=(flags&1u)!=0u; return bvec2(!hit || (a.r==0.0 && b.r==0.0),hit);

}
}
/// Evaluates the original independent predicate 77.
bvec2 predicate77(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
float expectedConfidence=multipliers.w!=0.0 && (flags&4u)!=0u ? 0.05 : multipliers.y;
bool valid=((flags>>16)&7u)==uint(multipliers.x) && b.r==expectedConfidence
    && ((flags&1u)!=0u)==(multipliers.z!=0.0)
    && ((flags&32u)!=0u)==(uint(multipliers.x)==5u);
if(uint(multipliers.x)!=5u && uint(multipliers.x)!=6u)valid=valid && a.r==0.0;
return bvec2(valid,false);

}
}
/// Evaluates the original independent predicate 78.
bvec2 predicate78(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r>=9.99 && a.r<=10.01,false);

}
}
/// Evaluates the original independent predicate 79.
bvec2 predicate79(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool hit=(floatBitsToUint(c.g)&1u)!=0u;
bool valid=b.r==0.0 && (flags&1u)==1u && (flags&32u)==0u
    && all(equal(d.rgb,vec3(0))) && all(lessThanEqual(abs(a.rgb-d.rgb),vec3(0.002)));
return bvec2(!hit || valid,hit);

}
}
/// Evaluates the original independent predicate 80.
bvec2 predicate80(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool hit=(floatBitsToUint(c.g)&1u)!=0u;
return bvec2(!hit || (b.r==0.0 && all(equal(a.rgb,vec3(0)))),hit);

}
}
/// Evaluates the original independent predicate 81.
bvec2 predicate81(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool hit=(floatBitsToUint(c.g)&1u)!=0u;
bool valid=b.r==multipliers.x && all(greaterThanEqual(a.rgb,vec3(multipliers.y)))
    && all(lessThanEqual(a.rgb,vec3(multipliers.z)));
return bvec2(!hit || valid,hit);

}
}
/// Evaluates the original independent predicate 82.
bvec2 predicate82(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.b>.1 && a.r/a.b>=.098 && a.r/a.b<=.102 && a.g/a.b>=.848 && a.g/a.b<=.852,false);

}
}
/// Evaluates the original independent predicate 83.
bvec2 predicate83(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r>.1 && a.g/a.r>=.448 && a.g/a.r<=.452 && a.b==0,false);

}
}
/// Evaluates the original independent predicate 84.
bvec2 predicate84(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(greaterThanEqual(a.rgb,multipliers.rgb-vec3(.001))) && all(lessThanEqual(a.rgb,multipliers.rgb+vec3(.001))),false);

}
}
/// Evaluates the original independent predicate 85.
bvec2 predicate85(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.b>.1,false);

}
}
/// Evaluates the original independent predicate 86.
bvec2 predicate86(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise float difference=a.r-a.b;
bool boundary=abs(difference)<.001;
bool classified=boundary?(a.g>=a.r-.001 && a.g<=a.r+.001):(a.r/a.b>=.098 && a.r/a.b<=.102);
return bvec2(a.b>.1 && classified,boundary);

}
}
/// Evaluates the original independent predicate 87.
bvec2 predicate87(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool lit=multipliers.x!=0.0,opaque=multipliers.y!=0.0;
return bvec2(((flags&1u)!=0u)==opaque && (flags&32u)==0u
    && ((flags>>16)&7u)==(lit?2u:4u) && b.r==(lit?1.0:0.0)
    && all(greaterThanEqual(a.rgb,vec3(lit?0.249:0.0)))
    && all(lessThanEqual(a.rgb,vec3(lit?0.253:0.0))),false);

}
}
/// Evaluates the original independent predicate 88.
bvec2 predicate88(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise vec3 normal=a.rgb*2-1;
precise float length=sqrt((normal.x*normal.x+normal.y*normal.y)+normal.z*normal.z);
if(length>.001)normal=normal/length;
precise vec3 difference=abs(normal-b.rgb);
return bvec2(all(lessThan(difference,vec3(multipliers.x))),false);

}
}
/// Evaluates the original independent predicate 89.
bvec2 predicate89(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise vec3 difference=abs(a.rgb-b.rgb);return bvec2(all(lessThan(difference,vec3(multipliers.x))) && a.a>=.9,false);

}
}
/// Evaluates the original independent predicate 90.
bvec2 predicate90(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.a>=.5,a.a>.5);

}
}
/// Evaluates the original independent predicate 91.
bvec2 predicate91(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.a<.1 && all(lessThan(abs(a.rgb),vec3(multipliers.x))),false);

}
}
/// Evaluates the original independent predicate 92.
bvec2 predicate92(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.a>.4 && a.a<.6,false);

}
}
/// Evaluates the original independent predicate 93.
bvec2 predicate93(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(!any(isnan(a)) && a.a<.5,false);

}
}
/// Evaluates the original independent predicate 94.
bvec2 predicate94(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(greaterThanEqual(a.rgb,b.rgb-vec3(.005))) && all(lessThanEqual(a.rgb,b.rgb+vec3(.005))),false);

}
}
/// Evaluates the original independent predicate 95.
bvec2 predicate95(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool valid=true;
// Match the original strict channel gates; absent baseline channels are not asserted.
for(int c=0;c<3;c++)if(b[c]>.01){precise float ratio=a[c]/b[c];precise float delta=ratio-multipliers[c];valid=valid && abs(delta)<.2;}
return bvec2(valid,false);

}
}
/// Evaluates the original independent predicate 96.
bvec2 predicate96(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(lessThan(a.rgb,vec3(multipliers.x))),false);

}
}
/// Evaluates the original independent predicate 97.
bvec2 predicate97(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(((floatBitsToUint(a.g)>>16)&7u)==2u,false);

}
}
/// Evaluates the original independent predicate 98.
bvec2 predicate98(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(lessThan(abs(a),vec4(multipliers.x))),false);

}
}
/// Evaluates the original independent predicate 99.
bvec2 predicate99(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=b.r==1;return bvec2(!selected || (a.r>.9 && a.g<.1 && a.b<.1),selected);

}
}
/// Evaluates the original independent predicate 100.
bvec2 predicate100(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r>.9 && a.g<.1 && a.b<.1,false);

}
}
/// Evaluates the original independent predicate 101.
bvec2 predicate101(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,b.r==0 && a.b>.1);

}
}
/// Evaluates the original independent predicate 102.
bvec2 predicate102(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise vec3 delta=abs(a.rgb-b.rgb);return bvec2(true,any(greaterThan(delta,vec3(.1))));

}
}
/// Evaluates the original independent predicate 103.
bvec2 predicate103(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(lessThan(abs(a.rgb),vec3(multipliers.x))),false);

}
}
/// Evaluates the original independent predicate 104.
bvec2 predicate104(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(any(greaterThan(a.rgb,vec3(.01))),false);

}
}
/// Evaluates the original independent predicate 105.
bvec2 predicate105(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=(coordinate.x%8==2 || coordinate.x%8==6) && (coordinate.y%8==2 || coordinate.y%8==6);
precise float decoded=exp(a.a)-1;
return bvec2(!selected || abs(decoded-multipliers.x)<multipliers.y,selected);

}
}
/// Evaluates the original independent predicate 106.
bvec2 predicate106(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.r<.1 && a.g>.9 && a.b>.9);

}
}
/// Evaluates the original independent predicate 107.
bvec2 predicate107(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.g>.01 && a.b>.01);

}
}
/// Evaluates the original independent predicate 108.
bvec2 predicate108(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,any(greaterThan(a.rgb,vec3(.001))));

}
}
/// Evaluates the original independent predicate 109.
bvec2 predicate109(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise float distance=exp(a.a)-1;return bvec2(true,distance<multipliers.x);

}
}
/// Evaluates the original independent predicate 110.
bvec2 predicate110(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise float distance=exp(a.a)-1;return bvec2(true,distance>multipliers.x);

}
}
/// Evaluates the original independent predicate 111.
bvec2 predicate111(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,any(greaterThan(a.rgb,vec3(.01)))!=any(greaterThan(b.rgb,vec3(.01))));

}
}
/// Evaluates the original independent predicate 112.
bvec2 predicate112(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,any(greaterThan(a.rgb,vec3(.01))));

}
}
/// Evaluates the original independent predicate 113.
bvec2 predicate113(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool color=all(greaterThanEqual(a.rgb,vec3(multipliers.x))) && all(lessThanEqual(a.rgb,vec3(multipliers.y)));
bool confidence=b.r>=multipliers.z && (multipliers.z!=0 || b.r==0);
return bvec2(color && confidence && (floatBitsToUint(b.g)&32u)!=0u,false);

}
}
/// Evaluates the original independent predicate 114.
bvec2 predicate114(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.r>.001);

}
}
/// Evaluates the original independent predicate 115.
bvec2 predicate115(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise vec3 difference=b.rgb-c.rgb;
precise float delta=(difference.r*.2126+difference.g*.7152)+difference.b*.0722;
precise float brightness=abs(delta)/(1+abs(delta));
vec3 expected=brightness*vec3(delta>=0?1:0,.35,delta<0?1:0);
bool response=all(greaterThanEqual(a.rgb,expected-vec3(.002))) && all(lessThanEqual(a.rgb,expected+vec3(.002)));
precise float energy=(a.r+a.g)+a.b;
return bvec2(response && (multipliers.x==1?energy>.01:all(equal(a.rgb,vec3(0)))),false);

}
}
/// Evaluates the original independent predicate 116.
bvec2 predicate116(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise float brightness=(a.r+a.g)+a.b;
bool color=multipliers.x==1?brightness>.1:all(greaterThanEqual(a.rgb,vec3(-.000001))) && all(lessThanEqual(a.rgb,vec3(.000001)));
return bvec2(color && a.a>.1 && same(a,b,8u) && all(equal(b.rgb,vec3(0))),false);

}
}
/// Evaluates the original independent predicate 117.
bvec2 predicate117(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(value.r!=0 || value.g!=0,true);

}
}
/// Evaluates the original independent predicate 118.
bvec2 predicate118(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool represented=!isnan(a.r);return bvec2(!represented || (a.r>=multipliers.x && a.r<=multipliers.y),represented);

}
}
/// Evaluates the original independent predicate 119.
bvec2 predicate119(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,value.a>.5);

}
}
/// Evaluates the original independent predicate 120.
bvec2 predicate120(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,any(greaterThan(value.rgb,vec3(.001))));

}
}
/// Evaluates the original independent predicate 121.
bvec2 predicate121(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2((value.r!=0 || value.g!=0) && roundEven(value.b*255)==9 && value.a==0,true);

}
}
/// Evaluates the original independent predicate 122.
bvec2 predicate122(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool represented=!isnan(a.r);return bvec2(!represented || (a.r>=-.02 && a.r<=.02),represented);

}
}
/// Evaluates the original independent predicate 123.
bvec2 predicate123(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise float brightness=((a.r+a.g)+a.b)/3;
vec3 prior=texelFetch(actualImage,ivec3(max(coordinate.x-1,0),coordinate.y,coordinate.z),0).rgb;
precise float previous=coordinate.x==0?-1:((prior.r+prior.g)+prior.b)/3;
return bvec2(brightness>=previous-.05,false);

}
}
/// Evaluates the original independent predicate 124.
bvec2 predicate124(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool left=coordinate.x==0;bool right=coordinate.x==extent.x-1;return bvec2((!left || a.r>a.b) && (!right || a.b>a.r),left || right);

}
}
/// Evaluates the original independent predicate 125.
bvec2 predicate125(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(floatBitsToUint(a.a)==64u,false);

}
}
/// Evaluates the original independent predicate 126.
bvec2 predicate126(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(floatBitsToUint(a.a)==uint(multipliers.x),false);

}
}
/// Evaluates the original independent predicate 127.
bvec2 predicate127(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(abs(a.r)<=multipliers.x && abs(a.g)<=multipliers.x && (floatBitsToUint(a.a)&1u)!=0u,false);

}
}
/// Evaluates the original independent predicate 128.
bvec2 predicate128(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r>=multipliers.x && a.r<=multipliers.y && abs(a.g)<=multipliers.z && (floatBitsToUint(a.a)&1u)!=0u,false);

}
}
/// Evaluates the original independent predicate 129.
bvec2 predicate129(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(greaterThanEqual(a.rgb,multipliers.rgb-vec3(multipliers.w))) && all(lessThanEqual(a.rgb,multipliers.rgb+vec3(multipliers.w))),false);

}
}
/// Evaluates the original independent predicate 130.
bvec2 predicate130(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool inside=coordinate.x>=16 && coordinate.x<48 && coordinate.y>=16 && coordinate.y<48;
bool valid=inside ? (!any(isnan(a)) && !any(isinf(a))) : all(equal(a,multipliers));
return bvec2(valid,false);

}
}
/// Evaluates the original independent predicate 131.
bvec2 predicate131(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool inside=coordinate.x>=16 && coordinate.x<48 && coordinate.y>=16 && coordinate.y<48;
return bvec2(true,inside && a.a>0.01 && a.a<0.99);

}
}
/// Evaluates the original independent predicate 132.
bvec2 predicate132(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool inside=coordinate.x>=16 && coordinate.x<48 && coordinate.y>=16 && coordinate.y<48;
vec4 expected=inside?b:vec4(0.5,0.5,1.0,0.5);
return bvec2(all(lessThanEqual(abs(a-expected),vec4(0.001))),false);

}
}
/// Evaluates the original independent predicate 133.
bvec2 predicate133(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(greaterThanEqual(a.rgb,vec3(9.9))) && all(lessThanEqual(a.rgb,vec3(10.1)))
    && all(equal(c.rgb,vec3(0))) && b.r==1.0 && (flags&32u)!=0u,false);

}
}
/// Evaluates the original independent predicate 134.
bvec2 predicate134(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2((flags&32u)==0u,false);

}
}
/// Evaluates the original independent predicate 135.
bvec2 predicate135(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r==(coordinate.x<2?.125:.25) && (coordinate.x<2 || b.r>0),false);

}
}
/// Evaluates the original independent predicate 136.
bvec2 predicate136(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r>=3.99999 && a.r<=4.00001 && a.g>=1.99999 && a.g<=2.00001 && a.b>=.99999 && a.b<=1.00001,false);

}
}
/// Evaluates the original independent predicate 137.
bvec2 predicate137(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=coordinate.y==8 && coordinate.x>=int(multipliers.x)+17 && coordinate.x<=26;
float previous=texelFetch(actualImage,ivec3(max(coordinate.x-1,0),coordinate.y,0),0).r;
return bvec2(!selected || a.r<=previous+.00001,selected);

}
}
/// Evaluates the original independent predicate 138.
bvec2 predicate138(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r>=1.99 && a.r<=2.01 && a.g>=.995 && a.g<=1.005 && a.b>=.4975 && a.b<=.5025,false);

}
}
/// Evaluates the original independent predicate 139.
bvec2 predicate139(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
float horizontal=texelFetch(actualImage,ivec3(128-coordinate.x,coordinate.y,0),0).r;
float vertical=texelFetch(actualImage,ivec3(coordinate.x,64-coordinate.y,0),0).r;
bool valid=!isnan(a.r) && !isinf(a.r) && a.r>=0 && horizontal>=a.r-.0002 && horizontal<=a.r+.0002 && vertical>=a.r-.0002 && vertical<=a.r+.0002;
return bvec2(valid,a.r>.00001);

}
}
/// Evaluates the original independent predicate 140.
bvec2 predicate140(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r==a.g&&a.r==a.b&&roundEven(a.a*255)==73,false);

}
}
/// Evaluates the original independent predicate 141.
bvec2 predicate141(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,abs(a.a-b.a)>0.01);

}
}
/// Evaluates the original independent predicate 142.
bvec2 predicate142(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,any(greaterThan(abs(a.rgb-b.rgb),vec3(0.001))));

}
}
/// Evaluates the original independent predicate 143.
bvec2 predicate143(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(lessThan(abs(a.rgb-vec3(0.5,0.5,1.0)),vec3(0.02))),false);

}
}
/// Evaluates the original independent predicate 144.
bvec2 predicate144(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,!isnan(a.a) && !isinf(a.a) && a.a>0.999);

}
}
/// Evaluates the original independent predicate 145.
bvec2 predicate145(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.a>.5);

}
}
/// Evaluates the original independent predicate 146.
bvec2 predicate146(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise vec4 delta=abs(a-b);return bvec2(true,any(greaterThan(delta,vec4(.0001))));

}
}
/// Evaluates the original independent predicate 147.
bvec2 predicate147(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,any(greaterThan(a.rgb,vec3(multipliers.x))));

}
}
/// Evaluates the original independent predicate 148.
bvec2 predicate148(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise vec3 expected=(b.rgb+c.rgb)+d.rgb;precise vec3 delta=abs(a.rgb-expected);return bvec2(all(lessThanEqual(delta,vec3(multipliers.x))),false);

}
}
/// Evaluates the original independent predicate 149.
bvec2 predicate149(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.a>0.999);

}
}
/// Evaluates the original independent predicate 150.
bvec2 predicate150(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.a<0.001);

}
}
/// Evaluates the original independent predicate 151.
bvec2 predicate151(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise vec2 recovered=a.rg*65535;return bvec2(all(equal(roundEven(recovered),b.rg)),false);

}
}
/// Evaluates the original independent predicate 152.
bvec2 predicate152(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r>.1,false);

}
}
/// Evaluates the original independent predicate 153.
bvec2 predicate153(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(greaterThanEqual(a,multipliers-vec4(.001))) && all(lessThanEqual(a,multipliers+vec4(.001))),false);

}
}
/// Evaluates the original independent predicate 154.
bvec2 predicate154(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
uint unavailableFlags=floatBitsToUint(d.g);
bool valid=all(equal(c.rgb,vec3(0))) && all(greaterThanEqual(a.rgb,vec3(multipliers.x)))
    && all(lessThanEqual(a.rgb,vec3(multipliers.y))) && d.r==0.0 && (unavailableFlags&1u)==1u
    && ((unavailableFlags>>16)&7u)==4u && (flags&1u)==1u
    && ((flags>>16)&7u)==uint(multipliers.z) && c.a==a.a && b.r==1.0 && (flags&32u)==0u;
return bvec2(valid,false);

}
}
/// Evaluates the original independent predicate 155.
bvec2 predicate155(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.b!=0,false);

}
}
/// Evaluates the original independent predicate 156.
bvec2 predicate156(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r>=multipliers.x && a.r<=multipliers.y && a.a==2,false);

}
}
/// Evaluates the original independent predicate 157.
bvec2 predicate157(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise float green=a.r*0.8,blue=a.r*0.6;
return bvec2(a.g>=green-0.02 && a.g<=green+0.02 && a.b>=blue-0.02 && a.b<=blue+0.02,false);

}
}
/// Evaluates the original independent predicate 158.
bvec2 predicate158(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise vec4 difference=abs(a-b);
return bvec2(all(lessThanEqual(difference,vec4(multipliers.x))),false);

}
}
/// Evaluates the original independent predicate 159.
bvec2 predicate159(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.a==4,a.r>.5);

}
}
/// Evaluates the original independent predicate 160.
bvec2 predicate160(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(greaterThanEqual(a.rgb,vec3(multipliers.x))) && all(lessThanEqual(a.rgb,vec3(multipliers.y))) && a.a==multipliers.z,false);

}
}
/// Evaluates the original independent predicate 161.
bvec2 predicate161(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(greaterThanEqual(a.rgb,vec3(multipliers.x))) && all(lessThanEqual(a.rgb,vec3(multipliers.y))) && a.a==1,false);

}
}
/// Evaluates the original independent predicate 162.
bvec2 predicate162(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=b.a==1 && a.a==0;return bvec2(!selected || all(equal(a.rgb,vec3(0))),selected);

}
}
/// Evaluates the original independent predicate 163.
bvec2 predicate163(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2((a.a==0 || a.a==1) && (a.a!=0 || all(equal(a,vec4(0)))),a.a==1);

}
}
/// Evaluates the original independent predicate 164.
bvec2 predicate164(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool hit=(flags&1u)!=0u; return bvec2(!hit || (b.r==1.0 && a.r>=multipliers.x && a.r<=multipliers.y),hit);

}
}
/// Evaluates the original independent predicate 165.
bvec2 predicate165(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(c.a==a.a && (floatBitsToUint(d.g)&63u)==(flags&63u) && ((flags>>16)&7u)==4u && b.r==0.0 && a.r==0.0,false);

}
}
/// Evaluates the original independent predicate 166.
bvec2 predicate166(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool hit=(floatBitsToUint(d.g)&1u)!=0u; return bvec2(!hit || (b.r==1.0 && (flags&1u)==1u && a.r>=11.99 && a.r<=12.01),hit);

}
}
/// Evaluates the original independent predicate 167.
bvec2 predicate167(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool hit=(flags&1u)!=0u; return bvec2(!hit || b.r==1.0,hit);

}
}
/// Evaluates the original independent predicate 168.
bvec2 predicate168(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.r>b.r+0.01 && c.r<a.r-0.001 && c.r>b.r+0.0001);

}
}
/// Evaluates the original independent predicate 169.
bvec2 predicate169(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.a<b.a-0.01);

}
}
/// Evaluates the original independent predicate 170.
bvec2 predicate170(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
int pixel=int(index),x=pixel%16,y=pixel/16,probe=x/8+(y/8)*2,direction=(y%8)*8+x%8;
bool traced=direction/8==(int(multipliers.x)+probe)%8;
return bvec2(traced || same(a,b,15u),!traced && b.r>0.001);

}
}
/// Evaluates the original independent predicate 171.
bvec2 predicate171(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
int pixel=int(index),x=pixel%16,y=pixel/16,probe=x/8+(y/8)*2,direction=(y%8)*8+x%8;
bool traced=direction/8==(int(multipliers.x)+probe)%8;
return bvec2(traced || (all(equal(a,vec4(0))) && b.r==0.0),false);

}
}
/// Evaluates the original independent predicate 172.
bvec2 predicate172(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(lessThanEqual(abs(a.rgb-b.rgb),vec3(multipliers.x))),false);

}
}
/// Evaluates the original independent predicate 173.
bvec2 predicate173(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r>=-.04001 && a.r<=.04001 && !any(isnan(a.gba)) && !any(isinf(a.gba)) && a.a>=.5 && a.a<=1.001,false);

}
}
/// Evaluates the original independent predicate 174.
bvec2 predicate174(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(!isnan(a.r) && !isinf(a.r) && abs(a.r)<=.031251 && a.b>=0 && a.b<=1,false);

}
}
/// Evaluates the original independent predicate 175.
bvec2 predicate175(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(greaterThanEqual(a,b-vec4(.0001))) && all(lessThanEqual(a,b+vec4(.0001))),false);

}
}
/// Evaluates the original independent predicate 176.
bvec2 predicate176(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool valid=coordinate.y!=0||coordinate.x==0||all(lessThanEqual(a.rgb,texelFetch(actualImage,coordinate-ivec3(1,0,0),0).rgb));return bvec2(valid,false);

}
}
/// Evaluates the original independent predicate 177.
bvec2 predicate177(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=(coordinate.y==13||coordinate.y==14)&&(coordinate.x==0||coordinate.x==4);return bvec2(!selected||a.r!=multipliers.x,false);

}
}
/// Evaluates the original independent predicate 178.
bvec2 predicate178(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool complete=d.a==1 || multipliers.x==1;
precise vec2 difference=abs(c.gb-d.gb);
bool valid=complete?(same(a,b,15u) && same(c,d,9u) && all(lessThanEqual(difference,vec2(.000001)))):(a.a==0 && c.a==0);
return bvec2(valid,false);

}
}
/// Evaluates the original independent predicate 179.
bvec2 predicate179(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.a==0);

}
}
/// Evaluates the original independent predicate 180.
bvec2 predicate180(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
uvec4 actual=floatBitsToUint(a),expected=texelFetch(expectedImage,coordinate,0);
bool valid=true;
for(int channel=0;channel<4;channel++)if((channels&(1u<<channel))!=0u)valid=valid && actual[channel]==expected[channel];
return bvec2(valid,false);

}
}
/// Evaluates the original independent predicate 181.
bvec2 predicate181(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
vec4 expected=uintBitsToFloat(texelFetch(expectedImage,ivec3(coordinate.xy,coordinate.z*2),0));
vec4 allowed=uintBitsToFloat(texelFetch(expectedImage,ivec3(coordinate.xy,coordinate.z*2+1),0));
precise vec4 difference=abs(a-expected);
bool valid=true;
for(int channel=0;channel<4;channel++)if((channels&(1u<<channel))!=0u)
    valid=valid && difference[channel]<=allowed[channel];
return bvec2(valid,false);

}
}
/// Evaluates the original independent predicate 182.
bvec2 predicate182(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool valid=abs(c.r-1.2)<=2e-6 && abs(d.a-5.0)<=2e-6 && a.b==0.0; bool selected=b.g==1.0;return bvec2(!selected || valid,selected);

}
}
/// Evaluates the original independent predicate 183.
bvec2 predicate183(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool valid=roundEven(double(c.r)*1e5)==80000.0 && roundEven(double(d.a)*1e5)==500000.0 && a.b==0.0; bool selected=b.g==1.0;return bvec2(!selected || valid,selected);

}
}
/// Evaluates the original independent predicate 184.
bvec2 predicate184(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool valid=a.r==0.0 && d.a==1.0 && a.b==11.0; bool selected=b.g==1.0;return bvec2(!selected || valid,selected);

}
}
/// Evaluates the original independent predicate 185.
bvec2 predicate185(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool valid=d.g==2.0 && d.a==1.0 && c.a==0.0 && a.b==4.0; bool selected=b.g==1.0;return bvec2(!selected || valid,selected);

}
}
/// Evaluates independently authored scenario expectations; its second result counts selected witnesses.
bvec2 authored(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d) {
    // Decode only trace observations; ordinary bit predicates must not perform arithmetic on metadata.
if(evaluation.w>=2.0){a.rgb*=evaluation.x;if(evaluation.w==2.0)c.rgb*=evaluation.y;else d.rgb*=evaluation.z;}uint flags=floatBitsToUint(b.g);
ivec3 selected=ivec3(0);vec4 value=a;if(evaluation.w==1.0){selected=ivec3(a.xyz);value=texelFetch(historyImage,selected,0);}
bvec2 result=bvec2(true,false);
switch(predicateChoice){
case 1: result=predicate1(coordinate,index,a,b,c,d,selected,value,flags);break;
case 2: result=predicate2(coordinate,index,a,b,c,d,selected,value,flags);break;
case 3: result=predicate3(coordinate,index,a,b,c,d,selected,value,flags);break;
case 4: result=predicate4(coordinate,index,a,b,c,d,selected,value,flags);break;
case 5: result=predicate5(coordinate,index,a,b,c,d,selected,value,flags);break;
case 6: result=predicate6(coordinate,index,a,b,c,d,selected,value,flags);break;
case 7: result=predicate7(coordinate,index,a,b,c,d,selected,value,flags);break;
case 8: result=predicate8(coordinate,index,a,b,c,d,selected,value,flags);break;
case 9: result=predicate9(coordinate,index,a,b,c,d,selected,value,flags);break;
case 10: result=predicate10(coordinate,index,a,b,c,d,selected,value,flags);break;
case 11: result=predicate11(coordinate,index,a,b,c,d,selected,value,flags);break;
case 12: result=predicate12(coordinate,index,a,b,c,d,selected,value,flags);break;
case 13: result=predicate13(coordinate,index,a,b,c,d,selected,value,flags);break;
case 14: result=predicate14(coordinate,index,a,b,c,d,selected,value,flags);break;
case 15: result=predicate15(coordinate,index,a,b,c,d,selected,value,flags);break;
case 16: result=predicate16(coordinate,index,a,b,c,d,selected,value,flags);break;
case 17: result=predicate17(coordinate,index,a,b,c,d,selected,value,flags);break;
case 18: result=predicate18(coordinate,index,a,b,c,d,selected,value,flags);break;
case 19: result=predicate19(coordinate,index,a,b,c,d,selected,value,flags);break;
case 20: result=predicate20(coordinate,index,a,b,c,d,selected,value,flags);break;
case 21: result=predicate21(coordinate,index,a,b,c,d,selected,value,flags);break;
case 22: result=predicate22(coordinate,index,a,b,c,d,selected,value,flags);break;
case 23: result=predicate23(coordinate,index,a,b,c,d,selected,value,flags);break;
case 24: result=predicate24(coordinate,index,a,b,c,d,selected,value,flags);break;
case 25: result=predicate25(coordinate,index,a,b,c,d,selected,value,flags);break;
case 26: result=predicate26(coordinate,index,a,b,c,d,selected,value,flags);break;
case 27: result=predicate27(coordinate,index,a,b,c,d,selected,value,flags);break;
case 28: result=predicate28(coordinate,index,a,b,c,d,selected,value,flags);break;
case 29: result=predicate29(coordinate,index,a,b,c,d,selected,value,flags);break;
case 30: result=predicate30(coordinate,index,a,b,c,d,selected,value,flags);break;
case 31: result=predicate31(coordinate,index,a,b,c,d,selected,value,flags);break;
case 32: result=predicate32(coordinate,index,a,b,c,d,selected,value,flags);break;
case 33: result=predicate33(coordinate,index,a,b,c,d,selected,value,flags);break;
case 34: result=predicate34(coordinate,index,a,b,c,d,selected,value,flags);break;
case 35: result=predicate35(coordinate,index,a,b,c,d,selected,value,flags);break;
case 36: result=predicate36(coordinate,index,a,b,c,d,selected,value,flags);break;
case 37: result=predicate37(coordinate,index,a,b,c,d,selected,value,flags);break;
case 38: result=predicate38(coordinate,index,a,b,c,d,selected,value,flags);break;
case 39: result=predicate39(coordinate,index,a,b,c,d,selected,value,flags);break;
case 40: result=predicate40(coordinate,index,a,b,c,d,selected,value,flags);break;
case 41: result=predicate41(coordinate,index,a,b,c,d,selected,value,flags);break;
case 42: result=predicate42(coordinate,index,a,b,c,d,selected,value,flags);break;
case 43: result=predicate43(coordinate,index,a,b,c,d,selected,value,flags);break;
case 44: result=predicate44(coordinate,index,a,b,c,d,selected,value,flags);break;
case 45: result=predicate45(coordinate,index,a,b,c,d,selected,value,flags);break;
case 46: result=predicate46(coordinate,index,a,b,c,d,selected,value,flags);break;
case 47: result=predicate47(coordinate,index,a,b,c,d,selected,value,flags);break;
case 48: result=predicate48(coordinate,index,a,b,c,d,selected,value,flags);break;
case 49: result=predicate49(coordinate,index,a,b,c,d,selected,value,flags);break;
case 50: result=predicate50(coordinate,index,a,b,c,d,selected,value,flags);break;
case 51: result=predicate51(coordinate,index,a,b,c,d,selected,value,flags);break;
case 52: result=predicate52(coordinate,index,a,b,c,d,selected,value,flags);break;
case 53: result=predicate53(coordinate,index,a,b,c,d,selected,value,flags);break;
case 54: result=predicate54(coordinate,index,a,b,c,d,selected,value,flags);break;
case 55: result=predicate55(coordinate,index,a,b,c,d,selected,value,flags);break;
case 56: result=predicate56(coordinate,index,a,b,c,d,selected,value,flags);break;
case 57: result=predicate57(coordinate,index,a,b,c,d,selected,value,flags);break;
case 58: result=predicate58(coordinate,index,a,b,c,d,selected,value,flags);break;
case 59: result=predicate59(coordinate,index,a,b,c,d,selected,value,flags);break;
case 60: result=predicate60(coordinate,index,a,b,c,d,selected,value,flags);break;
case 61: result=predicate61(coordinate,index,a,b,c,d,selected,value,flags);break;
case 62: result=predicate62(coordinate,index,a,b,c,d,selected,value,flags);break;
case 63: result=predicate63(coordinate,index,a,b,c,d,selected,value,flags);break;
case 64: result=predicate64(coordinate,index,a,b,c,d,selected,value,flags);break;
case 65: result=predicate65(coordinate,index,a,b,c,d,selected,value,flags);break;
case 66: result=predicate66(coordinate,index,a,b,c,d,selected,value,flags);break;
case 67: result=predicate67(coordinate,index,a,b,c,d,selected,value,flags);break;
case 68: result=predicate68(coordinate,index,a,b,c,d,selected,value,flags);break;
case 69: result=predicate69(coordinate,index,a,b,c,d,selected,value,flags);break;
case 70: result=predicate70(coordinate,index,a,b,c,d,selected,value,flags);break;
case 71: result=predicate71(coordinate,index,a,b,c,d,selected,value,flags);break;
case 72: result=predicate72(coordinate,index,a,b,c,d,selected,value,flags);break;
case 73: result=predicate73(coordinate,index,a,b,c,d,selected,value,flags);break;
case 74: result=predicate74(coordinate,index,a,b,c,d,selected,value,flags);break;
case 75: result=predicate75(coordinate,index,a,b,c,d,selected,value,flags);break;
case 76: result=predicate76(coordinate,index,a,b,c,d,selected,value,flags);break;
case 77: result=predicate77(coordinate,index,a,b,c,d,selected,value,flags);break;
case 78: result=predicate78(coordinate,index,a,b,c,d,selected,value,flags);break;
case 79: result=predicate79(coordinate,index,a,b,c,d,selected,value,flags);break;
case 80: result=predicate80(coordinate,index,a,b,c,d,selected,value,flags);break;
case 81: result=predicate81(coordinate,index,a,b,c,d,selected,value,flags);break;
case 82: result=predicate82(coordinate,index,a,b,c,d,selected,value,flags);break;
case 83: result=predicate83(coordinate,index,a,b,c,d,selected,value,flags);break;
case 84: result=predicate84(coordinate,index,a,b,c,d,selected,value,flags);break;
case 85: result=predicate85(coordinate,index,a,b,c,d,selected,value,flags);break;
case 86: result=predicate86(coordinate,index,a,b,c,d,selected,value,flags);break;
case 87: result=predicate87(coordinate,index,a,b,c,d,selected,value,flags);break;
case 88: result=predicate88(coordinate,index,a,b,c,d,selected,value,flags);break;
case 89: result=predicate89(coordinate,index,a,b,c,d,selected,value,flags);break;
case 90: result=predicate90(coordinate,index,a,b,c,d,selected,value,flags);break;
case 91: result=predicate91(coordinate,index,a,b,c,d,selected,value,flags);break;
case 92: result=predicate92(coordinate,index,a,b,c,d,selected,value,flags);break;
case 93: result=predicate93(coordinate,index,a,b,c,d,selected,value,flags);break;
case 94: result=predicate94(coordinate,index,a,b,c,d,selected,value,flags);break;
case 95: result=predicate95(coordinate,index,a,b,c,d,selected,value,flags);break;
case 96: result=predicate96(coordinate,index,a,b,c,d,selected,value,flags);break;
case 97: result=predicate97(coordinate,index,a,b,c,d,selected,value,flags);break;
case 98: result=predicate98(coordinate,index,a,b,c,d,selected,value,flags);break;
case 99: result=predicate99(coordinate,index,a,b,c,d,selected,value,flags);break;
case 100: result=predicate100(coordinate,index,a,b,c,d,selected,value,flags);break;
case 101: result=predicate101(coordinate,index,a,b,c,d,selected,value,flags);break;
case 102: result=predicate102(coordinate,index,a,b,c,d,selected,value,flags);break;
case 103: result=predicate103(coordinate,index,a,b,c,d,selected,value,flags);break;
case 104: result=predicate104(coordinate,index,a,b,c,d,selected,value,flags);break;
case 105: result=predicate105(coordinate,index,a,b,c,d,selected,value,flags);break;
case 106: result=predicate106(coordinate,index,a,b,c,d,selected,value,flags);break;
case 107: result=predicate107(coordinate,index,a,b,c,d,selected,value,flags);break;
case 108: result=predicate108(coordinate,index,a,b,c,d,selected,value,flags);break;
case 109: result=predicate109(coordinate,index,a,b,c,d,selected,value,flags);break;
case 110: result=predicate110(coordinate,index,a,b,c,d,selected,value,flags);break;
case 111: result=predicate111(coordinate,index,a,b,c,d,selected,value,flags);break;
case 112: result=predicate112(coordinate,index,a,b,c,d,selected,value,flags);break;
case 113: result=predicate113(coordinate,index,a,b,c,d,selected,value,flags);break;
case 114: result=predicate114(coordinate,index,a,b,c,d,selected,value,flags);break;
case 115: result=predicate115(coordinate,index,a,b,c,d,selected,value,flags);break;
case 116: result=predicate116(coordinate,index,a,b,c,d,selected,value,flags);break;
case 117: result=predicate117(coordinate,index,a,b,c,d,selected,value,flags);break;
case 118: result=predicate118(coordinate,index,a,b,c,d,selected,value,flags);break;
case 119: result=predicate119(coordinate,index,a,b,c,d,selected,value,flags);break;
case 120: result=predicate120(coordinate,index,a,b,c,d,selected,value,flags);break;
case 121: result=predicate121(coordinate,index,a,b,c,d,selected,value,flags);break;
case 122: result=predicate122(coordinate,index,a,b,c,d,selected,value,flags);break;
case 123: result=predicate123(coordinate,index,a,b,c,d,selected,value,flags);break;
case 124: result=predicate124(coordinate,index,a,b,c,d,selected,value,flags);break;
case 125: result=predicate125(coordinate,index,a,b,c,d,selected,value,flags);break;
case 126: result=predicate126(coordinate,index,a,b,c,d,selected,value,flags);break;
case 127: result=predicate127(coordinate,index,a,b,c,d,selected,value,flags);break;
case 128: result=predicate128(coordinate,index,a,b,c,d,selected,value,flags);break;
case 129: result=predicate129(coordinate,index,a,b,c,d,selected,value,flags);break;
case 130: result=predicate130(coordinate,index,a,b,c,d,selected,value,flags);break;
case 131: result=predicate131(coordinate,index,a,b,c,d,selected,value,flags);break;
case 132: result=predicate132(coordinate,index,a,b,c,d,selected,value,flags);break;
case 133: result=predicate133(coordinate,index,a,b,c,d,selected,value,flags);break;
case 134: result=predicate134(coordinate,index,a,b,c,d,selected,value,flags);break;
case 135: result=predicate135(coordinate,index,a,b,c,d,selected,value,flags);break;
case 136: result=predicate136(coordinate,index,a,b,c,d,selected,value,flags);break;
case 137: result=predicate137(coordinate,index,a,b,c,d,selected,value,flags);break;
case 138: result=predicate138(coordinate,index,a,b,c,d,selected,value,flags);break;
case 139: result=predicate139(coordinate,index,a,b,c,d,selected,value,flags);break;
case 140: result=predicate140(coordinate,index,a,b,c,d,selected,value,flags);break;
case 141: result=predicate141(coordinate,index,a,b,c,d,selected,value,flags);break;
case 142: result=predicate142(coordinate,index,a,b,c,d,selected,value,flags);break;
case 143: result=predicate143(coordinate,index,a,b,c,d,selected,value,flags);break;
case 144: result=predicate144(coordinate,index,a,b,c,d,selected,value,flags);break;
case 145: result=predicate145(coordinate,index,a,b,c,d,selected,value,flags);break;
case 146: result=predicate146(coordinate,index,a,b,c,d,selected,value,flags);break;
case 147: result=predicate147(coordinate,index,a,b,c,d,selected,value,flags);break;
case 148: result=predicate148(coordinate,index,a,b,c,d,selected,value,flags);break;
case 149: result=predicate149(coordinate,index,a,b,c,d,selected,value,flags);break;
case 150: result=predicate150(coordinate,index,a,b,c,d,selected,value,flags);break;
case 151: result=predicate151(coordinate,index,a,b,c,d,selected,value,flags);break;
case 152: result=predicate152(coordinate,index,a,b,c,d,selected,value,flags);break;
case 153: result=predicate153(coordinate,index,a,b,c,d,selected,value,flags);break;
case 154: result=predicate154(coordinate,index,a,b,c,d,selected,value,flags);break;
case 155: result=predicate155(coordinate,index,a,b,c,d,selected,value,flags);break;
case 156: result=predicate156(coordinate,index,a,b,c,d,selected,value,flags);break;
case 157: result=predicate157(coordinate,index,a,b,c,d,selected,value,flags);break;
case 158: result=predicate158(coordinate,index,a,b,c,d,selected,value,flags);break;
case 159: result=predicate159(coordinate,index,a,b,c,d,selected,value,flags);break;
case 160: result=predicate160(coordinate,index,a,b,c,d,selected,value,flags);break;
case 161: result=predicate161(coordinate,index,a,b,c,d,selected,value,flags);break;
case 162: result=predicate162(coordinate,index,a,b,c,d,selected,value,flags);break;
case 163: result=predicate163(coordinate,index,a,b,c,d,selected,value,flags);break;
case 164: result=predicate164(coordinate,index,a,b,c,d,selected,value,flags);break;
case 165: result=predicate165(coordinate,index,a,b,c,d,selected,value,flags);break;
case 166: result=predicate166(coordinate,index,a,b,c,d,selected,value,flags);break;
case 167: result=predicate167(coordinate,index,a,b,c,d,selected,value,flags);break;
case 168: result=predicate168(coordinate,index,a,b,c,d,selected,value,flags);break;
case 169: result=predicate169(coordinate,index,a,b,c,d,selected,value,flags);break;
case 170: result=predicate170(coordinate,index,a,b,c,d,selected,value,flags);break;
case 171: result=predicate171(coordinate,index,a,b,c,d,selected,value,flags);break;
case 172: result=predicate172(coordinate,index,a,b,c,d,selected,value,flags);break;
case 173: result=predicate173(coordinate,index,a,b,c,d,selected,value,flags);break;
case 174: result=predicate174(coordinate,index,a,b,c,d,selected,value,flags);break;
case 175: result=predicate175(coordinate,index,a,b,c,d,selected,value,flags);break;
case 176: result=predicate176(coordinate,index,a,b,c,d,selected,value,flags);break;
case 177: result=predicate177(coordinate,index,a,b,c,d,selected,value,flags);break;
case 178: result=predicate178(coordinate,index,a,b,c,d,selected,value,flags);break;
case 179: result=predicate179(coordinate,index,a,b,c,d,selected,value,flags);break;
case 180: result=predicate180(coordinate,index,a,b,c,d,selected,value,flags);break;
case 181: result=predicate181(coordinate,index,a,b,c,d,selected,value,flags);break;
case 182: result=predicate182(coordinate,index,a,b,c,d,selected,value,flags);break;
case 183: result=predicate183(coordinate,index,a,b,c,d,selected,value,flags);break;
case 184: result=predicate184(coordinate,index,a,b,c,d,selected,value,flags);break;
case 185: result=predicate185(coordinate,index,a,b,c,d,selected,value,flags);break;
}
return result;
}
#endif
/// Validates the entire dispatched domain or independently completes histogram validation.
void main() {
    if(mode==11||mode==12){statistics();return;}
    uint index=gl_GlobalInvocationID.x+gl_GlobalInvocationID.y*gl_NumWorkGroups.x*gl_WorkGroupSize.x;
    uint total=uint(extent.x*extent.y*extent.z);
    if(mode==4) {
        uint ranks=uint(rankSide*rankSide);
        if(index>=ranks*uint(rankSlices))return;
        if(index==0u)imageStore(resultImage,ivec2(12,0),uvec4(0x48495354u));
        imageAtomicAdd(resultImage,ivec2(11,0),1u);
        if(imageLoad(histogram,ivec2(int(index%ranks),int(index/ranks))).r!=1u){
            imageAtomicAdd(resultImage,ivec2(2,0),1u);
            // A missing rank has no source texel; report its rank/slice instead of inventing coordinates.
            imageAtomicMin(resultImage,ivec2(10,0),index);
        }
        return;
    }
    if(index>=total)return;
    if(index==0u)imageStore(resultImage,ivec2(0,0),uvec4(0x47505541u));
    imageAtomicAdd(resultImage,ivec2(1,0),1u);
    if(index%uint(pixelStride)!=uint(pixelOffset))return;
    ivec3 p=ivec3(int(index)%extent.x,(int(index)/extent.x)%extent.y,int(index)/(extent.x*extent.y));
    #if KIND == 0 && AUTHORED_PROGRAM
    if(mode==21) {
        bvec2 result=authored(p,index,texelFetch(actualImage,p,0),
            texelFetch(historyImage,min(p,textureSize(historyImage,0)-1),0),
            texelFetch(additionalImage,min(p,textureSize(additionalImage,0)-1),0),
            texelFetch(fourthImage,min(p,textureSize(fourthImage,0)-1),0));
        if(!result.x)fail(index*4u,2);
        if(result.y){imageAtomicAdd(resultImage,ivec2(14,0),1u);imageAtomicMin(resultImage,ivec2(10,0),index);}
        return;
    }
    #endif
    uvec4 value=fetchBits(p,false);
    uvec4 expected=(mode==2||mode==6||mode==7||mode==19||mode==20)?fetchBits(p,true):(mode==1||mode==8||mode==10||mode==16)?texelFetch(expectedImage,p,0):uvec4(constantValue);
    uvec4 doubleLow=uvec4(0),doubleHigh=uvec4(0);
    if(mode==15){doubleLow=texelFetch(expectedImage,ivec3(p.xy,p.z*2),0);doubleHigh=texelFetch(expectedImage,ivec3(p.xy,p.z*2+1),0);}
    uvec4 upperLow=uvec4(0),upperHigh=uvec4(0);
    if(mode==17) {
        doubleLow=texelFetch(expectedImage,ivec3(p.xy,p.z*4),0);doubleHigh=texelFetch(expectedImage,ivec3(p.xy,p.z*4+1),0);
        upperLow=texelFetch(expectedImage,ivec3(p.xy,p.z*4+2),0);upperHigh=texelFetch(expectedImage,ivec3(p.xy,p.z*4+3),0);
    }
    bool matching=constantValue!=0u;
#if KIND == 0
    if(mode==10) {
        // Preserve the authored float addition grouping; this is generic arithmetic, not a rendering oracle.
        precise vec4 pair=texelFetch(historyImage,ivec3(p.xy,1),0)+texelFetch(historyImage,ivec3(p.xy,2),0);
        precise vec4 base=texelFetch(historyImage,ivec3(p.xy,0),0)+pair;
        expected=floatBitsToUint(base+uintBitsToFloat(expected));
    }
    if(mode==13)expected=floatBitsToUint(uintBitsToFloat(value[referenceChannel])*multipliers);
#endif
    for(uint c=0u;c<4u;c++) {
        if((channels&(1u<<c))==0u)continue;
        imageAtomicAdd(resultImage,ivec2(15,0),1u);
        uint scalar=index*4u+c,bits=value[c];
    #if KIND == 0
        if(mode==18) {
            bool accepted=finiteBits(bits)&&number(bits)>=number(channelMinimum[c])&&number(bits)<=number(channelMaximum[c]);
            matching=constantValue!=0u?matching&&accepted:matching||accepted;
        }
        if(mode==19) {
            precise float scaled=uintBitsToFloat(expected[c])*multipliers.x;
            precise float base=scaled/multipliers.w;
            precise float lower=base+float(bounds.x),upper=base+float(bounds.y);
            float n=uintBitsToFloat(bits);
            if(!(n>=lower&&n<=upper))fail(scalar,2);
        }
    #endif
        if(!finiteBits(bits))fail(scalar,3);
        else {
            imageAtomicMin(resultImage,ivec2(7,0),ordered(bits));
            imageAtomicMax(resultImage,ivec2(8,0),ordered(bits));
            double n=number(bits);
            if(n>0.0)imageAtomicAdd(resultImage,ivec2(5,0),1u);
            if(n!=0.0)imageAtomicAdd(resultImage,ivec2(13,0),1u);
            bool clearCoverage=mode==14 && number(value[referenceChannel])==0.0;
            if(clearCoverage ? n!=0.0 : n<bounds.x || n>bounds.y)fail(scalar,4);
        }
        if((mode==1||mode==2||mode==5||mode==10||mode==13)&&!equalBits(bits,expected[c]))fail(scalar,2);
        if(mode==15&&(!finiteBits(bits)||abs(number(bits)-packDouble2x32(uvec2(doubleLow[c],doubleHigh[c])))>tolerance))fail(scalar,2);
        if(mode==17&&(!finiteBits(bits)||number(bits)<packDouble2x32(uvec2(doubleLow[c],doubleHigh[c]))||
            number(bits)>packDouble2x32(uvec2(upperLow[c],upperHigh[c]))))fail(scalar,2);
        if(mode==16&&(expected.a&(1u<<c))!=0u) {
            imageAtomicAdd(resultImage,ivec2(14,0),1u);
            if(!equalBits(bits,expected[c]))fail(scalar,2);
        }
        if(mode==5&&equalBits(bits,expected[c]))imageAtomicAdd(resultImage,ivec2(14,0),1u);
        if(mode==6&&bits!=expected[c])fail(scalar,2);
        if(mode==7&&number(expected[c])!=0.0&&!equalBits(bits,expected[c]))fail(scalar,2);
        if(mode==9&&(bits&constantValue)!=0u)imageAtomicAdd(resultImage,ivec2(14,0),1u);
    }
    if(mode==18&&matching)imageAtomicAdd(resultImage,ivec2(14,0),1u);
#if KIND == 0
    if(mode==20) {
        precise float scaled=uintBitsToFloat(expected.r)*multipliers.x;
        precise float base=scaled/multipliers.w;
        precise float limit=base+multipliers.y;
        if(uintBitsToFloat(value.r)>limit&&texelFetch(additionalImage,p,0).r>multipliers.z)
            imageAtomicAdd(resultImage,ivec2(14,0),1u);
    }
#endif
    if(mode==8) {
        vec3 difference=uintBitsToFloat(value.xyz)-uintBitsToFloat(expected.xyz);
        if(!(length(difference)<=float(tolerance)))fail(index*4u,2);
    }
    if(mode==3) {
        uint ranks=uint(rankSide*rankSide),slice=index/ranks;
        double n=number(value.r);
        if(!finiteBits(value.r)||n<0.0||n>=1.0)fail(index*4u,2);
        else {
            uint rank=uint(n*double(ranks));
            if(!equalBits(value.r,floatBitsToUint((float(rank)+0.5)/float(ranks))))fail(index*4u,2);
            imageAtomicAdd(histogram,ivec2(int(rank),int(slice)),1u);
        }
        if(slice>0u) {
            ivec3 first=ivec3(int(index%ranks)%rankSide,int(index%ranks)/rankSide,0);
            if(value.r!=fetchBits(first,false).r)imageAtomicOr(resultImage,ivec2(9,0),1u<<slice);
        }
    }
}
shared double partialMoments[512];
/// Reads one finite selected component with explicit scalar accumulation.
double selectedMoment(uint bits,uint channel) {
    return (channels&(1u<<channel))!=0u&&finiteBits(bits)?number(bits):0.0;
}
/// Reads one independently stored double partial from its integer representation.
double loadMoment(uint group,int channel,int offset) {
    int address=int(group)*16+channel*2+offset;
    return packDouble2x32(uvec2(imageLoad(histogram,ivec2(address,0)).r,imageLoad(histogram,ivec2(address+1,0)).r));
}
/// Writes one explicit vector lane using explicitly indexed scalar moments.
void storeMoment(uint group,int channel,double sum,double square) {
    uvec2 s=unpackDouble2x32(sum),q=unpackDouble2x32(square);
    int address=mode==11?int(group)*16+channel*2:16+channel*2;
    if(mode==11) {
        imageStore(histogram,ivec2(address,0),uvec4(s.x));imageStore(histogram,ivec2(address+1,0),uvec4(s.y));
        imageStore(histogram,ivec2(address+8,0),uvec4(q.x));imageStore(histogram,ivec2(address+9,0),uvec4(q.y));
    } else {
        imageStore(resultImage,ivec2(address,0),uvec4(s.x));imageStore(resultImage,ivec2(address+1,0),uvec4(s.y));
        imageStore(resultImage,ivec2(address+8,0),uvec4(q.x));imageStore(resultImage,ivec2(address+9,0),uvec4(q.y));
    }
}
/// Reduces every selected component with deterministic double accumulation, returning only four sums and moments.
void statistics() {
    uint local=gl_LocalInvocationID.x,group=gl_WorkGroupID.x;
    double moments[8];
    for(int c=0;c<8;c++)moments[c]=0.0;
    if(mode==11) {
        uint total=uint(extent.x*extent.y*extent.z);
        for(uint index=gl_GlobalInvocationID.x;index<total;index+=gl_NumWorkGroups.x*64u) {
            ivec3 p=ivec3(int(index)%extent.x,(int(index)/extent.x)%extent.y,int(index)/(extent.x*extent.y));
            uvec4 bits=fetchBits(p,false);
        #if KIND == 0 && STATISTIC_EXPRESSION == 1
            {
                // This is the independently authored equation A*h-b, never a Jacobi update or production residual executable.
                precise float neighbors=texelFetch(actualImage,ivec3((p.x+extent.x-1)%extent.x,p.y,0),0).r
                    +texelFetch(actualImage,ivec3((p.x+1)%extent.x,p.y,0),0).r;
                neighbors=neighbors+texelFetch(actualImage,ivec3(p.x,(p.y+extent.y-1)%extent.y,0),0).r;
                neighbors=neighbors+texelFetch(actualImage,ivec3(p.x,(p.y+1)%extent.y,0),0).r;
                precise float center=4.0*uintBitsToFloat(bits.r);
                precise float laplacian=neighbors-center;
                precise float residual=laplacian-uintBitsToFloat(texelFetch(expectedImage,p,0).r);
                bits=floatBitsToUint(vec4(residual,0,0,0));
                if(!finiteBits(bits.r))fail(index*4u,3);
            }
        #endif
            for(int c=0;c<4;c++) {
                double value=selectedMoment(bits[c],uint(c));
            #if STATISTIC_EXPRESSION == 2
                value=abs(value);
            #elif KIND == 0 && STATISTIC_EXPRESSION == 3
                uvec4 weight=texelFetch(expectedImage,p,0);
                double first=value*packDouble2x32(weight.rg);
                value=first*packDouble2x32(weight.ba);
            #endif
                moments[c]+=value;
            #if KIND == 0 && STATISTIC_EXPRESSION == 1
                precise float square=float(value)*float(value);moments[c+4]+=double(square);
            #else
                moments[c+4]+=value*value;
            #endif
            }
        }
    } else {
        for(uint index=local;index<256u;index+=64u)for(int c=0;c<4;c++) {
            moments[c]+=loadMoment(index,c,0);
            moments[c+4]+=loadMoment(index,c,8);
        }
    }
    for(int c=0;c<8;c++)partialMoments[c*64+int(local)]=moments[c];
    barrier();
    for(uint step=32u;step>0u;step>>=1u) {
        if(local<step)for(int c=0;c<8;c++)
            partialMoments[c*64+int(local)]+=partialMoments[c*64+int(local+step)];
        barrier();
    }
    if(local!=0u)return;
    for(int c=0;c<4;c++)storeMoment(group,c,partialMoments[c*64],partialMoments[(c+4)*64]);
    if(mode==11)imageAtomicAdd(resultImage,ivec2(11,0),1u);
    else {imageStore(resultImage,ivec2(12,0),uvec4(0x53554d53u));imageAtomicAdd(resultImage,ivec2(14,0),1u);}
}
