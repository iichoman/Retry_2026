using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Offline practice uses the same visible loot container; online drops are server requests.</summary>
public static class DroppedItemSpawner
{
    public static bool Drop(PlayerInventory owner,int slotIndex,int amount)
    {
        if(owner==null||owner.IsServerOwned||amount<=0||owner.GetComponent<Player_State>()?.IsDead==true)return false;
        var slot=owner.GetSlot(slotIndex);if(slot==null||slot.IsEmpty||amount>slot.count)return false;
        var prefab=CombatVfxCatalog.Load()?.LootPrefab;
        var go=prefab!=null?Object.Instantiate(prefab):GameObject.CreatePrimitive(PrimitiveType.Cube);
        SceneManager.MoveGameObjectToScene(go,owner.gameObject.scene);
        go.name="Dropped · "+slot.item.DisplayName;go.transform.position=owner.transform.position;
        if(prefab==null)go.transform.localScale=new Vector3(.55f,.3f,.55f);
        var loot=go.GetComponent<LootContainer>()??go.AddComponent<LootContainer>();loot.enabled=true;loot.Clear();
        foreach(var collider in go.GetComponentsInChildren<Collider>(true))collider.enabled=true;
        if(go.GetComponentInChildren<Collider>()==null){var collider=go.AddComponent<SphereCollider>();collider.radius=.65f;collider.isTrigger=true;}
        if(!loot.TryAdd(slot.item,amount)||!owner.TryRemoveAt(slotIndex,amount)){Object.Destroy(go);return false;}
        loot.SetAvailable(true);return true;
    }
}
