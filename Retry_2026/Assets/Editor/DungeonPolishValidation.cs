using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Runs in an isolated preview scene; never switches Play Mode or saves the user's open scene.
[InitializeOnLoad]
public static class DungeonPolishValidation
{
    const string Output = "Artifacts/DungeonPolish";
    static bool running;
    static int checks;
    static DungeonPolishValidation() => EditorApplication.update += Poll;
    static void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
    static async void Poll()
    {
        if (running || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Output + "/validate.request")) return;
        File.Delete(Output + "/validate.request"); running = true;
        try { await Run(); File.WriteAllText(Output + "/validation.txt", $"PASS {checks} checks: storm HP/roll/parry/death, sky-blue weapons, server/client furniture, reward symmetry, foliage, removed banners and reward renders.\n"); }
        catch (Exception e) { File.WriteAllText(Output + "/validation.txt", "FAIL\n" + e); Debug.LogException(e); }
        finally { running = false; }
    }
    public static async Task Run()
    {
        checks = 0;
        Check(Marshal.SizeOf<StormDamage>() == 12, "storm damage wire size");
        var ice = Resources.Load<Material>("AB/Ice").GetColor("_BaseColor");
        Check(Mathf.Abs(ice.r-.3f)<.001f && Mathf.Abs(ice.g-.75f)<.001f && ice.b==1, "ice weapon material is sky blue");
        foreach (string id in new[] { "frost_sword", "frost_greatsword" }) {
            var item = ItemCatalog.Load().Resolve(ItemHash.Of(id));
            Check(item != null && EquipmentPresentation.Accent(item) == ice && SkillPalette.ForWeapon(ItemHash.Of(id)) == ice, "ice sword/greatsword UI and effects use the same sky blue: " + id);
        }
        var source = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<DungeonGenerator_ChunkMesh>(true)).First();
        var scene = EditorSceneManager.NewPreviewScene();
        try {
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/LocalPlayerPrefab.prefab"));
            SceneManager.MoveGameObjectToScene(player, scene);
            foreach (var component in player.GetComponentsInChildren<MonoBehaviour>(true)) component.enabled = false;
            var hp = player.GetComponent<Player_State>(); var defense = player.GetComponent<PlayerDefenseController>();
            hp.SendMessage("Awake"); defense.SendMessage("Awake"); defense.SendMessage("OnDisable"); defense.SendMessage("OnEnable"); defense.SetRemotePresentation();
            int updates=0; hp.HpChanged += (_,__)=>updates++;
            defense.ApplyRemoteDefense(new PlayerPresentation { action=4, serial=1, duration=10, remaining=10, dx=1 });
            hp.NetworkApplyHp(90,false);hp.ApplyABHealth(100,90);
            Check(hp.CurrentHp==90 && !hp.IsHit && defense.IsDashing && updates==2 && !hp.TryConsumeHitAnimationRequest(out _), "storm and repeated HP snapshot preserve roll without requesting hit animation");
            hp.NetworkApplyHp(80);
            Check(hp.IsHit && !defense.IsBusy, "ordinary damage still interrupts defense");
            hp.CancelHitRecovery();defense.ApplyRemoteDefense(new PlayerPresentation { action=5, serial=2, duration=10, remaining=10 });
            hp.NetworkApplyHp(70,false);hp.ApplyABHealth(100,70);
            Check(!hp.IsHit && defense.IsParrying, "storm preserves parry");
            hp.NetworkApplyHp(0,false);Check(hp.IsDead && !defense.IsBusy, "storm death still stops defense");
            Object.DestroyImmediate(player);

            var fixture = new GameObject("Dungeon polish parity");SceneManager.MoveGameObjectToScene(fixture,scene);
            var generator=fixture.AddComponent<DungeonGenerator_ChunkMesh>();EditorUtility.CopySerialized(source,generator);
            generator.randomizeSeedOnGenerate=generator.generateOnStart=false;
            var rows=File.ReadAllLines("Artifacts/RewardRooms/server-rewards.csv").Skip(1).Where(l=>l.Length>0).Select(l=>l.Split(',').Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray()).ToArray();
            foreach(int seed in rows.Select(r=>(int)r[0]).Distinct()) {
                generator.seed=seed;generator.GenerateDataForValidation();
                foreach(var room in generator.Rooms.Where(r=>r.type==RoomType.Reward)) {
                    var slots=DungeonPropLayout.GenerateRoom(generator,room).Where(s=>!s.Supported).ToArray();
                    foreach(var slot in slots) {
                        var tile=generator.WorldToTile(slot.Center);tile.y=0;var mirror=RoomBlueprint.Mirror(room,tile);
                        Check(slots.Any(s=>{var t=generator.WorldToTile(s.Center);t.y=0;return t==mirror && s.Size==slot.Size && s.PropKind==slot.PropKind;}),"reward furniture has a matching partner");
                    }
                }
            }
            generator.seed=12345;generator.GenerateDataForValidation();
            var server=File.ReadAllLines("Artifacts/E/server-furniture.csv").Skip(1).Select(l=>l.Split(',').Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray()).ToArray();
            foreach(var room in generator.Rooms)foreach(var slot in DungeonPropLayout.GenerateRoom(generator,room).Where(s=>!s.Supported))
                Check(server.Any(r=>Mathf.Abs(r[0]-slot.Center.x)<.01f && Mathf.Abs(r[1]-slot.Center.z)<.01f && Mathf.Abs(r[2]-slot.Size.x*.5f-.5f)<.01f && Mathf.Abs(r[3]-slot.Size.z*.5f-.5f)<.01f),"server/client furniture collision parity");
            var dress=fixture.AddComponent<DungeonSetDressing>();dress.Rebuild();
            Check(!fixture.GetComponentsInChildren<Transform>().Any(t=>DungeonSetDressing.IsBanner(t.name)),"no active banner decorations");
            var pockets=fixture.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("FoliagePocket_")).ToArray();
            var scales=pockets.SelectMany(t=>t.Cast<Transform>()).Select(t=>t.localScale.x).ToArray();
            Check(scales.Any(s=>s>1.5f)&&scales.Any(s=>s<1),"foliage includes larger bushes and varied sizes");
            Object.DestroyImmediate(fixture);
            foreach(int kind in new[]{5,8}) {
                var first=rows.First(r=>(int)r[2]==kind);var roomRows=rows.Where(r=>r[0]==first[0]&&r[1]==first[1]).ToArray();
                var render=typeof(RewardRoomValidation).GetMethod("Render",BindingFlags.NonPublic|BindingFlags.Static);
                await (Task)render.Invoke(null,new object[]{source,scene,roomRows,kind});
                string file=kind==8?"trapped-vault.png":"treasure-vault.png";
                File.Copy("Artifacts/RewardRooms/"+file,Output+"/"+file,true);
            }
        } finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}
