using System;
using System.Runtime.InteropServices;
using UnityEngine;
public static class ABWire
{
    public const int Version = 1, Command = 200, State = 201, Actor = 202, Abandon = 12, RewardTeleport = 13;
}
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ABActorState
{
    public int version, entityId, weapon, statusMask, parrySerial;
    public float range, attackSpeed;
}
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ABCommand
{
    public int version, sequence, action, itemHash, offer;
}
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ABSnapshot
{
    public int version, sequence, result, weapon, passive, hp, maxHp, level, xp, required, offer, choice0, choice1,
        choice2;
    public float stamina, maxStamina, damage, attackSpeed, range, movement;
    public int statusMask;
}
public sealed class ABClient : MonoBehaviour
{
    public static ABClient Active { get; private set; }
    public bool Supported { get; private set; }
    public ABSnapshot State { get; private set; }
    NetworkBootstrap bootstrap;
    int sequence, appliedWeapon, parrySerial;
    bool localActorInitialized;
    bool equipmentInitialized;
    public event Action Changed;
    public NetworkBootstrap Bootstrap => bootstrap;
    public int LastRequestSequence => sequence;
    void Awake()
    {
        bootstrap = GetComponent<NetworkBootstrap>();
        Active = this;
        ExpeditionUI.Ensure();
        bootstrap.InventorySyncReceived += Inventory;
    }
    void Inventory(InventorySyncData data)
    {
        Changed?.Invoke();
    }
    void OnDestroy()
    {
        if (bootstrap != null)
            bootstrap.InventorySyncReceived -= Inventory;
        if (Active == this)
            Active = null;
    }
    public void Receive(byte[] bytes)
    {
        if (bytes == null || bytes.Length != Marshal.SizeOf<ABSnapshot>())
            return;
        var data = PacketIO.BytesToStruct<ABSnapshot>(bytes, 0);
        if (data.version != 1)
            return;
        Supported = true;
        State = data;
        sequence = Math.Max(sequence, data.sequence);
        if (bootstrap.LocalPlayer != null)
        {
            var health = bootstrap.LocalPlayer.GetComponent<Player_State>();
            if (health != null)
                health.ApplyABHealth(data.maxHp, data.hp);
            if (!equipmentInitialized || appliedWeapon != data.weapon)
            {
                var item=ItemCatalog.Load()?.Resolve(data.weapon) as EquipmentItemData;
                if(data.weapon==0)bootstrap.LocalPlayer.GetComponent<PlayerEquipment>()?.ClearABEquipment();
                else if(item!=null)bootstrap.LocalPlayer.GetComponent<PlayerEquipment>()?.ApplyABEquipment(item);
                appliedWeapon = data.weapon;
                equipmentInitialized=true;
            }
            bootstrap.LocalPlayer.GetComponent<PlayerDefenseController>()?.ReconcileDefense(data.sequence,data.result);
        }
        Changed?.Invoke();
    }
    public void ReceiveActor(byte[] bytes)
    {
        if (!Supported || bytes == null || bytes.Length != Marshal.SizeOf<ABActorState>())
            return;
        var data = PacketIO.BytesToStruct<ABActorState>(bytes, 0);
        if (data.version != ABWire.Version)
            return;
        var actor = data.entityId == bootstrap.Identity.LocalClientId ? bootstrap.LocalPlayer
                    : data.entityId > 0                               ? bootstrap.RemotePlayers.GetActor(data.entityId)
                                        : bootstrap.RemoteMonsters.GetActor(-data.entityId);
        if (actor == null)
            return;
        (actor.GetComponent<ABActorView>() ?? actor.AddComponent<ABActorView>()).Apply(data);
        if(actor==bootstrap.LocalPlayer && !localActorInitialized){localActorInitialized=true;parrySerial=data.parrySerial;}
        if (actor == bootstrap.LocalPlayer && data.parrySerial > parrySerial)
        {
            parrySerial = data.parrySerial;
            PooledVfxSpawner.Play(CombatVfxKind.SwordHit,
                                  actor.transform.position + actor.transform.forward * .6f + Vector3.up,
                                  Quaternion.identity, .7f);
        }
    }
    public bool TryUse(ItemData item)
    {
        if (!Supported || item == null)
            return false;
        int hash = ItemHash.Of(item.ItemId);
        var definition = ABItemDefinitions.Find(hash);
        if (definition == null)
            return false;
        Request(definition.kind == 0 ? 2 : 1, hash);
        return true;
    }
    public void Request(int action, int hash, int offer = 0)
    {
        if (!Supported || bootstrap.Session == null || !bootstrap.Session.IsConnected)
            return;
        // 패링 방향·구르기 시작 위치를 서버가 최신 값으로 보도록 입력을 먼저 보낸다.
        bootstrap.LocalPlayer?.GetComponent<LocalPlayerInputSender>()?.SendNow();
        bootstrap.Session.SendAB(
            new ABCommand { version = 1, sequence = ++sequence, action = action, itemHash = hash, offer = offer });
    }
    public int RequestAbandon()
    {
        if (bootstrap.Session == null || !bootstrap.Session.IsConnected) return 0;
        bootstrap.Session.SendAB(new ABCommand { version = 1, sequence = ++sequence, action = ABWire.Abandon });
        return sequence;
    }
    void Update()
    {
        if(bootstrap.Session!=null && bootstrap.Session.IsConnected)return;
        if(!Supported)return;
        Supported=false;appliedWeapon=sequence=parrySerial=0;localActorInitialized=equipmentInitialized=false;
        Changed?.Invoke();
    }
}
