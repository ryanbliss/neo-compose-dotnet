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
        private Button saveButton;
        private Text saveLabel;
        private Text questText;
        private Text title;
        private Text bitsText;
        private Text travelMeta;
        private Text inventoryButtonLabel;
        private GameObject inventoryBadge;
        private Text inventoryBadgeLabel;
        private RectTransform inventoryOverlay;
        private RectTransform inventoryList;
        private SystemMapUI systemMap;
        private IReadOnlyList<ReadOnlyItem> lastInventory = Array.Empty<ReadOnlyItem>();
        private int seenItemCount;
        private bool inventoryOpen;

        public void Render(
            string text,
            string questHint,
            int storm,
            ReadOnlyAnimationInfo shipAnimation,
            ReadOnlyAnimationInfo flareAnimation,
            AudioClip thrustSfx,
            ReadOnlyOutpost currentOutpost,
            IReadOnlyList<ReadOnlyOutpost> outposts,
            int bits,
            IReadOnlyList<ReadOnlyItem> inventory,
            Action<ReadOnlyOutpost> onVisitOutpost,
            Action onSave,
            Action onReset,
            Action onMenu
        )
        {
            EnsureBuilt(onSave, onReset, onMenu);

            title.text = $"{text}\n<size=18><color=#A3B3CC>Currently visiting {currentOutpost.FullDisplayText}</color></size>";
            lastInventory = inventory;
            if (inventoryOpen)
            {
                seenItemCount = inventory.Count;
                RebuildInventory(inventory);
            }
            UpdateInventoryChrome();
            systemMap.Render(outposts, currentOutpost, storm, shipAnimation, flareAnimation, thrustSfx, onVisitOutpost);
            bitsText.text = storm <= 0
                ? $"Bits: {bits}"
                : $"Bits: {bits}   <color=#FFAA66>Storms: {new string('▲', Math.Min(storm, 14))}</color>";
            questText.text = string.IsNullOrEmpty(questHint)
                ? ""
                : $"<color=#9FD0FF>{questHint}</color>";
            travelMeta.text = $"{outposts.Count(outpost => outpost.Save.Unlocked)} unlocked";
        }

        private void ToggleInventory()
        {
            inventoryOpen = !inventoryOpen;
            inventoryOverlay.gameObject.SetActive(inventoryOpen);
            if (inventoryOpen)
            {
                seenItemCount = lastInventory.Count;
                RebuildInventory(lastInventory);
            }
            UpdateInventoryChrome();
        }

        private void UpdateInventoryChrome()
        {
            inventoryButtonLabel.text = $"Cargo ({lastInventory.Count})";
            int unseen = lastInventory.Count - seenItemCount;
            bool showBadge = unseen > 0 && !inventoryOpen;
            inventoryBadge.SetActive(showBadge);
            if (showBadge)
            {
                inventoryBadgeLabel.text = unseen.ToString();
            }
        }

        public void Dispose()
        {
            if (root != null)
            {
                SampleUI.DestroyObject(root);
            }
        }

        private void EnsureBuilt(Action onSave, Action onReset, Action onMenu)
        {
            if (root != null) return;

            SampleUI.EnsureEventSystem();

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
            BuildHeader(panel.transform, onSave, onReset, onMenu);
            BuildContent(panel.transform);
        }

        private static RectTransform CreatePanel(Transform parent)
        {
            var panel = SampleUI.CreateRect(parent, "Panel");
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

        private void BuildHeader(Transform parent, Action onSave, Action onReset, Action onMenu)
        {
            var row = SampleUI.CreateRect(parent, "Header");
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 74f;
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 18f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;

            title = SampleUI.CreateText(row, "", 38, new Color(0.93f, 0.96f, 1f), FontStyle.Bold);
            title.supportRichText = true;
            title.lineSpacing = 0.9f;
            var titleLayout = title.gameObject.GetComponent<LayoutElement>();
            titleLayout.flexibleWidth = 1f;
            titleLayout.preferredHeight = 68f;

            var actions = SampleUI.CreateRect(row, "Actions");
            var actionLayout = actions.gameObject.AddComponent<HorizontalLayoutGroup>();
            actionLayout.spacing = 10f;
            actionLayout.childAlignment = TextAnchor.MiddleRight;
            actionLayout.childControlHeight = true;
            actionLayout.childControlWidth = true;
            actionLayout.childForceExpandHeight = false;
            actionLayout.childForceExpandWidth = false;
            var actionLayoutElement = actions.gameObject.AddComponent<LayoutElement>();
            actionLayoutElement.preferredWidth = 302f;
            actionLayoutElement.preferredHeight = 34f;

            var inventoryButton = SampleUI.CreateButton(actions, "Cargo", 130f, 34f, false, ToggleInventory);
            inventoryButtonLabel = inventoryButton.GetComponentInChildren<Text>();
            BuildInventoryBadge(inventoryButton.transform);
            SampleUI.CreateButton(actions, "Menu", 96f, 34f, false, onMenu);
            saveButton = SampleUI.CreateButton(actions, "Save", 96f, 34f, false, onSave);
            saveLabel = saveButton.GetComponentInChildren<Text>();
            SampleUI.CreateButton(actions, "Reset", 96f, 34f, false, onReset);
        }

        /// <summary>Small "unseen items" counter pinned to the Cargo button.</summary>
        private void BuildInventoryBadge(Transform parent)
        {
            var badge = SampleUI.CreateRect(parent, "Badge");
            badge.anchorMin = badge.anchorMax = new Vector2(1f, 1f);
            badge.pivot = new Vector2(0.6f, 0.4f);
            badge.sizeDelta = new Vector2(24f, 24f);
            badge.anchoredPosition = Vector2.zero;
            var image = badge.gameObject.AddComponent<Image>();
            image.color = new Color(0.95f, 0.45f, 0.25f, 1f);
            image.raycastTarget = false;
            inventoryBadgeLabel = SampleUI.CreateText(badge, "", 14, Color.white, FontStyle.Bold);
            inventoryBadgeLabel.alignment = TextAnchor.MiddleCenter;
            inventoryBadgeLabel.raycastTarget = false;
            var labelRect = inventoryBadgeLabel.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            inventoryBadge = badge.gameObject;
            inventoryBadge.SetActive(false);
        }

        /// <summary>
        /// Toggles the Save button into a non-interactive "Saving…" state while a
        /// commit (local + cloud) is in flight, so the player sees progress.
        /// </summary>
        public void SetSaving(bool saving)
        {
            if (saveButton == null) return;
            saveButton.interactable = !saving;
            if (saveLabel != null)
            {
                saveLabel.text = saving ? "Saving…" : "Save";
                // The button's ColorBlock only tints the background; dim the label
                // too so the disabled state reads clearly.
                saveLabel.color = saving ? new Color(0.55f, 0.60f, 0.68f) : Color.white;
            }
        }

        private void BuildContent(Transform parent)
        {
            // The map IS the game now — one full-width card; status lines ride
            // above it and the cargo list floats over it as an overlay.
            var card = CreateCard(parent, "TravelCard", 1f);
            CreateSectionHeader(card, "Hello System", out travelMeta);

            var statusRow = SampleUI.CreateRect(card, "StatusRow");
            statusRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
            var statusLayout = statusRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            statusLayout.spacing = 24f;
            statusLayout.childAlignment = TextAnchor.MiddleLeft;
            statusLayout.childControlHeight = true;
            statusLayout.childControlWidth = true;
            statusLayout.childForceExpandHeight = false;
            statusLayout.childForceExpandWidth = false;
            bitsText = SampleUI.CreateText(statusRow, "Bits: 0", 20, new Color(0.92f, 0.96f, 1f), FontStyle.Bold);
            bitsText.gameObject.GetComponent<LayoutElement>().preferredWidth = 420f;
            questText = SampleUI.CreateText(statusRow, "", 16, new Color(0.62f, 0.81f, 1f), FontStyle.Italic);
            questText.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;

            systemMap = new SystemMapUI();
            systemMap.Build(card);

            BuildInventoryOverlay(card);
        }

        /// <summary>
        /// The cargo manifest: a panel floating over the map's top-right,
        /// toggled from the header. Closed by default so the system stays
        /// the centerpiece.
        /// </summary>
        private void BuildInventoryOverlay(Transform card)
        {
            inventoryOverlay = SampleUI.CreateRect(card, "InventoryOverlay");
            // Ignore the card's vertical layout: float over the map.
            var overlayLayout = inventoryOverlay.gameObject.AddComponent<LayoutElement>();
            overlayLayout.ignoreLayout = true;
            inventoryOverlay.anchorMin = new Vector2(1f, 1f);
            inventoryOverlay.anchorMax = new Vector2(1f, 1f);
            inventoryOverlay.pivot = new Vector2(1f, 1f);
            inventoryOverlay.anchoredPosition = new Vector2(-24f, -96f);
            inventoryOverlay.sizeDelta = new Vector2(380f, 520f);

            var image = inventoryOverlay.gameObject.AddComponent<Image>();
            image.color = new Color(0.07f, 0.10f, 0.15f, 0.97f);

            var layout = inventoryOverlay.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 14, 16);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var header = SampleUI.CreateText(inventoryOverlay, "Cargo manifest", 20, new Color(0.88f, 0.91f, 0.96f), FontStyle.Bold);
            header.gameObject.GetComponent<LayoutElement>().preferredHeight = 28f;

            inventoryList = SampleUI.CreateRect(inventoryOverlay, "InventoryItems");
            inventoryList.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var listLayout = inventoryList.gameObject.AddComponent<VerticalLayoutGroup>();
            listLayout.spacing = 8f;
            listLayout.childAlignment = TextAnchor.UpperLeft;
            listLayout.childControlHeight = true;
            listLayout.childControlWidth = true;
            listLayout.childForceExpandHeight = false;
            listLayout.childForceExpandWidth = true;

            inventoryOverlay.gameObject.SetActive(false);
        }

        private static RectTransform CreateCard(Transform parent, string name, float widthRatio)
        {
            var card = SampleUI.CreateRect(parent, name);
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
            var row = SampleUI.CreateRect(parent, $"{label} Header");
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;

            var titleText = SampleUI.CreateText(row, label, 22, new Color(0.88f, 0.91f, 0.96f), FontStyle.Bold);
            titleText.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;
            meta = SampleUI.CreateText(row, "", 14, new Color(0.64f, 0.70f, 0.80f), FontStyle.Normal);
            meta.alignment = TextAnchor.MiddleRight;
            meta.gameObject.GetComponent<LayoutElement>().preferredWidth = 120f;
        }

        private void RebuildInventory(IReadOnlyList<ReadOnlyItem> inventory)
        {
            for (var i = inventoryList.childCount - 1; i >= 0; i--)
            {
                SampleUI.DestroyObject(inventoryList.GetChild(i).gameObject);
            }

            if (inventory.Count == 0)
            {
                var empty = SampleUI.CreateText(inventoryList, "No cargo yet.", 16, new Color(0.64f, 0.70f, 0.80f), FontStyle.Normal);
                empty.gameObject.GetComponent<LayoutElement>().preferredHeight = 30f;
                return;
            }

            foreach (var item in inventory.OrderBy(item => item.Name))
            {
                CreateInventoryRow(inventoryList, item);
            }
        }

        private static void CreateInventoryRow(Transform parent, ReadOnlyItem item)
        {
            var row = SampleUI.CreateRect(parent, item.Name);
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

            var name = SampleUI.CreateText(row, item.Name, 15, new Color(0.90f, 0.94f, 1f), FontStyle.Bold);
            name.alignment = TextAnchor.MiddleLeft;
            name.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;

            var value = SampleUI.CreateText(row, item.Value.ToString(), 15, new Color(0.72f, 0.80f, 0.92f), FontStyle.Normal);
            value.alignment = TextAnchor.MiddleRight;
            value.gameObject.GetComponent<LayoutElement>().preferredWidth = 70f;
        }
    }
}
