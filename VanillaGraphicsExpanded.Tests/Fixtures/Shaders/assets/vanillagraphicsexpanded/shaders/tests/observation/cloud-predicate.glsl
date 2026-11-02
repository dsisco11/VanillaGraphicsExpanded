/// Preserves .NET numeric equality for selected lanes, including subnormal metadata, signed zero and NaN.
bool same(vec4 a,vec4 b,uint mask) {
    uvec4 first=floatBitsToUint(a),second=floatBitsToUint(b);
    for(int channel=0;channel<4;channel++)if((mask&(1u<<channel))!=0u && !equalBits(first[channel],second[channel]))return false;
    return true;
}
/// Evaluates an independently authored domain contract.
bvec2 predicate13(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool fresh=a.g==1.0;
vec4 raw=texelFetch(historyImage,ivec3(coordinate.xy/2,0),0);
bool valid=raw.g>=1.0 && roundEven(double(raw.r)*1e5)==roundEven(double(c.r)*1e5);
if(d.r>=0.0)valid=valid && multipliers.x-d.r>=1.0 && multipliers.x-d.r<=7.0;
return bvec2(!fresh || valid,fresh);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate14(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(coordinate.x!=0 || a.r>texelFetch(actualImage,ivec3(4,coordinate.y,0),0).r,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate15(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.g>=1.0);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate16(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.a>1.0);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate17(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool carried=a.g==2.0;
bool valid=a.r==1.0 && b.a==4.0 && a.a==1.0 && all(lessThanEqual(abs(c-d),vec4(2e-5)));
return bvec2(!carried || valid,carried);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate18(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool fresh=a.g==1.0;
bool valid=a.a==0.0 && abs(b.a-1.0/(1.0-a.r))<=0.02 && (a.r!=0.0 || b.a==1.0);
return bvec2(!fresh || valid,fresh);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate19(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(same(a,b,15u),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate20(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(same(a,b,7u),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate21(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r==b.r,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate22(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool fresh=a.g==1.0;
return bvec2(!fresh || (roundEven(double(b.r)*1e5)==120000.0 && roundEven(double(c.a)*1e5)==500000.0),fresh);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate23(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool carry=a.g==2.0;return bvec2(!carry || (b.r==1.0 && c.a==4.0),carry);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate24(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.g==2.0);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate25(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.b==8.0);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate26(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool carry=a.g==2.0;
return bvec2(!carry || (a.a==(index%2u==0u?1.0:7.0) && b.a==4.0),carry);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate27(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=coordinate.x==3 && coordinate.y>=1 && coordinate.y<7;
return bvec2(!selected || (a.b==12.0 && b.r==1.0 && b.a==0.5 && c.r==0.5 && d.a>=4.0),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate28(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
// Integer masks preserve the Boolean contract without short-circuit merge values.
uint nominated=uint(coordinate.x==3)&uint(coordinate.y>=1)&uint(coordinate.y<7)&uint(a.g==2.0);
uint accepted=(1u-nominated)|uint(b.r==3.0);
return bvec2(accepted!=0u,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate29(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.b==9.0);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate30(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.b!=9.0 || b.g==0.0,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate31(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=coordinate.x>=4 && coordinate.x<7 && coordinate.y>=1 && coordinate.y<7 && a.g==2.0 && b.g==1.0; bool valid=b.a==4.0 && all(equal(roundEven(dvec3(c.rgb)*1e5),dvec3(200000,300000,400000)))
    && roundEven(double(c.a)*1e5)==50000.0
    && roundEven(double(b.b)*1e4)==roundEven(double(d.r)*1e4);
return bvec2(!selected || valid,selected);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate32(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=coordinate.x>=4 && coordinate.x<7 && coordinate.y>=1 && coordinate.y<7 && a.g==2.0 && b.g==1.0; bool valid=d.r==1.0 && all(equal(roundEven(dvec3(c.rgb)*1e5),dvec3(20000,40000,60000)));
return bvec2(!selected || valid,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate33(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=coordinate.x>=4 && coordinate.x<7 && coordinate.y>=1 && coordinate.y<7 && a.g==2.0 && b.g==1.0; return bvec2(true,selected && coordinate.x%2==0);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate34(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=coordinate.x>=4 && coordinate.x<7 && coordinate.y>=1 && coordinate.y<7 && a.g==2.0 && b.g==1.0; return bvec2(true,selected && coordinate.x%2!=0);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate35(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=coordinate.x>=1 && coordinate.x<7 && coordinate.y>=1 && coordinate.y<7 && a.g==2.0 && a.b==0.0; return bvec2(!selected || (b.r==2.0 && b.a==0.5 && c.r==0.5),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate36(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=coordinate.x>=1 && coordinate.x<7 && coordinate.y>=1 && coordinate.y<7 && a.g==2.0 && a.b==0.0; return bvec2(!selected || abs(b.r-c.r)<=0.004,selected);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate37(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=coordinate.x>=1 && coordinate.x<7 && coordinate.y>=1 && coordinate.y<7 && a.g==2.0 && a.b==0.0; return bvec2(!selected || (roundEven(double(b.a)*1e4)==roundEven(double(c.g)*1e4)
    && b.a>=1.0 && b.a<=4.0),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate182(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool valid=abs(c.r-1.2)<=2e-6 && abs(d.a-5.0)<=2e-6 && a.b==0.0; bool selected=b.g==1.0;return bvec2(!selected || valid,selected);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate183(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool valid=roundEven(double(c.r)*1e5)==80000.0 && roundEven(double(d.a)*1e5)==500000.0 && a.b==0.0; bool selected=b.g==1.0;return bvec2(!selected || valid,selected);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate184(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool valid=a.r==0.0 && d.a==1.0 && a.b==11.0; bool selected=b.g==1.0;return bvec2(!selected || valid,selected);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate185(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool valid=d.g==2.0 && d.a==1.0 && c.a==0.0 && a.b==4.0; bool selected=b.g==1.0;return bvec2(!selected || valid,selected);

}
}
/// Selects a declared contract within this numerical domain.
bvec2 authored(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d) {
    // Decode only trace observations; ordinary bit predicates must not perform arithmetic on metadata.
if(evaluation.w>=2.0){a.rgb*=evaluation.x;if(evaluation.w==2.0)c.rgb*=evaluation.y;else d.rgb*=evaluation.z;}uint flags=floatBitsToUint(b.g);
ivec3 selected=ivec3(0);vec4 value=a;if(evaluation.w==1.0){selected=ivec3(a.xyz);value=texelFetch(historyImage,selected,0);}
switch(predicateChoice){
case 13: return predicate13(coordinate,index,a,b,c,d,selected,value,flags);
case 14: return predicate14(coordinate,index,a,b,c,d,selected,value,flags);
case 15: return predicate15(coordinate,index,a,b,c,d,selected,value,flags);
case 16: return predicate16(coordinate,index,a,b,c,d,selected,value,flags);
case 17: return predicate17(coordinate,index,a,b,c,d,selected,value,flags);
case 18: return predicate18(coordinate,index,a,b,c,d,selected,value,flags);
case 19: return predicate19(coordinate,index,a,b,c,d,selected,value,flags);
case 20: return predicate20(coordinate,index,a,b,c,d,selected,value,flags);
case 21: return predicate21(coordinate,index,a,b,c,d,selected,value,flags);
case 22: return predicate22(coordinate,index,a,b,c,d,selected,value,flags);
case 23: return predicate23(coordinate,index,a,b,c,d,selected,value,flags);
case 24: return predicate24(coordinate,index,a,b,c,d,selected,value,flags);
case 25: return predicate25(coordinate,index,a,b,c,d,selected,value,flags);
case 26: return predicate26(coordinate,index,a,b,c,d,selected,value,flags);
case 27: return predicate27(coordinate,index,a,b,c,d,selected,value,flags);
case 28: return predicate28(coordinate,index,a,b,c,d,selected,value,flags);
case 29: return predicate29(coordinate,index,a,b,c,d,selected,value,flags);
case 30: return predicate30(coordinate,index,a,b,c,d,selected,value,flags);
case 31: return predicate31(coordinate,index,a,b,c,d,selected,value,flags);
case 32: return predicate32(coordinate,index,a,b,c,d,selected,value,flags);
case 33: return predicate33(coordinate,index,a,b,c,d,selected,value,flags);
case 34: return predicate34(coordinate,index,a,b,c,d,selected,value,flags);
case 35: return predicate35(coordinate,index,a,b,c,d,selected,value,flags);
case 36: return predicate36(coordinate,index,a,b,c,d,selected,value,flags);
case 37: return predicate37(coordinate,index,a,b,c,d,selected,value,flags);
case 182: return predicate182(coordinate,index,a,b,c,d,selected,value,flags);
case 183: return predicate183(coordinate,index,a,b,c,d,selected,value,flags);
case 184: return predicate184(coordinate,index,a,b,c,d,selected,value,flags);
case 185: return predicate185(coordinate,index,a,b,c,d,selected,value,flags);
}
return bvec2(false,false);
}
