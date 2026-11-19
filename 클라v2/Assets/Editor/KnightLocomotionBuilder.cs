using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

public static class KnightLocomotionBuilder
{
    public const string Output = "Artifacts/KnightLocomotion";
    public static void Build()
    {
        Directory.CreateDirectory(Output);
        var source = AssetDatabase.LoadAllAssetsAtPath(KnightGreatswordBuilder.KnightModel).OfType<AnimationClip>().First(c => c.name == "knight_Walk");
        string clipPath = KnightGreatswordBuilder.Folder + "/KnightWalk.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        EditorUtility.CopySerialized(source, clip);
        clip.name = "KnightWalk";
        // Close the authored first/last pose mismatch, keeping the original hip sway.
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            float closure = curve.Evaluate(clip.length) - curve.Evaluate(0);
            var keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i].value -= closure * keys[i].time / clip.length;
                keys[i].inTangent -= closure / clip.length;
                keys[i].outTangent -= closure / clip.length;
            }
            curve.keys = keys;
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }
        clip.EnsureQuaternionContinuity();
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.startTime = 0; settings.stopTime = source.length; settings.loopTime = true; settings.loopBlend = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
        var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(KnightGreatswordBuilder.KnightModel));
        var distance = new AnimationCurve(new Keyframe(0, 0));
        try
        {
            var feet = model.GetComponentsInChildren<Transform>().Where(t => t.name == "LeftFoot" || t.name == "RightFoot").ToArray();
            if (feet.Length != 2) throw new Exception("Knight feet were not found");
            clip.SampleAnimation(model, 0);
            var previous = feet.Select(t => t.position).ToArray();
            float travelled = 0;
            const int samples = 240;
            for (int i = 1; i <= samples; i++)
            {
                float phase = (float)i / samples;
                clip.SampleAnimation(model, clip.length * phase);
                float backwards = 0;
                float floor = Mathf.Min(feet[0].position.y, feet[1].position.y);
                for (int f = 0; f < 2; f++)
                {
                    if (feet[f].position.y <= floor + .008f)
                        backwards = Mathf.Max(backwards, previous[f].z - feet[f].position.z);
                    previous[f] = feet[f].position;
                }
                travelled += Mathf.Max(0, backwards);
                distance.AddKey(phase, travelled);
            }
            if (travelled < .5f || travelled > 1.5f) throw new Exception("Unexpected knight stride: " + travelled);
            for (int i = 0; i < distance.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(distance, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(distance, i, AnimationUtility.TangentMode.Linear);
            }
            File.WriteAllText(Output + "/stride.txt", $"Source cycle={clip.length:F3}s; reconstructed stride={travelled:F4} model units; elite stride={travelled*2.1f*1.3f:F3}m; chase playback={1f*clip.length/(travelled*2.1f*1.3f):F3}x\n");
        }
        finally { Object.DestroyImmediate(model); }
        string controllerPath = KnightGreatswordBuilder.Folder + "/KnightElite.controller";
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (!controller.parameters.Any(p => p.name == "WalkRate")) controller.AddParameter("WalkRate", AnimatorControllerParameterType.Float);
        var locomotion = controller.layers[0].stateMachine.states.First(s => s.state.name == "Locomotion").state;
        locomotion.speed = 1; locomotion.speedParameter = "WalkRate"; locomotion.speedParameterActive = true;
        var blend = (BlendTree)locomotion.motion;
        var children = blend.children; children[1].motion = clip; children[1].threshold = .15f; blend.children = children;
        var prefab = PrefabUtility.LoadPrefabContents(KnightGreatswordBuilder.KnightPath);
        try
        {
            var animator = prefab.GetComponentInChildren<Animator>();
            var movement = animator.GetComponent<KnightLocomotion>() ?? animator.gameObject.AddComponent<KnightLocomotion>();
            movement.Configure(distance, clip.length);
            animator.applyRootMotion = true; // OnAnimatorMove consumes only the reconstructed walking displacement.
            PrefabUtility.SaveAsPrefabAsset(prefab, KnightGreatswordBuilder.KnightPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        EditorUtility.SetDirty(clip); EditorUtility.SetDirty(controller); EditorUtility.SetDirty(locomotion); EditorUtility.SetDirty(blend);
        AssetDatabase.SaveAssets();
        File.WriteAllLines(Output + "/changed-assets.txt", new[] { clipPath, controllerPath, KnightGreatswordBuilder.KnightPath });
    }
    public static void Inspect()
    {
        Directory.CreateDirectory(Output);
        var clip = AssetDatabase.LoadAllAssetsAtPath(KnightGreatswordBuilder.KnightModel).OfType<AnimationClip>().First(c => c.name == "knight_Walk");
        var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(KnightGreatswordBuilder.KnightModel));
        try
        {
            var report = new StringBuilder($"Walk length={clip.length}, averageSpeed={clip.averageSpeed}\n");
            var bones = model.GetComponentsInChildren<Transform>();
            report.AppendLine(string.Join("\n", bones.Select(t => AnimationUtility.CalculateTransformPath(t, model.transform))));
            foreach (var binding in AnimationUtility.GetCurveBindings(clip).Where(b => b.propertyName.StartsWith("m_LocalPosition") && (b.path.EndsWith("Hips") || b.path == "Armature")))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                report.AppendLine($"{binding.path} {binding.propertyName}: {curve.Evaluate(0)} -> {curve.Evaluate(clip.length)} min={curve.keys.Min(k=>k.value)} max={curve.keys.Max(k=>k.value)}");
            }
            var selected = bones.Where(t => t.name.Contains("Foot") || t.name == "Hips").ToArray();
            for (int i = 0; i <= 29; i++)
            {
                clip.SampleAnimation(model, clip.length * i / 29);
                report.AppendLine($"t={clip.length*i/29:F3} " + string.Join(" ", selected.Select(t => $"{t.name}={t.position.ToString("F4")}")));
            }
            File.WriteAllText(Output + "/source-walk.txt", report.ToString());
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        finally { Object.DestroyImmediate(model); }
    }
}
