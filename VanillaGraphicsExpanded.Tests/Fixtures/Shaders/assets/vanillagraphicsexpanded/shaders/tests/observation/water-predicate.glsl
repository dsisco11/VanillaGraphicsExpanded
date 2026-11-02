/// Preserves .NET numeric equality for selected lanes, including subnormal metadata, signed zero and NaN.
bool same(vec4 a,vec4 b,uint mask) {
    uvec4 first=floatBitsToUint(a),second=floatBitsToUint(b);
    for(int channel=0;channel<4;channel++)if((mask&(1u<<channel))!=0u && !equalBits(first[channel],second[channel]))return false;
    return true;
}
/// Evaluates an independently authored domain contract.
bvec2 predicate54(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool eligible=all(greaterThanEqual(coordinate.xy,ivec2(64)))&&all(lessThan(coordinate.xy,ivec2(192)))&&!(a.a<=0||b.a<=0);return bvec2(!eligible||!isnan(abs(a.r/a.a-b.r/b.a)),eligible);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate55(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool eligible=all(greaterThanEqual(coordinate.xy,ivec2(64)))&&all(lessThan(coordinate.xy,ivec2(192)))&&!(a.a<=0||b.a<=0);return bvec2(true,eligible&&abs(a.r/a.a-b.r/b.a)>.01);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate176(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool valid=coordinate.y!=0||coordinate.x==0||all(lessThanEqual(a.rgb,texelFetch(actualImage,coordinate-ivec3(1,0,0),0).rgb));return bvec2(valid,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate177(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=(coordinate.y==13||coordinate.y==14)&&(coordinate.x==0||coordinate.x==4);return bvec2(!selected||a.r!=multipliers.x,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate178(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool complete=d.a==1 || multipliers.x==1;
precise vec2 difference=abs(c.gb-d.gb);
bool valid=complete?(same(a,b,15u) && same(c,d,9u) && all(lessThanEqual(difference,vec2(.000001)))):(a.a==0 && c.a==0);
return bvec2(valid,false);

}
}
/// Selects a declared contract within this numerical domain.
bvec2 authored(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d) {
    // Decode only trace observations; ordinary bit predicates must not perform arithmetic on metadata.
if(evaluation.w>=2.0){a.rgb*=evaluation.x;if(evaluation.w==2.0)c.rgb*=evaluation.y;else d.rgb*=evaluation.z;}uint flags=floatBitsToUint(b.g);
ivec3 selected=ivec3(0);vec4 value=a;if(evaluation.w==1.0){selected=ivec3(a.xyz);value=texelFetch(historyImage,selected,0);}
switch(predicateChoice){
case 54: return predicate54(coordinate,index,a,b,c,d,selected,value,flags);
case 55: return predicate55(coordinate,index,a,b,c,d,selected,value,flags);
case 176: return predicate176(coordinate,index,a,b,c,d,selected,value,flags);
case 177: return predicate177(coordinate,index,a,b,c,d,selected,value,flags);
case 178: return predicate178(coordinate,index,a,b,c,d,selected,value,flags);
}
return bvec2(false,false);
}
