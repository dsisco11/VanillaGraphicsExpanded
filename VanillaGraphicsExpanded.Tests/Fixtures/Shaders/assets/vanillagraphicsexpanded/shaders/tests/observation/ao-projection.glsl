/// Authors independent inputs without using the production algorithm under test.
vec4 project(vec4 a,vec4 b,ivec3 coordinate,uint index){switch(predicateChoice){
case 1:{
a.g+=20.0;return a;
}
case 2:{
return a;
}
case 14:{
return vec4(0,-a.b,0,0);
}
case 15:{
precise vec3 contribution=a.rgb-b.rgb;return vec4(contribution,0);
}
}return vec4(0);}
