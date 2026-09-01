#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CubemapBaker))]
public class CubemapBakerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        CubemapBaker baker = (CubemapBaker)target;
        if (!GUILayout.Button("Bake Cubemap")) return;

        string path = EditorUtility.SaveFilePanelInProject("Bake Cubemap", $"{baker.name}_Cubemap", "png", "Select save location");
        if (!string.IsNullOrEmpty(path)) baker.Bake(path);
    }
}
#endif
