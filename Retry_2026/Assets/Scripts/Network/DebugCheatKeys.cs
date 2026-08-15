using UnityEngine;
using UnityEngine.InputSystem;

// ============================================================================
//  DebugCheatKeys  [테스트 전용 — 배포 전 삭제]
//
//  F9 : 탈출 방 근처로 이동 (탈출 자체는 정상 흐름대로 진행)
//
//  위치는 서버가 권위라 클라가 스스로 순간이동하면 되돌려진다.
//  서버가 좌표를 옮기고 PLAYER_MOVE로 통보하는 방식이며,
//  서버 GameSession.cpp의 DEBUG_ALLOW_TELEPORT가 true여야 동작한다.
//
//  이동 후에는 실제로 포탈에 들어가 7초 홀드해야 탈출된다.
//  (검증까지 건너뛰려면 서버의 DEBUG_INSTANT_EXTRACT를 true로)
//
//  NetworkBootstrap이 자식으로 자동 생성한다.
// ============================================================================
public class DebugCheatKeys : MonoBehaviour
{
    [SerializeField] private bool enableCheats = true;

    private NetworkBootstrap bootstrap;

    public void Initialize(NetworkBootstrap bs)
    {
        bootstrap = bs;
    }

    private void Update()
    {
        if (!enableCheats || bootstrap == null) return;

        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (kb.f9Key.wasPressedThisFrame)
        {
            if (bootstrap.Session == null || !bootstrap.Session.IsConnected)
            {
                Debug.LogWarning("[치트] 세션 미연결 — 이동 요청 불가");
                return;
            }

            Debug.Log("<color=yellow>[치트] F9 탈출 방 이동 요청</color>");
            bootstrap.RequestDebugTeleportExit();
        }
    }
}
