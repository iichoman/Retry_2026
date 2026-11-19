using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
[InitializeOnLoad]
public static class ElementIconUpdate
{
 static bool running;
 static ElementIconUpdate(){EditorApplication.update+=Poll;}
 static async void Poll(){
  const string path="Artifacts/SwingCharge/icons.request";
  if(running||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(path))return;
  File.Delete(path);running=true;
  try{
   await WeaponIconBuilder.Build();
   foreach(string id in new[]{"flame_greatsword","frost_greatsword"}){
    var fx=GreatswordSkillEffect.Descent(Vector3.zero,1,1,ItemHash.Of(id));
    try{var expected=SkillPalette.ForWeapon(ItemHash.Of(id));var sword=fx.transform.Find("Celestial greatsword");
     foreach(var renderer in sword.GetComponentsInChildren<MeshRenderer>())if(renderer.sharedMaterial.GetColor("_BaseColor")!=expected)throw new Exception("Sword color mismatch: "+id);
    }finally{UnityEngine.Object.DestroyImmediate(fx.gameObject);}
   }
   File.WriteAllText("Artifacts/SwingCharge/icons-result.txt","PASS textured weapon icons regenerated and Q sword materials match elemental palette\n");
  }catch(Exception e){File.WriteAllText("Artifacts/SwingCharge/icons-result.txt",e.ToString());Debug.LogException(e);}finally{running=false;}
 }
}
