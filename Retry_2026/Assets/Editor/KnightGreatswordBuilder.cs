using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class KnightGreatswordBuilder
{
    public const string Folder = "Assets/CombatContent/KnightGreatsword";
    public const string KnightPath = "Assets/Resources/KnightElite.prefab";
    public const string KnightModel = "Assets/GameObjects/Monster/knight/knight.fbx";
    public const string SwordModel = "Assets/GameObjects/Player/Player_Model/chA/greatSword.fbx";
    public const string SwordAnimation = "Assets/GameObjects/Player/Player_Model/chA/greatSwordAni.fbx";
    public const string PlayerController = "Assets/GameObjects/Player/Player_Animation/Player_AnimCtrler.controller";
    public const string PracticeScene = "Assets/Scenes/KnightGreatswordPractice.unity";

    [MenuItem("Retry/Knight and Greatsword/Build Content")]
    public static void Build()
    {
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        BuildItem();
        BuildPlayerAnimation();
        AssetDatabase.SaveAssets();
        foreach (var path in new[] { "Assets/Prefabs/LocalPlayerPrefab.prefab", "Assets/Prefabs/RemotePlayerPrefab.prefab" })
        {
            Backup(path);
            var root = PrefabUtility.LoadPrefabContents(path);
            try { AddGreatsword(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        BuildKnight();
        CombatRefinementBuilder.Build();
        KnightLocomotionBuilder.Build();
        PatchOfflinePlayers();
        BuildPractice();
        AssetDatabase.SaveAssets();
        Debug.Log("KnightGreatsword content built successfully");
    }

    static void BuildItem()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/Resources/ItemCatalog.asset");
        Backup(AssetDatabase.GetAssetPath(catalog));
        var item = AssetDatabase.LoadAssetAtPath<EquipmentItemData>(Folder + "/Greatsword.asset");
        if (item == null) { item = ScriptableObject.CreateInstance<EquipmentItemData>(); AssetDatabase.CreateAsset(item, Folder + "/Greatsword.asset"); }
        var definition = ABItemDefinitions.All.First(i => i.id == "greatsword_001");
        Set(item, "itemId", definition.id); Set(item, "displayName", definition.label);
        Set(item, "description", definition.description); Set(item, "weaponId", GreatswordWeapon.Id);
        Set(item, "itemType", (int)ItemType.Weapon); Set(item, "rarity", (int)ItemRarity.Rare);
        Set(item, "equipmentSlot", (int)EquipmentSlotType.Weapon);
        Set(item, "extractionValue", 450); Set(item, "maxStack", 1);
        Set(item, "icon", ABIconBuilder.Get(definition));
        var items = catalog.Items.ToList();
        if (!items.Contains(item)) items.Add(item);
        catalog.EditorSetItems(items); EditorUtility.SetDirty(catalog);
        if (!catalog.ValidateCatalog()) throw new Exception("Invalid catalog after adding greatsword");
    }

    static AnimationClip CopyClip(string sourcePath, string sourceName, string name, bool loop)
    {
        var source = AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<AnimationClip>().First(c => c.name == sourceName);
        string path = Folder + "/" + name + ".anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = UnityEngine.Object.Instantiate(source); AssetDatabase.CreateAsset(clip, path); }
        else EditorUtility.CopySerialized(source, clip);
        clip.name = name;
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop; settings.loopBlend = loop;
        settings.keepOriginalOrientation = true; settings.loopBlendOrientation = true;
        settings.keepOriginalPositionY = true; settings.loopBlendPositionY = true; settings.loopBlendPositionXZ = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
        EditorUtility.SetDirty(clip);
        return clip;
    }

    static void BuildPlayerAnimation()
    {
        Backup(PlayerController);
        var clip = CopyClip(SwordAnimation, "greatSword_Attack", "GreatswordCleave", false);
        if (!clip.humanMotion) throw new Exception("Greatsword animation must use the player humanoid avatar");
        // Keep authored upper/lower body relationships, but never translate the gameplay root.
        foreach (string axis in new[] { "x", "z" })
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT." + axis), AnimationCurve.Constant(0, clip.length, 0));
        AnimationUtility.SetAnimationEvents(clip, new[] {
            new AnimationEvent { functionName="AnimEvent_BeginSwing", time=clip.length * GreatswordWeapon.SwingStart / GreatswordWeapon.AttackSeconds },
            new AnimationEvent { functionName="AnimEvent_EndSwing", time=clip.length * (GreatswordWeapon.SwingStart + GreatswordWeapon.SwingDuration) / GreatswordWeapon.AttackSeconds }
        });
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerController);
        var machine = controller.layers[0].stateMachine;
        var attacks = machine.stateMachines.FirstOrDefault(m => m.stateMachine.name == "test").stateMachine ?? machine;
        var state = attacks.states.Select(s => s.state).FirstOrDefault(s => s.name == "greatsword_attack_1") ?? attacks.AddState("greatsword_attack_1");
        var locomotion = machine.states.First(s => s.state.name == "Blend Tree").state;
        Configure(state, clip, GreatswordWeapon.AttackSeconds, "Attack", locomotion);
        EditorUtility.SetDirty(controller);
    }

    static void Configure(AnimatorState state, AnimationClip clip, float seconds, string tag, AnimatorState next)
    {
        state.motion = clip; state.speed = clip.length / seconds; state.tag = tag; state.speedParameterActive = false;
        foreach (var transition in state.transitions.ToArray()) state.RemoveTransition(transition);
        if (next != null)
        {
            var transition = state.AddTransition(next);
            transition.hasExitTime = true; transition.exitTime = 1; transition.hasFixedDuration = true; transition.duration = .08f;
        }
        EditorUtility.SetDirty(state);
    }

    public static void AddGreatsword(GameObject root)
    {
        if (root.GetComponentInChildren<GreatswordWeapon>(true) != null) return;
        var ordinary = root.GetComponentInChildren<test_sword>(true);
        if (ordinary == null) throw new Exception("Missing original sword socket on " + root.name);
        var originalBlade = ordinary.GetComponentInChildren<WeaponHitbox>(true).transform;
        var holder = new GameObject("Greatsword"); holder.transform.SetParent(ordinary.transform.parent, false);
        var weapon = holder.AddComponent<GreatswordWeapon>();
        var blade = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SwordModel));
        blade.name = "Greatsword Blade"; blade.transform.SetParent(holder.transform, false);
        blade.transform.localPosition = originalBlade.localPosition;
        blade.transform.localRotation = originalBlade.localRotation;
        var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Greatsword.mat");
        if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, Folder + "/Greatsword.mat"); }
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/GameObjects/Player/Player_Model/chA/greatSword.png"));
        material.SetColor("_BaseColor", Color.white); material.SetFloat("_Metallic", .45f); material.SetFloat("_Smoothness", .32f);
        blade.GetComponent<Renderer>().sharedMaterial = material; EditorUtility.SetDirty(material);
        var bounds = blade.GetComponent<MeshFilter>().sharedMesh.bounds;
        var box = blade.AddComponent<BoxCollider>(); box.center = bounds.center; box.size = bounds.size + new Vector3(.001f, 0, .002f); box.isTrigger = true; box.enabled = false;
        var hitbox = blade.AddComponent<WeaponHitbox>(); Set(hitbox, "hitboxCollider", box);
        Set(hitbox, "forwardCleave", true); Set(hitbox, "cleaveLength", 3.5f); Set(hitbox, "cleaveWidth", 3f);
        Set(weapon, "weaponHitbox", hitbox);
        var a = new GameObject("Blade Trail Base").transform; a.SetParent(blade.transform, false); a.localPosition = new Vector3(0, Mathf.Max(0, bounds.min.y + bounds.size.y * .22f), 0);
        var b = new GameObject("Blade Trail Tip").transform; b.SetParent(blade.transform, false); b.localPosition = new Vector3(0, bounds.max.y, 0);
        var trailObject = new GameObject("Greatsword Swing Trail"); trailObject.transform.SetParent(holder.transform, false);
        var trail = trailObject.AddComponent<WeaponSwingTrail>();
        Set(trail, "bladeBase", a); Set(trail, "bladeTip", b);
        var oldTrail = ordinary.GetComponentInChildren<WeaponSwingTrail>(true);
        var oldSerialized = new SerializedObject(oldTrail);
        Set(trail, "trailMaterial", oldSerialized.FindProperty("trailMaterial").objectReferenceValue);
        Set(trail, "baseColor", new Color(.75f,.86f,1,.7f)); Set(trail, "edgeColor", new Color(.95f,.68f,.2f,.3f));
        var hb = new SerializedObject(hitbox); hb.FindProperty("swingTrails").arraySize = 1;
        hb.FindProperty("swingTrails").GetArrayElementAtIndex(0).objectReferenceValue = trail; hb.ApplyModifiedPropertiesWithoutUndo();
        weapon.CorrectBladeFacing();
        holder.SetActive(false);
    }

    static void BuildKnight()
    {
        string controllerPath = Folder + "/KnightElite.controller";
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        if (controller.layers.Length == 0) controller.AddLayer("Base Layer");
        foreach (var layer in controller.layers) foreach (var child in layer.stateMachine.states) layer.stateMachine.RemoveState(child.state);
        if (!controller.parameters.Any(p => p.name == "Mspeed")) controller.AddParameter("Mspeed", AnimatorControllerParameterType.Float);
        var idle = KnightClip("knight_Idle", "KnightIdle", true);
        var walk = KnightClip("knight_Walk", "KnightWalk", true);
        var attackClip = KnightClip("knight_Attack", "KnightAttack", false);
        var hit = KnightClip("knight_Attacked", "KnightHit", false);
        KnightDeathBuilder.Build();
        var death = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/KnightDeath.anim");
        AnimationUtility.SetAnimationEvents(attackClip, new[] {
            new AnimationEvent { functionName="AnimEvent_BeginSwing", time=attackClip.length * .72f / 1.35f },
            new AnimationEvent { functionName="AnimEvent_EndSwing", time=attackClip.length * 1.08f / 1.35f }
        });
        var machine = controller.layers[0].stateMachine;
        var locomotion = machine.AddState("Locomotion");
        var blend = new BlendTree { name="Knight Locomotion", blendParameter="Mspeed", useAutomaticThresholds=false };
        AssetDatabase.AddObjectToAsset(blend, controller); blend.AddChild(idle, 0); blend.AddChild(walk, 3.5f); locomotion.motion = blend;
        machine.defaultState = locomotion;
        Configure(machine.AddState("Attack"), attackClip, 1.35f, "Attack", locomotion);
        Configure(machine.AddState("Hit"), hit, .38f, "Hit", locomotion);
        Configure(machine.AddState("Die"), death, 1.4f, "Death", null);
        CombatHUDContent.BuildKnightAttack(controller);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        var root = new GameObject("KnightElite");
        try
        {
            root.layer = LayerMask.NameToLayer("Monster"); if(root.layer<0)root.layer=0;
            root.transform.localScale = Vector3.one * 1.3f;
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(KnightModel));
            model.name="Knight Model"; model.transform.SetParent(root.transform, false); model.transform.localScale=Vector3.one*2.1f;
            var animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController=controller; animator.applyRootMotion=false; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            model.AddComponent<KnightAnimationEvents>();
            var state = root.AddComponent<Monster_State>();
            Set(state,"monsterType",(int)MonsterType.Elite); Set(state,"maxHp",300); Set(state,"attackPower",16); Set(state,"defense",0);
            var collider=root.AddComponent<CapsuleCollider>(); collider.center=Vector3.up*1.05f; collider.height=2.1f; collider.radius=.46f;
            var nav=root.AddComponent<NavMeshAgent>(); nav.speed=1f; nav.acceleration=14; nav.angularSpeed=360; nav.radius=.46f; nav.height=2.1f; nav.stoppingDistance=2f;
            var ai=root.AddComponent<Monster_Skull>(); Set(ai,"monsterId","knight_elite"); Set(ai,"detectRange",18f); Set(ai,"attackRange",2.5f); Set(ai,"moveSpeed",1f); Set(ai,"wanderRadius",3f);
            var attack=root.AddComponent<Monster_Attack>(); Set(attack,"monster",ai); Set(attack,"monsterState",state); Set(attack,"attackCooldown",3.5f);
            var hitRoot=new GameObject("Knight Sword Hitbox"); hitRoot.transform.SetParent(root.transform,false); hitRoot.layer=root.layer;
            var hitCollider=hitRoot.AddComponent<BoxCollider>(); hitCollider.center=new Vector3(0,1.05f,1.25f); hitCollider.size=new Vector3(1.3f,1.8f,2.5f); hitCollider.isTrigger=true; hitCollider.enabled=false;
            var body=hitRoot.AddComponent<Rigidbody>(); body.isKinematic=true; body.useGravity=false;
            var hitbox=hitRoot.AddComponent<MonsterHitbox>(); Set(hitbox,"hitboxCollider",hitCollider); Set(hitbox,"targetLayers",LayerMask.GetMask("Player"));
            var serializedAttack=new SerializedObject(attack); serializedAttack.FindProperty("attackHitboxes").arraySize=1;
            serializedAttack.FindProperty("attackHitboxes").GetArrayElementAtIndex(0).objectReferenceValue=hitbox; serializedAttack.ApplyModifiedPropertiesWithoutUndo();
            var animation=root.AddComponent<Monster_Animation>(); Set(animation,"animator",animator); Set(animation,"monsterAttack",attack); Set(animation,"monsterState",state); Set(animation,"navMeshAgent",nav);
            var loot=root.AddComponent<MonsterLoot>(); Set(loot,"monsterState",state); Set(loot,"lootContainer",root.GetComponent<LootContainer>());
            var serializedLoot=new SerializedObject(loot); var drops=serializedLoot.FindProperty("drops"); drops.arraySize=2;
            ItemData[] items={AssetDatabase.LoadAssetAtPath<EquipmentItemData>(Folder+"/Greatsword.asset"),ItemCatalog.Load().Resolve("rare_gem")};
            for(int i=0;i<2;i++) {var drop=drops.GetArrayElementAtIndex(i);drop.FindPropertyRelative("item").objectReferenceValue=items[i];drop.FindPropertyRelative("minAmount").intValue=1;drop.FindPropertyRelative("maxAmount").intValue=1;drop.FindPropertyRelative("dropChance").floatValue=1;}
            serializedLoot.ApplyModifiedPropertiesWithoutUndo();
            root.AddComponent<HitEffectReceiver>();
            var hpSource=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Monster1.prefab").transform.Find("Monster HP Bar");
            var hp=UnityEngine.Object.Instantiate(hpSource.gameObject,root.transform); hp.name="Knight HP Bar"; hp.transform.localPosition=Vector3.up*2.4f;
            var healthBar=hp.GetComponent<MonsterHealthBarUI>(); Set(healthBar,"monsterState",state); Set(healthBar,"root",hp);
            PrefabUtility.SaveAsPrefabAsset(root,KnightPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static AnimationClip KnightClip(string name,string target,bool loop)
    {
        var clip=CopyClip(KnightModel,name,target,loop);
        foreach(var binding in AnimationUtility.GetCurveBindings(clip))
            if(binding.path=="Armature/Hips" && (binding.propertyName=="m_LocalPosition.x" || binding.propertyName=="m_LocalPosition.z"))
                AnimationUtility.SetEditorCurve(clip,binding,AnimationCurve.Constant(0,clip.length,0));
        return clip;
    }

    static void PatchOfflinePlayers()
    {
        // SampleScene contains an older embedded player as well as the online prefab path.
        var setup=EditorSceneManager.GetSceneManagerSetup();
        try
        {
            const string path="Assets/Scenes/SampleScene.unity"; Backup(path);
            var scene=EditorSceneManager.OpenScene(path);
            foreach(var root in scene.GetRootGameObjects())
            {
                foreach(var player in root.GetComponentsInChildren<Player_Attack>(true)) AddGreatsword(player.gameObject);
                foreach(var spawner in root.GetComponentsInChildren<MonsterSpawner>(true)) Set(spawner,"knightElitePrefab",AssetDatabase.LoadAssetAtPath<GameObject>(KnightPath));
            }
            EditorSceneManager.SaveScene(scene);
        }
        finally {RestoreScenes(setup);}
    }

    static void BuildPractice()
    {
        var setup=EditorSceneManager.GetSceneManagerSetup();
        try
        {
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/DefensePractice.unity");
            foreach(var root in scene.GetRootGameObjects())
                if(root.GetComponent<OfflineDefensePractice>()!=null || root.name=="Offline Melee Practice Dummy") UnityEngine.Object.DestroyImmediate(root);
            var player=UnityEngine.Object.FindFirstObjectByType<Player_State>(); AddGreatsword(player.gameObject); player.transform.position=new Vector3(0,.05f,-5);
            var equipment=player.GetComponent<PlayerEquipment>(); Set(equipment,"applyStartingLoadout",true); Set(equipment,"startingWeaponItem",AssetDatabase.LoadAssetAtPath<EquipmentItemData>(Folder+"/Greatsword.asset"));
            var knight=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(KnightPath),scene); knight.transform.position=new Vector3(0,0,3);
            var practice=new GameObject("Knight Greatsword Practice").AddComponent<KnightGreatswordPractice>(); practice.Player=player.gameObject; practice.Knight=knight;
            var camera=UnityEngine.Object.FindFirstObjectByType<Camera>(); camera.transform.position=new Vector3(0,14,-13); camera.transform.LookAt(Vector3.up); camera.fieldOfView=60;
            EditorSceneManager.SaveScene(scene,PracticeScene);
        }
        finally {RestoreScenes(setup);}
    }

    [MenuItem("Retry/Knight and Greatsword/Open Practice")]
    public static void OpenPractice()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())EditorSceneManager.OpenScene(PracticeScene);
    }

    static void RestoreScenes(SceneSetup[] setup)
    {
        if (setup.Any(s=>s.isLoaded && s.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup);
        else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
    }

    public static void Set(UnityEngine.Object target,string name,object value)
    {
        var serialized=new SerializedObject(target); var property=serialized.FindProperty(name);
        if(property==null)throw new Exception(target.name+" missing property "+name);
        if(value is UnityEngine.Object o)property.objectReferenceValue=o;
        else if(value is int i)property.intValue=i;
        else if(value is float f)property.floatValue=f;
        else if(value is bool b)property.boolValue=b;
        else if(value is string s)property.stringValue=s;
        else if(value is Color color)property.colorValue=color;
        else if(value==null)property.objectReferenceValue=null;
        else throw new Exception("Unsupported value "+name);
        serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(target);
    }
    static void Backup(string path)
    {
        var destination=Path.Combine("..","revision_backup_KnightGreatsword_20261116","Retry_2026",path);
        if(File.Exists(destination))return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Copy(path,destination);
    }
}
