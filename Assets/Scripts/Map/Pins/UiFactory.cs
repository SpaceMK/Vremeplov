using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TalesTensor.Map
{
    /// <summary>
    /// Tiny helpers for assembling uGUI elements in code, so the energy HUD and the
    /// pin cards can be fully self-building (no prefabs / editor wiring), consistent
    /// with how the rest of the map self-assembles.
    /// </summary>
    public static class UiFactory
    {
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static Image Panel(string name, Transform parent, Color color, Sprite sprite = null)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = Image.Type.Sliced; // rounded-panel sprites scale cleanly
            }
            return img;
        }

        public static Image Icon(string name, Transform parent, Sprite sprite, Color color)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        public static TextMeshProUGUI Text(string name, Transform parent, string text,
            float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            // Runtime-created TMP defaults word wrapping OFF (the editor's create menu
            // turns it on), so long copy would run off its box. Wrap by default; the
            // box width then decides where lines break.
            t.enableWordWrapping = true;
            return t;
        }

        /// <summary>Stretch a RectTransform to fill its parent with optional uniform padding.</summary>
        public static void Stretch(RectTransform rt, float padding = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padding, padding);
            rt.offsetMax = new Vector2(-padding, -padding);
        }

        public static void Anchor(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 anchoredPos, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
        }
    }
}
