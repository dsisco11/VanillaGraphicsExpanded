/// Preserves .NET numeric equality for selected lanes, including subnormal metadata, signed zero and NaN.
bool same(vec4 a,vec4 b,uint mask) {
    uvec4 first=floatBitsToUint(a),second=floatBitsToUint(b);
    for(int channel=0;channel<4;channel++)if((mask&(1u<<channel))!=0u && !equalBits(first[channel],second[channel]))return false;
    return true;
}
/// Evaluates an independently authored domain contract.
bvec2 predicate40(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.r!=0);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate41(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.b>.1);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate42(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,any(greaterThan(a,b+vec4(.001))));

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate43(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise vec4 difference=abs(a-b);
precise vec4 allowed=vec4(.001)+.002*abs(b);
bool finite=!any(isnan(a)) && !any(isinf(a)) && !any(isnan(b)) && !any(isinf(b));
return bvec2(finite && all(lessThanEqual(difference,allowed)) && (multipliers.x==0 || (all(equal(a,vec4(0))) && all(equal(b,vec4(0))))),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate44(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise float difference=abs(a.r-multipliers.x);return bvec2(difference<=.004,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate53(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
float horizontal=texelFetch(actualImage,ivec3(64-coordinate.x,coordinate.y,0),0).r;
float vertical=texelFetch(actualImage,ivec3(coordinate.x,32-coordinate.y,0),0).r;
return bvec2(a.r>=0 && a.r<=8 && horizontal>=a.r-.0001 && horizontal<=a.r+.0001
    && vertical>=a.r-.0001 && vertical<=a.r+.0001,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate136(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r>=3.99999 && a.r<=4.00001 && a.g>=1.99999 && a.g<=2.00001 && a.b>=.99999 && a.b<=1.00001,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate137(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=coordinate.y==8 && coordinate.x>=int(multipliers.x)+17 && coordinate.x<=26;
float previous=texelFetch(actualImage,ivec3(max(coordinate.x-1,0),coordinate.y,0),0).r;
return bvec2(!selected || a.r<=previous+.00001,selected);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate138(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r>=1.99 && a.r<=2.01 && a.g>=.995 && a.g<=1.005 && a.b>=.4975 && a.b<=.5025,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate139(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
float horizontal=texelFetch(actualImage,ivec3(128-coordinate.x,coordinate.y,0),0).r;
float vertical=texelFetch(actualImage,ivec3(coordinate.x,64-coordinate.y,0),0).r;
bool valid=!isnan(a.r) && !isinf(a.r) && a.r>=0 && horizontal>=a.r-.0002 && horizontal<=a.r+.0002 && vertical>=a.r-.0002 && vertical<=a.r+.0002;
return bvec2(valid,a.r>.00001);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate151(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise vec2 recovered=a.rg*65535;return bvec2(all(equal(roundEven(recovered),b.rg)),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate152(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r>.1,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate153(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(greaterThanEqual(a,multipliers-vec4(.001))) && all(lessThanEqual(a,multipliers+vec4(.001))),false);

}
}
/// Evaluates an independently authored domain contract.
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
/// Evaluates an independently authored domain contract.
bvec2 predicate155(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.b!=0,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate156(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r>=multipliers.x && a.r<=multipliers.y && a.a==2,false);

}
}
/// Selects a declared contract within this numerical domain.
bvec2 authored(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d) {
    // Decode only trace observations; ordinary bit predicates must not perform arithmetic on metadata.
if(evaluation.w>=2.0){a.rgb*=evaluation.x;if(evaluation.w==2.0)c.rgb*=evaluation.y;else d.rgb*=evaluation.z;}uint flags=floatBitsToUint(b.g);
ivec3 selected=ivec3(0);vec4 value=a;if(evaluation.w==1.0){selected=ivec3(a.xyz);value=texelFetch(historyImage,selected,0);}
switch(predicateChoice){
case 40: return predicate40(coordinate,index,a,b,c,d,selected,value,flags);
case 41: return predicate41(coordinate,index,a,b,c,d,selected,value,flags);
case 42: return predicate42(coordinate,index,a,b,c,d,selected,value,flags);
case 43: return predicate43(coordinate,index,a,b,c,d,selected,value,flags);
case 44: return predicate44(coordinate,index,a,b,c,d,selected,value,flags);
case 53: return predicate53(coordinate,index,a,b,c,d,selected,value,flags);
case 136: return predicate136(coordinate,index,a,b,c,d,selected,value,flags);
case 137: return predicate137(coordinate,index,a,b,c,d,selected,value,flags);
case 138: return predicate138(coordinate,index,a,b,c,d,selected,value,flags);
case 139: return predicate139(coordinate,index,a,b,c,d,selected,value,flags);
case 151: return predicate151(coordinate,index,a,b,c,d,selected,value,flags);
case 152: return predicate152(coordinate,index,a,b,c,d,selected,value,flags);
case 153: return predicate153(coordinate,index,a,b,c,d,selected,value,flags);
case 154: return predicate154(coordinate,index,a,b,c,d,selected,value,flags);
case 155: return predicate155(coordinate,index,a,b,c,d,selected,value,flags);
case 156: return predicate156(coordinate,index,a,b,c,d,selected,value,flags);
}
return bvec2(false,false);
}
