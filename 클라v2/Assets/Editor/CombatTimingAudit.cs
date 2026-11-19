using UnityEditor;using UnityEngine;using System.IO;using System.Linq;
public static class CombatTimingAudit {
 public static void Run(){var clip=AssetDatabase.LoadAllAssetsAtPath("Assets/GameObjects/Monster/goblin/goblin_Attack.fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__"));File.WriteAllText("Artifacts/attack-timing.txt",$"length={clip.length} fps={clip.frameRate}\n"+string.Join("\n",clip.events.Select(e=>$"{e.functionName}={e.time}")));}
}
