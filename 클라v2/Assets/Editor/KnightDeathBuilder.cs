using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

public static class KnightDeathBuilder
{
    // Bake the project's authored backward fall onto the knight's generic skeleton.
    // The supplied knight_Die take only bends its knees and never reaches the floor.
    public static void Build()
    {
        string tempPath = AssetDatabase.GenerateUniqueAssetPath("Assets/Editor/KnightDeathRetarget.fbx");
        GameObject model = null;
        PlayableGraph graph = default;
        try
        {
            AssetDatabase.CopyAsset(KnightGreatswordBuilder.KnightModel, tempPath);
            var importer = (ModelImporter)AssetImporter.GetAtPath(tempPath);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.preserveHierarchy = true;
            importer.importAnimation = false;
            importer.SaveAndReimport();
            model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(tempPath));
            var animator = model.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new Exception("Knight retarget avatar is invalid");
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var source = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/PlayerDefense/Animations/Combat/PlayerDeath.anim");
            graph = PlayableGraph.Create("Bake Knight Death"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var playable = AnimationClipPlayable.Create(graph, source);
            AnimationPlayableOutput.Create(graph, "Death", animator).SetSourcePlayable(playable); graph.Play();
            var bones = model.GetComponentsInChildren<Transform>().Where(t => t != model.transform).ToArray();
            var positions = bones.Select(_ => new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() }).ToArray();
            var rotations = bones.Select(_ => new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() }).ToArray();
            var previous = new Quaternion[bones.Length];
            for (int frame = 0; frame <= 42; frame++)
            {
                float time = frame / 30f;
                playable.SetTime(source.length * frame / 42d); graph.Evaluate(0);
                for (int i = 0; i < bones.Length; i++)
                {
                    var p = bones[i].localPosition; var q = bones[i].localRotation;
                    if (frame > 0 && Quaternion.Dot(previous[i], q) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    previous[i] = q;
                    for (int axis = 0; axis < 3; axis++) positions[i][axis].AddKey(time, p[axis]);
                    for (int axis = 0; axis < 4; axis++) rotations[i][axis].AddKey(time, q[axis]);
                }
            }
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(KnightGreatswordBuilder.Folder + "/KnightDeath.anim");
            if (clip == null)
            {
                clip = new AnimationClip { name = "KnightDeath" };
                AssetDatabase.CreateAsset(clip, KnightGreatswordBuilder.Folder + "/KnightDeath.anim");
            }
            clip.ClearCurves(); clip.frameRate = 30;
            for (int i = 0; i < bones.Length; i++)
            {
                string path = AnimationUtility.CalculateTransformPath(bones[i], model.transform);
                for (int axis = 0; axis < 3; axis++) AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition." + "xyz"[axis]), Reduce(positions[i][axis], .0002f));
                for (int axis = 0; axis < 4; axis++) AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation." + "xyzw"[axis]), Reduce(rotations[i][axis], .0005f));
            }
            clip.EnsureQuaternionContinuity(); clip.wrapMode = WrapMode.ClampForever;
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0; settings.stopTime = 1.4f; settings.loopTime = settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
            EditorUtility.SetDirty(clip); AssetDatabase.SaveAssets();
        }
        finally
        {
            if (graph.IsValid()) graph.Destroy();
            if (model != null) Object.DestroyImmediate(model);
            AssetDatabase.DeleteAsset(tempPath);
        }
    }

    public static AnimationCurve Reduce(AnimationCurve curve, float tolerance)
    {
        var keys = curve.keys;
        var keep = new SortedSet<int> { 0, keys.Length - 1 };
        void Split(int first, int last)
        {
            float maximum = tolerance; int selected = -1;
            for (int i = first + 1; i < last; i++)
            {
                float t = (keys[i].time - keys[first].time) / (keys[last].time - keys[first].time);
                float error = Mathf.Abs(keys[i].value - Mathf.Lerp(keys[first].value, keys[last].value, t));
                if (error > maximum) { maximum = error; selected = i; }
            }
            if (selected < 0) return;
            keep.Add(selected); Split(first, selected); Split(selected, last);
        }
        Split(0, keys.Length - 1);
        var result = new AnimationCurve(keep.Select(i => keys[i]).ToArray());
        for (int i = 0; i < result.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(result, i, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(result, i, AnimationUtility.TangentMode.Linear);
        }
        return result;
    }
}
