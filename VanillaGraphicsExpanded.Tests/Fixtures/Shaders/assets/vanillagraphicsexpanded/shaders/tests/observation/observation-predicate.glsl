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
return bvec2(historyAt(ivec3(0),0).r==7.0,b.r==7.0);

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
/// Selects a declared contract within this numerical domain.
bvec2 authored(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d) {
    // Decode only trace observations; ordinary bit predicates must not perform arithmetic on metadata.
if(evaluation.w>=2.0){a.rgb*=evaluation.x;if(evaluation.w==2.0)c.rgb*=evaluation.y;else d.rgb*=evaluation.z;}uint flags=floatBitsToUint(b.g);
ivec3 selected=ivec3(0);vec4 value=a;
#if POINT_SOURCE != 0
if(mode==26){selected=ivec3(expectedWord(index*3u),expectedWord(index*3u+1u),expectedWord(index*3u+2u));
#if POINT_SOURCE == 4
value=historyAt(ivec3(int(index)%historySize().x,int(index)/historySize().x,0),0);
#else
value=historyAt(selected,0);
#endif
}
else
#endif
if(evaluation.w==1.0){selected=ivec3(a.xyz);value=historyAt(selected,0);}
switch(predicateChoice){
case 187: return bvec2(roundEven(value.r*255.0)==float(selected.x),true);
case 8: return predicate8(coordinate,index,a,b,c,d,selected,value,flags);
case 9: return predicate9(coordinate,index,a,b,c,d,selected,value,flags);
case 47: return predicate47(coordinate,index,a,b,c,d,selected,value,flags);
case 48: return predicate48(coordinate,index,a,b,c,d,selected,value,flags);
case 49: return predicate49(coordinate,index,a,b,c,d,selected,value,flags);
case 50: return predicate50(coordinate,index,a,b,c,d,selected,value,flags);
case 51: return predicate51(coordinate,index,a,b,c,d,selected,value,flags);
case 52: return predicate52(coordinate,index,a,b,c,d,selected,value,flags);
}
return bvec2(false,false);
}
