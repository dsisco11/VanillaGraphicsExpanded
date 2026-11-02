/// Copies the complete original domain, including the final partial workgroup.
void main(){
    uint index=gl_GlobalInvocationID.x+gl_GlobalInvocationID.y*gl_NumWorkGroups.x*gl_WorkGroupSize.x;
    if(index>=uint(extent.x*extent.y*extent.z))return;
    ivec3 p=ivec3(int(index)%extent.x,(int(index)/extent.x)%extent.y,int(index)/(extent.x*extent.y));
    imageStore(destinationImage,p,project(vec4(texelFetch(sourceImage,p,0)),((constantValue&2u)!=0u ? vec4(texelFetch(referenceImage,p,0)) : vec4(texelFetch(sourceImage,p,0))),p,index));
}
