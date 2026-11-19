using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class LocalBossBuilder
{
    public const string PrefabPath="Assets/CombatContent/Prefabs/DungeonGuardianOffline.prefab";
    public const string ScenePath="Assets/Scenes/BossBattleOffline.unity";
    [MenuItem("Tools/Retry/Boss/Open Offline Battle")]
    public static void Open()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) return;
        if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(ScenePath);
    }
    public static void Build()
    {
        for(int i=0;i<SceneManager.sceneCount;i++) if(SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scenes before building local boss content.");
        var previous=EditorSceneManager.GetSceneManagerSetup();
        var source=PrefabUtility.LoadPrefabContents("Assets/CombatContent/Prefabs/DungeonGuardian.prefab");
        try
        {
            if(source.GetComponent<LocalBossController>()==null) source.AddComponent<LocalBossController>();
            source.GetComponent<LootContainer>().enabled=true;
            var health=new SerializedObject(source.GetComponent<Monster_State>());
            health.FindProperty("monsterType").enumValueIndex=(int)MonsterType.Boss;
            health.FindProperty("maxHp").intValue=2500; health.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(source,PrefabPath);
        }
        finally {PrefabUtility.UnloadPrefabContents(source);}
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        try
        {
            var sample=EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            foreach(var root in sample.GetRootGameObjects()) foreach(var spawner in root.GetComponentsInChildren<MonsterSpawner>(true))
            {
                var serialized=new SerializedObject(spawner); serialized.FindProperty("localBossPrefab").objectReferenceValue=prefab; serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.SaveScene(sample);
            var arena=EditorSceneManager.OpenScene("Assets/Scenes/DefensePractice.unity");
            foreach(var root in arena.GetRootGameObjects())
                if(root.GetComponent<OfflineDefensePractice>()!=null || root.name=="Offline Melee Practice Dummy") UnityEngine.Object.DestroyImmediate(root);
            var player=UnityEngine.Object.FindFirstObjectByType<Player_State>(); player.transform.position=new Vector3(0,.05f,-5);
            var boss=(GameObject)PrefabUtility.InstantiatePrefab(prefab,arena); boss.transform.position=new Vector3(0,0,3);
            var ai=boss.GetComponent<LocalBossController>(); ai.Arena=new Bounds(Vector3.zero,new Vector3(19,30,19));
            PrefabUtility.RecordPrefabInstancePropertyModifications(ai);
            // Short standalone encounter; the dungeon boss has 2500 HP.
            var hp=new SerializedObject(boss.GetComponent<Monster_State>()); hp.FindProperty("maxHp").intValue=600; hp.ApplyModifiedPropertiesWithoutUndo();
            var camera=UnityEngine.Object.FindFirstObjectByType<Camera>(); camera.transform.position=new Vector3(0,17,-14); camera.transform.LookAt(Vector3.zero); camera.fieldOfView=60;
            var hud=new GameObject("Offline Boss HUD").AddComponent<LocalBossArenaHud>(); hud.Player=player; hud.Boss=ai;
            EditorSceneManager.SaveScene(arena,ScenePath);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Artifacts/LocalBoss");
            File.WriteAllText("Artifacts/LocalBoss/content.txt","PASS: local boss prefab, SampleScene spawner reference and playable offline arena saved. Arena HP 600; dungeon HP 2500.\n");
        }
        finally {EditorSceneManager.RestoreSceneManagerSetup(previous);}
    }
}
