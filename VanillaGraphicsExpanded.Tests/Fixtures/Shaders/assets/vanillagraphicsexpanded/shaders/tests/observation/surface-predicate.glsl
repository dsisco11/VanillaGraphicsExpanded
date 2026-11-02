/// Preserves .NET numeric equality for selected lanes, including subnormal metadata, signed zero and NaN.
bool same(vec4 a,vec4 b,uint mask) {
    uvec4 first=floatBitsToUint(a),second=floatBitsToUint(b);
    for(int channel=0;channel<4;channel++)if((mask&(1u<<channel))!=0u && !equalBits(first[channel],second[channel]))return false;
    return true;
}
/// Evaluates an independently authored domain contract.
bvec2 predicate46(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=b.r==1;
bool color=all(greaterThanEqual(a.rgb,vec3(0))) && all(lessThanEqual(a.rgb,vec3(65504)));
return bvec2(!selected || (a.a==1 && color),selected);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate58(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise vec3 difference=abs(a.rgb-multipliers.rgb);return bvec2(all(lessThan(difference,vec3(multipliers.w))),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate59(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool green=a.g>0.8 && a.r<0.2 && a.b<0.2;
bool yellow=a.r>0.8 && a.g>0.8 && a.b<0.2;
bool red=a.r>0.8 && a.g<0.2 && a.b<0.2;
return bvec2(true,green || yellow || red);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate60(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise float brightness=a.r+a.g;brightness=brightness+a.b;return bvec2(true,brightness>0.1);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate61(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise vec3 difference=abs(a.rgb-vec3(0.5));return bvec2(all(lessThan(difference,vec3(0.2))),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate62(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.r<0.01 && a.g<0.01 && a.b<0.01);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate63(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.r>0.01 || a.g>0.01 || a.b>0.01);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate64(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
ivec2 origin=coordinate.xy*2;
vec4 block=vec4(historyAt(ivec3(origin,0),0).r,
    historyAt(ivec3(origin+ivec2(1,0),0),0).r,
    historyAt(ivec3(origin+ivec2(0,1),0),0).r,
    historyAt(ivec3(origin+ivec2(1,1),0),0).r);
float expected=min(min(block.x,block.y),min(block.z,block.w));
precise float difference=abs(a.r-expected);
return bvec2(!any(isnan(block)) && difference<multipliers.x,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate65(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r>=9.99 && a.r<=10.01 && b.r==1.0,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate66(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(b.r==1.0 && a.g>=9.99 && a.g<=10.01,abs(c.r-a.r)>0.03);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate67(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(equal(a.rgb,vec3(0))) && same(a,c,7u) && b.r==0.0 && (flags&1u)==1u && (flags&32u)==0u,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate68(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r==0.0 && b.r==0.0 && (flags&34u)==0u,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate69(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r>=9.9 && a.r<=10.1 && c.r==0.0 && b.r==1.0 && (flags&32u)!=0u,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate70(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.r>9.0);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate71(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.r==0.0);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate72(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r==0.0 && b.r==0.0 && (flags&1u)==1u,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate73(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r==0.0 && a.a==0.0 && b.r==0.0 && (flags&1u)==1u,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate74(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(equal(a.rgb,vec3(0))) && b.r==0.0,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate75(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2((flags&1u)!=0u && (flags&32u)==0u,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate76(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool hit=(flags&1u)!=0u; return bvec2(!hit || (a.r==0.0 && b.r==0.0),hit);

}
}
/// Evaluates an independently authored domain contract.
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
/// Evaluates an independently authored domain contract.
bvec2 predicate78(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r>=9.99 && a.r<=10.01,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate79(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool hit=(floatBitsToUint(c.g)&1u)!=0u;
bool valid=b.r==0.0 && (flags&1u)==1u && (flags&32u)==0u
    && all(equal(d.rgb,vec3(0))) && all(lessThanEqual(abs(a.rgb-d.rgb),vec3(0.002)));
return bvec2(!hit || valid,hit);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate80(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool hit=(floatBitsToUint(c.g)&1u)!=0u;
return bvec2(!hit || (b.r==0.0 && all(equal(a.rgb,vec3(0)))),hit);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate81(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool hit=(floatBitsToUint(c.g)&1u)!=0u;
bool valid=b.r==multipliers.x && all(greaterThanEqual(a.rgb,vec3(multipliers.y)))
    && all(lessThanEqual(a.rgb,vec3(multipliers.z)));
return bvec2(!hit || valid,hit);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate82(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.b>.1 && a.r/a.b>=.098 && a.r/a.b<=.102 && a.g/a.b>=.848 && a.g/a.b<=.852,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate83(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r>.1 && a.g/a.r>=.448 && a.g/a.r<=.452 && a.b==0,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate84(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(greaterThanEqual(a.rgb,multipliers.rgb-vec3(.001))) && all(lessThanEqual(a.rgb,multipliers.rgb+vec3(.001))),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate85(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.b>.1,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate86(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise float difference=a.r-a.b;
bool boundary=abs(difference)<.001;
bool classified=boundary?(a.g>=a.r-.001 && a.g<=a.r+.001):(a.r/a.b>=.098 && a.r/a.b<=.102);
return bvec2(a.b>.1 && classified,boundary);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate87(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool lit=multipliers.x!=0.0,opaque=multipliers.y!=0.0;
return bvec2(((flags&1u)!=0u)==opaque && (flags&32u)==0u
    && ((flags>>16)&7u)==(lit?2u:4u) && b.r==(lit?1.0:0.0)
    && all(greaterThanEqual(a.rgb,vec3(lit?0.249:0.0)))
    && all(lessThanEqual(a.rgb,vec3(lit?0.253:0.0))),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate88(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise vec3 normal=a.rgb*2-1;
precise float length=sqrt((normal.x*normal.x+normal.y*normal.y)+normal.z*normal.z);
if(length>.001)normal=normal/length;
precise vec3 difference=abs(normal-b.rgb);
return bvec2(all(lessThan(difference,vec3(multipliers.x))),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate89(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise vec3 difference=abs(a.rgb-b.rgb);return bvec2(all(lessThan(difference,vec3(multipliers.x))) && a.a>=.9,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate90(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.a>=.5,a.a>.5);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate91(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.a<.1 && all(lessThan(abs(a.rgb),vec3(multipliers.x))),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate92(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.a>.4 && a.a<.6,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate93(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(!any(isnan(a)) && a.a<.5,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate94(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(greaterThanEqual(a.rgb,b.rgb-vec3(.005))) && all(lessThanEqual(a.rgb,b.rgb+vec3(.005))),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate95(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool valid=true;
// Match the original strict channel gates; absent baseline channels are not asserted.
for(int c=0;c<3;c++)if(b[c]>.01){precise float ratio=a[c]/b[c];precise float delta=ratio-multipliers[c];valid=valid && abs(delta)<.2;}
return bvec2(valid,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate96(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(lessThan(a.rgb,vec3(multipliers.x))),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate97(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(((floatBitsToUint(a.g)>>16)&7u)==2u,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate98(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(lessThan(abs(a),vec4(multipliers.x))),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate99(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=b.r==1;return bvec2(!selected || (a.r>.9 && a.g<.1 && a.b<.1),selected);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate100(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r>.9 && a.g<.1 && a.b<.1,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate101(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,b.r==0 && a.b>.1);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate102(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise vec3 delta=abs(a.rgb-b.rgb);return bvec2(true,any(greaterThan(delta,vec3(.1))));

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate103(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(lessThan(abs(a.rgb),vec3(multipliers.x))),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate104(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(any(greaterThan(a.rgb,vec3(.01))),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate105(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=(coordinate.x%8==2 || coordinate.x%8==6) && (coordinate.y%8==2 || coordinate.y%8==6);
precise float decoded=exp(a.a)-1;
return bvec2(!selected || abs(decoded-multipliers.x)<multipliers.y,selected);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate106(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.r<.1 && a.g>.9 && a.b>.9);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate107(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.g>.01 && a.b>.01);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate108(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,any(greaterThan(a.rgb,vec3(.001))));

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate109(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise float distance=exp(a.a)-1;return bvec2(true,distance<multipliers.x);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate110(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise float distance=exp(a.a)-1;return bvec2(true,distance>multipliers.x);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate111(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,any(greaterThan(a.rgb,vec3(.01)))!=any(greaterThan(b.rgb,vec3(.01))));

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate112(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,any(greaterThan(a.rgb,vec3(.01))));

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate113(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool color=all(greaterThanEqual(a.rgb,vec3(multipliers.x))) && all(lessThanEqual(a.rgb,vec3(multipliers.y)));
bool confidence=b.r>=multipliers.z && (multipliers.z!=0 || b.r==0);
return bvec2(color && confidence && (floatBitsToUint(b.g)&32u)!=0u,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate114(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.r>.001);

}
}
/// Evaluates an independently authored domain contract.
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
/// Evaluates an independently authored domain contract.
bvec2 predicate116(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise float brightness=(a.r+a.g)+a.b;
bool color=multipliers.x==1?brightness>.1:all(greaterThanEqual(a.rgb,vec3(-.000001))) && all(lessThanEqual(a.rgb,vec3(.000001)));
return bvec2(color && a.a>.1 && same(a,b,8u) && all(equal(b.rgb,vec3(0))),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate117(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(value.r!=0 || value.g!=0,true);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate118(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool represented=!isnan(a.r);return bvec2(!represented || (a.r>=multipliers.x && a.r<=multipliers.y),represented);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate119(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,value.a>.5);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate120(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,any(greaterThan(value.rgb,vec3(.001))));

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate121(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2((value.r!=0 || value.g!=0) && roundEven(value.b*255)==9 && value.a==0,true);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate122(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool represented=!isnan(a.r);return bvec2(!represented || (a.r>=-.02 && a.r<=.02),represented);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate123(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
precise float brightness=((a.r+a.g)+a.b)/3;
vec3 prior=texelFetch(actualImage,ivec3(max(coordinate.x-1,0),coordinate.y,coordinate.z),0).rgb;
precise float previous=coordinate.x==0?-1:((prior.r+prior.g)+prior.b)/3;
return bvec2(brightness>=previous-.05,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate124(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool left=coordinate.x==0;bool right=coordinate.x==extent.x-1;return bvec2((!left || a.r>a.b) && (!right || a.b>a.r),left || right);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate125(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(floatBitsToUint(a.a)==64u,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate126(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(floatBitsToUint(a.a)==uint(multipliers.x),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate127(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(abs(a.r)<=multipliers.x && abs(a.g)<=multipliers.x && (floatBitsToUint(a.a)&1u)!=0u,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate128(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.r>=multipliers.x && a.r<=multipliers.y && abs(a.g)<=multipliers.z && (floatBitsToUint(a.a)&1u)!=0u,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate129(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(greaterThanEqual(a.rgb,multipliers.rgb-vec3(multipliers.w))) && all(lessThanEqual(a.rgb,multipliers.rgb+vec3(multipliers.w))),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate159(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(a.a==4,a.r>.5);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate160(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(greaterThanEqual(a.rgb,vec3(multipliers.x))) && all(lessThanEqual(a.rgb,vec3(multipliers.y))) && a.a==multipliers.z,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate161(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(greaterThanEqual(a.rgb,vec3(multipliers.x))) && all(lessThanEqual(a.rgb,vec3(multipliers.y))) && a.a==1,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate162(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool selected=b.a==1 && a.a==0;return bvec2(!selected || all(equal(a.rgb,vec3(0))),selected);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate163(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2((a.a==0 || a.a==1) && (a.a!=0 || all(equal(a,vec4(0)))),a.a==1);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate164(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool hit=(flags&1u)!=0u; return bvec2(!hit || (b.r==1.0 && a.r>=multipliers.x && a.r<=multipliers.y),hit);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate165(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(c.a==a.a && (floatBitsToUint(d.g)&63u)==(flags&63u) && ((flags>>16)&7u)==4u && b.r==0.0 && a.r==0.0,false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate166(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool hit=(floatBitsToUint(d.g)&1u)!=0u; return bvec2(!hit || (b.r==1.0 && (flags&1u)==1u && a.r>=11.99 && a.r<=12.01),hit);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate167(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
bool hit=(flags&1u)!=0u; return bvec2(!hit || b.r==1.0,hit);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate168(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.r>b.r+0.01 && c.r<a.r-0.001 && c.r>b.r+0.0001);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate169(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.a<b.a-0.01);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate170(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
int pixel=int(index),x=pixel%16,y=pixel/16,probe=x/8+(y/8)*2,direction=(y%8)*8+x%8;
bool traced=direction/8==(int(multipliers.x)+probe)%8;
return bvec2(traced || same(a,b,15u),!traced && b.r>0.001);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate171(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
int pixel=int(index),x=pixel%16,y=pixel/16,probe=x/8+(y/8)*2,direction=(y%8)*8+x%8;
bool traced=direction/8==(int(multipliers.x)+probe)%8;
return bvec2(traced || (all(equal(a,vec4(0))) && b.r==0.0),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate172(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(all(lessThanEqual(abs(a.rgb-b.rgb),vec3(multipliers.x))),false);

}
}
/// Evaluates an independently authored domain contract.
bvec2 predicate179(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d,ivec3 selected,vec4 value,uint flags) {
{
return bvec2(true,a.a==0);

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
case 186: return predicate88(coordinate,index,a,vec4(multipliers.yzw,0),c,d,selected,value,flags);
case 46: return predicate46(coordinate,index,a,b,c,d,selected,value,flags);
case 58: return predicate58(coordinate,index,a,b,c,d,selected,value,flags);
case 59: return predicate59(coordinate,index,a,b,c,d,selected,value,flags);
case 60: return predicate60(coordinate,index,a,b,c,d,selected,value,flags);
case 61: return predicate61(coordinate,index,a,b,c,d,selected,value,flags);
case 62: return predicate62(coordinate,index,a,b,c,d,selected,value,flags);
case 63: return predicate63(coordinate,index,a,b,c,d,selected,value,flags);
case 64: return predicate64(coordinate,index,a,b,c,d,selected,value,flags);
case 65: return predicate65(coordinate,index,a,b,c,d,selected,value,flags);
case 66: return predicate66(coordinate,index,a,b,c,d,selected,value,flags);
case 67: return predicate67(coordinate,index,a,b,c,d,selected,value,flags);
case 68: return predicate68(coordinate,index,a,b,c,d,selected,value,flags);
case 69: return predicate69(coordinate,index,a,b,c,d,selected,value,flags);
case 70: return predicate70(coordinate,index,a,b,c,d,selected,value,flags);
case 71: return predicate71(coordinate,index,a,b,c,d,selected,value,flags);
case 72: return predicate72(coordinate,index,a,b,c,d,selected,value,flags);
case 73: return predicate73(coordinate,index,a,b,c,d,selected,value,flags);
case 74: return predicate74(coordinate,index,a,b,c,d,selected,value,flags);
case 75: return predicate75(coordinate,index,a,b,c,d,selected,value,flags);
case 76: return predicate76(coordinate,index,a,b,c,d,selected,value,flags);
case 77: return predicate77(coordinate,index,a,b,c,d,selected,value,flags);
case 78: return predicate78(coordinate,index,a,b,c,d,selected,value,flags);
case 79: return predicate79(coordinate,index,a,b,c,d,selected,value,flags);
case 80: return predicate80(coordinate,index,a,b,c,d,selected,value,flags);
case 81: return predicate81(coordinate,index,a,b,c,d,selected,value,flags);
case 82: return predicate82(coordinate,index,a,b,c,d,selected,value,flags);
case 83: return predicate83(coordinate,index,a,b,c,d,selected,value,flags);
case 84: return predicate84(coordinate,index,a,b,c,d,selected,value,flags);
case 85: return predicate85(coordinate,index,a,b,c,d,selected,value,flags);
case 86: return predicate86(coordinate,index,a,b,c,d,selected,value,flags);
case 87: return predicate87(coordinate,index,a,b,c,d,selected,value,flags);
case 88: return predicate88(coordinate,index,a,b,c,d,selected,value,flags);
case 89: return predicate89(coordinate,index,a,b,c,d,selected,value,flags);
case 90: return predicate90(coordinate,index,a,b,c,d,selected,value,flags);
case 91: return predicate91(coordinate,index,a,b,c,d,selected,value,flags);
case 92: return predicate92(coordinate,index,a,b,c,d,selected,value,flags);
case 93: return predicate93(coordinate,index,a,b,c,d,selected,value,flags);
case 94: return predicate94(coordinate,index,a,b,c,d,selected,value,flags);
case 95: return predicate95(coordinate,index,a,b,c,d,selected,value,flags);
case 96: return predicate96(coordinate,index,a,b,c,d,selected,value,flags);
case 97: return predicate97(coordinate,index,a,b,c,d,selected,value,flags);
case 98: return predicate98(coordinate,index,a,b,c,d,selected,value,flags);
case 99: return predicate99(coordinate,index,a,b,c,d,selected,value,flags);
case 100: return predicate100(coordinate,index,a,b,c,d,selected,value,flags);
case 101: return predicate101(coordinate,index,a,b,c,d,selected,value,flags);
case 102: return predicate102(coordinate,index,a,b,c,d,selected,value,flags);
case 103: return predicate103(coordinate,index,a,b,c,d,selected,value,flags);
case 104: return predicate104(coordinate,index,a,b,c,d,selected,value,flags);
case 105: return predicate105(coordinate,index,a,b,c,d,selected,value,flags);
case 106: return predicate106(coordinate,index,a,b,c,d,selected,value,flags);
case 107: return predicate107(coordinate,index,a,b,c,d,selected,value,flags);
case 108: return predicate108(coordinate,index,a,b,c,d,selected,value,flags);
case 109: return predicate109(coordinate,index,a,b,c,d,selected,value,flags);
case 110: return predicate110(coordinate,index,a,b,c,d,selected,value,flags);
case 111: return predicate111(coordinate,index,a,b,c,d,selected,value,flags);
case 112: return predicate112(coordinate,index,a,b,c,d,selected,value,flags);
case 113: return predicate113(coordinate,index,a,b,c,d,selected,value,flags);
case 114: return predicate114(coordinate,index,a,b,c,d,selected,value,flags);
case 115: return predicate115(coordinate,index,a,b,c,d,selected,value,flags);
case 116: return predicate116(coordinate,index,a,b,c,d,selected,value,flags);
case 117: return predicate117(coordinate,index,a,b,c,d,selected,value,flags);
case 118: return predicate118(coordinate,index,a,b,c,d,selected,value,flags);
case 119: return predicate119(coordinate,index,a,b,c,d,selected,value,flags);
case 120: return predicate120(coordinate,index,a,b,c,d,selected,value,flags);
case 121: return predicate121(coordinate,index,a,b,c,d,selected,value,flags);
case 122: return predicate122(coordinate,index,a,b,c,d,selected,value,flags);
case 123: return predicate123(coordinate,index,a,b,c,d,selected,value,flags);
case 124: return predicate124(coordinate,index,a,b,c,d,selected,value,flags);
case 125: return predicate125(coordinate,index,a,b,c,d,selected,value,flags);
case 126: return predicate126(coordinate,index,a,b,c,d,selected,value,flags);
case 127: return predicate127(coordinate,index,a,b,c,d,selected,value,flags);
case 128: return predicate128(coordinate,index,a,b,c,d,selected,value,flags);
case 129: return predicate129(coordinate,index,a,b,c,d,selected,value,flags);
case 159: return predicate159(coordinate,index,a,b,c,d,selected,value,flags);
case 160: return predicate160(coordinate,index,a,b,c,d,selected,value,flags);
case 161: return predicate161(coordinate,index,a,b,c,d,selected,value,flags);
case 162: return predicate162(coordinate,index,a,b,c,d,selected,value,flags);
case 163: return predicate163(coordinate,index,a,b,c,d,selected,value,flags);
case 164: return predicate164(coordinate,index,a,b,c,d,selected,value,flags);
case 165: return predicate165(coordinate,index,a,b,c,d,selected,value,flags);
case 166: return predicate166(coordinate,index,a,b,c,d,selected,value,flags);
case 167: return predicate167(coordinate,index,a,b,c,d,selected,value,flags);
case 168: return predicate168(coordinate,index,a,b,c,d,selected,value,flags);
case 169: return predicate169(coordinate,index,a,b,c,d,selected,value,flags);
case 170: return predicate170(coordinate,index,a,b,c,d,selected,value,flags);
case 171: return predicate171(coordinate,index,a,b,c,d,selected,value,flags);
case 172: return predicate172(coordinate,index,a,b,c,d,selected,value,flags);
case 179: return predicate179(coordinate,index,a,b,c,d,selected,value,flags);
}
return bvec2(false,false);
}
