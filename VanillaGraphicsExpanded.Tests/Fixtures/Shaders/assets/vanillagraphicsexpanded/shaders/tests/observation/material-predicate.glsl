/// Preserves .NET numeric equality for selected lanes, including subnormal metadata, signed zero and NaN.
bool same(vec4 a,vec4 b,uint mask) {
    uvec4 first=floatBitsToUint(a),second=floatBitsToUint(b);
    for(int channel=0;channel<4;channel++)if((mask&(1u<<channel))!=0u && !equalBits(first[channel],second[channel]))return false;
    return true;
}
/// Evaluates an independently authored domain contract.
bvec2 predicate11(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r>=multipliers.x && a.r<=multipliers.y,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate38(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool monotonic=coordinate.x==0 || a.r+1.0/255.0>=texelFetch(actualImage,ivec3(coordinate.x-1,0,0),0).r;
return bvec2(monotonic && abs(a.g-a.r)<=multipliers.x && abs(a.b-a.r)<=multipliers.x,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate39(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,any(greaterThan(a.rgb,vec3(0))));

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate45(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,any(greaterThan(abs(a.rgb-b.rgb),vec3(.02))));

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate56(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r==-16 && a.g==(coordinate.x==1?1:0),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate57(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise float detail=a.b-a.g;return bvec2(abs(double(detail)-.25lf)<=.00001lf,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate130(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool inside=coordinate.x>=16 && coordinate.x<48 && coordinate.y>=16 && coordinate.y<48;
bool valid=inside ? (!any(isnan(a)) && !any(isinf(a))) : all(equal(a,multipliers));
return bvec2(valid,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate131(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool inside=coordinate.x>=16 && coordinate.x<48 && coordinate.y>=16 && coordinate.y<48;
return bvec2(true,inside && a.a>0.01 && a.a<0.99);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate132(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool inside=coordinate.x>=16 && coordinate.x<48 && coordinate.y>=16 && coordinate.y<48;
vec4 expected=inside?b:vec4(0.5,0.5,1.0,0.5);
return bvec2(all(lessThanEqual(abs(a-expected),vec4(0.001))),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate135(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r==(coordinate.x<2?.125:.25) && (coordinate.x<2 || b.r>0),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate140(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r==a.g&&a.r==a.b&&roundEven(a.a*255)==73,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate141(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,abs(a.a-b.a)>0.01);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate142(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,any(greaterThan(abs(a.rgb-b.rgb),vec3(0.001))));

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate143(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(lessThan(abs(a.rgb-vec3(0.5,0.5,1.0)),vec3(0.02))),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate144(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,!isnan(a.a) && !isinf(a.a) && a.a>0.999);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate145(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.a>.5);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate146(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise vec4 delta=abs(a-b);return bvec2(true,any(greaterThan(delta,vec4(.0001))));

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate147(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,any(greaterThan(a.rgb,vec3(multipliers.x))));

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate148(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise vec3 expected=(b.rgb+c.rgb)+d.rgb;precise vec3 delta=abs(a.rgb-expected);return bvec2(all(lessThanEqual(delta,vec3(multipliers.x))),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate149(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.a>0.999);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate150(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.a<0.001);

}
}
/// Selects a declared contract within this numerical domain.
bvec2 authored(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d) {
    // Decode only trace observations; ordinary bit predicates must not perform arithmetic on metadata.
if(evaluation.w>=2.0){a.rgb*=evaluation.x;if(evaluation.w==2.0)c.rgb*=evaluation.y;else d.rgb*=evaluation.z;}uint flags=floatBitsToUint(b.g);
ivec3 selected=ivec3(0);vec4 value=a;if(evaluation.w==1.0){selected=ivec3(a.xyz);value=texelFetch(historyImage,selected,0);}
switch(predicateChoice){
case 11: return predicate11(coordinate,index,a,b,c,d,selected,value,flags);
case 38: return predicate38(coordinate,index,a,b,c,d,selected,value,flags);
case 39: return predicate39(coordinate,index,a,b,c,d,selected,value,flags);
case 45: return predicate45(coordinate,index,a,b,c,d,selected,value,flags);
case 56: return predicate56(coordinate,index,a,b,c,d,selected,value,flags);
case 57: return predicate57(coordinate,index,a,b,c,d,selected,value,flags);
case 130: return predicate130(coordinate,index,a,b,c,d,selected,value,flags);
case 131: return predicate131(coordinate,index,a,b,c,d,selected,value,flags);
case 132: return predicate132(coordinate,index,a,b,c,d,selected,value,flags);
case 135: return predicate135(coordinate,index,a,b,c,d,selected,value,flags);
case 140: return predicate140(coordinate,index,a,b,c,d,selected,value,flags);
case 141: return predicate141(coordinate,index,a,b,c,d,selected,value,flags);
case 142: return predicate142(coordinate,index,a,b,c,d,selected,value,flags);
case 143: return predicate143(coordinate,index,a,b,c,d,selected,value,flags);
case 144: return predicate144(coordinate,index,a,b,c,d,selected,value,flags);
case 145: return predicate145(coordinate,index,a,b,c,d,selected,value,flags);
case 146: return predicate146(coordinate,index,a,b,c,d,selected,value,flags);
case 147: return predicate147(coordinate,index,a,b,c,d,selected,value,flags);
case 148: return predicate148(coordinate,index,a,b,c,d,selected,value,flags);
case 149: return predicate149(coordinate,index,a,b,c,d,selected,value,flags);
case 150: return predicate150(coordinate,index,a,b,c,d,selected,value,flags);
}
return bvec2(false,false);
}
