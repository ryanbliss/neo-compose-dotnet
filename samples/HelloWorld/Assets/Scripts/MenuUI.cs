// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HelloWorld.Assets.Scripts
{
    /// <summary>
    /// Snapshot of the runtime authentication cell for the menu: whether cloud sync
    /// is enabled, the signed-in identity, and any in-progress device-flow prompt.
    /// </summary>
    internal readonly struct MenuAuthInfo
    {
        public MenuAuthInfo(
            bool cloudEnabled,
            bool signedIn,
            bool busy,
            string identity,
            string userCode,
            string verificationUri)
        {
            CloudEnabled = cloudEnabled;
            SignedIn = signedIn;
            Busy = busy;
            Identity = identity;
            UserCode = userCode;
            VerificationUri = verificationUri;
        }

        public bool CloudEnabled { get; }
        public bool SignedIn { get; }
        public bool Busy { get; }
        public string Identity { get; }
        public string UserCode { get; }
        public string VerificationUri { get; }
    }

    /// <summary>
    /// Save-driven main menu UI for the sample: a loading state, an auth cell (device
    /// flow), the browsable save list (local + cloud) with Continue / Clone / Archive,
    /// a "Create new" action, and a modal prompt the SDK lifecycle events
    /// (conflict / migration / clone) are wired to. Built in code, mirroring
    /// <see cref="CoreUI"/>.
    /// </summary>
    internal sealed class MenuUI : IDisposable
    {
        private GameObject root;
        private RectTransform authCell;
        private Text authStatus;
        private Button authButton;
        private Text authButtonLabel;
        private Text loadingLabel;
        private RectTransform saveList;
        private Button createButton;
        private GameObject modal;
        private GameObject loadingOverlay;

        public void Render(
            bool loading,
            IReadOnlyList<NeoSaveListEntry> saves,
            MenuAuthInfo auth,
            Action onCreateNew,
            Action<string> onContinue,
            Action<string> onClone,
            Action<string> onDelete,
            Action onSignIn)
        {
            EnsureBuilt(onCreateNew, onSignIn);

            loadingLabel.gameObject.SetActive(loading);
            saveList.gameObject.SetActive(!loading);
            createButton.gameObject.SetActive(!loading);

            RenderAuthCell(auth, onSignIn);
            if (!loading)
            {
                RebuildSaveList(saves, onContinue, onClone, onDelete);
            }
        }

        /// <summary>Shows or hides the menu panel (kept alive so prompts still work during gameplay).</summary>
        public void SetMenuVisible(bool visible)
        {
            if (root != null) root.SetActive(visible);
        }

        /// <summary>
        /// Shows a modal prompt with one button per option, on its own top-level
        /// canvas so it works whether the menu or the gameplay screen is showing.
        /// Used for the lifecycle dialogs (keep-local / keep-remote, migrate / skip,
        /// clone / cancel).
        /// </summary>
        public void ShowPrompt(string title, string message, IReadOnlyList<(string label, Action onClick)> options)
        {
            DismissPrompt();
            SampleUI.EnsureEventSystem();

            modal = new GameObject("HelloWorld Modal", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var modalCanvas = modal.GetComponent<Canvas>();
            modalCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            modalCanvas.sortingOrder = 20;
            var modalScaler = modal.GetComponent<CanvasScaler>();
            modalScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            modalScaler.referenceResolution = new Vector2(1920f, 1080f);
            modalScaler.matchWidthOrHeight = 0.5f;

            var scrimRect = SampleUI.CreateRect(modal.transform, "Scrim");
            scrimRect.anchorMin = Vector2.zero;
            scrimRect.anchorMax = Vector2.one;
            scrimRect.offsetMin = Vector2.zero;
            scrimRect.offsetMax = Vector2.zero;
            scrimRect.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);

            var dialog = SampleUI.CreateRect(modal.transform, "Dialog");
            dialog.anchorMin = new Vector2(0.5f, 0.5f);
            dialog.anchorMax = new Vector2(0.5f, 0.5f);
            dialog.pivot = new Vector2(0.5f, 0.5f);
            dialog.sizeDelta = new Vector2(560f, 260f);
            dialog.gameObject.AddComponent<Image>().color = new Color(0.10f, 0.13f, 0.18f, 1f);
            var dialogLayout = dialog.gameObject.AddComponent<VerticalLayoutGroup>();
            dialogLayout.padding = new RectOffset(26, 26, 24, 24);
            dialogLayout.spacing = 14f;
            dialogLayout.childControlHeight = true;
            dialogLayout.childControlWidth = true;
            dialogLayout.childForceExpandWidth = true;

            var titleText = SampleUI.CreateText(dialog, title, 24, new Color(0.93f, 0.96f, 1f), FontStyle.Bold);
            titleText.gameObject.GetComponent<LayoutElement>().preferredHeight = 34f;
            var body = SampleUI.CreateText(dialog, message, 16, new Color(0.80f, 0.86f, 0.94f), FontStyle.Normal);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.gameObject.GetComponent<LayoutElement>().flexibleHeight = 1f;

            var buttonRow = SampleUI.CreateRect(dialog, "Options");
            buttonRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
            var rowLayout = buttonRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 10f;
            rowLayout.childAlignment = TextAnchor.MiddleRight;
            rowLayout.childControlHeight = true;
            rowLayout.childControlWidth = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;

            foreach (var (label, onClick) in options)
            {
                var captured = onClick;
                SampleUI.CreateButton(buttonRow, label, 150f, 38f, true, () =>
                {
                    DismissPrompt();
                    captured?.Invoke();
                });
            }
        }

        /// <summary>
        /// Shows a full-screen loading overlay on its own top canvas while a save
        /// loads into gameplay (the menu hides itself first). Especially needed with
        /// cloud sync, where loading the save can hit the network. Idempotent.
        /// </summary>
        public void ShowLoadingOverlay(string message)
        {
            HideLoadingOverlay();
            SampleUI.EnsureEventSystem();

            loadingOverlay = new GameObject(
                "HelloWorld Loading",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = loadingOverlay.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 15;
            var scaler = loadingOverlay.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var scrim = SampleUI.CreateRect(loadingOverlay.transform, "Scrim");
            scrim.anchorMin = Vector2.zero;
            scrim.anchorMax = Vector2.one;
            scrim.offsetMin = Vector2.zero;
            scrim.offsetMax = Vector2.zero;
            // Opaque so it fully covers the gameplay screen while it builds in.
            scrim.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.07f, 0.10f, 1f);

            var label = SampleUI.CreateText(
                scrim, message, 22, new Color(0.82f, 0.88f, 0.96f), FontStyle.Bold);
            label.alignment = TextAnchor.MiddleCenter;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
        }

        public void HideLoadingOverlay()
        {
            if (loadingOverlay != null)
            {
                SampleUI.DestroyObject(loadingOverlay);
                loadingOverlay = null;
            }
        }

        public void Dispose()
        {
            DismissPrompt();
            HideLoadingOverlay();
            if (root != null) SampleUI.DestroyObject(root);
        }

        private void RenderAuthCell(MenuAuthInfo auth, Action onSignIn)
        {
            authCell.gameObject.SetActive(auth.CloudEnabled);
            if (!auth.CloudEnabled) return;

            if (auth.SignedIn)
            {
                authStatus.text = $"Signed in as {auth.Identity}";
                authButton.gameObject.SetActive(false);
                return;
            }

            authButton.gameObject.SetActive(true);
            authButton.interactable = !auth.Busy;
            authButtonLabel.text = auth.Busy ? "Signing in…" : "Sign in";
            authStatus.text = auth.Busy && !string.IsNullOrEmpty(auth.UserCode)
                ? $"Enter code <b>{auth.UserCode}</b> at {auth.VerificationUri}"
                : "Sign in to browse and sync cloud saves.";
        }

        private void RebuildSaveList(
            IReadOnlyList<NeoSaveListEntry> saves,
            Action<string> onContinue,
            Action<string> onClone,
            Action<string> onDelete)
        {
            for (var i = saveList.childCount - 1; i >= 0; i--)
            {
                SampleUI.DestroyObject(saveList.GetChild(i).gameObject);
            }

            if (saves.Count == 0)
            {
                var empty = SampleUI.CreateText(saveList, "No saves yet — create a new game to begin.", 15,
                    new Color(0.54f, 0.61f, 0.73f), FontStyle.Normal);
                FixedHeight(empty.gameObject, 28f);
                return;
            }

            foreach (var save in saves)
            {
                CreateSaveRow(save, onContinue, onClone, onDelete);
            }
        }

        private void CreateSaveRow(
            NeoSaveListEntry save,
            Action<string> onContinue,
            Action<string> onClone,
            Action<string> onDelete)
        {
            var customId = save.customId;
            var row = SampleUI.CreateRect(saveList, $"Save {customId}");
            FixedHeight(row.gameObject, 52f);
            row.gameObject.AddComponent<Image>().color = new Color(0.11f, 0.14f, 0.20f, 1f);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(14, 10, 8, 8);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            string badge = save.isLocalOnly ? "local" : "synced";
            var label = $"{Name(save)}  <size=11><color=#7C8AA3>({badge})</color></size>";
            var nameText = SampleUI.CreateText(row, label, 15, new Color(0.92f, 0.96f, 1f), FontStyle.Bold);
            nameText.supportRichText = true;
            nameText.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;

            if (save.needsMigration)
            {
                var warn = SampleUI.CreateText(row, "needs migration", 12, new Color(0.95f, 0.74f, 0.4f), FontStyle.Italic);
                warn.alignment = TextAnchor.MiddleRight;
                warn.gameObject.GetComponent<LayoutElement>().preferredWidth = 118f;
            }

            SampleUI.CreateButton(row, "Continue", 104f, 34f, true, () => onContinue(customId));
            // Clone and Delete work whether the save is local-only or cloud-synced.
            SampleUI.CreateButton(row, "Clone", 76f, 34f, false, () => onClone(customId));
            SampleUI.CreateButton(row, "Delete", 80f, 34f, false, () => onDelete(customId));
        }

        private static string Name(NeoSaveListEntry save) =>
            string.IsNullOrWhiteSpace(save.name) ? save.customId : save.name;

        private void EnsureBuilt(Action onCreateNew, Action onSignIn)
        {
            if (root != null) return;

            SampleUI.EnsureEventSystem();

            root = new GameObject("HelloWorld Menu", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var panel = SampleUI.CreateRect(root.transform, "MenuPanel");
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(720f, 560f);
            panel.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.10f, 0.14f, 0.98f);
            var panelLayout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(28, 28, 24, 24);
            panelLayout.spacing = 12f;
            panelLayout.childControlHeight = true;
            panelLayout.childControlWidth = true;
            panelLayout.childForceExpandHeight = false;
            panelLayout.childForceExpandWidth = true;

            var heading = SampleUI.CreateText(panel, "Save Files", 26, new Color(0.95f, 0.97f, 1f), FontStyle.Bold);
            FixedHeight(heading.gameObject, 34f);

            var subtitle = SampleUI.CreateText(panel, "Continue a game or start a new one.", 14, new Color(0.54f, 0.61f, 0.73f), FontStyle.Normal);
            FixedHeight(subtitle.gameObject, 20f);

            BuildAuthCell(panel, onSignIn);

            loadingLabel = SampleUI.CreateText(panel, "Loading saves…", 15, new Color(0.66f, 0.74f, 0.86f), FontStyle.Normal);
            FixedHeight(loadingLabel.gameObject, 26f);

            saveList = SampleUI.CreateRect(panel, "SaveList");
            saveList.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var listLayout = saveList.gameObject.AddComponent<VerticalLayoutGroup>();
            listLayout.spacing = 8f;
            listLayout.childControlHeight = true;
            listLayout.childControlWidth = true;
            listLayout.childForceExpandHeight = false;
            listLayout.childForceExpandWidth = true;

            createButton = SampleUI.CreateButton(panel, "Create new game", 0f, 44f, true, () => onCreateNew());
            var createLayout = createButton.gameObject.GetComponent<LayoutElement>();
            createLayout.minWidth = 0f;
            createLayout.preferredWidth = 0f;
            createLayout.flexibleWidth = 1f;
        }

        /// <summary>
        /// Pins a row to a fixed height so a nested layout group can't make it
        /// flex-grow (UGUI's Horizontal/VerticalLayoutGroup default
        /// <c>childForceExpandHeight = true</c>, which otherwise reports a flexible
        /// height and lets the row balloon to fill its parent).
        /// </summary>
        private static void FixedHeight(GameObject go, float height)
        {
            var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            element.flexibleHeight = 0f;
        }

        private void BuildAuthCell(Transform parent, Action onSignIn)
        {
            authCell = SampleUI.CreateRect(parent, "AuthCell");
            FixedHeight(authCell.gameObject, 50f);
            authCell.gameObject.AddComponent<Image>().color = new Color(0.11f, 0.14f, 0.20f, 1f);
            var layout = authCell.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(14, 10, 8, 8);
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            authStatus = SampleUI.CreateText(authCell, "", 14, new Color(0.80f, 0.87f, 0.96f), FontStyle.Normal);
            authStatus.supportRichText = true;
            authStatus.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;

            authButton = SampleUI.CreateButton(authCell, "Sign in", 108f, 34f, true, () => onSignIn());
            authButtonLabel = authButton.GetComponentInChildren<Text>();
        }

        private void DismissPrompt()
        {
            if (modal != null)
            {
                SampleUI.DestroyObject(modal);
                modal = null;
            }
        }

    }
}
