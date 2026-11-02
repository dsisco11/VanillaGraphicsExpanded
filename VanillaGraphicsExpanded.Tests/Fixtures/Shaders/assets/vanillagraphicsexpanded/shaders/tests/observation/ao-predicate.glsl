/// Preserves .NET numeric equality for selected lanes, including subnormal metadata, signed zero and NaN.
bool same(vec4 a,vec4 b,uint mask) {
    uvec4 first=floatBitsToUint(a),second=floatBitsToUint(b);
    for(int channel=0;channel<4;channel++)if((mask&(1u<<channel))!=0u && !equalBits(first[channel],second[channel]))return false;
    return true;
}
/// Evaluates an independently authored domain contract.
bvec2 predicate1(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=b.r>multipliers.x;
return bvec2(!selected || (a.r>=0.995 && a.r<=1.001),false);

}
}
/// Evaluates an independently authored domain contract.
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
/// Evaluates an independently authored domain contract.
bvec2 predicate3(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(coordinate.x>=int(multipliers.x) || (a.r>=0.995 && a.r<=1.001),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate4(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise vec3 lower=b.rgb-vec3(0.00001),upper=b.rgb+vec3(0.00001),combinedUpper=d.rgb+vec3(0.0001);
bool valid=all(greaterThanEqual(a.rgb,lower)) && all(lessThanEqual(a.rgb,upper))
    && !any(isnan(c.rgb)) && !any(isinf(c.rgb))
    && all(greaterThanEqual(c.rgb,vec3(-0.00001))) && all(lessThanEqual(c.rgb,combinedUpper));
return bvec2(valid,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate5(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(coordinate.x!=30 || (a.r>=0.995 && a.r<=1.001),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate6(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(coordinate.x%4>=2 || a.r==1.0,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate7(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
ivec2 childSize=ivec2(multipliers.xy),parentSize=ivec2(multipliers.zw);
ivec2 first=coordinate.xy*childSize/parentSize;
ivec2 end=((coordinate.xy+1)*childSize+parentSize-1)/parentSize;
uint expected=0u;
// Keep each child's contribution explicit; excluded reads stay in bounds and contribute no bits.
for(int dy=0;dy<3;dy++)for(int dx=0;dx<3;dx++)
    {
        ivec2 child=first+ivec2(dx,dy);
        uint inside=uint(child.x<end.x)&uint(child.y<end.y);
        float depth=texelFetch(additionalImage,ivec3(min(child,childSize-1),0),0).r;
        expected|=(inside&uint(depth==b.r))<<uint(dy*3+dx);
    }
return bvec2(uint(a.r)==expected,false);

}
}
/// Selects a declared contract within this numerical domain.
bvec2 authored(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d) {
    // Decode only trace observations; ordinary bit predicates must not perform arithmetic on metadata.
if(evaluation.w>=2.0){a.rgb*=evaluation.x;if(evaluation.w==2.0)c.rgb*=evaluation.y;else d.rgb*=evaluation.z;}uint flags=floatBitsToUint(b.g);
ivec3 selected=ivec3(0);vec4 value=a;if(evaluation.w==1.0){selected=ivec3(a.xyz);value=texelFetch(historyImage,selected,0);}
switch(predicateChoice){
case 1: return predicate1(coordinate,index,a,b,c,d,selected,value,flags);
case 2: return predicate2(coordinate,index,a,b,c,d,selected,value,flags);
case 3: return predicate3(coordinate,index,a,b,c,d,selected,value,flags);
case 4: return predicate4(coordinate,index,a,b,c,d,selected,value,flags);
case 5: return predicate5(coordinate,index,a,b,c,d,selected,value,flags);
case 6: return predicate6(coordinate,index,a,b,c,d,selected,value,flags);
case 7: return predicate7(coordinate,index,a,b,c,d,selected,value,flags);
}
return bvec2(false,false);
}
