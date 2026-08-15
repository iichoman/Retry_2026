using UnityEngine;

// ============================================================================
//  LocalIdentity
//  본인 클라이언트의 ID와 상태를 보관하는 단일 글로벌 객체.
//  씬 어디서든 LocalIdentity.Instance.LocalClientId 식으로 접근.
//  NetworkBootstrap이 자동으로 생성/관리.
// ============================================================================
public class LocalIdentity : MonoBehaviour
{
    public static LocalIdentity Instance { get; private set; }

    public int LocalClientId { get; private set; } = 0;
    public string PlayerName { get; private set; } = "Player";

    public bool IsConnectedToLobby { get; set; }
    public bool IsConnectedToSession { get; set; }

    public bool IsAuthenticated => LocalClientId > 0;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // DontDestroyOnLoad는 root GameObject에만 동작. 자식이면 스킵.
        if (transform.parent == null)
            DontDestroyOnLoad(gameObject);
    }

    public void SetLocalClientId(int id)
    {
        LocalClientId = id;
        Debug.Log($"[LocalIdentity] LocalClientId 설정됨 = {id}");
    }

    public void SetPlayerName(string name)
    {
        if (!string.IsNullOrWhiteSpace(name)) PlayerName = name;
    }
}