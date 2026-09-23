using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds a ready-to-spawn <c>MotherTeresa.prefab</c> from the imported Meshy AI figure, so the
/// AR scene can drop one prefab into the "The Skopje Calling" quest (see the character override
/// on <see cref="ARFloorPlacer"/>).
///
/// The Meshy model is a Generic, unrigged mesh (no avatar/animations), so the prefab is a static
/// figure. This tool also builds a URP/Lit material from the Meshy PBR maps (base colour + normal)
/// and marks the normal map as a normal map, since the raw import lands on the built-in shader
/// (which renders magenta under URP).
///
/// Run once after importing/updating the model: <c>Tools ▸ Tales Tensor ▸ Build Mother Teresa
/// Prefab</c>. Safe to re-run — it overwrites the material and prefab in place.
/// </summary>
public static class MotherTeresaPrefabBuilder
{
    const string Folder = "Assets/Characters/MatherTeresa";
    const string Fbx = Folder + "/Meshy_AI_Give_me_a_full_body_t_0921191122_texture.fbx";
    const string BaseTex = Folder + "/Meshy_AI_Give_me_a_full_body_t_0921191122_texture.png";
    const string NormalTex = Folder + "/Meshy_AI_Give_me_a_full_body_t_0921191122_texture_normal.png";
    const string MatPath = Folder + "/MotherTeresa.mat";
    const string PrefabPath = Folder + "/MotherTeresa.prefab";
    // Pivot fix-up for the Meshy model (applied to the "Model" child, in model units).
    static readonly Vector3 ModelOffset = new Vector3(0f, 0f, 8.5f);
    static readonly Vector3 ModelRotation = new Vector3(-90f, 0f, 0f);

    [MenuItem("Tools/Tales Tensor/Build Mother Teresa Prefab", priority = 22)]
    public static void BuildMotherTeresaPrefab()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        {
            Debug.LogError("[Tales Tensor] URP/Lit shader not found — is URP installed?");
            return;
        }

        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
        if (fbx == null)
        {
            Debug.LogError($"[Tales Tensor] Mother Teresa FBX not found at {Fbx}; cannot build prefab.");
            return;
        }

        // Make sure the normal map is imported as a normal map (Meshy exports it as a plain PNG).
        var normalImporter = AssetImporter.GetAtPath(NormalTex) as TextureImporter;
        if (normalImporter != null && normalImporter.textureType != TextureImporterType.NormalMap)
        {
            normalImporter.textureType = TextureImporterType.NormalMap;
            normalImporter.SaveAndReimport();
        }

        var baseTex = AssetDatabase.LoadAssetAtPath<Texture>(BaseTex);
        var normalTex = AssetDatabase.LoadAssetAtPath<Texture>(NormalTex);

        // URP/Lit material from the Meshy maps. Metallic/roughness maps aren't packed to URP's
        // metallic-gloss layout, so we keep a plain matte surface (fine for a robed figure).
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null)
        {
            mat = new Material(lit);
            AssetDatabase.CreateAsset(mat, MatPath);
        }
        else
        {
            mat.shader = lit;
        }
        if (baseTex != null) mat.SetTexture("_BaseMap", baseTex);
        mat.SetColor("_BaseColor", Color.white);
        if (normalTex != null)
        {
            mat.SetTexture("_BumpMap", normalTex);
            mat.SetFloat("_BumpScale", 1f);
            mat.EnableKeyword("_NORMALMAP");
        }
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Smoothness", 0.3f);
        EditorUtility.SetDirty(mat);

        // Instantiate the FBX under an empty root, apply the URP material to every renderer,
        // save as a prefab. The Meshy export lies on its back and sits off its origin, so the
        // model child is stood up and re-centred here: ARFloorPlacer resets the root's
        // rotation/scale when it seats the figure, so the fix-up must live on the child.
        var instance = new GameObject("MotherTeresa");
        var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        model.name = "Model";
        model.transform.SetParent(instance.transform, false);
        model.transform.localPosition = ModelOffset;
        model.transform.localEulerAngles = ModelRotation;
        try
        {
            foreach (var r in instance.GetComponentsInChildren<Renderer>())
            {
                var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }

            PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath, out bool ok);
            if (ok) Debug.Log($"[Tales Tensor] Built Mother Teresa prefab at {PrefabPath}.");
            else Debug.LogError($"[Tales Tensor] Failed to save Mother Teresa prefab at {PrefabPath}.");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
