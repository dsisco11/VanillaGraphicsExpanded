/// Authors independent inputs without using the production algorithm under test.
vec4 project(vec4 a,vec4 b,ivec3 coordinate,uint index){switch(predicateChoice){
case 3:{
precise vec4 square=a*a; return square;
}
case 4:{
return roundEven(a*255);
}
case 5:{
return vec4(dot(a.rgb,vec3(0.2126,0.7152,0.0722)),0,0,0);
}
case 17:{
if(index!=0u)return vec4(0);precise float sum=0;for(int z=0;z<extent.z;z++)for(int y=0;y<extent.y;y++)for(int x=0;x<extent.x;x++){vec4 value=vec4(texelFetch(sourceImage,ivec3(x,y,z),0));sum=sum+(value.r);}return vec4(sum,0,0,0);
}
case 18:{
if(index!=0u)return vec4(0);precise float sum=0;for(int z=0;z<extent.z;z++)for(int y=0;y<extent.y;y++)for(int x=0;x<extent.x;x++){vec4 value=vec4(texelFetch(sourceImage,ivec3(x,y,z),0));sum=sum+((value.r+value.g)+value.b);}return vec4(sum,0,0,0);
}
case 19:{
return vec4(a.r+parameters.x,float(coordinate.x),float(coordinate.y),1.0);
}
}return vec4(0);}
