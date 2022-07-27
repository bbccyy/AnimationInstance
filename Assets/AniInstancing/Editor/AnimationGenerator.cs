using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditorInternal;
using System.IO;


namespace AnimationInstancing
{
    public class AnimationGenerator : EditorWindow
    {
        private static AnimationGenerator s_window;
        Vector2 scrollPosition;
        Vector2 scrollPosition2;
        private GameObject generatedObject;
        [SerializeField]
        private GameObject generatedPrefab;
        [SerializeField]
        private GameObject generatedFbx;
        private bool exposeAttachments;
        private bool showAttachmentSetting = false;
        private ExtraBoneInfo extraBoneInfo;
        //用户手动选取的额外骨骼节点<节点名，是否被用户选择>
        private Dictionary<string, bool> selectExtraBone = new Dictionary<string, bool>();
        [SerializeField]
        private List<AnimationClip> customClips = new List<AnimationClip>();
        private Dictionary<string, bool> generateAnims = new Dictionary<string, bool>();
        

        private ArrayList aniInfo = new ArrayList();
        private int aniFps = 15;

        class AnimationBakeInfo
        {
            public SkinnedMeshRenderer[] meshRender;
            public Animator animator;
            public int workingFrame;
            public float length;            //anim clip 动画时长，单位秒 
            public int layer;               //动画状态机的层级，对于简单 AnimState来说，一般为0 
            public AnimationInfo info;
        }
        private Dictionary<int, AnimationInstancingMgr.VertexCache> generateVertexCachePool;
        private Dictionary<int, ArrayList> generateMatrixDataPool;
        private GenerateOjbectInfo[] generateObjectData;
        private List<AnimationBakeInfo> generateInfo;
        private int currentDataIndex;
        private int generateCount;
        private AnimationBakeInfo workingInfo;
        private int totalFrame;
        private Dictionary<UnityEditor.Animations.AnimatorState, UnityEditor.Animations.AnimatorStateTransition[]> cacheTransition;
        private Dictionary<AnimationClip, UnityEngine.AnimationEvent[]> cacheAnimationEvent;
        //一个prefab上所有skinnedMeshRenderer自带的骨骼节点全在里面 
        private Transform[] boneTransform;
        // to cache the bone count of object in bake flow
        int boneCount = 20;
        const int BakeFrameCount = 10000;
        int textureBlockWidth = 4;
        int textureBlockHeight = 10;
        int[] stardardTextureSize = { 64, 128, 256, 512, 1024 };
        int bakedTextureIndex;
        private Texture2D[] bakedBoneTexture = null;
        private int pixelx = 0, pixely = 0;

        // Use this for initialization
        private void OnEnable()
        {
            generateInfo = new List<AnimationBakeInfo>();
            cacheTransition = new Dictionary<UnityEditor.Animations.AnimatorState, UnityEditor.Animations.AnimatorStateTransition[]>();
            cacheAnimationEvent = new Dictionary<AnimationClip, UnityEngine.AnimationEvent[]>();
            generatedPrefab = null;
            generateVertexCachePool = new Dictionary<int, AnimationInstancingMgr.VertexCache>();
            generateMatrixDataPool = new Dictionary<int, ArrayList>();
            generateObjectData = new GenerateOjbectInfo[BakeFrameCount];
            for (int i = 0; i != generateObjectData.Length; ++i)
            {
                generateObjectData[i] = new GenerateOjbectInfo();
            }
            EditorApplication.update += GenerateAnimation;
        }

        void OnDisable()
        {
            EditorApplication.update -= GenerateAnimation;
        }
        private void Reset()
        {
            pixelx = 0;
            pixely = 0;
            bakedTextureIndex = 0;
            if (generateVertexCachePool != null)
                generateVertexCachePool.Clear();
            if (generateMatrixDataPool != null)
                generateMatrixDataPool.Clear();
            currentDataIndex = 0;
        }

        void GenerateAnimation()
        {
            if (generateInfo.Count > 0 && workingInfo == null)
            {
                workingInfo = generateInfo[0];
                generateInfo.RemoveAt(0);

                workingInfo.animator.gameObject.SetActive(true);
                workingInfo.animator.Update(0);
                workingInfo.animator.Play(workingInfo.info.animationNameHash);
                workingInfo.animator.Update(0);
                workingInfo.workingFrame = 0;
                return;
            }
            if (workingInfo != null)
            {
                //float time = workingInfo.animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                //Debug.Log("The time is" + time);
                for (int j = 0; j != workingInfo.meshRender.Length; ++j)
                {
                    GenerateBoneMatrix(workingInfo.meshRender[j].name.GetHashCode(),
                                            workingInfo.info.animationNameHash,
                                            workingInfo.workingFrame,
                                            Matrix4x4.identity,
                                            false);
                }
                //Debug.Log("The length is" + workingInfo.animator.velocity.magnitude);
                workingInfo.info.velocity[workingInfo.workingFrame] = workingInfo.animator.velocity;
                workingInfo.info.angularVelocity[workingInfo.workingFrame] = workingInfo.animator.angularVelocity * Mathf.Rad2Deg;

                if (++workingInfo.workingFrame >= workingInfo.info.totalFrame)
                {
                    aniInfo.Add(workingInfo.info);
                    if (generateInfo.Count == 0)
                    {
                        foreach (var obj in cacheTransition)
                        {
                            obj.Key.transitions = obj.Value;
                        }
                        cacheTransition.Clear();
                        foreach (var obj in cacheAnimationEvent)
                        {
                            UnityEditor.AnimationUtility.SetAnimationEvents(obj.Key, obj.Value);
                        }
                        cacheAnimationEvent.Clear();
                        PrepareBoneTexture(aniInfo);
                        SetupAnimationTexture(aniInfo);
                        SaveAnimationInfo(generatedPrefab.name);
                        DestroyImmediate(workingInfo.animator.gameObject);
                        EditorUtility.ClearProgressBar();
                    }

                    if (workingInfo.animator != null)
                    {
                        workingInfo.animator.gameObject.transform.position = Vector3.zero;
                        workingInfo.animator.gameObject.transform.rotation = Quaternion.identity;
                    }
                    workingInfo = null;
                    return;
                }
                
                float deltaTime = workingInfo.length / (workingInfo.info.totalFrame - 1);
                workingInfo.animator.Update(deltaTime);
                // EditorUtility.DisplayProgressBar("Generating Animations",
                //     string.Format("Animation '{0}' is Generating.", workingInfo.info.animationName),
                //     ((float)(generateCount - generateInfo.Count) / generateCount));


				//Debug.Log(string.Format("Animation '{0}' is Generating. Current frame is {1}", workingInfo.info.animationName, workingInfo.workingFrame));
                
                
            }
        }

