using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
[StructLayout(LayoutKind.Sequential,Pack=1)]
public struct EnvironmentState {
 public int version,id,kind,state,serial;
 public float x,y,z,dx,dz,length,width;
 public long deadline;
}
/// Server-owned traps. No client damage or local trigger prediction.
public sealed class EnvironmentClient:MonoBehaviour {
 public const int Packet=203,ChestSource=-700000,RewardChestSource=-800000;
 readonly Dictionary<int,EnvironmentTrapView> traps=new();
 NetworkBootstrap bootstrap; bool connected;
 void Awake(){bootstrap=GetComponent<NetworkBootstrap>();}
 public void Receive(byte[] bytes){
  if(bytes==null||bytes.Length!=56)return;var s=PacketIO.BytesToStruct<EnvironmentState>(bytes,0);
  if(s.version!=1||s.id<=0||s.serial<0||s.kind<1||s.kind>2||s.state<0||s.state>3||!float.IsFinite(s.x)||!float.IsFinite(s.y)||!float.IsFinite(s.z)||!float.IsFinite(s.dx)||!float.IsFinite(s.dz)||!float.IsFinite(s.length)||!float.IsFinite(s.width)||Mathf.Abs(s.dx*s.dx+s.dz*s.dz-1)>.001f||s.length<=0||s.length>40||s.width<=0||s.width>4)return;
  if(!traps.TryGetValue(s.id,out var view)){var go=new GameObject("Trap_"+s.id);go.transform.SetParent(transform);view=go.AddComponent<EnvironmentTrapView>();traps.Add(s.id,view);}
  view.Apply(s);
 }
 void Update(){bool online=bootstrap.Session!=null&&bootstrap.Session.IsConnected;
  if(connected&&!online){foreach(var v in traps.Values)if(v!=null)Destroy(v.gameObject);traps.Clear();}connected=online;
  var player=bootstrap.LocalPlayer;if(player==null)return;
  foreach(var v in traps.Values)v.SetVisible((v.transform.position-player.transform.position).sqrMagnitude<90*90);
 }
}
public sealed class EnvironmentTrapView:MonoBehaviour {
 const float ArrowOffset=1.4f,ArrowSpeed=32f;
 EnvironmentState state; bool initialized; Transform spikes,arrow; MeshRenderer warning; MaterialPropertyBlock block;
 GameObject model,visuals;
 public void Apply(EnvironmentState next){
  if(initialized&&next.serial<=state.serial)return;
  state=next;transform.position=new Vector3(next.x,next.y,next.z);
  if(!initialized){Build();initialized=true;}
 }
 public void SetVisible(bool visible){if(visuals!=null&&visuals.activeSelf!=visible)visuals.SetActive(visible);}
 static void RemoveCollider(GameObject go){var collider=go.GetComponent<Collider>();if(collider==null)return;collider.enabled=false;if(Application.isPlaying)Destroy(collider);else DestroyImmediate(collider);}
 void Build(){
  visuals=new GameObject("Visuals");visuals.transform.SetParent(transform,false);
  var prefab=Resources.Load<GameObject>(state.kind==1?"EContent/Spikes":"EContent/ArrowEmitter");
  if(prefab!=null){model=Instantiate(prefab,visuals.transform);model.transform.localPosition=state.kind==1?Vector3.zero:new Vector3(state.dx*(state.length+.4f),1.15f,state.dz*(state.length+.4f));model.transform.localRotation=Quaternion.LookRotation(new Vector3(-state.dx,0,-state.dz));spikes=model.transform;}
  var plate=GameObject.CreatePrimitive(PrimitiveType.Cube);plate.name="Pressure plate";plate.transform.SetParent(visuals.transform,false);plate.transform.localPosition=Vector3.up*.025f;plate.transform.localScale=new Vector3(state.kind==1?state.width:1.4f,.05f,state.kind==1?state.length:1.4f);RemoveCollider(plate);plate.GetComponent<Renderer>().sharedMaterial=Resources.Load<Material>("EContent/Iron");
  var area=GameObject.CreatePrimitive(PrimitiveType.Cube);area.name="Danger area";area.transform.SetParent(visuals.transform,false);RemoveCollider(area);
  area.transform.localPosition=Vector3.up*.07f+(state.kind==2?new Vector3(state.dx,0,state.dz)*(state.length-ArrowOffset)*.5f:Vector3.zero);
  area.transform.localRotation=Quaternion.LookRotation(new Vector3(state.dx,0,state.dz));area.transform.localScale=new Vector3(state.width,.025f,state.length+(state.kind==2?ArrowOffset:0));
  warning=area.GetComponent<MeshRenderer>();warning.sharedMaterial=Resources.Load<Material>("EContent/Warning");block=new MaterialPropertyBlock();
  if(state.kind==2){var prefabArrow=Resources.Load<GameObject>("EContent/TrapArrow");var a=prefabArrow!=null?Instantiate(prefabArrow,visuals.transform):GameObject.CreatePrimitive(PrimitiveType.Cube);a.name="Arrow";a.transform.SetParent(visuals.transform,false);if(prefabArrow==null){RemoveCollider(a);a.transform.localScale=new Vector3(.07f,.07f,.7f);a.GetComponent<Renderer>().sharedMaterial=Resources.Load<Material>("EContent/Iron");}arrow=a.transform;arrow.localRotation=Quaternion.LookRotation(new Vector3(-state.dx,0,-state.dz));}
 }
 void Update(){if(!initialized)return;long remaining=state.deadline-ServerClock.NowMs;bool active=state.state==2&&remaining>0;bool windup=state.state==1&&remaining>0;
  warning.enabled=windup;block.SetColor("_BaseColor",Color.Lerp(new Color(.5f,.18f,.025f),new Color(1,.35f,.04f),1-Mathf.Clamp01(remaining/(state.kind==1?1000f:250f))));warning.SetPropertyBlock(block);
  if(state.kind==1&&spikes!=null)spikes.localScale=new Vector3(state.width/2,active?1:.04f,state.length/2);
  if(arrow!=null){float distance=state.length+.4f-((state.length+.4f+ArrowOffset)/ArrowSpeed-remaining*.001f)*ArrowSpeed;arrow.gameObject.SetActive(active&&distance>=-ArrowOffset);arrow.localPosition=new Vector3(state.dx,0,state.dz)*Mathf.Clamp(distance,-ArrowOffset,state.length)+Vector3.up*1.1f;}
 }
}
