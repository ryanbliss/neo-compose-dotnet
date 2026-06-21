// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System;
using HelloWorld.Assets.Scripts.Neo;
using NeoCompose.Runtime;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

namespace HelloWorld.Assets.Scripts
{
    /// <summary>
    /// A small 2D landing scene powered by the generated World grid content.
    /// This is intentionally sample code, but it exercises the same generated
    /// layer wrappers and Unity renderer that a game would use directly.
    /// </summary>
    internal sealed class LandingSceneUI : IDisposable
    {
        private GameObject root;
        private Camera camera;
        private NeoTileGridRenderer renderer;
        private Action closeAction;

        public bool IsOpen => root != null && root.activeSelf;

        public int RenderedTilemapCount =>
            root == null ? 0 : root.GetComponentsInChildren<Tilemap>(includeInactive: true).Length;

        public void Show(ReadOnlyOldConsoleLandingGridContent content, Action onClose)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            closeAction = onClose;
            EnsureBuilt();

            root.SetActive(true);
            renderer.Clear();
            renderer.Render(content);
            Frame(content);
        }

        public void Hide()
        {
            if (root != null) root.SetActive(false);
        }

        public void Dispose()
        {
            if (root != null)
            {
                SampleUI.DestroyObject(root);
                root = null;
            }
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

            SampleUI.CreateButton(header, "Launch", 126f, 38f, true, () => closeAction?.Invoke());
        }

        private void Frame(ReadOnlyOldConsoleLandingGridContent content)
        {
            Bounds bounds = BoundsFor(content);
            float screenHeight = Mathf.Max(1, Screen.height);
            float aspect = Mathf.Max(1f, Screen.width / screenHeight);
            float width = Mathf.Max(1f, bounds.size.x + 3f);
            float height = Mathf.Max(1f, bounds.size.y + 3f);

            camera.transform.position = new Vector3(bounds.center.x, bounds.center.y, -10f);
            camera.orthographicSize = Mathf.Max(3.5f, height * 0.5f, width / (2f * aspect));
        }

        private static Bounds BoundsFor(ReadOnlyOldConsoleLandingGridContent content)
        {
            var accumulator = new CellBoundsAccumulator();
            foreach (var layer in content.TileLayersInOrder)
            {
                foreach (var tile in layer.GetTiles())
                {
                    accumulator.Include(tile.Cell);
                }
            }
            foreach (var layer in content.ObjectLayersInOrder)
            {
                foreach (var obj in layer.GetObjects())
                {
                    if (obj.Footprint.Count == 0)
                    {
                        accumulator.Include(obj.Cell);
                        continue;
                    }
                    foreach (var cell in obj.Footprint)
                    {
                        accumulator.Include(cell);
                    }
                }
            }
            return accumulator.ToBounds();
        }

        private struct CellBoundsAccumulator
        {
            private bool hasCells;
            private int minX;
            private int minY;
            private int maxX;
            private int maxY;

            public void Include(Vector2Int cell)
            {
                if (!hasCells)
                {
                    minX = maxX = cell.x;
                    minY = maxY = cell.y;
                    hasCells = true;
                    return;
                }

                minX = Mathf.Min(minX, cell.x);
                minY = Mathf.Min(minY, cell.y);
                maxX = Mathf.Max(maxX, cell.x);
                maxY = Mathf.Max(maxY, cell.y);
            }

            public Bounds ToBounds()
            {
                if (!hasCells)
                {
                    return new Bounds(Vector3.zero, new Vector3(10f, 7f, 1f));
                }

                var center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, 0f);
                var size = new Vector3(maxX - minX + 1f, maxY - minY + 1f, 1f);
                return new Bounds(center, size);
            }
        }
    }
}
