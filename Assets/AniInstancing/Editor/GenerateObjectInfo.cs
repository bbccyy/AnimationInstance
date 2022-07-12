using UnityEngine;
using UnityEditor;
using System;

namespace AnimationInstancing
{
    public class GenerateOjbectInfo
    {
        public Matrix4x4 worldMatrix;
        public int nameCode;            //meshRender[j].name.GetHashCode()
        public float animationTime;     //working frame
        public int stateName;           //animationNameHash
        public int frameIndex;
        public int boneListIndex = -1;
        public Matrix4x4[] boneMatrix;  //from bone local to root-bone local 
    }
}