        [MenuItem("AnimationInstancing/Animation Generator", false)]
        static void MakeWindow()
        {
            s_window = GetWindow(typeof(AnimationGenerator)) as AnimationGenerator;
            //ms_window.oColor = GUI.contentColor;
        }

        private void OnGUI()
        {
            GUI.skin.label.richText = true;
            GUILayout.BeginHorizontal();
            {
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndHorizontal();

            GameObject prefab = EditorGUILayout.ObjectField("Asset to Generate", generatedPrefab, typeof(GameObject), true) as GameObject;
            if (prefab != generatedPrefab)
            {
                generateAnims.Clear();
                customClips.Clear();
                generatedPrefab = prefab;

                SkinnedMeshRenderer[] meshRender = generatedPrefab.GetComponentsInChildren<SkinnedMeshRenderer>();
                List<Matrix4x4> bindPose = new List<Matrix4x4>(150);
                boneTransform = RuntimeHelper.MergeBone(meshRender, bindPose);
            }

            bool error = false;
            if (generatedPrefab)
            {
                exposeAttachments = EditorGUILayout.Toggle("Enable Attachments", exposeAttachments);
                if (exposeAttachments)
                {
                    showAttachmentSetting = EditorGUILayout.Foldout(showAttachmentSetting, "Attachment setting");
                    if (showAttachmentSetting)
                    {
                        EditorGUI.BeginChangeCheck();
                        GameObject fbx = EditorGUILayout.ObjectField("FBX refrenced by Prefab:", generatedFbx, typeof(GameObject), false) as GameObject;
                        if (EditorGUI.EndChangeCheck())
                        {
                            if (fbx != generatedFbx)
                            {
                                SkinnedMeshRenderer[] meshRender = generatedPrefab.GetComponentsInChildren<SkinnedMeshRenderer>();
                                List<Matrix4x4> bindPose = new List<Matrix4x4>(150);
                                boneTransform = RuntimeHelper.MergeBone(meshRender, bindPose);

                                generatedFbx = fbx;
                                var allTrans = generatedPrefab.GetComponentsInChildren<Transform>().ToList();
                                allTrans.RemoveAll(q => boneTransform.Contains(q)); //从 genPrefab 的所有Transform中去除属于骨骼的部分 

                                //var allTrans = boneTransform;
                                selectExtraBone.Clear();
                                for (int i = 0; i != allTrans.Count; ++i)
                                {
                                    selectExtraBone.Add(allTrans[i].name, false);
                                }
                            }
                            else if (fbx == null)
                            {
                                selectExtraBone.Clear();
                            }
                        }
                        if (selectExtraBone.Count > 0)
                        {
                            var temp = new Dictionary<string, bool>();
                            foreach (var obj in selectExtraBone)
                            {
                                temp[obj.Key] = obj.Value;
                            }
                            scrollPosition2 = GUILayout.BeginScrollView(scrollPosition2);
                            foreach (var obj in temp)
                            {
                                bool value = EditorGUILayout.Toggle(string.Format("   {0}", obj.Key), obj.Value);
                                selectExtraBone[obj.Key] = value;
                            }
                            GUILayout.EndScrollView();
                        }
                    }
                }
                else
                    generatedFbx = null;

                aniFps = EditorGUILayout.IntSlider("FPS", aniFps, 1, 120);
                //下面开始收录动画 
                Animator animator = generatedPrefab.GetComponentInChildren<Animator>();
                if (animator == null)
                {
                    EditorGUILayout.LabelField("Error: The prefab should have a Animator Component.");
                    return;
                }
                if (animator.runtimeAnimatorController == null)
                {
                    EditorGUILayout.LabelField("Error: The prefab's Animator should have a Animator Controller.");
                    return;
                }
                //该方法读取animator中baseLayer层全部motion
                //返回这些mothin(Clip)，同时也会将Clip.name填充到generateAnims 
                var clips = GetClips(animator);
                string[] clipNames = generateAnims.Keys.ToArray();
                int totalFrames = 0;
                List<int> frames = new List<int>();
                foreach (var clipName in clipNames)
                {
                    if (!generateAnims[clipName])
                        continue;

					AnimationClip clip = clips.Find(delegate(AnimationClip c) {
						if (c != null)
							return c.name == clipName;
						return false;
					});
                    int framesToBake = clip ? (int)(clip.length * aniFps / 1.0f) : 1;
					framesToBake = Mathf.Clamp(framesToBake, 1, framesToBake);  //这是当前动画Clip所需帧数 
                    totalFrames += framesToBake;
                    frames.Add(framesToBake); //累进帧数
                }

                int textureCount = 1;
                int textureWidth = CalculateTextureSize(out textureCount, frames.ToArray(), boneTransform);
                error = textureCount == 0;
                if (textureCount == 0)
                    EditorGUILayout.LabelField("Error: There is certain animation's frames which is larger than a whole texture.");
                else if (textureCount == 1)
                    EditorGUILayout.LabelField(string.Format("Animation Texture will be one {0} X {1} texture", textureWidth, textureWidth));
                else
                    EditorGUILayout.LabelField(string.Format("Animation Texture will be {2} 1024 X 1024 and one {0} X {1} textures", textureWidth, textureWidth, textureCount - 1));

                scrollPosition = GUILayout.BeginScrollView(scrollPosition);
                foreach (var clipName in clipNames)
                {
					AnimationClip clip = clips.Find(delegate(AnimationClip c) {
						if (c != null)
							return c.name == clipName;
						return false;
					});
                    int framesToBake = clip ? (int)(clip.length * aniFps / 1.0f) : 1;
					framesToBake = Mathf.Clamp(framesToBake, 1, framesToBake);
                    GUILayout.BeginHorizontal();
                    {
                        generateAnims[clipName] = EditorGUILayout.Toggle(string.Format("({0}) {1} ", framesToBake, clipName), generateAnims[clipName]);
                        GUI.enabled = generateAnims[clipName];
                        //frameSkips[clipName] = Mathf.Clamp(EditorGUILayout.IntField(frameSkips[clipName]), 1, fps);
                        GUI.enabled = true;
                    }
                    GUILayout.EndHorizontal();
                    if (framesToBake > 5000)
                    {
                        GUI.skin.label.richText = true;
                        EditorGUILayout.LabelField("<color=red>Long animations degrade performance, consider using a higher frame skip value.</color>", GUI.skin.label);
                    }
                }
                GUILayout.EndScrollView();
            }

            if (generatedPrefab && !error)
            {
                if (GUILayout.Button(string.Format("Generate")))
                {
                    //BakeAnimation();
                    BakeWithAnimator();
                }
            }
        }
        void BakeAnimation()
        {
#if UNITY_ANDROID || UNITY_IPHONE
        Debug.LogError("You can't bake animations on IOS or Android. Please switch to PC.");
        return;
#endif
            if (generatedPrefab != null)
            {
                GameObject obj = Instantiate(generatedPrefab);
                obj.transform.position = Vector3.zero;
                obj.transform.rotation = Quaternion.identity;
                Animator animator = obj.GetComponentInChildren<Animator>();
                
                AnimationInstancing script = obj.GetComponent<AnimationInstancing>();
                Debug.Assert(script);
                SkinnedMeshRenderer[] meshRender = obj.GetComponentsInChildren<SkinnedMeshRenderer>();
                List<Matrix4x4> bindPose = new List<Matrix4x4>(150);
                Transform[] boneTransform = RuntimeHelper.MergeBone(meshRender, bindPose);
                Reset();
                AddMeshVertex2Generate(meshRender, boneTransform, bindPose.ToArray());

                Transform rootNode = meshRender[0].rootBone;
                for (int j = 0; j != meshRender.Length; ++j)
                {
                    meshRender[j].enabled = true;
                }

                int frames = 0;
                var clips = GetClips(true);
                foreach (AnimationClip animClip in clips)
                {
                    //float lastFrameTime = 0;
                    int aniName = animClip.name.GetHashCode();
                    int bakeFrames = Mathf.CeilToInt(animClip.length * aniFps);

                    AnimationInfo info = new AnimationInfo();
                    info.animationNameHash = aniName;
                    info.animationIndex = frames;
                    info.totalFrame = bakeFrames;

                    //bool rotationRootMotion = false, positionRootMotion = false;
                    //for (int i = 0; i < bakeFrames; i += 1)
                    //{
                    //    float bakeDelta = Mathf.Clamp01(((float)i / bakeFrames));
                    //    float animationTime = bakeDelta * animClip.length;
                    //    animClip.SampleAnimation(obj, animationTime);

                    //    info.position[i] = rootNode.localPosition;
                    //    info.rotation[i] = rootNode.localRotation;
                    //    if (i > 0 && info.position[i] != info.position[i - 1])
                    //    {
                    //        positionRootMotion = true;
                    //    }
                    //    if (i > 0 && info.rotation[i] != info.rotation[i - 1])
                    //    {
                    //        rotationRootMotion = true;
                    //    }
                    //}
                    //info.rootMotion = positionRootMotion;

                    Matrix4x4 rootMatrix1stFrame = Matrix4x4.identity;
                    animator.applyRootMotion = true;
                    animator.Play("TestState", 0);
                    //                 animator.StartRecording(bakeFrames);
                    //                 for (int i = 0; i < bakeFrames; i += 1)
                    //                 {
                    //                     animator.Update(1.0f / m_fps);
                    //                 }
                    //                 animator.StopRecording();
                    // 
                    //                 animator.StartPlayback();
                    //                 animator.playbackTime = 0;
                    //AnimationInstancing script = m_prefab.GetComponent<AnimationInstancing>();
                    for (int i = 0; i < bakeFrames; i += 1)
                    {
                        //float bakeDelta = Mathf.Clamp01(((float)i / bakeFrames));
                        //float animationTime = bakeDelta * animClip.length;
                        //float normalizedTime = animationTime / animClip.length;

                        //UnityEditor.Animations.AnimatorController ac = animator.runtimeAnimatorController;
                        //UnityEditorInternal.StateMachine sm = ac.GetLayerStateMachine(0);


                        //AnimatorStateInfo nameInfo = animator.GetCurrentAnimatorStateInfo(0);

                        //                     if (lastFrameTime == 0)
                        //                     {
                        //                         float nextBakeDelta = Mathf.Clamp01(((float)(i + 1) / bakeFrames));
                        //                         float nextAnimationTime = nextBakeDelta * animClip.length;
                        //                         lastFrameTime = animationTime - nextAnimationTime;
                        //                     }
                        //                     animator.Update(animationTime - lastFrameTime);
                        //                     lastFrameTime = animationTime;

                        animator.Update(1.0f / bakeFrames);

                        //animClip.SampleAnimation(obj, animationTime);

                        //if (i == 0)
                        //{
                        //    rootMatrix1stFrame = boneTransform[0].localToWorldMatrix;
                        //}
                        for (int j = 0; j != meshRender.Length; ++j)
                        {
                            GenerateBoneMatrix(meshRender[j].name.GetHashCode(),
                                                    aniName,
                                                    i,
                                                    rootMatrix1stFrame,
                                                    info.rootMotion);
                        }
                    }

                    aniInfo.Add(info);
                    frames += bakeFrames;
                    SetupAnimationTexture(aniInfo);
                }
                //AnimationInstancingMgr.Instance.ExportBoneTexture(m_prefab.name);
                SaveAnimationInfo(generatedPrefab.name);

                DestroyImmediate(obj);
            }
        }


        void BakeWithAnimator()
        {
            if (generatedPrefab != null)
            {
                generatedObject = Instantiate(generatedPrefab);
                Selection.activeGameObject = generatedObject;
                generatedObject.transform.position = Vector3.zero;
                generatedObject.transform.rotation = Quaternion.identity;
                Animator animator = generatedObject.GetComponentInChildren<Animator>();
                
                AnimationInstancing script = generatedObject.GetComponent<AnimationInstancing>();
                Debug.Assert(script);
                SkinnedMeshRenderer[] meshRender = generatedObject.GetComponentsInChildren<SkinnedMeshRenderer>();
                List<Matrix4x4> bindPose = new List<Matrix4x4>(150);
                Transform[] boneTransform = RuntimeHelper.MergeBone(meshRender, bindPose);

                // calculate the bindpose of attached points
                if (generatedFbx)
                {
                    List<Transform> listExtra = new List<Transform>();
                    Transform[] trans = generatedFbx.GetComponentsInChildren<Transform>();
                    Transform[] bakedTrans = generatedObject.GetComponentsInChildren<Transform>();
                    foreach (var obj in selectExtraBone)
                    {
                        if (!obj.Value)
                            continue;

                        for (int i = 0; i != trans.Length; ++i)
                        {
                            Transform tran = trans[i] as Transform;
                            if (tran.name == obj.Key)
                            {
                                bindPose.Add(tran.localToWorldMatrix); 
                                listExtra.Add(bakedTrans[i]);
                            }
                        }
                    }

                    Transform[] totalTransform = new Transform[boneTransform.Length + listExtra.Count];
                    System.Array.Copy(boneTransform, totalTransform, boneTransform.Length);
                    System.Array.Copy(listExtra.ToArray(), 0, totalTransform, boneTransform.Length, listExtra.Count);
                    boneTransform = totalTransform;
                    //boneTransform = boneTransform;

                    extraBoneInfo = new ExtraBoneInfo();
                    extraBoneInfo.extraBone = new string[listExtra.Count];
                    extraBoneInfo.extraBindPose = new Matrix4x4[listExtra.Count];
                    for (int i = 0; i != listExtra.Count; ++i)
                    {
                        extraBoneInfo.extraBone[i] = listExtra[i].name;
                        extraBoneInfo.extraBindPose[i] = bindPose[bindPose.Count - listExtra.Count + i];
                    }
                }
                Reset();
                AddMeshVertex2Generate(meshRender, boneTransform, bindPose.ToArray());

                Transform rootNode = meshRender[0].rootBone;
                for (int j = 0; j != meshRender.Length; ++j)
                {
                    meshRender[j].enabled = true;
                }
                animator.applyRootMotion = true;
                totalFrame = 0;

                UnityEditor.Animations.AnimatorController controller = animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
                Debug.Assert(controller.layers.Length > 0);
                cacheTransition.Clear();
                cacheAnimationEvent.Clear();
                UnityEditor.Animations.AnimatorControllerLayer layer = controller.layers[0];
                AnalyzeStateMachine(layer.stateMachine, animator, meshRender, 0, aniFps, 0);
                generateCount = generateInfo.Count;
            }
        }


        void AnalyzeStateMachine(UnityEditor.Animations.AnimatorStateMachine stateMachine,
            Animator animator,
            SkinnedMeshRenderer[] meshRender,
            int layer,
            int bakeFPS,
            int animationIndex)
        {
            AnimationInstancing instance = generatedPrefab.GetComponent<AnimationInstancing>();
            if (instance == null)
            {
                Debug.LogError("You should select a prefab with AnimationInstancing component.");
                return;
            }
			instance.prototype = generatedPrefab;

            for (int i = 0; i != stateMachine.states.Length; ++i)  //递归遍历动画状态机 
            {
                ChildAnimatorState state = stateMachine.states[i];
                AnimationClip clip = state.state.motion as AnimationClip;
                bool needBake = false;
                if (clip == null)
                    continue;
                if (!generateAnims.TryGetValue(clip.name, out needBake)) //可能有些动画被用户标记为不烘焙，这里过滤 
                    continue;
                foreach (var obj in generateInfo)
                {
                    if (obj.info.animationName == clip.name)
                    {
                        needBake = false;
                        break;
                    }
                }

                if (!needBake)
                    continue;

                AnimationBakeInfo bake = new AnimationBakeInfo();
                bake.length = clip.averageDuration;
                bake.animator = animator;
				bake.animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                bake.meshRender = meshRender;
                bake.layer = layer;
                bake.info = new AnimationInfo();
                bake.info.animationName = clip.name;
                bake.info.animationNameHash = state.state.nameHash;
                bake.info.animationIndex = animationIndex;
                bake.info.totalFrame = (int)(bake.length * bakeFPS + 0.5f) + 1;
                bake.info.totalFrame = Mathf.Clamp(bake.info.totalFrame, 1, bake.info.totalFrame);
                bake.info.fps = bakeFPS;
                bake.info.rootMotion = true;
                bake.info.wrapMode = clip.isLooping? WrapMode.Loop: clip.wrapMode;
                if (bake.info.rootMotion)
                {
                    bake.info.velocity = new Vector3[bake.info.totalFrame];
                    bake.info.angularVelocity = new Vector3[bake.info.totalFrame];
                }
                generateInfo.Add(bake);
                animationIndex += bake.info.totalFrame;
                totalFrame += bake.info.totalFrame;

                bake.info.eventList = new List<AnimationEvent>();
                //AnimationClip clip = state.state.motion as AnimationClip;
                foreach (var evt in clip.events)
                {
                    AnimationEvent aniEvent = new AnimationEvent();
                    aniEvent.function = evt.functionName;
                    aniEvent.floatParameter = evt.floatParameter;
                    aniEvent.intParameter = evt.intParameter;
                    aniEvent.stringParameter = evt.stringParameter;
                    aniEvent.time = evt.time;
                    if (evt.objectReferenceParameter != null)
                        aniEvent.objectParameter = evt.objectReferenceParameter.name;
                    else
                        aniEvent.objectParameter = "";
                    bake.info.eventList.Add(aniEvent);
                }

                cacheTransition.Add(state.state, state.state.transitions);
                state.state.transitions = null;
                cacheAnimationEvent.Add(clip, clip.events);
                UnityEngine.AnimationEvent[] tempEvent = new UnityEngine.AnimationEvent[0];
                UnityEditor.AnimationUtility.SetAnimationEvents(clip, tempEvent);
            }
            for (int i = 0; i != stateMachine.stateMachines.Length; ++i)
            {
                AnalyzeStateMachine(stateMachine.stateMachines[i].stateMachine, animator, meshRender, layer, bakeFPS, animationIndex);
            }
        }


        private void SaveAnimationInfo(string name)
        {
            string folderName = "AnimationTexture";
            string path = Application.dataPath + "/" + folderName + "/";
            if (!Directory.Exists(path))
                AssetDatabase.CreateFolder("Assets", folderName);
            FileStream file = File.Open(path + name + ".bytes", FileMode.Create);
            BinaryWriter writer = new BinaryWriter(file);
            writer.Write(aniInfo.Count);
            foreach (var obj in aniInfo)
            {
                AnimationInfo info = (AnimationInfo)obj;
                //writer.Write(info.animationNameHash);
                writer.Write(info.animationName);
                writer.Write(info.animationIndex);
                writer.Write(info.textureIndex);
                writer.Write(info.totalFrame);
                writer.Write(info.fps);
                writer.Write(info.rootMotion);
                writer.Write((int)info.wrapMode);
                if (info.rootMotion)
                {   //凡是rootMotion，需要按照animator指定的速度播放动画 
                    Debug.Assert(info.totalFrame == info.velocity.Length);
                    for (int i = 0; i != info.velocity.Length; ++i)
                    {
                        writer.Write(info.velocity[i].x);
                        writer.Write(info.velocity[i].y);
                        writer.Write(info.velocity[i].z);

                        writer.Write(info.angularVelocity[i].x);
                        writer.Write(info.angularVelocity[i].y);
                        writer.Write(info.angularVelocity[i].z);
                    }
                }
                writer.Write(info.eventList.Count);
                foreach (var evt in info.eventList)
                {
                    writer.Write(evt.function);
                    writer.Write(evt.floatParameter);
                    writer.Write(evt.intParameter);
                    writer.Write(evt.stringParameter);
                    writer.Write(evt.time);
                    writer.Write(evt.objectParameter);
                }
            }

            writer.Write(exposeAttachments);
            if (exposeAttachments)
            {
                writer.Write(extraBoneInfo.extraBone.Length);
                for (int i = 0; i != extraBoneInfo.extraBone.Length; ++i)
                {
                    writer.Write(extraBoneInfo.extraBone[i]);
                }
                for (int i = 0; i != extraBoneInfo.extraBindPose.Length; ++i)
                {
                    for (int j = 0; j != 16; ++j)
                    {
                        writer.Write(extraBoneInfo.extraBindPose[i][j]);
                    }
                }
            }

            Texture2D[] texture = bakedBoneTexture;
            writer.Write(texture.Length);
            writer.Write(textureBlockWidth);
            writer.Write(textureBlockHeight);
            for (int i = 0; i != texture.Length; ++i)
            {
                byte[] bytes = texture[i].GetRawTextureData();
                writer.Write(texture[i].width);
                writer.Write(texture[i].height);
                writer.Write(bytes.Length);
                writer.Write(bytes);
            }

            file.Close();
            aniInfo.Clear();
        }

        private List<AnimationClip> GetClips(bool bakeFromAnimator)
        {
            Object[] gameObject = new Object[] { generatedPrefab };
            var clips = EditorUtility.CollectDependencies(gameObject).ToList();
            if (bakeFromAnimator)
            {
                clips.Clear();
                clips.AddRange(generatedPrefab.GetComponentInChildren<Animator>().runtimeAnimatorController.animationClips);
            }
            else
            {
                clips = EditorUtility.CollectDependencies(gameObject).ToList();
                foreach (var obj in clips.ToArray())
                    clips.AddRange(AssetDatabase.LoadAllAssetRepresentationsAtPath(AssetDatabase.GetAssetPath(obj)));
                clips.AddRange(customClips.Select(q => (Object)q));
                clips.RemoveAll(q => q is AnimationClip == false || q == null);
            }

            foreach (AnimationClip clip in clips)
            {
                if (generateAnims.ContainsKey(clip.name) == false)
                    generateAnims.Add(clip.name, true);
            }
            clips.RemoveAll(q => generateAnims.ContainsKey(q.name) == false);
            clips.RemoveAll(q => generateAnims[q.name] == false);

            var distinctClips = clips.Select(q => (AnimationClip)q).Distinct().ToList();
            for (int i = 0; i < distinctClips.Count; i++)
            {
                if (generateAnims.ContainsKey(distinctClips[i].name) == false)
                    generateAnims.Add(distinctClips[i].name, true);
            }
            return distinctClips;
        }


        private List<AnimationClip> GetClips(Animator animator)
        {
            UnityEditor.Animations.AnimatorController controller = animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
            return GetClipsFromStatemachine(controller.layers[0].stateMachine);  //插件不支持baseLayer以外的动画Clip 
        }

        private List<AnimationClip> GetClipsFromStatemachine(UnityEditor.Animations.AnimatorStateMachine stateMachine)
        {
            List<AnimationClip> list = new List<AnimationClip>();
            for (int i = 0; i != stateMachine.states.Length; ++i)
            {
                UnityEditor.Animations.ChildAnimatorState state = stateMachine.states[i];
				if (state.state.motion is UnityEditor.Animations.BlendTree)
				{
					UnityEditor.Animations.BlendTree blendTree = state.state.motion as UnityEditor.Animations.BlendTree;
					ChildMotion[] childMotion = blendTree.children;
					for(int j = 0; j != childMotion.Length; ++j) 
					{
						list.Add(childMotion[j].motion as AnimationClip);
					}
				}
				else if (state.state.motion != null)
                	list.Add(state.state.motion as AnimationClip);
            }
            for (int i = 0; i != stateMachine.stateMachines.Length; ++i)
            {
                list.AddRange(GetClipsFromStatemachine(stateMachine.stateMachines[i].stateMachine));
            }

            var distinctClips = list.Select(q => q).Distinct().ToList();
            for (int i = 0; i < distinctClips.Count; i++)
            {
                if (distinctClips[i] && generateAnims.ContainsKey(distinctClips[i].name) == false)
                    generateAnims.Add(distinctClips[i].name, true);
            }
            return list;
        }

        private void GenerateBoneMatrix(int nameCode,
            int stateName,
            float stateTime,
            Matrix4x4 rootMatrix1stFrame,
            bool rootMotion)
        {
            UnityEngine.Profiling.Profiler.BeginSample("AddBoneMatrix()");
            AnimationInstancingMgr.VertexCache vertexCache = null;
            bool find = generateVertexCachePool.TryGetValue(nameCode, out vertexCache);
            if (!find)
                return;

            GenerateOjbectInfo matrixData = generateObjectData[currentDataIndex++];
            matrixData.nameCode = nameCode;
            matrixData.stateName = stateName;
            matrixData.animationTime = stateTime;
            matrixData.worldMatrix = Matrix4x4.identity;
            matrixData.frameIndex = -1;
            matrixData.boneListIndex = -1;

            UnityEngine.Profiling.Profiler.BeginSample("AddBoneMatrix:update the matrix");
            if (generateMatrixDataPool.ContainsKey(stateName))
            {
                ArrayList list = generateMatrixDataPool[stateName];
                matrixData.boneMatrix = UtilityHelper.CalculateSkinMatrix(
                        vertexCache.bonePose,
                        vertexCache.bindPose,
                        rootMatrix1stFrame,
                        rootMotion);

                GenerateOjbectInfo data = new GenerateOjbectInfo();
                UtilityHelper.CopyMatrixData(data, matrixData);
                list.Add(data);
            }
            else
            {
                UnityEngine.Profiling.Profiler.BeginSample("AddBoneMatrix:ContainsKey");
                matrixData.boneMatrix = UtilityHelper.CalculateSkinMatrix(
                    vertexCache.bonePose,
                    vertexCache.bindPose,
                    rootMatrix1stFrame,
                    rootMotion);

                ArrayList list = new ArrayList();
                GenerateOjbectInfo data = new GenerateOjbectInfo();
                UtilityHelper.CopyMatrixData(data, matrixData);
                list.Add(data);
                generateMatrixDataPool[stateName] = list;

                UnityEngine.Profiling.Profiler.EndSample();
            }
            UnityEngine.Profiling.Profiler.EndSample();

            UnityEngine.Profiling.Profiler.EndSample();
        }

        private void AddMeshVertex2Generate(SkinnedMeshRenderer[] meshRender,
            Transform[] boneTransform,
            Matrix4x4[] bindPose)
        {
            boneCount = boneTransform.Length;
            textureBlockWidth = 4;
            textureBlockHeight = boneCount;
            for (int i = 0; i != meshRender.Length; ++i)
            {
                Mesh m = meshRender[i].sharedMesh;
                if (m == null)
                    continue;

                int nameCode = meshRender[i].name.GetHashCode();
                if (generateVertexCachePool.ContainsKey(nameCode))
                    continue;

                AnimationInstancingMgr.VertexCache vertexCache = new AnimationInstancingMgr.VertexCache();
                generateVertexCachePool[nameCode] = vertexCache;
                vertexCache.nameCode = nameCode;
                vertexCache.bonePose = boneTransform;
                vertexCache.bindPose = bindPose;
                break;
            }
        }

        private void PrepareBoneTexture(ArrayList infoList)
        {
            int count = 1;
            int[] frames = new int[infoList.Count];
            for (int i = 0; i != infoList.Count; ++i)
            {
                AnimationInfo info = infoList[i] as AnimationInfo;
                frames[i] = info.totalFrame;
            }
            int textureWidth = CalculateTextureSize(out count, frames);
            //int textureHeight = textureWidth;
            Debug.Assert(textureWidth > 0);

            bakedBoneTexture = new Texture2D[count];
            TextureFormat format = TextureFormat.RGBAHalf;
            for (int i = 0; i != count; ++i)
            {   //注意，如果有多张，理论上最后一张纹理的size使用textureWidth标记的值（一般会小于1024），而前面所有纹理size=1024
                //参考如下逻辑，判断条件应当为 count > 1 && i < count -1 ? ... 
                int width = count > 1 && i < count ? stardardTextureSize[stardardTextureSize.Length - 1] : textureWidth;
                bakedBoneTexture[i] = new Texture2D(width, width, format, false);
                bakedBoneTexture[i].filterMode = FilterMode.Point;
            }
        }

        // calculate the texture count and every size
        public int CalculateTextureSize(out int textureCount, int[] frames, Transform[] bone = null)
        {
            int textureWidth = stardardTextureSize[0]; //{ 64, 128, 256, 512, 1024 };
            int blockWidth = 0;
            int blockHeight = 0;
            if (bone != null)   //带有骨骼节点 
            {
                boneCount = bone.Length;
                blockWidth = 4;
                blockHeight = boneCount;    //每个骨骼点占用4个像素 
            }
            else
            {
                blockWidth = textureBlockWidth;     //4
                blockHeight = textureBlockHeight;   //10
            }

            //算法解析：优先向最大size纹理塞全部动画，如果最大sizeA塞满，则创建新的最大sizeA继续，对于最后一张最大sizeA的纹理，是有可能存在很多空闲空间的，
            //为了不浪费空间，取消最后一张大纹理，从小纹理向大纹理寻找适合的，也就是第一张能完整的装下原本最后一张纹理的全部内容的纹理，返新找到的纹理sizeB，以及总的纹理个数count，
            //实际上一共需要用到 count - 1 张sizeA的大纹理，外加1张sizeB的纹理，该方法默认外部模块知道怎么处理这组返回值（sizeB + count)。
            //如果最大size塞不满一张，则用次一级纹理尝试装填全部动画，依次下探过程中，只要出现当前次级纹理容量不够的情况，就直接回滚到使用1张上级纹理并返回；
            //另一方面，如果下探过程中，当前次级纹理也有富裕空间，则向更次级纹理迭代，总之全部动画只会塞在1张纹理里，方法会返回该纹理的大小，以及count=1.
            int count = 1;  //需要使用的纹理个数 
            for (int i = stardardTextureSize.Length - 1; i >= 0; --i)   //从最大 size=1024 开始往前遍历，寻找最合适的 
            {
                int size = stardardTextureSize[i];                      //最开始是1024 
                int blockCountEachLine = size / blockWidth;             //当前纹理每行最大block数 
                int x = 0, y = 0;   //x表示纹理x轴下标，若x不为0，则代表已有(x/blockWidth)帧布置好了，y代表当前纹理已经被占用的行数
                int k = 0;          //最终当前在处理的动画的序号，便于换页后继续 
                for (int j = 0; j != frames.Length; ++j) //遍历每一个动画 
                {   //frame -> 当前动画帧数。认为一个block代表一帧，n帧=n*block，可求得n占用行数和末尾列数 
                    int frame = frames[j];
                    int currentLineEmptyBlockCount = (size - x) / blockWidth % blockCountEachLine; //注:一行全空白的返回值是0，代表都能用！ 
                    bool check = x == 0 && y == 0;          //check==true 表示当前是新纹理，还没被使用过 
                    x = (x + frame % blockCountEachLine * blockWidth) % size; //x是最终塞下当前动画全部frame后，x轴坐标停留的地方 
                    if (frame > currentLineEmptyBlockCount) //是否要启动换行逻辑:计算更新y值 
                    {
                        y += (frame - currentLineEmptyBlockCount) / blockCountEachLine * blockHeight; //需要几行来塞余下的frames 
                        y += currentLineEmptyBlockCount > 0 ? blockHeight : 0;  //粗略判断是否追加一行，主要因为currentLineEmptyBC==0时代表意义的问题，需要额外的修正 
                    }

                    if (y + blockHeight > size) //触发换页逻辑！纹理余下的空间不够再塞入一个block高度 
                    {
                        x = y = 0;
                        ++count;
                        k = j--;        //注意，这里变量j也回退了一格，新纹理将会重头存放当前动画的全部帧 
                        if (check)      //满足进入check分支的条件: 新起一张纹理，填充第一个动画，同时还触发了换页，一般很难进入该分支的  
                        {
                            if (i == stardardTextureSize.Length - 1) //当前是最大size的纹理，检测下是否有单个动画过大的问题，如没有，则新起同size纹理一张，继续填充 
                            {
                                //Debug.LogError("There is certain animation's frame larger than a texture.");
                                textureCount = 0;
                                return -1;
                            }
                            else
                            {
                                //i != stardardTextureSize.Length - 1  --> 当前纹理不是最大的纹理 
                                //基于全部动画填不满一张纹理时(suitable为false)，会自动降档，寻找次一级纹理填充的逻辑
                                //说明上一级纹理足够填充，但是本级纹理连第一段动画都不够填，
                                //这里直接跳出循环，后续逻辑是用上一层size的纹理1张 
                                break;
                            }
                        }
                    }
                }
                //情况1. 第一次循环到此，最大size的纹理已经使用了count > 1张（触发了换页），变量k指向最后一张纹理存放的第一段动画的索引
                //情况2. 第一次循环到此，最大size的纹理只使用了count == 1张（没触发换页），k 为 0
                //情况3. 不是第一次循环到此，当前size小于最大size，此size纹理已经使用了count > 1张（触发了换页），变量k指向最后一张纹理中第一段动画的索引 
                //情况4. 不是第一次循环到此，当前size小于最大size，只使用了count == 1张纹理（没触发换页），k 为 0
                bool suitable = false;
                if (count > 1 && i == stardardTextureSize.Length - 1) //触发换页，且当前工作在最大尺寸纹理上 (情况1)
                {   //这边逻辑有点奇怪，会找到第一张能完整放下“一段”动画的纹理（不是余下全部动画），然后直接返回该纹理的size 
                    //我认为的可能的逻辑是:从小到大变量纹理table，找到第一个能放下余下全部动画的纹理，然后再退出循环 
                    for (int m = 0; m != stardardTextureSize.Length; ++m) //从小到大遍历候选纹理 
                    {
                        size = stardardTextureSize[m];
                        x = y = 0;
                        for (int n = k; n < frames.Length; ++n) //遍历余下的动画 
                        {
                            int frame = frames[n];
                            int currentLineEmptyBlockCount = (size - x) / blockWidth % blockCountEachLine; //注意 blockCountEachLine 定义位置 
                            x = (x + frame % blockCountEachLine * blockWidth) % size;
                            if (frame > currentLineEmptyBlockCount) //是否要换行 
                            {
                                y += (frame - currentLineEmptyBlockCount) / blockCountEachLine * blockHeight;
                                y += currentLineEmptyBlockCount > 0 ? blockHeight : 0;
                            }
                            if (y + blockHeight <= size)
                            {   //没有触发换页
                                suitable = true;
                                break;
                            }
                        }
                        if (suitable)
                        {
                            textureWidth = size;
                            break;
                        }
                    }
                }
                else if (count > 1) //触发换页，且资源无法装满上一层纹理 (情况3)
                {
                    textureWidth = stardardTextureSize[i + 1];  //直接用一整张上一层纹理得了
                    count = 1;                                  //是的，“一张”上一层的纹理 
                    suitable = true;                            //完工 
                }

                if (suitable)  //只要 count == 1， 就不可能进入suitable分支，从而提前跳出大循环 
                {
                    break; 
                }
                //如果没有触发换页(说明当前纹理资源太过于充裕了)，为避免浪费，将继续外层循环，向更小的纹理迭代 
                //对应（情况2）和（情况4）
            }
            textureCount = count;
            return textureWidth;
        }

        public void SetupAnimationTexture(ArrayList infoList)
        {
            int preNameCode = generateObjectData[0].stateName; //这是第一段动画的名字 
            //int preTextureIndex = m_bakedTextureIndex;
            for (int i = 0; i != currentDataIndex; ++i) //currentDataIndex在处理帧骨骼变换矩阵时会自增一次 
            {
                GenerateOjbectInfo matrixData = generateObjectData[i]; //这是动画中一帧的数据 
                if (matrixData.boneMatrix == null)
                    continue;
                //当处理一段新动画时，需要知道这段“动画”位于纹理中的确切起始位置 
                //既定位 AnimationInfo.animationIndex 以及 textureIndex的值 
                //这主要是受到动画不可以分处2张纹理的设定所致，不然是不需要这些复杂逻辑的 
                if (preNameCode != matrixData.stateName) 
                {
                    preNameCode = matrixData.stateName;         //更新动画名 
                    int totalFrames = currentDataIndex - i;     //剩余全部帧数 
                    for (int j = i; j != currentDataIndex; ++j)
                    {
                        if (preNameCode != generateObjectData[j].stateName)
                        {
                            totalFrames = j - i;    //计算得到当前动画帧数 
                            break;
                        }
                    }
                    //获取当前纹理的size 
                    int width = bakedBoneTexture[bakedTextureIndex].width;
                    int height = bakedBoneTexture[bakedTextureIndex].height;
                    int y = pixely;
                    //计算当前纹理行还能塞下多少个block 
                    int currentLineBlockCount = (width - pixelx) / textureBlockWidth % (width / textureBlockWidth);
                    totalFrames -= currentLineBlockCount;
                    //下面的分支用于判断是否要追加新的纹理，并且定位新纹理的索引和起始偏移 
                    if (totalFrames > 0)
                    {   //若用完当前行，任有剩余待处理帧 
                        int framesEachLine = width / textureBlockWidth;
                        y += (totalFrames / framesEachLine) * textureBlockHeight;
                        y += currentLineBlockCount > 0 ? textureBlockHeight : 0;    //y指向纹理行数，此时为最后一行block的起始位置处  
                        if (height < y + textureBlockHeight)
                        {   //最后一行无法塞入当前纹理(数据溢出)
                            ++bakedTextureIndex;//只增加1x纹理页 (此处隐含条件:单一动画不可能填满一整张纹理) 
                            pixelx = 0;         //重置x与y位置
                            pixely = 0;         //暗示动画不拆分到不同纹理中(同一段动画永远只在一张纹理中存放) 
                            Debug.Assert(bakedTextureIndex < bakedBoneTexture.Length);
                        }
                    }
                    //找到对应 AnimationInfo 并设置其 animationIndex 和 textureIndex 
                    foreach (var obj in infoList)
                    {
                        AnimationInfo info = obj as AnimationInfo;
                        if (info.animationNameHash == matrixData.stateName)
                        {
                            //animationIndex 用于定位当前动画起始位置在纹理的什么地方(用index = col + row * rowNum的形式表示)
                            info.animationIndex = pixelx / textureBlockWidth + pixely / textureBlockHeight * bakedBoneTexture[bakedTextureIndex].width / textureBlockWidth;
                            //textureIndex 用于表示当前动画位于哪一张纹理中 
                            info.textureIndex = bakedTextureIndex;
                        }
                    }
                }
                //以下分支负责将数据存入纹理 
                if (matrixData.boneMatrix != null)
                {
                    Debug.Assert(pixely + textureBlockHeight <= bakedBoneTexture[bakedTextureIndex].height);
                    Color[] color = UtilityHelper.Convert2Color(matrixData.boneMatrix); //数组长度为 4 * boneMatrix.length 
                    bakedBoneTexture[bakedTextureIndex].SetPixels(pixelx, pixely, textureBlockWidth, textureBlockHeight, color);
                    //frameIndex用于定位到当前动画帧在纹理中的位置(同样使用index = col + row * rowNum得形式表示)
                    matrixData.frameIndex = pixelx / textureBlockWidth + pixely / textureBlockHeight * bakedBoneTexture[bakedTextureIndex].width / textureBlockWidth; 
                    pixelx += textureBlockWidth;    //更新列索引 
                    if (pixelx + textureBlockWidth > bakedBoneTexture[bakedTextureIndex].width)
                    {   //用尽当前列，转下一行 
                        pixelx = 0;
                        pixely += textureBlockHeight;
                    }
                    if (pixely + textureBlockHeight > bakedBoneTexture[bakedTextureIndex].height)
                    {   //用尽所有的row，切下一张纹理 
                        Debug.Assert(generateObjectData[i + 1].stateName != matrixData.stateName);
                        ++bakedTextureIndex;
                        pixelx = 0;
                        pixely = 0;
                        Debug.Assert(bakedTextureIndex < bakedBoneTexture.Length);
                    }
                }
                else
                {   //这个分支不会进入，留着何用? 
                    Debug.Assert(false);
                    ArrayList list = generateMatrixDataPool[matrixData.stateName];
                    GenerateOjbectInfo originalData = list[matrixData.boneListIndex] as GenerateOjbectInfo;
                    matrixData.frameIndex = originalData.frameIndex;

                }
            }
            currentDataIndex = 0;
        }

        [DrawGizmo(GizmoType.NotInSelectionHierarchy | GizmoType.Selected)]
        static void DrawGizmo(AnimationInstancing instance, GizmoType gizmoType)
        {
            //Gizmos.DrawSphere(instance.gameObject.transform.position, 3);
            if (!instance.enabled)
                return;
            if (EditorApplication.isPlaying)
                return;

            GameObject obj = instance.gameObject;
            SkinnedMeshRenderer[] render = obj.GetComponentsInChildren<SkinnedMeshRenderer>();
            for (int i = 0; i != render.Length; ++i)
            {
                Gizmos.DrawMesh(render[i].sharedMesh, obj.transform.position, obj.transform.rotation); 
            }
            
        }
    }
}