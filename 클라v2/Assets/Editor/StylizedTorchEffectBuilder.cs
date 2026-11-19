using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class StylizedTorchEffectBuilder
{
    [MenuItem("Tools/Retry/Add Stylized Torch Effect To Selected")]
    public static void AddToSelected()
    {
        AddToSelected(false);
    }

    [MenuItem("Tools/Retry/Rebuild Stylized Torch Effect On Selected")]
    public static void RebuildSelected()
    {
        AddToSelected(true);
    }

    private static void AddToSelected(bool clearExistingGeneratedObjects)
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null)
        {
            Debug.LogWarning("Select a torch GameObject or prefab instance first.");
            return;
        }

        bool isAsset = EditorUtility.IsPersistent(selected);
        if (!isAsset)
        {
            Undo.RegisterFullObjectHierarchyUndo(selected, "Add Stylized Torch Effect");
        }

        StylizedTorchFlame effect = selected.GetComponent<StylizedTorchFlame>();
        if (effect == null)
        {
            effect = isAsset
                ? selected.AddComponent<StylizedTorchFlame>()
                : Undo.AddComponent<StylizedTorchFlame>(selected);
        }

        effect.RebuildEffect(clearExistingGeneratedObjects);
        MarkHierarchyDirty(selected);

        if (isAsset)
        {
            PrefabUtility.SavePrefabAsset(selected);
            AssetDatabase.SaveAssets();
        }
        else
        {
            EditorSceneManager.MarkSceneDirty(selected.scene);
        }

        Selection.activeGameObject = selected;
    }

    [MenuItem("Tools/Retry/Add Stylized Torch Effect To Selected", true)]
    public static bool ValidateAddToSelected()
    {
        return Selection.activeGameObject != null;
    }

    [MenuItem("Tools/Retry/Rebuild Stylized Torch Effect On Selected", true)]
    public static bool ValidateRebuildSelected()
    {
        return Selection.activeGameObject != null;
    }

    private static void MarkHierarchyDirty(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            EditorUtility.SetDirty(children[i].gameObject);
            Component[] components = children[i].GetComponents<Component>();
            for (int j = 0; j < components.Length; j++)
            {
                if (components[j] != null)
                {
                    EditorUtility.SetDirty(components[j]);
                }
            }
        }
    }
}
