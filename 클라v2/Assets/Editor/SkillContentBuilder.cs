using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class SkillContentBuilder
{
    public static void Build(){
        const string root="Assets/PlayerDefense/Animations/Skills";Directory.CreateDirectory(root);AssetDatabase.Refresh();
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/GameObjects/Player/Player_Animation/Player_AnimCtrler.controller");
        for(int i=0;i<2;i++){
            // Reuse normalized humanoid sword clips already retargeted for this player, with no basic-attack events.
            var source=AssetDatabase.LoadAssetAtPath<AnimationClip>($"Assets/PlayerDefense/Animations/Combat/Sword{(i==0?1:3)}.anim");
            string name=i==0?"SkillE":"SkillQ",path=$"{root}/{name}.anim";
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(clip==null){clip=Object.Instantiate(source);clip.name=name;AssetDatabase.CreateAsset(clip,path);}else EditorUtility.CopySerialized(source,clip);
            AnimationUtility.SetAnimationEvents(clip,new AnimationEvent[0]);
            var machine=controller.layers[0].stateMachine;var state=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name==name)??machine.AddState(name);
            state.motion=clip;state.tag="Skill";state.speed=1;EditorUtility.SetDirty(state);EditorUtility.SetDirty(clip);AssetDatabase.SaveAssetIfDirty(clip);
        }
        EditorUtility.SetDirty(controller);AssetDatabase.SaveAssetIfDirty(controller);
    }
}
