using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Read-only diagnostics: never moves the player or creates a local boss.
[InitializeOnLoad]
public static class BossConnectionDiagnostics
{
    private static double nextPoll;
    private static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    static BossConnectionDiagnostics() { EditorApplication.update += Poll; }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        string request = Path.Combine(Root, "boss-diagnostics.request");
        if (!File.Exists(request)) return;
        File.Delete(request);
        Capture();
    }

    [MenuItem("Retry/Diagnostics/Capture Boss Connection")]
    public static void Capture()
    {
        var report = new StringBuilder();
        report.AppendLine($"UTC={System.DateTime.UtcNow:O} playing={Application.isPlaying} project={Root}");
        foreach (var bs in Resources.FindObjectsOfTypeAll<NetworkBootstrap>())
        {
            if (!bs.gameObject.scene.IsValid()) continue;
            report.AppendLine($"Bootstrap={bs.name} active={bs.isActiveAndEnabled} scene={bs.gameObject.scene.path}");
            report.AppendLine($"SessionConnected={bs.Session?.IsConnected} GameStarted={bs.SessionGameStarted} Online={NetworkRuntime.IsOnline}");
            report.AppendLine($"PlayerPosition={(bs.LocalPlayer != null ? bs.LocalPlayer.transform.position.ToString("F2") : "MISSING")}");
            var attack = bs.LocalPlayer != null ? bs.LocalPlayer.GetComponent<Player_Attack>() : null;
            bool canRequest = bs.TryGetAttackWeaponKind(attack, out int kind);
            report.AppendLine($"EquipmentSyncReceived={bs.HasServerEquipment} attackWeaponAvailable={canRequest} kind={kind} pendingTeleport={bs.IsAwaitingDebugTeleport}");
            if (bs.LocalPlayer != null) report.AppendLine($"AttackRequestsSent={bs.LocalPlayer.GetComponent<LocalPlayerAttackSender>()?.RequestsSent}");
            report.AppendLine($"BossPrefab={(bs.RemoteMonsters != null && bs.RemoteMonsters.BossPrefab != null ? AssetDatabase.GetAssetPath(bs.RemoteMonsters.BossPrefab) : "MISSING")}");
            if (bs.RemoteMonsters != null)
                foreach (Transform monster in bs.RemoteMonsters.transform)
                    report.AppendLine($"RemoteMonster={monster.name} position={monster.position:F2} active={monster.gameObject.activeInHierarchy}");
        }
        foreach (var dungeon in Resources.FindObjectsOfTypeAll<DungeonGenerator_ChunkMesh>())
        {
            if (!dungeon.gameObject.scene.IsValid()) continue;
            report.AppendLine($"Dungeon={dungeon.name} rooms={dungeon.Rooms.Count} seed={dungeon.seed} localRandomize={dungeon.randomizeSeedOnGenerate} worldOrigin={dungeon.TileToWorld(Vector3Int.zero)}");
            foreach (var room in dungeon.Rooms)
            {
                if (room.type != RoomType.Boss) continue;
                Vector3 min = dungeon.TileToWorld(room.bounds.min);
                Vector3 max = dungeon.TileToWorld(room.bounds.max);
                report.AppendLine($"BossRoom={room.id} min={min:F2} max={max:F2} floorTiles={room.floorTiles.Count}");
            }
        }
        string folder = Path.Combine(Root, "Artifacts");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "boss-connection-diagnostics.txt"), report.ToString());
        Debug.Log("[BossDiagnostics]\n" + report);
    }
}
