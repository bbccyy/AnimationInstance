/*
THIS FILE IS PART OF Animation Instancing PROJECT
AnimationInstancing.cs - The core part of the Animation Instancing library

©2017 Jin Xiaoyu. All Rights Reserved.
*/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AnimationInstancing
{
    public class AnimationEvent
    {
        public string function;
        public int intParameter;
        public float floatParameter;
        public string stringParameter;
        public string objectParameter;
        public float time;
    }

    public class AnimationInfo
    {
        public string animationName;        //当前动画 clip.name
        public int animationNameHash;       //当前动画状态机 state.nameHash
        public int totalFrame;              //通过动画时长与FPS换算出来的总帧数 
        public int fps;                     //设定的FPS，默认为15 
        public int animationIndex;          //当前动画首帧所处位置 -> 展开整个动画状态机中的动画，按访问顺序并铺开，每份动画占用totalFrame帧的空间 
        public int textureIndex;            //当前动画所在纹理的索引 -> 可索引到一张 Texture2D 纹理 
        public bool rootMotion;             //root motion开关，开启后动画播放速度和角速度会调整
        public WrapMode wrapMode;
        public Vector3[] velocity;
        public Vector3[] angularVelocity;
        public List<AnimationEvent> eventList; 
    }

    public class ExtraBoneInfo
    {
        public string[] extraBone;
        public Matrix4x4[] extraBindPose;
    }

    public class ComparerHash : IComparer<AnimationInfo>
    {
        public int Compare(AnimationInfo x, AnimationInfo y)
        {
            return x.animationNameHash.CompareTo(y.animationNameHash);
        }
    }
}
