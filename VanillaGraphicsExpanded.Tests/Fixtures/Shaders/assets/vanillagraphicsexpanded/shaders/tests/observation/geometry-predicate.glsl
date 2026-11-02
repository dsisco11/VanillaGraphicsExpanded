/// Preserves .NET numeric equality for selected lanes, including subnormal metadata, signed zero and NaN.
bool same(vec4 a,vec4 b,uint mask) {
    uvec4 first=floatBitsToUint(a),second=floatBitsToUint(b);
    for(int channel=0;channel<4;channel++)if((mask&(1u<<channel))!=0u && !equalBits(first[channel],second[channel]))return false;
    return true;
}
/// Evaluates an independently authored domain contract.
bvec2 predicate133(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(greaterThanEqual(a.rgb,vec3(9.9))) && all(lessThanEqual(a.rgb,vec3(10.1)))
    && all(equal(c.rgb,vec3(0))) && b.r==1.0 && (flags&32u)!=0u,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate134(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2((flags&32u)==0u,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate173(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r>=-.04001 && a.r<=.04001 && !any(isnan(a.gba)) && !any(isinf(a.gba)) && a.a>=.5 && a.a<=1.001,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate174(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(!isnan(a.r) && !isinf(a.r) && abs(a.r)<=.031251 && a.b>=0 && a.b<=1,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate175(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(greaterThanEqual(a,b-vec4(.0001))) && all(lessThanEqual(a,b+vec4(.0001))),false);

}
}
/// Selects a declared contract within this numerical domain.
bvec2 authored(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d) {
    // Decode only trace observations; ordinary bit predicates must not perform arithmetic on metadata.
if(evaluation.w>=2.0){a.rgb*=evaluation.x;if(evaluation.w==2.0)c.rgb*=evaluation.y;else d.rgb*=evaluation.z;}uint flags=floatBitsToUint(b.g);
ivec3 selected=ivec3(0);vec4 value=a;if(evaluation.w==1.0){selected=ivec3(a.xyz);value=texelFetch(historyImage,selected,0);}
switch(predicateChoice){
case 133: return predicate133(coordinate,index,a,b,c,d,selected,value,flags);
case 134: return predicate134(coordinate,index,a,b,c,d,selected,value,flags);
case 173: return predicate173(coordinate,index,a,b,c,d,selected,value,flags);
case 174: return predicate174(coordinate,index,a,b,c,d,selected,value,flags);
case 175: return predicate175(coordinate,index,a,b,c,d,selected,value,flags);
}
return bvec2(false,false);
}
