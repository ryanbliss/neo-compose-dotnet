// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HelloWorld.Assets.Scripts
{
    /// <summary>
    /// Shared immediate-mode-style uGUI primitives for the sample's code-built UI
    /// (<see cref="CoreUI"/>, <see cref="MenuUI"/>, <see cref="DialogueUI"/>): rects,
    /// text, buttons, button color blocks, the event system, and play/edit-mode-safe
    /// object destruction. Keeping these in one place avoids each view re-deriving the
    /// same layout boilerplate.
    /// </summary>
    internal static class SampleUI
    {
        public static RectTransform CreateRect(Transform parent, string name)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        public static Text CreateText(Transform parent, string value, int fontSize, Color color, FontStyle fontStyle)
        {
            var rect = CreateRect(parent, "Text");
            var text = rect.gameObject.AddComponent<Text>();
            text.text = value;
            text.font = BuiltInFont;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = color;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            var layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = Mathf.Ceil(fontSize * 1.35f);
            return text;
        }

        public static Button CreateButton(
            Transform parent, string label, float width, float height, bool primary, Action action)
        {
            var rect = CreateRect(parent, label);
            var layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.preferredHeight = height;
            layout.minWidth = width;
            layout.minHeight = height;
            layout.flexibleWidth = 0f;
            layout.flexibleHeight = 0f;

            var image = rect.gameObject.AddComponent<Image>();
            image.color = primary ? new Color(0.20f, 0.38f, 0.66f, 1f) : new Color(0.16f, 0.18f, 0.23f, 1f);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = primary ? PrimaryColors() : SecondaryColors();
            button.onClick.AddListener(() => action());

            var text = CreateText(rect, label, primary ? 15 : 14, Color.white, primary ? FontStyle.Bold : FontStyle.Normal);
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            return button;
        }

        public static ColorBlock PrimaryColors()
        {
            return new ColorBlock
            {
                normalColor = new Color(0.20f, 0.38f, 0.66f, 1f),
                highlightedColor = new Color(0.29f, 0.52f, 0.85f, 1f),
                pressedColor = new Color(0.13f, 0.26f, 0.46f, 1f),
                selectedColor = new Color(0.24f, 0.45f, 0.74f, 1f),
                disabledColor = new Color(0.18f, 0.20f, 0.25f, 1f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f,
            };
        }

        public static ColorBlock SecondaryColors()
        {
            return new ColorBlock
            {
                normalColor = new Color(0.16f, 0.18f, 0.23f, 1f),
                // Clearly brighter on hover and on keyboard/controller selection, a
                // distinct darker press, and a visibly dimmer disabled state.
                highlightedColor = new Color(0.27f, 0.31f, 0.40f, 1f),
                pressedColor = new Color(0.10f, 0.12f, 0.15f, 1f),
                selectedColor = new Color(0.22f, 0.25f, 0.32f, 1f),
                disabledColor = new Color(0.11f, 0.12f, 0.14f, 1f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f,
            };
        }

        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;
            _ = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        public static void DestroyObject(UnityEngine.Object target)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }

        public static Font BuiltInFont =>
            Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ??
            Resources.GetBuiltinResource<Font>("Arial.ttf");
    }
}
