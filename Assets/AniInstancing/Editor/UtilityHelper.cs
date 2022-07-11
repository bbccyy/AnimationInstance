using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AnimationInstancing
{
    class UtilityHelper
    {
        public static Matrix4x4[] CalculateSkinMatrix(Transform[] bonePose,
            Matrix4x4[] bindPose,
            Matrix4x4 rootMatrix1stFrame,
            bool haveRootMotion)
        {
            if (bonePose.Length == 0) //没有骨骼节点直接返回 
                return null;

            Transform root = bonePose[0]; //定位到骨骼的根节点，以此为空间基准 
            while (root.parent != null)
            {
                root = root.parent;
            }
            Matrix4x4 rootMat = root.worldToLocalMatrix;
            //每个骨骼节点对应一个变化矩阵
            Matrix4x4[] matrix = new Matrix4x4[bonePose.Length];  
            for (int i = 0; i != bonePose.Length; ++i)
            {   //以下变化相当于将模型空间中一点，变换到root节点规定的局部空间中去  
                matrix[i] = rootMat * bonePose[i].localToWorldMatrix * bindPose[i];
            }
            return matrix;
        }


        public static void CopyMatrixData(GenerateOjbectInfo dst, GenerateOjbectInfo src)
        {
            dst.animationTime = src.animationTime;
            dst.boneListIndex = src.boneListIndex;
            dst.frameIndex = src.frameIndex;
            dst.nameCode = src.nameCode;
            dst.stateName = src.stateName;
            dst.worldMatrix = src.worldMatrix;
            dst.boneMatrix = src.boneMatrix;
        }

        public static Color[] Convert2Color(Matrix4x4[] boneMatrix)
        {
            Color[] color = new Color[boneMatrix.Length * 4];
            int index = 0;
            foreach (var obj in boneMatrix)
            {
                color[index++] = obj.GetRow(0);
                color[index++] = obj.GetRow(1);
                color[index++] = obj.GetRow(2);
                color[index++] = obj.GetRow(3);
            }
            return color;
        }
    }
}
