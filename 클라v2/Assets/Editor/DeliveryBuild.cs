using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
[InitializeOnLoad]
public static class DeliveryBuild
{
 static bool running;
 static DeliveryBuild(){EditorApplication.update+=Poll;}
 static void Poll(){
  const string request="Artifacts/delivery.request";
  if(running||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||!File.Exists(request))return;
  string output=File.ReadAllText(request).Trim();File.Delete(request);running=true;
  try{
   Directory.CreateDirectory(output);
   for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++){
    var scene=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
    if(scene.isDirty&&!string.IsNullOrEmpty(scene.path)){
     string backup="Artifacts/DeliverySceneBackup/"+scene.path;Directory.CreateDirectory(Path.GetDirectoryName(backup));if(!File.Exists(backup))File.Copy(scene.path,backup);
     UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
    }
   }
   var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),locationPathName=Path.Combine(output,"Retry.exe"),target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
   File.WriteAllText(Path.Combine(output,"BuildResult.txt"),$"{report.summary.result}; errors={report.summary.totalErrors}; warnings={report.summary.totalWarnings}; duration={report.summary.totalTime}");
  }catch(Exception e){File.WriteAllText(Path.Combine(output,"BuildResult.txt"),"FAILED: "+e);UnityEngine.Debug.LogException(e);}finally{running=false;}
 }
}
