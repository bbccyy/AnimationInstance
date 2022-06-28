using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimationInstancing
{
    public class RuntimeHelper
    {
        // Merge all bones to a single array and merge all bind pose
        public static Transform[] MergeBone(SkinnedMeshRenderer[] meshRender, List<Matrix4x4> bindPose)
        {
            UnityEngine.Profiling.Profiler.BeginSample("MergeBone()");
            List<Transform> listTransform = new List<Transform>(150); //用来塞骨骼的节点 
            for (int i = 0; i != meshRender.Length; ++i)
            {
                Transform[] bones = meshRender[i].bones;
                Matrix4x4[] checkBindPose = meshRender[i].sharedMesh.bindposes;  //bindpose 用于将模型空间中的皮肤转到骨骼空间下 
                for (int j = 0; j != bones.Length; ++j) //一套骨骼由多个节点组成 
                {
#if UNITY_EDITOR
                    Debug.Assert(checkBindPose[j].determinant != 0, "The bind pose can't be 0 matrix.");
#endif
                    // the bind pose is correct base on the skinnedMeshRenderer, so we need to replace it
                    int index = listTransform.FindIndex(q => q == bones[j]); //查看是否已经录入过bones[j]
                    if (index < 0)
                    {   //之前没有录入过，那么添加这块骨头！ 
                        listTransform.Add(bones[j]);
                        if (bindPose != null)
                        {
                            bindPose.Add(checkBindPose[j]); //注意，bindpose与bone在下标上是对齐的 
                        }
                    }
                    else
                    {
                        bindPose[index] = checkBindPose[j]; //完全相同的骨头，用最新的bindpose矩阵 
                    }
                }
                meshRender[i].enabled = false;  //我们看到的在烘焙后角色prefab中skinnedMeshRenderer被失活原因在此 
            }
            UnityEngine.Profiling.Profiler.EndSample();
            return listTransform.ToArray();
        }

        public static Quaternion QuaternionFromMatrix(Matrix4x4 mat)
        {
            Vector3 forward;
            forward.x = mat.m02;
            forward.y = mat.m12;
            forward.z = mat.m22;

            Vector3 upwards;
            upwards.x = mat.m01;
            upwards.y = mat.m11;
            upwards.z = mat.m21;

            return Quaternion.LookRotation(forward, upwards);
        }
    }
}
