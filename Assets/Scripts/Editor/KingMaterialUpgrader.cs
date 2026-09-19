using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// One-time editor conversion of the imported King character's materials from the
/// Built-in Standard shader to URP/Lit, done ahead of time (so nothing converts at
/// runtime). Run once after importing the character: <c>Tools ▸ Tales Tensor ▸ Convert
/// King Materials to URP</c>. Safe to re-run — already-URP materials are skipped.
/// </summary>
public static class KingMaterialUpgrader
{
    const string KingFolder = "Assets/Characters/King";

    [MenuItem("Tools/Tales Tensor/Convert King Materials to URP", priority = 20)]
    public static void ConvertKingMaterials()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        {
            Debug.LogError("[Tales Tensor] URP/Lit shader not found — is URP installed?");
            return;
        }
        if (!AssetDatabase.IsValidFolder(KingFolder))
        {
            Debug.LogWarning($"[Tales Tensor] King folder not found at {KingFolder}; nothing to convert.");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { KingFolder });
        int converted = 0, skipped = 0;
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null) continue;
            if (mat.shader.name.StartsWith("Universal Render Pipeline")) { skipped++; continue; }

            ConvertInPlace(mat, lit);
            EditorUtility.SetDirty(mat);
            converted++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[Tales Tensor] King materials: converted {converted} to URP/Lit, {skipped} already URP.");
    }

    /// <summary>Swap a Standard material to URP/Lit, carrying its texture slots / values.
    /// Reads everything before changing the shader, since URP/Lit doesn't keep the
    /// Standard-only <c>_MainTex</c>/<c>_Color</c> properties.</summary>
    static void ConvertInPlace(Material m, Shader lit)
    {
        Texture main = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
        Color color = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
        Texture bump = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
        float bumpScale = m.HasProperty("_BumpScale") ? m.GetFloat("_BumpScale") : 1f;
        Texture metalGloss = m.HasProperty("_MetallicGlossMap") ? m.GetTexture("_MetallicGlossMap") : null;
        float metallic = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0f;
        float glossiness = m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness") : 0.5f;
        Texture occlusion = m.HasProperty("_OcclusionMap") ? m.GetTexture("_OcclusionMap") : null;
        bool emission = m.IsKeywordEnabled("_EMISSION");
        Color emissionColor = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
        Texture emissionMap = m.HasProperty("_EmissionMap") ? m.GetTexture("_EmissionMap") : null;
        bool cutout = m.IsKeywordEnabled("_ALPHATEST_ON") ||
                      (m.HasProperty("_Mode") && Mathf.Approximately(m.GetFloat("_Mode"), 1f));
        float cutoff = m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.5f;

        m.shader = lit;

        if (main != null) m.SetTexture("_BaseMap", main);
        m.SetColor("_BaseColor", color);

        if (bump != null)
        {
            m.SetTexture("_BumpMap", bump);
            m.SetFloat("_BumpScale", bumpScale);
            m.EnableKeyword("_NORMALMAP");
        }

        if (metalGloss != null)
        {
            m.SetTexture("_MetallicGlossMap", metalGloss);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
        }
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", glossiness);

        if (occlusion != null)
        {
            m.SetTexture("_OcclusionMap", occlusion);
            m.EnableKeyword("_OCCLUSIONMAP");
        }

        if (emission)
        {
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", emissionColor);
            if (emissionMap != null) m.SetTexture("_EmissionMap", emissionMap);
        }

        if (cutout)
        {
            // URP/Lit opaque surface with alpha clipping (matches Standard "Cutout").
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", cutoff);
            m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.AlphaTest;
        }
    }
}
