/// Preserves .NET numeric equality for selected lanes, including subnormal metadata, signed zero and NaN.
bool same(vec4 a,vec4 b,uint mask) {
    uvec4 first=floatBitsToUint(a),second=floatBitsToUint(b);
    for(int channel=0;channel<4;channel++)if((mask&(1u<<channel))!=0u && !equalBits(first[channel],second[channel]))return false;
    return true;
}
/// Evaluates an independently authored domain contract.
bvec2 predicate8(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,equalBits(floatBitsToUint(a[int(multipliers.y)]),floatBitsToUint(multipliers.x)));

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate9(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.r==multipliers.x);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate47(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(value.r==1,true);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate48(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate49(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(bvec4(b.r==a.r*2.0,c.r==a.r,d.r==a.r,b.g==1.0)), index%2u==0u);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate50(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(texelFetch(historyImage,ivec3(0),0).r==7.0,b.r==7.0);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate51(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,index==194u);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate52(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(same(a,b,3u),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate180(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
uvec4 actual=floatBitsToUint(a),expected=texelFetch(expectedImage,coordinate,0);
bool valid=true;
uint compared=0u;
for(int channel=0;channel<4;channel++)if((channels&(1u<<channel))!=0u) { imageAtomicAdd(resultImage,ivec2(15,0),1u); compared++; valid=valid && actual[channel]==expected[channel]; }
imageAtomicAdd(resultImage,ivec2(19,0),compared);
return bvec2(valid,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate181(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
vec4 expected=uintBitsToFloat(texelFetch(expectedImage,ivec3(coordinate.xy,coordinate.z*2),0));
vec4 allowed=uintBitsToFloat(texelFetch(expectedImage,ivec3(coordinate.xy,coordinate.z*2+1),0));
precise vec4 difference=abs(a-expected);
bool valid=true;
uint compared=0u;
for(int channel=0;channel<4;channel++)if((channels&(1u<<channel))!=0u)
    { imageAtomicAdd(resultImage,ivec2(15,0),1u); compared++; valid=valid && difference[channel]<=allowed[channel]; }
imageAtomicAdd(resultImage,ivec2(19,0),compared);
return bvec2(valid,false);

}
}
/// Selects a declared contract within this numerical domain.
bvec2 authored(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d) {
    // Decode only trace observations; ordinary bit predicates must not perform arithmetic on metadata.
if(evaluation.w>=2.0){a.rgb*=evaluation.x;if(evaluation.w==2.0)c.rgb*=evaluation.y;else d.rgb*=evaluation.z;}uint flags=floatBitsToUint(b.g);
ivec3 selected=ivec3(0);vec4 value=a;if(evaluation.w==1.0){selected=ivec3(a.xyz);value=texelFetch(historyImage,selected,0);}
switch(predicateChoice){
case 8: return predicate8(coordinate,index,a,b,c,d,selected,value,flags);
case 9: return predicate9(coordinate,index,a,b,c,d,selected,value,flags);
case 47: return predicate47(coordinate,index,a,b,c,d,selected,value,flags);
case 48: return predicate48(coordinate,index,a,b,c,d,selected,value,flags);
case 49: return predicate49(coordinate,index,a,b,c,d,selected,value,flags);
case 50: return predicate50(coordinate,index,a,b,c,d,selected,value,flags);
case 51: return predicate51(coordinate,index,a,b,c,d,selected,value,flags);
case 52: return predicate52(coordinate,index,a,b,c,d,selected,value,flags);
case 180: return predicate180(coordinate,index,a,b,c,d,selected,value,flags);
case 181: return predicate181(coordinate,index,a,b,c,d,selected,value,flags);
}
return bvec2(false,false);
}
