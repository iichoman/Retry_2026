using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;

public static class SkillWire { public const int Command=220,State=221,Event=222; }
[StructLayout(LayoutKind.Sequential,Pack=1)]
public struct SkillCommand { public int version,sequence,skill;public float dx,dz; }
[StructLayout(LayoutKind.Sequential,Pack=1)]
public struct SkillState { public int version,entityId,sequence,result;public float eCooldown,qCooldown,castLeft;public int skill,serial;
    public float eDuration,qDuration,power,area,castSpeed,buffLeft;public int resetSerial; }
[StructLayout(LayoutKind.Sequential,Pack=1)]
public struct SkillEvent { public int version,entityId,serial,skill,phase;public float x,y,z,dx,dz,remaining;public int weapon;public float area,windup; }

public sealed class SkillClient : MonoBehaviour
{
    public static SkillClient Active {get;private set;}
    public static bool HasPendingCast => Active!=null && Active.pending;
    public static bool IsAiming=>Active!=null&&Active.aiming;
    public static bool IsJudgementPending => Active != null && Active.pending && Active.pendingPoint;
    bool pendingPoint;
    bool aiming;Vector3 aimPoint;GreatswordSkillEffect aimMarker;
    public SkillState State {get;private set;}
    public bool Supported => supported && bootstrap != null && bootstrap.Session != null && bootstrap.Session.IsConnected && bootstrap.SessionGameStarted;
    public float ECooldown=>Mathf.Max(0,State.eCooldown-(Time.unscaledTime-receivedAt));
    public float QCooldown=>Mathf.Max(0,State.qCooldown-(Time.unscaledTime-receivedAt));
    public float EDuration=>State.eDuration>0?State.eDuration:8;
    public float QDuration=>State.qDuration>0?State.qDuration:20;
    public float BuffLeft=>Mathf.Max(0,State.buffLeft-(Time.unscaledTime-receivedAt));
    NetworkBootstrap bootstrap;int sequence;bool supported,pending;float receivedAt,pendingAt;
    readonly Dictionary<long,SkillEffect> waves=new();
    readonly Dictionary<int,(SkillEvent data,float time)> casts=new();
    void Awake(){Active=this;bootstrap=GetComponent<NetworkBootstrap>();}
    public void Receive(int packet,byte[] bytes)
    {
        if(packet==SkillWire.State){
            if(bytes==null||(bytes.Length!=36&&bytes.Length!=64))return;
            if(bytes.Length==36){var padded=new byte[64];Array.Copy(bytes,padded,36);bytes=padded;}
            var s=PacketIO.BytesToStruct<SkillState>(bytes,0);
            if((s.version!=1&&s.version!=2)||s.entityId!=bootstrap.Identity.LocalClientId||s.sequence<State.sequence)return;
            if(s.version==1){s.eDuration=8;s.qDuration=20;s.power=s.area=s.castSpeed=1;}
            State=s;supported=true;receivedAt=Time.unscaledTime;sequence=Math.Max(sequence,s.sequence);
            if(pending&&s.sequence>=sequence){pending=false;if(s.result!=0){bootstrap.LocalPlayer?.GetComponent<PlayerSkillView>()?.Cancel();ExpeditionUI.Active?.Notify("스킬 사용 불가 · 위치와 재사용 시간을 확인하세요.");}}
            return;
        }
        if(bytes==null||(bytes.Length!=44&&bytes.Length!=56))return;
        if(bytes.Length==44){var padded=new byte[56];Array.Copy(bytes,padded,44);bytes=padded;}
        var ev=PacketIO.BytesToStruct<SkillEvent>(bytes,0);
        if(ev.version==1){ev.area=1;ev.windup=ev.skill==1?.18f:.75f;}
        if((ev.version!=1&&ev.version!=2)||!float.IsFinite(ev.area)||ev.area<1||ev.area>2.01f||!float.IsFinite(ev.windup)||ev.windup<=0||ev.windup>2.5f
            ||ev.skill<1||ev.skill>5||ev.phase<0||ev.phase>4||!float.IsFinite(ev.x)||!float.IsFinite(ev.y)||!float.IsFinite(ev.z)||!float.IsFinite(ev.remaining)
            ||!float.IsFinite(ev.dx)||!float.IsFinite(ev.dz)||Mathf.Abs(ev.dx*ev.dx+ev.dz*ev.dz-1)>.01f)return;
        long key=((long)ev.entityId<<32)|(uint)ev.serial;
        if(ev.phase==1)casts[ev.entityId]=(ev,Time.unscaledTime);
        var actor=Actor(ev.entityId);
        if(ev.phase==0||ev.phase==3){casts.Remove(ev.entityId);actor?.GetComponent<PlayerSkillView>()?.Cancel();}
        if(ev.phase==1&&actor!=null)ApplyCast(actor,ev);
        if(ev.phase==2){
            if(ev.skill==1){
                // Initial cast release is followed by the first authoritative flight sample.
                if(!waves.TryGetValue(key,out var effect)||effect==null){effect=SkillEffect.CreateWave();waves[key]=effect;}
                effect.Flight(ev);
            }else if(ev.skill==4)GreatswordSkillEffect.Impact(new Vector3(ev.x,ev.y,ev.z),ev.area,ev.weapon);
            else SkillEffect.Slash(new Vector3(ev.x,ev.y,ev.z),new Vector3(ev.dx,0,ev.dz),ev.weapon,ev.area);
        }
        if(ev.phase==4&&waves.Remove(key,out var ended)&&ended!=null)Destroy(ended.gameObject);
    }
    GameObject Actor(int id)=>id==bootstrap.Identity.LocalClientId?bootstrap.LocalPlayer:bootstrap.RemotePlayers.GetActor(id);
    static void ApplyCast(GameObject actor,SkillEvent ev){(actor.GetComponent<PlayerSkillView>()??actor.AddComponent<PlayerSkillView>()).Begin(ev);}
    public static Vector3 AimDirection(Transform actor,Camera camera)
    {
        if(camera==null)return actor.forward;
        // Skills follow the view yaw even on the frame the OS releases/restores the cursor.
        var forward=Vector3.ProjectOnPlane(camera.transform.forward,Vector3.up);
        return forward.sqrMagnitude>.01f?forward.normalized:actor.forward;
    }
    void Update()
    {
        bool online=bootstrap.Session!=null&&bootstrap.Session.IsConnected&&bootstrap.LocalPlayer!=null;
        if(!online){CancelAim();supported=pending=false;sequence=0;State=default;casts.Clear();foreach(var effect in waves.Values)if(effect!=null)Destroy(effect.gameObject);waves.Clear();return;}
        foreach(var pair in new List<KeyValuePair<int,(SkillEvent data,float time)>>(casts)){
            var data=pair.Value.data;float age=Time.unscaledTime-pair.Value.time;
            if(age>data.remaining){casts.Remove(pair.Key);continue;}
            var actor=Actor(pair.Key);if(actor!=null){data.remaining-=age;ApplyCast(actor,data);}
        }
        // Do not retry timed-out casts automatically: an accepted request may already be on cooldown.
        if(pending&&Time.unscaledTime-pendingAt>2)pending=false;
        var player=bootstrap.LocalPlayer;var input=player.GetComponent<Defalult_Input>();var health=player.GetComponent<Player_State>();
        if(!Application.isFocused||!supported||pending||!bootstrap.SessionGameStarted||bootstrap.IsAwaitingDebugTeleport||input==null||!input.isActiveAndEnabled||!input.IsGameplayInputEnabled
            ||health==null||health.IsDead||health.IsHit||PlayerSkillView.Busy(player)||player.GetComponent<PlayerDefenseController>()?.IsBusy==true
            ||ABClient.Active!=null&&(ABClient.Active.State.statusMask&4)!=0||player.GetComponent<Player_Attack>()?.IsSwordEquipped!=true){CancelAim();return;}
        var kb=Keyboard.current;if(kb==null){CancelAim();return;}
        bool great=player.GetComponent<Player_Attack>().IsGreatswordEquipped;
        if(aiming){
            if(!great||kb.escapeKey.wasPressedThisFrame||Mouse.current?.rightButton.wasPressedThisFrame==true){CancelAim();return;}
            UpdateAim(player);
            if(kb.qKey.wasReleasedThisFrame){var target=aimPoint;CancelAim();Send(player,2,target,true);}return;
        }
        int skill=kb.qKey.wasPressedThisFrame?2:kb.eKey.wasPressedThisFrame?1:0;
        if(skill==0||(skill==1?ECooldown:QCooldown)>0)return;
        if(great&&skill==2){ExpeditionUI.Active?.Notify("Q를 놓아 투하 · 우클릭/ESC로 취소");aiming=true;player.GetComponent<Player_LockOnSystem>()?.ClearLockOn();player.GetComponent<Player_Attack>()?.CancelCurrentAttack();aimPoint=player.transform.position+AimDirection(player.transform,Camera.main)*6;aimMarker=GreatswordSkillEffect.Marker(aimPoint,3.5f*Mathf.Max(1,State.area));return;}
        Send(player,skill,AimDirection(player.transform,Camera.main),false);
    }
    void Send(GameObject player,int skill,Vector3 aim,bool point){
        player.GetComponent<LocalPlayerInputSender>()?.SendNow();
        bootstrap.LocalPlayer?.GetComponent<LocalPlayerInputSender>()?.SendNow();   // 시전 위치를 최신으로
        bootstrap.Session.SendSkill(new SkillCommand{version=point?2:1,sequence=++sequence,skill=skill,dx=aim.x,dz=aim.z});
        pending=true;pendingPoint=point;pendingAt=Time.unscaledTime;
    }
    void UpdateAim(GameObject player){
        var camera=Camera.main;if(camera==null)return;
        // The mouse rotates the aim camera; the ground marker follows its centre ray.
        Ray ray=camera.ViewportPointToRay(new Vector3(.5f,.5f,0));
        var plane=new Plane(Vector3.up,player.transform.position-Vector3.up*.05f);
        if(plane.Raycast(ray,out float distance)){
            var point=ray.GetPoint(distance);var offset=Vector3.ProjectOnPlane(point-player.transform.position,Vector3.up);
            aimPoint=player.transform.position+Vector3.ClampMagnitude(offset,15.5f);
            aimPoint.y=player.transform.position.y-.01f;
            var hits=Physics.RaycastAll(aimPoint+Vector3.up*3,Vector3.down,8,~0,QueryTriggerInteraction.Ignore);
            Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
            foreach(var hit in hits){if(hit.normal.y<.6f||hit.collider.GetComponentInParent<Player_State>()!=null||hit.collider.GetComponentInParent<Monster_State>()!=null)continue;aimPoint.y=hit.point.y+.04f;break;}
            if(aimMarker!=null)aimMarker.transform.position=aimPoint;
        }
    }
    void CancelAim(){aiming=false;if(aimMarker!=null)Destroy(aimMarker.gameObject);aimMarker=null;}
    void OnDisable()=>CancelAim();

    void OnDestroy(){CancelAim();if(Active==this)Active=null;}
}
