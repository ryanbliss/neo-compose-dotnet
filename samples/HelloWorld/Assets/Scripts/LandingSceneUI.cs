// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System;
using HelloWorld.Assets.Scripts.Neo;
using NeoCompose.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace HelloWorld.Assets.Scripts
{
    /// <summary>
    /// Presentation shell for the Old Console Landing sample scene. It owns Unity
    /// objects, tilemap rendering, input polling, and chrome; <see cref="LandingSceneGameplay"/>
    /// owns the generated Neo content and gameplay lifecycle.
    /// </summary>
    internal sealed class LandingSceneUI : IDisposable
    {
        private GameObject root;
        private Camera camera;
        private NeoTileGridRenderer renderer;
        private GameObject promptPanel;
        private Text promptText;
        private Text statusText;
        private GameObject loadingPanel;
        private Text loadingText;

        public event Action<Vector2Int> MoveRequested;
        public event Action InteractRequested;
        public event Action CloseRequested;

        public bool IsOpen => root != null && root.activeSelf;

        public void Show()
        {
            EnsureBuilt();
            root.SetActive(true);
        }

        public void Tick()
        {
            if (!IsOpen) return;

            if (Input.GetKeyDown(KeyCode.W)) MoveRequested?.Invoke(Vector2Int.up);
            if (Input.GetKeyDown(KeyCode.S)) MoveRequested?.Invoke(Vector2Int.down);
            if (Input.GetKeyDown(KeyCode.A)) MoveRequested?.Invoke(Vector2Int.left);
            if (Input.GetKeyDown(KeyCode.D)) MoveRequested?.Invoke(Vector2Int.right);
            if (Input.GetKeyDown(KeyCode.E)) InteractRequested?.Invoke();
        }

        public void Dispose()
        {
            if (root != null)
            {
                SampleUI.DestroyObject(root);
                root = null;
            }
        }

        public async Awaitable RenderGridAsync(ReadOnlyOldConsoleLandingGridContent content)
        {
            // Restart-safe: a newer RenderAsync (or destroying the renderer)
            // cancels this one, so no cancellation plumbing is needed here.
            // The player is an ordinary SDK-rendered object (the
            // PlayerSpawnObject instance); gameplay moves it by writing its
            // Session-storage Position, which the renderer mirrors onto the
            // spawned GameObject.
            await renderer.RenderAsync(content);
        }

        public void RenderChrome(string prompt, string status)
        {
            if (promptText != null) promptText.text = prompt ?? string.Empty;
            if (statusText != null) statusText.text = status ?? string.Empty;
        }

        public void SetPromptVisible(bool visible)
        {
            if (promptPanel != null && promptPanel.activeSelf != visible)
            {
                promptPanel.SetActive(visible);
            }
        }

        public void SetLoadingVisible(bool visible, string message = null)
        {
            if (loadingText != null && !string.IsNullOrEmpty(message))
            {
                loadingText.text = message;
            }

            if (loadingPanel != null && loadingPanel.activeSelf != visible)
            {
                loadingPanel.SetActive(visible);
            }
        }

        public void Frame(Bounds bounds)
        {
            float screenHeight = Mathf.Max(1, Screen.height);
            float aspect = Mathf.Max(1f, Screen.width / screenHeight);
            float width = Mathf.Max(1f, bounds.size.x + 3f);
            float height = Mathf.Max(1f, bounds.size.y + 3f);

            camera.transform.position = new Vector3(bounds.center.x, bounds.center.y, -10f);
            camera.orthographicSize = Mathf.Max(3.5f, height * 0.5f, width / (2f * aspect));
        }

        private void EnsureBuilt()
        {
            if (root != null) return;

            SampleUI.EnsureEventSystem();

            root = new GameObject("Old Console Landing Scene");

            var cameraGo = new GameObject("Landing Camera", typeof(Camera));
            cameraGo.transform.SetParent(root.transform, false);
            camera = cameraGo.GetComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.015f, 0.018f, 0.025f, 1f);
            camera.depth = 20f;

            var world = new GameObject("World");
            world.transform.SetParent(root.transform, false);
            renderer = world.AddComponent<NeoTileGridRenderer>();
            renderer.CellSize = 1f;
            renderer.RenderObjects = true;

            BuildChrome();
            root.SetActive(false);
        }

        private void BuildChrome()
        {
            var canvasGo = new GameObject("Landing UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(root.transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var header = SampleUI.CreateRect(canvasGo.transform, "LandingHeader");
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = Vector2.one;
            header.pivot = new Vector2(0.5f, 1f);
            header.offsetMin = new Vector2(0f, -86f);
            header.offsetMax = Vector2.zero;
            var background = header.gameObject.AddComponent<Image>();
            background.color = new Color(0.04f, 0.055f, 0.075f, 0.86f);

            var layout = header.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 12, 12);
            layout.spacing = 18f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;

            var title = SampleUI.CreateText(
                header,
                "OLD CONSOLE LANDING\n<size=18><color=#9FB3C8>Capitol sub-basement / recovery grid</color></size>",
                34,
                new Color(0.93f, 0.96f, 1f),
                FontStyle.Bold);
            title.supportRichText = true;
            title.lineSpacing = 0.9f;
            var titleLayout = title.gameObject.GetComponent<LayoutElement>();
            titleLayout.flexibleWidth = 1f;
            titleLayout.preferredHeight = 62f;

            SampleUI.CreateButton(header, "Launch", 126f, 38f, true, () => CloseRequested?.Invoke());

            BuildPrompt(canvasGo.transform);
            BuildLoadingOverlay(canvasGo.transform);
        }

        private void BuildPrompt(Transform parent)
        {
            var panel = SampleUI.CreateRect(parent, "LandingPrompt");
            promptPanel = panel.gameObject;
            panel.anchorMin = new Vector2(0f, 0f);
            panel.anchorMax = new Vector2(0f, 0f);
            panel.pivot = new Vector2(0f, 0f);
            panel.anchoredPosition = new Vector2(28f, 28f);
            panel.sizeDelta = new Vector2(640f, 96f);

            var background = panel.gameObject.AddComponent<Image>();
            background.color = new Color(0.04f, 0.055f, 0.075f, 0.82f);

            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 12, 12);
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;

            promptText = SampleUI.CreateText(panel, string.Empty, 24, new Color(0.96f, 0.98f, 1f), FontStyle.Bold);
            promptText.gameObject.GetComponent<LayoutElement>().preferredHeight = 30f;
            statusText = SampleUI.CreateText(panel, string.Empty, 18, new Color(0.73f, 0.82f, 0.92f), FontStyle.Normal);
            statusText.gameObject.GetComponent<LayoutElement>().preferredHeight = 34f;
        }

        private void BuildLoadingOverlay(Transform parent)
        {
            var panel = SampleUI.CreateRect(parent, "LandingLoading");
            loadingPanel = panel.gameObject;
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;

            var scrim = panel.gameObject.AddComponent<Image>();
            scrim.color = new Color(0.015f, 0.018f, 0.025f, 0.56f);

            var loadingBox = SampleUI.CreateRect(panel, "LandingLoadingBox");
            loadingBox.anchorMin = new Vector2(0.5f, 0.5f);
            loadingBox.anchorMax = new Vector2(0.5f, 0.5f);
            loadingBox.pivot = new Vector2(0.5f, 0.5f);
            loadingBox.anchoredPosition = Vector2.zero;
            loadingBox.sizeDelta = new Vector2(520f, 112f);
            var background = loadingBox.gameObject.AddComponent<Image>();
            background.color = new Color(0.04f, 0.055f, 0.075f, 0.92f);

            var layout = loadingBox.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 18, 18);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = false;

            loadingText = SampleUI.CreateText(
                loadingBox,
                "Loading tile grid...",
                24,
                new Color(0.96f, 0.98f, 1f),
                FontStyle.Bold);
            loadingText.gameObject.GetComponent<LayoutElement>().preferredHeight = 34f;

            var hint = SampleUI.CreateText(
                loadingBox,
                "Streaming generated tilemaps over a few frames.",
                18,
                new Color(0.73f, 0.82f, 0.92f),
                FontStyle.Normal);
            hint.gameObject.GetComponent<LayoutElement>().preferredHeight = 28f;

            loadingPanel.SetActive(false);
        }

    }
}
