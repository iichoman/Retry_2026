using UnityEngine;
using UnityEngine.InputSystem;
using Unity.AI.Navigation;

// Standalone, local-only encounter for inspecting the shipped prefabs and switching weapons.
[DefaultExecutionOrder(-200)]
public sealed class KnightGreatswordPractice : MonoBehaviour
{
    public GameObject Player, Knight;
    private UnityEngine.AI.NavMeshAgent agent;
    private void Awake()
    {
        agent=Knight.GetComponent<UnityEngine.AI.NavMeshAgent>();
        agent.enabled=false;
    }
    private void Start()
    {
        var surface=gameObject.AddComponent<NavMeshSurface>();
        surface.useGeometry=UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask=LayerMask.GetMask("Wall", "Default");
        surface.BuildNavMesh();
        if(UnityEngine.AI.NavMesh.SamplePosition(Knight.transform.position,out var hit,2,UnityEngine.AI.NavMesh.AllAreas))
            Knight.transform.position=hit.position;
        agent.enabled=true;
        Player.GetComponent<Defalult_Input>()?.SetInputMode(PlayerInputMode.Gameplay);
        Player.GetComponent<PlayerInventory>()?.TryAdd(ItemCatalog.Load().Resolve("sword_001"),1);
    }
    private void Update()
    {
        if(Keyboard.current==null || NetworkRuntime.IsOnline)return;
        if(Keyboard.current.digit1Key.wasPressedThisFrame)Equip("sword_001");
        if(Keyboard.current.digit2Key.wasPressedThisFrame)Equip("greatsword_001");
    }
    void Equip(string id)
    {
        var inventory=Player.GetComponent<PlayerInventory>();
        for(int i=0;i<inventory.Capacity;i++)
            if(inventory.GetSlot(i)?.item?.ItemId==id){Player.GetComponent<PlayerEquipment>().TryEquipFromInventory(i);return;}
    }
    private void OnGUI()
    {
        GUI.Box(new Rect(15,15,490,80),"정예 기사 · 대검 시험장\n1: 일반검   2: 대검   좌클릭: 공격   기존 이동/회피/패링 키 사용\n기사 HP 300 / 피해 16 · 대검 피해 45 / 단타 1.6초");
    }
}
