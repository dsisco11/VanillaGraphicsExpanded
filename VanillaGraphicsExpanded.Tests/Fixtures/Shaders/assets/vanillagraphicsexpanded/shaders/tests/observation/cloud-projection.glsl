/// Authors independent inputs without using the production algorithm under test.
vec4 project(vec4 a,vec4 b,ivec3 coordinate,uint index){switch(predicateChoice){
case 7:{
return vec4(b.g==1.0?parameters.x:a.r,max(a.g,b.g==1.0?1.0:0.0),0,0);
}
case 8:{
return vec4(a.r,0,0,0);
}
case 9:{
a.r+=1.0;return a;
}
case 10:{
a.a=4.0;return a;
}
case 11:{
a.a=index==4u?4.0:0.0;return a;
}
case 16:{
return vec4(0,0,0,a.r);
}
}return vec4(0);}
