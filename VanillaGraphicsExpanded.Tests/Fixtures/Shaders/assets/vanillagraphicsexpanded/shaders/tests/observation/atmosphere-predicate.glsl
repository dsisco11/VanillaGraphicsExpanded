/// Preserves .NET numeric equality for selected lanes, including subnormal metadata, signed zero and NaN.
bool same(vec4 a,vec4 b,uint mask) {
    uvec4 first=floatBitsToUint(a),second=floatBitsToUint(b);
    for(int channel=0;channel<4;channel++)if((mask&(1u<<channel))!=0u && !equalBits(first[channel],second[channel]))return false;
    return true;
}
/// Evaluates an independently authored domain contract.
bvec2 predicate10(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise float reconstructed=a.r+b.r*d.g;
reconstructed=reconstructed+c.r*d.b;
reconstructed=reconstructed/multipliers.x;
return bvec2(a.r>0.0 && abs(reconstructed-d.r)<=2e-6,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate12(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise vec3 value=a.rgb/multipliers.x;
precise vec3 error=abs(value/b.rgb-vec3(1));
bool valid=!any(isnan(value)) && !any(isinf(value)) && all(greaterThanEqual(value,vec3(0)))
    && all(lessThanEqual(value,vec3(multipliers.y))) && error.r<=0.0101 && error.g<=0.0101 && error.b<=0.0201;
return bvec2(valid,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate157(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise float green=a.r*0.8,blue=a.r*0.6;
return bvec2(a.g>=green-0.02 && a.g<=green+0.02 && a.b>=blue-0.02 && a.b<=blue+0.02,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate158(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise vec4 difference=abs(a-b);
return bvec2(all(lessThanEqual(difference,vec4(multipliers.x))),false);

}
}
/// Selects a declared contract within this numerical domain.
bvec2 authored(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d) {
    // Decode only trace observations; ordinary bit predicates must not perform arithmetic on metadata.
if(evaluation.w>=2.0){a.rgb*=evaluation.x;if(evaluation.w==2.0)c.rgb*=evaluation.y;else d.rgb*=evaluation.z;}uint flags=floatBitsToUint(b.g);
ivec3 selected=ivec3(0);vec4 value=a;if(evaluation.w==1.0){selected=ivec3(a.xyz);value=texelFetch(historyImage,selected,0);}
switch(predicateChoice){
case 10: return predicate10(coordinate,index,a,b,c,d,selected,value,flags);
case 12: return predicate12(coordinate,index,a,b,c,d,selected,value,flags);
case 157: return predicate157(coordinate,index,a,b,c,d,selected,value,flags);
case 158: return predicate158(coordinate,index,a,b,c,d,selected,value,flags);
}
return bvec2(false,false);
}
