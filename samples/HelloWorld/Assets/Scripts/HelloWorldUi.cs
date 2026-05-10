// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using HelloWorld.Assets.Scripts.Neo;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HelloWorld.Assets.Scripts
{
    /// <summary>
    /// Camera-backed Unity UI for the HelloWorld sample. Keeps layout and
    /// presentation separate from the SDK-facing behaviour.
    /// </summary>
    internal sealed class HelloWorldUi : IDisposable
    {
        private static readonly Planet[] AllPlanets =
        {
            Planet.mercury,
            Planet.venus,
            Planet.earth,
            Planet.mars,
            Planet.jupiter,
        };

        private GameObject root;
        private Text title;
        private Text visitedMeta;
        private Text travelMeta;
        private RectTransform visitedGrid;
        private readonly Dictionary<string, Button> planetButtons = new();
        private readonly Dictionary<string, Text> planetButtonLabels = new();

        public void Render(
            string text,
            Planet world,
            IReadOnlyList<PlanetVisit> visitedPlanets,
            Action<Planet> onVisitPlanet,
            Action onSave,
            Action onReset
        )
        {
            EnsureBuilt(onVisitPlanet, onSave, onReset);

            title.text = $"{text}\n<size=18><color=#A3B3CC>Currently orbiting {DisplayName(world)}</color></size>";
            RebuildVisited(visitedPlanets);
            visitedMeta.text = $"{visitedPlanets.Select(visit => visit.World.optionId).Distinct().Count()} visited";
            travelMeta.text = $"{AllPlanets.Length} destinations";

            foreach (var planet in AllPlanets)
            {
                var current = planet.Equals(world);
                var key = planet.optionId;
                planetButtons[key].interactable = !current;
                planetButtonLabels[key].text = $"Visit {DisplayName(planet)}";
                planetButtonLabels[key].color = current
                    ? new Color(0.62f, 0.68f, 0.76f)
                    : Color.white;
            }
        }

        public void Dispose()
        {
            if (root != null)
            {
                DestroyObject(root);
            }
        }

        private void EnsureBuilt(Action<Planet> onVisitPlanet, Action onSave, Action onReset)
        {
            if (root != null) return;

            EnsureEventSystem();

            root = new GameObject("HelloWorld UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = Camera.main;
            canvas.planeDistance = 1f;
            if (canvas.worldCamera == null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var panel = CreatePanel(root.transform);
            BuildHeader(panel.transform, onSave, onReset);
            BuildContent(panel.transform, onVisitPlanet);
        }

        private static RectTransform CreatePanel(Transform parent)
        {
            var panel = CreateRect(parent, "Panel");
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            panel.offsetMin = new Vector2(30f, 30f);
            panel.offsetMax = new Vector2(-30f, -30f);

            var image = panel.gameObject.AddComponent<Image>();
            image.color = new Color(0.06f, 0.08f, 0.11f, 0.96f);

            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 26, 28);
            layout.spacing = 18f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            return panel;
        }

        private void BuildHeader(Transform parent, Action onSave, Action onReset)
        {
            var row = CreateRect(parent, "Header");
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 74f;
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 18f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;

            title = CreateText(row, "", 38, new Color(0.93f, 0.96f, 1f), FontStyle.Bold);
            title.supportRichText = true;
            title.lineSpacing = 0.9f;
            var titleLayout = title.gameObject.GetComponent<LayoutElement>();
            titleLayout.flexibleWidth = 1f;
            titleLayout.preferredHeight = 68f;

            var actions = CreateRect(row, "Actions");
            var actionLayout = actions.gameObject.AddComponent<HorizontalLayoutGroup>();
            actionLayout.spacing = 10f;
            actionLayout.childAlignment = TextAnchor.MiddleRight;
            actionLayout.childControlHeight = true;
            actionLayout.childControlWidth = true;
            actionLayout.childForceExpandHeight = false;
            actionLayout.childForceExpandWidth = false;
            var actionLayoutElement = actions.gameObject.AddComponent<LayoutElement>();
            actionLayoutElement.preferredWidth = 202f;
            actionLayoutElement.preferredHeight = 34f;

            CreateButton(actions, "Save", 96f, 34f, false, onSave);
            CreateButton(actions, "Reset", 96f, 34f, false, onReset);
        }

        private void BuildContent(Transform parent, Action<Planet> onVisitPlanet)
        {
            var row = CreateRect(parent, "Content");
            row.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 18f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = false;

            BuildVisitedCard(row);
            BuildTravelCard(row, onVisitPlanet);
        }

        private void BuildVisitedCard(Transform parent)
        {
            var card = CreateCard(parent, "VisitedCard", 0.42f);
            CreateSectionHeader(card, "Visited planets", out visitedMeta);

            visitedGrid = CreateRect(card, "VisitedPlanets");
            visitedGrid.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var grid = visitedGrid.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(132f, 34f);
            grid.spacing = new Vector2(8f, 8f);
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
        }

        private void BuildTravelCard(Transform parent, Action<Planet> onVisitPlanet)
        {
            var card = CreateCard(parent, "TravelCard", 0.58f);
            CreateSectionHeader(card, "Travel", out travelMeta);

            var gridRect = CreateRect(card, "PlanetButtons");
            gridRect.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var grid = gridRect.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(172f, 44f);
            grid.spacing = new Vector2(10f, 10f);
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;

            foreach (var planet in AllPlanets)
            {
                var captured = planet;
                var button = CreateButton(gridRect, $"Visit {DisplayName(planet)}", 172f, 44f, true, () => onVisitPlanet(captured));
                planetButtons[planet.optionId] = button;
                planetButtonLabels[planet.optionId] = button.GetComponentInChildren<Text>();
            }
        }

        private static RectTransform CreateCard(Transform parent, string name, float widthRatio)
        {
            var card = CreateRect(parent, name);
            var layoutElement = card.gameObject.AddComponent<LayoutElement>();
            layoutElement.flexibleWidth = widthRatio;
            layoutElement.flexibleHeight = 1f;
            var image = card.gameObject.AddComponent<Image>();
            image.color = new Color(0.10f, 0.13f, 0.18f, 1f);

            var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 18, 20);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            return card;
        }

        private static void CreateSectionHeader(Transform parent, string label, out Text meta)
        {
            var row = CreateRect(parent, $"{label} Header");
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;

            var titleText = CreateText(row, label, 22, new Color(0.88f, 0.91f, 0.96f), FontStyle.Bold);
            titleText.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;
            meta = CreateText(row, "", 14, new Color(0.64f, 0.70f, 0.80f), FontStyle.Normal);
            meta.alignment = TextAnchor.MiddleRight;
            meta.gameObject.GetComponent<LayoutElement>().preferredWidth = 120f;
        }

        private void RebuildVisited(IReadOnlyList<PlanetVisit> visitedPlanets)
        {
            for (var i = visitedGrid.childCount - 1; i >= 0; i--)
            {
                DestroyObject(visitedGrid.GetChild(i).gameObject);
            }

            var visitedNames = visitedPlanets
                .Select(visit => DisplayName(visit.World))
                .Distinct()
                .ToArray();
            if (visitedNames.Length == 0)
            {
                var empty = CreateText(visitedGrid, "No planets visited yet.", 16, new Color(0.64f, 0.70f, 0.80f), FontStyle.Normal);
                return;
            }

            foreach (var visitedName in visitedNames)
            {
                CreateChip(visitedGrid, visitedName);
            }
        }

        private static Button CreateButton(
            Transform parent,
            string label,
            float width,
            float height,
            bool primary,
            Action action)
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

            var text = CreateText(rect, label, primary ? 14 : 13, Color.white, primary ? FontStyle.Bold : FontStyle.Normal);
            text.alignment = TextAnchor.MiddleCenter;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            return button;
        }

        private static void CreateChip(Transform parent, string label)
        {
            var chip = CreateRect(parent, label);
            var layout = chip.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = Mathf.Max(86f, label.Length * 12f + 30f);
            layout.preferredHeight = 32f;
            var image = chip.gameObject.AddComponent<Image>();
            image.color = new Color(0.18f, 0.30f, 0.48f, 1f);
            var text = CreateText(chip, label, 15, new Color(0.92f, 0.96f, 1f), FontStyle.Bold);
            text.alignment = TextAnchor.MiddleCenter;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(14f, 0f);
            text.rectTransform.offsetMax = new Vector2(-14f, 0f);
        }

        private static Text CreateText(Transform parent, string value, int fontSize, Color color, FontStyle fontStyle)
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

        private static RectTransform CreateRect(Transform parent, string name)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        private static ColorBlock PrimaryColors()
        {
            return new ColorBlock
            {
                normalColor = new Color(0.20f, 0.38f, 0.66f, 1f),
                highlightedColor = new Color(0.25f, 0.47f, 0.78f, 1f),
                pressedColor = new Color(0.15f, 0.29f, 0.50f, 1f),
                selectedColor = new Color(0.20f, 0.38f, 0.66f, 1f),
                disabledColor = new Color(0.20f, 0.22f, 0.27f, 1f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f,
            };
        }

        private static ColorBlock SecondaryColors()
        {
            return new ColorBlock
            {
                normalColor = new Color(0.16f, 0.18f, 0.23f, 1f),
                highlightedColor = new Color(0.22f, 0.25f, 0.31f, 1f),
                pressedColor = new Color(0.12f, 0.14f, 0.18f, 1f),
                selectedColor = new Color(0.16f, 0.18f, 0.23f, 1f),
                disabledColor = new Color(0.14f, 0.15f, 0.18f, 1f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f,
            };
        }

        private static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;

            _ = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        private static void DestroyObject(UnityEngine.Object target)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
                return;
            }

            UnityEngine.Object.DestroyImmediate(target);
        }

        private static string DisplayName(Planet planet)
        {
            var value = planet.optionId;
            return value.Length == 0
                ? value
                : char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        private static Font BuiltInFont =>
            Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ??
            Resources.GetBuiltinResource<Font>("Arial.ttf");
    }
}
