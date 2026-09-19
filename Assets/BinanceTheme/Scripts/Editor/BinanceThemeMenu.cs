using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace BinanceTheme.EditorTools
{
    /// <summary>
    /// One-click setup for the Binance theme:
    ///   1. Generate Fonts     — bakes IBM Plex Sans TTFs into TMP SDF font assets.
    ///   2. Create/Update Theme — builds BinanceTheme.asset and wires the fonts.
    ///   3. Apply To Open Scenes — adds a controller to every Canvas and re-skins it.
    /// Run them in order from the <c>Tools ▸ Binance Theme</c> menu.
    /// </summary>
    public static class BinanceThemeMenu
    {
        const string Root        = "Assets/BinanceTheme";
        const string FontsDir    = Root + "/Fonts";
        const string ThemePath   = Root + "/BinanceTheme.asset";

        // TTF source -> generated "<name> SDF.asset"
        static readonly (string ttf, string sdf)[] Fonts =
        {
            ("IBMPlexSans-Regular.ttf",  "IBMPlexSans-Regular SDF.asset"),
            ("IBMPlexSans-Medium.ttf",   "IBMPlexSans-Medium SDF.asset"),
            ("IBMPlexSans-SemiBold.ttf", "IBMPlexSans-SemiBold SDF.asset"),
            ("IBMPlexSans-Bold.ttf",     "IBMPlexSans-Bold SDF.asset"),
        };

        [MenuItem("Tools/Binance Theme/1. Generate Fonts", priority = 0)]
        public static void GenerateFonts()
        {
            foreach (var (ttf, sdf) in Fonts)
            {
                string ttfPath = FontsDir + "/" + ttf;
                string sdfPath = FontsDir + "/" + sdf;

                var font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
                if (font == null)
                {
                    Debug.LogWarning($"[Binance Theme] Missing TTF: {ttfPath}");
                    continue;
                }
                if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(sdfPath) != null)
                {
                    Debug.Log($"[Binance Theme] Already exists, skipping: {sdf}");
                    continue;
                }

                // Dynamic atlas: glyphs render on demand — light to bake, ideal for mobile.
                var fontAsset = TMP_FontAsset.CreateFontAsset(
                    font, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                    1024, 1024, AtlasPopulationMode.Dynamic, true);

                fontAsset.name = Path.GetFileNameWithoutExtension(sdf);
                AssetDatabase.CreateAsset(fontAsset, sdfPath);

                // Persist generated material + atlas texture as sub-assets.
                if (fontAsset.material != null)
                {
                    fontAsset.material.name = fontAsset.name + " Material";
                    AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
                }
                if (fontAsset.atlasTextures != null)
                {
                    foreach (var tex in fontAsset.atlasTextures)
                    {
                        if (tex != null && !AssetDatabase.Contains(tex))
                            AssetDatabase.AddObjectToAsset(tex, fontAsset);
                    }
                }
                EditorUtility.SetDirty(fontAsset);
                Debug.Log($"[Binance Theme] Generated {sdf}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [MenuItem("Tools/Binance Theme/2. Create or Update Theme Asset", priority = 1)]
        public static void CreateOrUpdateTheme()
        {
            var theme = AssetDatabase.LoadAssetAtPath<BinanceTheme>(ThemePath);
            bool created = false;
            if (theme == null)
            {
                theme = ScriptableObject.CreateInstance<BinanceTheme>();
                AssetDatabase.CreateAsset(theme, ThemePath);
                created = true;
            }

            theme.bodyFont    = Load(FontsDir + "/IBMPlexSans-Regular SDF.asset");
            theme.headingFont = Load(FontsDir + "/IBMPlexSans-SemiBold SDF.asset")
                                ?? Load(FontsDir + "/IBMPlexSans-Bold SDF.asset");

            EditorUtility.SetDirty(theme);
            AssetDatabase.SaveAssets();
            Selection.activeObject = theme;
            Debug.Log($"[Binance Theme] {(created ? "Created" : "Updated")} {ThemePath}. " +
                      $"bodyFont={(theme.bodyFont ? theme.bodyFont.name : "<none — run step 1>")}");
        }

        [MenuItem("Tools/Binance Theme/3. Apply To Open Scenes", priority = 2)]
        public static void ApplyToOpenScenes()
        {
            var theme = AssetDatabase.LoadAssetAtPath<BinanceTheme>(ThemePath);
            if (theme == null)
            {
                Debug.LogError("[Binance Theme] No theme asset. Run step 2 first.");
                return;
            }

            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (canvases.Length == 0)
            {
                Debug.LogWarning("[Binance Theme] No Canvas found in the open scene(s).");
                return;
            }

            int count = 0;
            foreach (var canvas in canvases)
            {
                if (canvas.transform.parent is RectTransform) continue; // only root canvases

                var controller = canvas.GetComponent<BinanceThemeController>();
                if (controller == null)
                    controller = canvas.gameObject.AddComponent<BinanceThemeController>();
                controller.theme = theme;
                controller.Apply();

                EditorUtility.SetDirty(controller);
                count++;
            }

            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log($"[Binance Theme] Applied to {count} root canvas(es). Save the scene to keep changes.");
        }

        static TMP_FontAsset Load(string path) => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
    }
}
