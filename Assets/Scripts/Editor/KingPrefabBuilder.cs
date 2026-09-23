using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds a ready-to-spawn <c>King.prefab</c> from the imported Edinburgh King FBX, so the
/// AR scene can load one prefab instead of loading the raw FBX and wiring the Animator up at
/// runtime (see <see cref="ARFloorPlacer.BuildModel"/>).
///
/// The prefab is an instance of the FBX with its <see cref="Animator"/> pre-configured:
/// the humanoid avatar (already carried by the FBX root) plus the <c>EdinburghKingAnim</c>
/// controller and root motion disabled. Materials are the FBX's own, which the
/// <see cref="KingMaterialUpgrader"/> converts to URP.
///
/// Run once after importing/updating the character: <c>Tools ▸ Tales Tensor ▸ Build King
/// Prefab</c>. Safe to re-run — it overwrites the existing prefab in place. It also runs the
/// URP material conversion first so the saved prefab is never left on the built-in shaders.
/// </summary>
public static class KingPrefabBuilder
{
    const string FbxPath = "Assets/Characters/King/Resources/CH2_Edinburgh_LOD1.fbx";
    const string ControllerPath = "Assets/Characters/King/Resources/EdinburghKingAnim.controller";
    const string PrefabPath = "Assets/Characters/King/Resources/King.prefab";

    [MenuItem("Tools/Tales Tensor/Build King Prefab", priority = 21)]
    public static void BuildKingPrefab()
    {
        // Make sure the King's materials are URP before we bake them into the prefab.
        KingMaterialUpgrader.ConvertKingMaterials();

        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (fbx == null)
        {
            Debug.LogError($"[Tales Tensor] King FBX not found at {FbxPath}; cannot build prefab.");
            return;
        }

        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        if (controller == null)
            Debug.LogWarning($"[Tales Tensor] King animator controller not found at {ControllerPath}; " +
                             "prefab will be built without a controller.");

        // Instantiate the FBX so we can configure a concrete Animator, then save that as a prefab.
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        instance.name = "King";
        try
        {
            var animator = instance.GetComponentInChildren<Animator>();
            if (animator == null)
            {
                // Humanoid FBX roots import with an Animator; add one on the root as a fallback.
                animator = instance.AddComponent<Animator>();
                var src = fbx.GetComponentInChildren<Animator>();
                if (src != null) animator.avatar = src.avatar;
            }

            if (controller != null) animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            bool ok;
            PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath, out ok);
            if (ok)
                Debug.Log($"[Tales Tensor] Built King prefab at {PrefabPath} " +
                          $"(avatar={(animator.avatar != null ? animator.avatar.name : "none")}, " +
                          $"controller={(controller != null ? controller.name : "none")}).");
            else
                Debug.LogError($"[Tales Tensor] Failed to save King prefab at {PrefabPath}.");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
