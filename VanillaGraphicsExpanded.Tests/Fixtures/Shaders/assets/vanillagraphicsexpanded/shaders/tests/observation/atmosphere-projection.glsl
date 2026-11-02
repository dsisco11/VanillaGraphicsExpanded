/// Authors independent inputs without using the production algorithm under test.
vec4 project(vec4 a,vec4 b,ivec3 coordinate,uint index){switch(predicateChoice){
case 6:{
precise vec3 physical=a.rgb/parameters.x;
precise vec3 weighted=physical*b.r;
return vec4(weighted,0);
}
case 23:{
precise vec3 radiance=a.rgb/parameters.x;return vec4(radiance,0);
}
}return vec4(0);}
