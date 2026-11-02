/// Authors independent inputs without using the production algorithm under test.
vec4 project(vec4 a,vec4 b,ivec3 coordinate,uint index){switch(predicateChoice){
case 21:{
precise float value=((a.r+a.g)+a.b)/3;return vec4(value,0,0,0);
}
case 22:{
if(index!=0u)return vec4(0);precise float sum=0;orderedVisits=0u;for(int z=0;z<extent.z;z++)for(int y=0;y<extent.y;y++)for(int x=0;x<extent.x;x++){vec4 value=vec4(texelFetch(sourceImage,ivec3(x,y,z),0));orderedVisits++;sum=sum+(value.r*value.r);}return vec4(sum,0,0,0);
}
case 27:{
return vec4(a.rgb,1024);
}
}return vec4(0);}
