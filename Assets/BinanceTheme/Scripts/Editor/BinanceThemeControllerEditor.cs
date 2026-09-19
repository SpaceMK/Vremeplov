using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BinanceTheme.EditorTools
{
    [CustomEditor(typeof(BinanceThemeController))]
    public class BinanceThemeControllerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            if (GUILayout.Button("Apply Theme Now", GUILayout.Height(28)))
            {
                var controller = (BinanceThemeController)target;
                Undo.RecordObjects(controller.GetComponentsInChildren<Component>(true), "Apply Binance Theme");
                controller.Apply();
                EditorUtility.SetDirty(controller);
                if (!Application.isPlaying)
                    EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            }
        }
    }
}
