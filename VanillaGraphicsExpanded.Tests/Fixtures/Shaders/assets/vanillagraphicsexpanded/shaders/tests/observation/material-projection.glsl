/// Authors independent inputs without using the production algorithm under test.
vec4 project(vec4 a,vec4 b,ivec3 coordinate,uint index){switch(predicateChoice){
case 12:{
precise vec3 physical=a.rgb/parameters.x;return vec4(physical,0);
}
case 13:{
return vec4(a.rgb,0.37);
}
case 20:{
return a/b;
}
case 25:{
precise float scaled=a.r*255;precise float delta=scaled-(100+(float(coordinate.x/8)+.5)/8);return vec4(delta,0,0,0);
}
case 26:{
if(index!=0u)return vec4(0);precise float sum=0;for(int z=0;z<extent.z;z++)for(int y=0;y<extent.y;y++)for(int x=0;x<extent.x;x++){vec4 value=vec4(texelFetch(sourceImage,ivec3(x,y,z),0));sum=sum+((value.r*.2126+value.g*.7152)+value.b*.0722);}return vec4(sum,0,0,0);
}
}return vec4(0);}
