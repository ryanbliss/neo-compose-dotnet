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
    internal sealed class CoreUI : IDisposable
    {
        private GameObject root;
        private Text title;
        private Text visitedMeta;
        private Text bitsText;
        private Text inventoryMeta;
        private Text travelMeta;
        private RectTransform visitedGrid;
        private RectTransform inventoryList;
        private RectTransform outpostGrid;
        private readonly Dictionary<string, Button> outpostButtons = new();
        private readonly Dictionary<string, Image> outpostButtonImages = new();
        private readonly Dictionary<string, Text> outpostButtonLabels = new();

        public void Render(
            string text,
            ReadOnlyOutpost currentOutpost,
            IReadOnlyList<ReadOnlyOutpost> outposts,
            IReadOnlyList<PlanetVisit> visitedPlanets,
            int bits,
            IReadOnlyList<ReadOnlyItem> inventory,
            Action<ReadOnlyOutpost> onVisitOutpost,
            Action onSave,
            Action onReset
        )
        {
            EnsureBuilt(onSave, onReset);

            title.text = $"{text}\n<size=18><color=#A3B3CC>Currently visiting {currentOutpost.FullDisplayText}</color></size>";
            RebuildVisited(visitedPlanets);
            RebuildInventory(inventory);
            RebuildOutposts(outposts, currentOutpost, onVisitOutpost);
            visitedMeta.text = $"{visitedPlanets.Select(visit => visit.World.optionId).Distinct().Count()} visited";
            bitsText.text = $"Bits: {bits}";
            inventoryMeta.text = $"{inventory.Count} item{(inventory.Count == 1 ? "" : "s")}";
            travelMeta.text = $"{outposts.Count(outpost => outpost.Save.Unlocked)} unlocked";
        }

        public void Dispose()
        {
            if (root != null)
            {
                DestroyObject(root);
            }
        }

        private void EnsureBuilt(Action onSave, Action onReset)
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
            BuildContent(panel.transform);
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

        private void BuildContent(Transform parent)
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
            BuildTravelCard(row);
        }

        private void BuildVisitedCard(Transform parent)
        {
            var card = CreateCard(parent, "VisitedCard", 0.42f);
            CreateSectionHeader(card, "Visited planets", out visitedMeta);

            visitedGrid = CreateRect(card, "VisitedPlanets");
            var visitedLayout = visitedGrid.gameObject.AddComponent<LayoutElement>();
            visitedLayout.preferredHeight = 132f;
            visitedLayout.flexibleHeight = 0f;
            var grid = visitedGrid.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(132f, 34f);
            grid.spacing = new Vector2(8f, 8f);
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;

            bitsText = CreateText(card, "Bits: 0", 20, new Color(0.92f, 0.96f, 1f), FontStyle.Bold);
            bitsText.gameObject.GetComponent<LayoutElement>().preferredHeight = 34f;

            CreateSectionHeader(card, "Inventory", out inventoryMeta);

            inventoryList = CreateRect(card, "InventoryItems");
            inventoryList.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var listLayout = inventoryList.gameObject.AddComponent<VerticalLayoutGroup>();
            listLayout.spacing = 8f;
            listLayout.childAlignment = TextAnchor.UpperLeft;
            listLayout.childControlHeight = true;
            listLayout.childControlWidth = true;
            listLayout.childForceExpandHeight = false;
            listLayout.childForceExpandWidth = true;
        }

        private void BuildTravelCard(Transform parent)
        {
            var card = CreateCard(parent, "TravelCard", 0.58f);
            CreateSectionHeader(card, "Outposts", out travelMeta);

            outpostGrid = CreateRect(card, "OutpostButtons");
            outpostGrid.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var grid = outpostGrid.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(260f, 46f);
            grid.spacing = new Vector2(10f, 10f);
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
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

        private void RebuildInventory(IReadOnlyList<ReadOnlyItem> inventory)
        {
            for (var i = inventoryList.childCount - 1; i >= 0; i--)
            {
                DestroyObject(inventoryList.GetChild(i).gameObject);
            }

            if (inventory.Count == 0)
            {
                var empty = CreateText(inventoryList, "No inventory items yet.", 16, new Color(0.64f, 0.70f, 0.80f), FontStyle.Normal);
                empty.gameObject.GetComponent<LayoutElement>().preferredHeight = 30f;
                return;
            }

            foreach (var item in inventory.OrderBy(item => item.Name))
            {
                CreateInventoryRow(inventoryList, item);
            }
        }

        private void RebuildOutposts(
            IReadOnlyList<ReadOnlyOutpost> outposts,
            ReadOnlyOutpost currentOutpost,
            Action<ReadOnlyOutpost> onVisitOutpost)
        {
            var seen = new HashSet<string>();
            var siblingIndex = 0;

            foreach (var outpost in outposts)
            {
                var captured = outpost;
                var key = outpost.valueId;
                seen.Add(key);
                var isCurrent = outpost.valueId == currentOutpost.valueId;
                var unlocked = outpost.Save.Unlocked;
                if (!outpostButtons.TryGetValue(key, out var button))
                {
                    button = CreateOutpostButton(
                        outpostGrid,
                        outpost.FullDisplayText,
                        () => onVisitOutpost(captured));
                    outpostButtons[key] = button;
                    outpostButtonImages[key] = button.transform.Find("Image").GetComponent<Image>();
                    outpostButtonLabels[key] = button.transform.Find("Label").GetComponent<Text>();
                }

                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => onVisitOutpost(captured));
                button.transform.SetSiblingIndex(siblingIndex++);
                button.interactable = unlocked && !isCurrent;

                var icon = outpostButtonImages[key];
                icon.sprite = TryGetOutpostImage(outpost);
                icon.enabled = icon.sprite != null;
                icon.color = unlocked
                    ? (isCurrent ? new Color(0.70f, 0.76f, 0.84f, 0.85f) : Color.white)
                    : new Color(0.48f, 0.52f, 0.60f, 0.75f);

                var label = outpostButtonLabels[key];
                label.text = outpost.FullDisplayText;
                label.color = unlocked
                    ? (isCurrent ? new Color(0.62f, 0.68f, 0.76f) : Color.white)
                    : new Color(0.50f, 0.55f, 0.63f);
            }

            foreach (var key in outpostButtons.Keys.Where(key => !seen.Contains(key)).ToArray())
            {
                DestroyObject(outpostButtons[key].gameObject);
                outpostButtons.Remove(key);
                outpostButtonImages.Remove(key);
                outpostButtonLabels.Remove(key);
            }
        }

        private static Button CreateOutpostButton(
            Transform parent,
            string label,
            Action action)
        {
            var rect = CreateRect(parent, label);
            var layoutElement = rect.gameObject.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = 260f;
            layoutElement.preferredHeight = 46f;
            layoutElement.minWidth = 260f;
            layoutElement.minHeight = 46f;
            layoutElement.flexibleWidth = 0f;
            layoutElement.flexibleHeight = 0f;

            var background = rect.gameObject.AddComponent<Image>();
            background.color = new Color(0.20f, 0.38f, 0.66f, 1f);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.colors = PrimaryColors();
            button.onClick.AddListener(() => action());

            var row = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(12, 14, 6, 6);
            row.spacing = 10f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlHeight = true;
            row.childControlWidth = true;
            row.childForceExpandHeight = false;
            row.childForceExpandWidth = false;

            var imageRect = CreateRect(rect, "Image");
            var imageLayout = imageRect.gameObject.AddComponent<LayoutElement>();
            imageLayout.preferredWidth = 30f;
            imageLayout.preferredHeight = 30f;
            imageLayout.minWidth = 30f;
            imageLayout.minHeight = 30f;
            var image = imageRect.gameObject.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;

            var text = CreateText(rect, label, 14, Color.white, FontStyle.Bold);
            text.gameObject.name = "Label";
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;
            return button;
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

        private static void CreateInventoryRow(Transform parent, ReadOnlyItem item)
        {
            var row = CreateRect(parent, item.Name);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            var image = row.gameObject.AddComponent<Image>();
            image.color = new Color(0.08f, 0.11f, 0.16f, 1f);

            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 0, 0);
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;

            var name = CreateText(row, item.Name, 15, new Color(0.90f, 0.94f, 1f), FontStyle.Bold);
            name.alignment = TextAnchor.MiddleLeft;
            name.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;

            var value = CreateText(row, item.Value.ToString(), 15, new Color(0.72f, 0.80f, 0.92f), FontStyle.Normal);
            value.alignment = TextAnchor.MiddleRight;
            value.gameObject.GetComponent<LayoutElement>().preferredWidth = 70f;
        }

        private static Sprite TryGetOutpostImage(ReadOnlyOutpost outpost)
        {
            try
            {
                return outpost.Image;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not resolve image for outpost '{outpost.FullDisplayText}': {exception.Message}");
                return null;
            }
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
            return planet.Text;
        }

        private static Font BuiltInFont =>
            Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ??
            Resources.GetBuiltinResource<Font>("Arial.ttf");
    }
}
