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
        private readonly List<Image> stormSegments = new();
        private Text stormLabel;
        private GameObject crashOverlay;

        public void Render(
            string text,
            string questHint,
            int storm,
            bool knowsStormExact,
            ReadOnlyAnimationInfo shipAnimation,
            ReadOnlyAnimationInfo flareAnimation,
            Sprite sunSprite,
            AudioClip thrustSfx,
            Func<string, Sprite> parentPlanetSprite,
            ReadOnlyOutpost currentOutpost,
            IReadOnlyList<ReadOnlyOutpost> outposts,
            int bits,
            IReadOnlyList<ReadOnlyItem> inventory,
            Func<ReadOnlyOutpost, bool> hasNewContent,
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
            systemMap.Render(outposts, currentOutpost, storm, shipAnimation, flareAnimation, sunSprite, thrustSfx, parentPlanetSprite, hasNewContent, onVisitOutpost);
            bitsText.text = $"Bits: {bits}";
            UpdateStormGauge(storm, knowsStormExact);
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

        /// <summary>
        /// The flare gauge: 12 segments toward overflow. With Storm Corn the
        /// reading is exact; without it the gauge only resolves coarse bands —
        /// the corn's hum is the precision instrument.
        /// </summary>
        private void BuildStormGauge(Transform parent)
        {
            var gauge = SampleUI.CreateRect(parent, "StormGauge");
            var gaugeLayout = gauge.gameObject.AddComponent<LayoutElement>();
            gaugeLayout.preferredWidth = 330f;
            gaugeLayout.preferredHeight = 28f;
            var rowLayout = gauge.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 3f;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlHeight = true;
            rowLayout.childControlWidth = true;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childForceExpandWidth = false;

            var caption = SampleUI.CreateText(gauge, "Flare", 15, new Color(0.92f, 0.74f, 0.52f), FontStyle.Bold);
            caption.gameObject.GetComponent<LayoutElement>().preferredWidth = 46f;
            for (var i = 0; i < 12; i++)
            {
                var segment = SampleUI.CreateRect(gauge, $"Segment {i}");
                var segmentLayout = segment.gameObject.AddComponent<LayoutElement>();
                segmentLayout.preferredWidth = 11f;
                segmentLayout.preferredHeight = 16f;
                var image = segment.gameObject.AddComponent<Image>();
                image.color = SegmentOff;
                stormSegments.Add(image);
            }
            stormLabel = SampleUI.CreateText(gauge, "", 14, new Color(0.92f, 0.74f, 0.52f), FontStyle.Bold);
            stormLabel.gameObject.GetComponent<LayoutElement>().preferredWidth = 110f;
            stormLabel.alignment = TextAnchor.MiddleLeft;
        }

        private static readonly Color SegmentOff = new(0.16f, 0.19f, 0.26f, 0.9f);

        private void UpdateStormGauge(int storm, bool knowsExact)
        {
            int clamped = Mathf.Clamp(storm, 0, 12);
            // Without the corn the gauge snaps to coarse bands; the player
            // knows the weather, not the countdown.
            int band = storm <= 0 ? 0 : storm < 3 ? 1 : storm < 6 ? 2 : storm < 9 ? 3 : 4;
            int lit = knowsExact ? clamped : band * 3;
            for (var i = 0; i < stormSegments.Count; i++)
            {
                stormSegments[i].color = i >= lit
                    ? SegmentOff
                    : Color.Lerp(
                        new Color(0.95f, 0.72f, 0.30f),
                        new Color(0.95f, 0.25f, 0.18f),
                        i / 11f);
            }
            if (knowsExact)
            {
                stormLabel.text = $"{clamped}/12";
                return;
            }
            if (band == 0) stormLabel.text = "Calm";
            else if (band == 1) stormLabel.text = "Restless";
            else if (band == 2) stormLabel.text = "Surging";
            else if (band == 3) stormLabel.text = "Tearing";
            else stormLabel.text = "CRITICAL";
        }

        /// <summary>
        /// The flare-overflow death screen. The world crashed; the player's
        /// cargo — impossibly — did not.
        /// </summary>
        public void ShowCrash(Action onReboot)
        {
            if (crashOverlay != null)
            {
                crashOverlay.SetActive(true);
                return;
            }
            var rect = SampleUI.CreateRect(root.transform, "CrashOverlay");
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var background = rect.gameObject.AddComponent<Image>();
            background.color = new Color(0.02f, 0.01f, 0.02f, 0.985f);

            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(160, 160, 140, 140);
            layout.spacing = 18f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var heading = SampleUI.CreateText(rect, "FATAL: FLARE OVERFLOW", 44, new Color(0.95f, 0.30f, 0.22f), FontStyle.Bold);
            heading.alignment = TextAnchor.MiddleCenter;
            heading.gameObject.GetComponent<LayoutElement>().preferredHeight = 64f;

            var body = SampleUI.CreateText(
                rect,
                "world 'HelloWorld' terminated unexpectedly (signal: SOLAR)\n" +
                "run #2,147,483,648 — integer overflow in epoch counter\n\n" +
                "...rebooting from factory image\n" +
                "...restoring planets: OK\n" +
                "...restoring people: OK\n" +
                "...restoring you: <color=#FFAA66>WARNING — cargo checksum persisted across reboot</color>",
                20,
                new Color(0.80f, 0.84f, 0.90f),
                FontStyle.Normal);
            body.alignment = TextAnchor.MiddleCenter;
            body.gameObject.GetComponent<LayoutElement>().preferredHeight = 220f;

            var buttonRow = SampleUI.CreateRect(rect, "RebootRow");
            buttonRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 52f;
            var buttonLayout = buttonRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            buttonLayout.childAlignment = TextAnchor.MiddleCenter;
            buttonLayout.childControlHeight = true;
            buttonLayout.childControlWidth = true;
            buttonLayout.childForceExpandHeight = false;
            buttonLayout.childForceExpandWidth = false;
            SampleUI.CreateButton(buttonRow, "REBOOT", 220f, 48f, true, () =>
            {
                crashOverlay.SetActive(false);
                onReboot();
            });

            crashOverlay = rect.gameObject;
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
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;

            // The map IS the screen; chrome rides on translucent strips.
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.spacing = 0f;
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
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 86f;
            var headerBackground = row.gameObject.AddComponent<Image>();
            headerBackground.color = new Color(0.04f, 0.06f, 0.10f, 0.82f);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 12, 12);
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
            actionLayoutElement.preferredWidth = 470f;
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

            var statusRow = SampleUI.CreateRect(card, "StatusRow");
            statusRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
            var statusBackground = statusRow.gameObject.AddComponent<Image>();
            statusBackground.color = new Color(0.04f, 0.06f, 0.10f, 0.72f);
            var statusLayout = statusRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            statusLayout.padding = new RectOffset(28, 28, 8, 8);
            statusLayout.spacing = 24f;
            statusLayout.childAlignment = TextAnchor.MiddleLeft;
            statusLayout.childControlHeight = true;
            statusLayout.childControlWidth = true;
            statusLayout.childForceExpandHeight = false;
            statusLayout.childForceExpandWidth = false;
            bitsText = SampleUI.CreateText(statusRow, "Bits: 0", 20, new Color(0.92f, 0.96f, 1f), FontStyle.Bold);
            bitsText.gameObject.GetComponent<LayoutElement>().preferredWidth = 180f;
            BuildStormGauge(statusRow);
            questText = SampleUI.CreateText(statusRow, "", 16, new Color(0.62f, 0.81f, 1f), FontStyle.Italic);
            questText.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;
            travelMeta = SampleUI.CreateText(statusRow, "", 14, new Color(0.64f, 0.70f, 0.80f), FontStyle.Normal);
            travelMeta.alignment = TextAnchor.MiddleRight;
            travelMeta.gameObject.GetComponent<LayoutElement>().preferredWidth = 110f;

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
            var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.spacing = 0f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            return card;
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
