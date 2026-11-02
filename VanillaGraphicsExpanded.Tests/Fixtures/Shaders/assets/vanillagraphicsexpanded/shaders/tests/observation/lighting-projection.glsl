/// Authors independent inputs without using the production algorithm under test.
vec4 project(vec4 a,vec4 b,ivec3 coordinate,uint index){switch(predicateChoice){
case 24:{
precise float moment=a.r*float(coordinate.x);return vec4(moment,0,0,0);
}
}return vec4(0);}
