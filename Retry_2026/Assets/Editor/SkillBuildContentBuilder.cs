using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Adds only the skill items. Existing weapons, inventory art and VFX assets stay intact.</summary>
public static class SkillBuildContentBuilder
{
    [MenuItem("Retry/Skills/Build skill items")]
    public static void Build()
    {
        const string folder="Assets/CombatContent/Items/AB";
        Directory.CreateDirectory(folder);
        var catalog=ItemCatalog.Load();var all=new List<ItemData>(catalog.Items);
        foreach(var row in ABItemDefinitions.All)
        {
            if(row.effect<16)continue;
            string path=folder+"/"+row.id+".asset";
            var item=catalog.Resolve(row.id)??AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if(item==null){item=ScriptableObject.CreateInstance<ItemData>();AssetDatabase.CreateAsset(item,path);}
            var so=new SerializedObject(item);
            so.FindProperty("itemId").stringValue=row.id;
            so.FindProperty("displayName").stringValue=row.label;
            so.FindProperty("description").stringValue=row.description;
            so.FindProperty("itemType").enumValueIndex=(int)(row.kind==0?ItemType.Consumable:ItemType.Passive);
            so.FindProperty("maxStack").intValue=row.kind==0?10:1;
            so.FindProperty("extractionValue").intValue=row.value;
            so.FindProperty("rarity").enumValueIndex=row.tier;
            so.FindProperty("icon").objectReferenceValue=ABIconBuilder.Get(row);
            so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(item);AssetDatabase.SaveAssetIfDirty(item);
            if(!all.Contains(item))all.Add(item);
        }
        catalog.EditorSetItems(all);
        if(!catalog.ValidateCatalog())throw new Exception("Skill item catalog failed validation");
        EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssetIfDirty(catalog);
    }
}
