using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
//using UnityEngine.Animations;
using UnityGuidRegenerator;

public class LoadAssetBundle : EditorWindow
{

    public string bundleName = "Assets/AssetBundle/troops1.bundle";

    [SerializeField]
    public GameObject instance = null;

    [MenuItem("Tool/Load bundle1")]
    public static void LoadBundleAsset()
    {
        GetWindow<LoadAssetBundle>(); 
    }

    private void OnGUI()
    {
        if (GUILayout.Button("Load"))
        {
            OnLoad();
        }

        if (GUILayout.Button("Save"))
        {
            OnSaveAnim();
        }

        if (GUILayout.Button("RegenerateGUID"))
        {
            OnRegenerateGUID();
        }
    }

    void OnRegenerateGUID()
    {
        string path = "Assets/AssetBundle/data";
        path = Path.GetFullPath(".") + Path.DirectorySeparatorChar + path;
        UnityGuidRegenerator.UnityGuidRegenerator reg = new UnityGuidRegenerator.UnityGuidRegenerator(path);
        if (reg != null)
        {
            reg.RegenerateGuids();
        }

    }

    void OnSaveAnim()
    {
        AssetBundle.UnloadAllAssetBundles(true);
        var ab = AssetBundle.LoadFromFile(bundleName);
        if (ab == null)
        {
            return;
        }

        string realPath = "Assets/GE/Integrations/Prefabs/BattleUnits/Militia.prefab";
        var obj = ab.LoadAsset(realPath) as GameObject;

        var anim = obj.GetComponentInChildren<Animator>();
        if (anim != null)
        {
            var ctrl = anim.runtimeAnimatorController;
            if (ctrl != null)
            {
                var clips = ctrl.animationClips;
                AnimationClip[] toClips = new AnimationClip[2];
                foreach (var cl in clips)
                {
                    Debug.LogWarning($"clip name = {cl.name}");
                    if (cl.name == "Idle")
                    {
                        toClips[0] = AnimationClip.Instantiate(cl);
                    }
                    if (cl.name == "Attack")
                    {
                        toClips[1] = AnimationClip.Instantiate(cl);
                    }
                }

                var testctrl = AnimatorController.CreateAnimatorControllerAtPath("Assets/AssetBundle/data.testctrl.controller");
                //testctrl.AddMotion(new Motion());
                testctrl.AddParameter("Idle", AnimatorControllerParameterType.Trigger);
                testctrl.AddParameter("Attack", AnimatorControllerParameterType.Trigger);

                var layer = testctrl.layers[0];
                var stmachine = layer.stateMachine;

                var idleSt = stmachine.AddState("Idle", new Vector3(stmachine.entryPosition.x + 500, stmachine.entryPosition.y + 100, 0));
                idleSt.motion = toClips[0];
                if (idleSt.motion != null)
                {
                    Debug.LogWarning($"idle st motion name = {idleSt.motion.name}");
                }

                var atSt = stmachine.AddState("Attack", new Vector3(stmachine.entryPosition.x + 500, stmachine.entryPosition.y - 100, 0));
                atSt.motion = toClips[1];
                if (atSt.motion != null)
                {
                    Debug.LogWarning($"at st motion name = {atSt.motion.name}");
                }

                string animPath = "Assets/AssetBundle/anim.controller";
                var tmp = AnimatorController.Instantiate(testctrl);
                Debug.LogWarning("save anim");
                AssetDatabase.CreateAsset(tmp, animPath);


            }

            if (anim.avatar != null)
            {
                string avatarPath = "Assets/AssetBundle/avatar.asset";
                Avatar av = Avatar.Instantiate(anim.avatar);
                av.name = anim.name;
                Debug.LogWarning("save avatar");
                AssetDatabase.CreateAsset(av, avatarPath);
            }
            
        }
    }

    void OnLoad()
    {
        Debug.LogWarning("run loader");
        AssetBundle.UnloadAllAssetBundles(true);
        var ab = AssetBundle.LoadFromFile(bundleName);
        if (ab != null)
        {
            string realPath = "Assets/GE/Integrations/Prefabs/BattleUnits/Militia.prefab";
            var obj = ab.LoadAsset(realPath);
            instance = AssetBundle.Instantiate(obj) as GameObject;

        }
    }
}
