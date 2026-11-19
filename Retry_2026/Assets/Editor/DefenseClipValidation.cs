using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class DefenseClipValidation
{
    const string Key="Retry.DefenseClips.Test";
    [Serializable] private class SceneSetup { public UnityEditor.SceneManagement.SceneSetup[] scenes; }
    static DefenseClipValidation() { EditorApplication.playModeStateChanged+=OnMode; }
    public static void Start()
    {
        for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
            if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene changes before validation.");
        SessionState.SetString(Key+".scenes",JsonUtility.ToJson(new SceneSetup{scenes=EditorSceneManager.GetSceneManagerSetup()}));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        SessionState.SetBool(Key,true); EditorApplication.EnterPlaymode();
    }
    static void OnMode(PlayModeStateChange mode)
    {
        if(!SessionState.GetBool(Key,false)) return;
        if(mode==PlayModeStateChange.EnteredPlayMode)
        {
            try { Validate(); File.WriteAllText("Artifacts/DefenseClips/runtime.txt","PASS: actual PlayMode Roll retarget; 8 directions with rotated actor, initial visual facing aligns with dash, full pelvis offset rotates with body, no accumulation, root position/rotation preserved and cancel restores visual transform. Sword_Block remains disabled. Offline fixture; no multiplayer test.\n"); }
            catch(Exception ex) { File.WriteAllText("Artifacts/DefenseClips/runtime.txt","FAIL\n"+ex); Debug.LogException(ex); }
            EditorApplication.ExitPlaymode();
        }
        if(mode==PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key,false);
            if(!Application.isBatchMode)
            {
                var setup=JsonUtility.FromJson<SceneSetup>(SessionState.GetString(Key+".scenes","{}"));
                if(setup.scenes!=null && setup.scenes.Length>0) EditorSceneManager.RestoreSceneManagerSetup(setup.scenes);
                return;
            }
            if(!File.ReadAllText("Artifacts/DefenseClips/runtime.txt").StartsWith("PASS")) {EditorApplication.Exit(1); return;}
            try {ExtractionContentBuild.BuildPlayer(); EditorApplication.Exit(0);}
            catch(Exception ex) {Debug.LogException(ex); EditorApplication.Exit(1);}
        }
    }
    static void Validate()
    {
        var holder=new GameObject("Inactive setup"); holder.SetActive(false);
        var player=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LocalPlayerPrefab.prefab"),holder.transform);
        foreach(var component in player.GetComponentsInChildren<MonoBehaviour>(true)) component.enabled=false;
        player.transform.SetParent(null); player.transform.position=Vector3.zero; player.transform.rotation=Quaternion.identity;
        var animator=player.GetComponent<Animator>(); animator.cullingMode=AnimatorCullingMode.AlwaysAnimate; animator.Rebind();
        var driver=player.GetComponent<PlayerDefenseAnimation>();
        var defense=player.GetComponent<PlayerDefenseController>();
        var begin=typeof(PlayerDefenseController).GetMethod("Begin",BindingFlags.NonPublic|BindingFlags.Instance);
        var started=typeof(PlayerDefenseController).GetField("startedAt",BindingFlags.NonPublic|BindingFlags.Instance);
        float duration=(float)typeof(PlayerDefenseController).GetField("dashDuration",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(defense);
        if(Mathf.Abs(duration-PlayerDefenseController.DefaultDashDuration)>.001f) throw new Exception("Prefab dash duration is stale");
        var stamina=player.AddComponent<PlayerStamina>();
        foreach(int maximum in new[]{100,150})
        {
            typeof(PlayerStamina).GetField("maxStamina",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(stamina,maximum);
            stamina.Refill();
            if(stamina.DashCost!=20 || !stamina.TrySpendDash() || stamina.Current!=maximum-20) throw new Exception("Dash must spend fixed 20");
            typeof(PlayerStamina).GetProperty("Current").GetSetMethod(true).Invoke(stamina,new object[]{19});
            if(stamina.TrySpendDash() || stamina.Current!=19) throw new Exception("Insufficient stamina must not be spent");
        }
        stamina.enabled=false;
        var update=typeof(PlayerDefenseAnimation).GetMethod("Update",BindingFlags.NonPublic|BindingFlags.Instance);
        var camera=new GameObject("Capture Camera").AddComponent<Camera>();
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.06f,.08f,.11f);
        var light=new GameObject("Light").AddComponent<Light>(); light.type=LightType.Directional; light.intensity=2; light.transform.rotation=Quaternion.Euler(40,-30,0);
        var hips=animator.GetBoneTransform(HumanBodyBones.Hips);
        var late=typeof(PlayerDefenseAnimation).GetMethod("LateUpdate",BindingFlags.NonPublic|BindingFlags.Instance);
        var directionSetter=typeof(PlayerDefenseController).GetProperty("DashDirection").GetSetMethod(true);
        foreach(float angle in new[]{0f,45f,90f,135f,180f,225f,270f,315f})
        {
            player.transform.rotation=Quaternion.Euler(0,73,0);
            animator.Rebind(); animator.Play("Blend Tree",0,0); animator.Update(.016f);
            begin.Invoke(defense,new[]{Enum.ToObject(begin.GetParameters()[0].ParameterType,1)});
            var direction=Quaternion.Euler(0,angle,0)*Vector3.forward;
            directionSetter.Invoke(defense,new object[]{direction});
            started.SetValue(defense,Time.time); update.Invoke(driver,null); animator.Update(0);
            var rawPosition=hips.position; var rawRotation=hips.rotation;
            var left=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var right=animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var rawForward=Vector3.Cross(right.position-left.position,Vector3.up).normalized;
            var correction=Quaternion.AngleAxis(Vector3.SignedAngle(rawForward,direction,Vector3.up),Vector3.up);
            late.Invoke(driver,null);
            var facing=Vector3.Cross(right.position-left.position,Vector3.up).normalized;
            if(Vector3.Dot(facing,direction)<.99f) throw new Exception("Roll facing differs from movement: "+angle);
            if(Vector3.Distance(hips.position,player.transform.position+correction*(rawPosition-player.transform.position))>.001f) throw new Exception("Pelvis offset not rotated");
            var once=hips.rotation; late.Invoke(driver,null);
            if(Quaternion.Angle(once,hips.rotation)>.01f) throw new Exception("Yaw accumulated");
            started.SetValue(defense,Time.time-duration*.25f); update.Invoke(driver,null); animator.Update(.016f); late.Invoke(driver,null);
            var delta=hips.rotation*Quaternion.Inverse(once); delta.ToAngleAxis(out float rollAngle,out var rollAxis);
            if(rollAngle>180) rollAxis=-rollAxis;
            var rollForward=Vector3.Cross(rollAxis,Vector3.up).normalized;
            if(Vector3.Dot(rollForward,direction)<.5f) throw new Exception("Somersault rotates away from movement: "+angle+" dot="+Vector3.Dot(rollForward,direction));
            // Re-sample the starting pose for the restoration assertion and direction capture.
            started.SetValue(defense,Time.time); update.Invoke(driver,null); animator.Update(0); late.Invoke(driver,null);
            if(player.transform.position.sqrMagnitude>.00001f || Quaternion.Angle(player.transform.rotation,Quaternion.Euler(0,73,0))>.01f) throw new Exception("Authoritative root changed");
            if(angle==90 || angle==180)
            {
                camera.transform.position=new Vector3(3,2.2f,4); camera.transform.LookAt(new Vector3(0,1,0)); camera.fieldOfView=40;
                CombatContentBuilder.CaptureCamera(camera,"Artifacts/DefenseClips/Direction_"+angle+".png");
            }
            defense.CancelDefense(); update.Invoke(driver,null);
            if(Vector3.Distance(hips.position,rawPosition)>.001f || Quaternion.Angle(hips.rotation,rawRotation)>.01f) throw new Exception("Cancel did not restore pose");
        }
        player.transform.rotation=Quaternion.identity;
        foreach(var action in new[]{1})
        {
            animator.Rebind(); animator.Play("Blend Tree",0,0); animator.Update(.016f);
            begin.Invoke(defense,new[]{Enum.ToObject(begin.GetParameters()[0].ParameterType,action)});
            started.SetValue(defense,Time.time); update.Invoke(driver,null); animator.Update(0);
            var initial=hips.rotation;
            started.SetValue(defense,Time.time-duration*.45f);
            update.Invoke(driver,null); animator.Update(.016f);
            string expected=action==1?PlayerDefenseAnimation.RollState:PlayerDefenseAnimation.ParryState;
            if(!animator.GetCurrentAnimatorStateInfo(action==1?0:animator.GetLayerIndex("Defense Upper Body")).IsName(expected)) throw new Exception("State not selected: "+expected);
            if(action==1 && Quaternion.Angle(initial,hips.rotation)<5) throw new Exception("Roll did not animate hips");
            if(player.transform.position.sqrMagnitude>.00001f || Quaternion.Angle(player.transform.rotation,Quaternion.identity)>.01f) throw new Exception("Animation moved authoritative root");
            camera.transform.position=new Vector3(3,2.2f,4); camera.transform.LookAt(new Vector3(0,1,0)); camera.fieldOfView=40;
            CombatContentBuilder.CaptureCamera(camera,"Artifacts/DefenseClips/"+expected+".png");
            defense.CancelDefense(); update.Invoke(driver,null); animator.Update(.15f);
            if(driver.IsPlaying) throw new Exception("Defense presentation remained active after cancel");
        }
    }
}
