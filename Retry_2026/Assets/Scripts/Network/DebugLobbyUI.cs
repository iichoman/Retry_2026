using UnityEngine;

// ============================================================================
//  DebugLobbyUI 임시 UI
//  화면 좌상단 OnGUI. 방 만들기 / 참가 / 시작.
// ============================================================================
public class DebugLobbyUI : MonoBehaviour
{
    private NetworkBootstrap bootstrap;
    private string roomNameInput = "TestRoom";
    private string roomIdInput = "1";
    private int lastRoomId = 0;
    private bool inRoom = false;
    private string lastResult = "";

    private GUIStyle btnStyle;
    private GUIStyle labelStyle;
    private GUIStyle textFieldStyle;

    private void Start()
    {
        bootstrap = GetComponentInParent<NetworkBootstrap>();
    }

    public void OnRoomResult(bool success, int roomId)
    {
        if (success)
        {
            lastRoomId = roomId;
            inRoom = true;
            lastResult = $"방 입장 성공: roomId={roomId}";
        }
        else
        {
            lastResult = "방 입장 실패";
        }
    }

    private void InitStylesIfNeeded()
    {
        if (btnStyle != null) return;
        btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 18, fixedHeight = 36 };
        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 16 };
        textFieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = 16, fixedHeight = 28 };
    }

    private void OnGUI()
    {
        if (bootstrap == null) return;
        InitStylesIfNeeded();

        const int W = 360;
        const int H = 520;
        GUILayout.BeginArea(new Rect(10, 10, W, H), GUI.skin.box);
        GUILayout.Label("=== Retry Debug Lobby ===", labelStyle);

        var id = bootstrap.Identity;
        if (id != null)
        {
            GUILayout.Label($"clientId: {id.LocalClientId}", labelStyle);
            GUILayout.Label($"lobby:   {(id.IsConnectedToLobby ? "ON" : "OFF")}", labelStyle);
            GUILayout.Label($"session: {(id.IsConnectedToSession ? "ON" : "OFF")}", labelStyle);
        }

        if (id != null && id.IsConnectedToSession)
        {
            GUILayout.Label("게임 진행 중.", labelStyle);
            GUILayout.EndArea();
            return;
        }

        if (id == null || !id.IsAuthenticated)
        {
            GUILayout.Label("로그인 처리 중...", labelStyle);
            GUILayout.EndArea();
            return;
        }

        GUILayout.Space(12);
        GUILayout.Label("방 이름:", labelStyle);
        roomNameInput = GUILayout.TextField(roomNameInput, textFieldStyle);
        if (GUILayout.Button("방 만들기", btnStyle))
        {
            bootstrap.Lobby.SendRoomCreate(roomNameInput);
        }

        GUILayout.Space(8);
        GUILayout.Label("방 ID:", labelStyle);
        roomIdInput = GUILayout.TextField(roomIdInput, textFieldStyle);
        if (GUILayout.Button("방 참가", btnStyle))
        {
            if (int.TryParse(roomIdInput, out int rid))
                bootstrap.Lobby.SendRoomJoin(rid);
        }

        if (inRoom)
        {
            GUILayout.Space(16);
            GUILayout.Label($"현재 방: {lastRoomId}", labelStyle);
            if (GUILayout.Button("게임 시작 (호스트)", btnStyle))
            {
                bootstrap.Lobby.SendGameStart();
            }
        }

        if (!string.IsNullOrEmpty(lastResult))
        {
            GUILayout.Space(10);
            GUILayout.Label(lastResult, labelStyle);
        }

        GUILayout.EndArea();
    }
}