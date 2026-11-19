using UnityEngine;
using UnityEngine.InputSystem;

// Only the dedicated offline arena: basic health, controls and corpse-loot access.
public sealed class LocalBossArenaHud : MonoBehaviour
{
    public Player_State Player;
    public LocalBossController Boss;
    private Font font;
    private GUIStyle style;
    private string feedback="";
    private void Update()
    {
        if(NetworkRuntime.IsOnline || Player==null || Player.IsDead || Boss==null || !Boss.Defeated) return;
        if(Keyboard.current==null || !Keyboard.current.fKey.wasPressedThisFrame) return;
        if(Vector3.Distance(Player.transform.position,Boss.transform.position)>4) {feedback="보스 시체 가까이 이동하세요."; return;}
        var loot=Boss.GetComponent<LootContainer>(); var inventory=Player.GetComponent<PlayerInventory>();
        if(inventory==null) return;
        for(int i=0;i<loot.Slots.Count;i++) loot.TryTakeSlot(i,inventory);
        feedback=loot.IsEmpty()?"보스 보상 획득 완료":"남은 보상이 있습니다. 인벤토리 공간을 확인하세요.";
    }
    private void OnGUI()
    {
        if(NetworkRuntime.IsOnline || Player==null) return;
        if(style==null) {font=Font.CreateDynamicFontFromOSFont(new[]{"Malgun Gothic","Arial"},18); style=new GUIStyle(GUI.skin.box){font=font,fontSize=18,alignment=TextAnchor.MiddleLeft};}
        GUI.Box(new Rect(12,Screen.height-100,720,88),$"오프라인 보스 전투 · HP {Player.CurrentHp}/{Player.MaxHp}\nWASD 이동 · 좌클릭 공격 · Space 회피 · 검 우클릭 패링 · 처치 후 F 보상 획득\n"+(Player.IsDead?"사망했습니다. Play를 다시 시작해 재도전하세요.":feedback),style);
    }
    private void OnDestroy() {if(font!=null) Destroy(font);}
}
