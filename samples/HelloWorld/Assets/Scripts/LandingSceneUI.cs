// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HelloWorld.Assets.Scripts.Neo;
using NeoCompose.Runtime;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

namespace HelloWorld.Assets.Scripts
{
    /// <summary>
    /// A small 2D landing scene powered by generated World grid content. It intentionally
    /// keeps gameplay code in the sample, while exercising the generated tile/object
    /// wrappers and the save-backed grid mutation APIs a game would use directly.
    /// </summary>
    internal sealed class LandingSceneUI : IDisposable
    {
        private const int BarrierRewardBits = 25;

        private GameObject root;
        private GameObject player;
        private Camera camera;
        private NeoTileGridRenderer renderer;
        private HelloWorldNeo neo;
        private ReadOnlyOldConsoleLandingGridContent content;
        private OldConsoleLandingGridContent saveContent;
        private BlockedPath blockedPath;
        private Func<Awaitable> saveAction;
        private Func<string, Action, bool> triggerDialogue;
        private Func<bool> dialogueIsOpen;
        private Action closeAction;
        private GameObject promptPanel;
        private Text promptText;
        private Text statusText;
        private GameObject loadingPanel;
        private Text loadingText;
        private Vector2Int playerCell;
        private bool glyphAttuned
        {
            get
            {
                return neo.Dialogues.HasVisited(LandingDialogueIds.BootGlyphAttuned);
            }
        }
        private bool barrierOpening;
        private bool rewardClaimed
        {
            get
            {
                return neo.Dialogues.HasVisited(LandingDialogueIds.BootGlyphSealReady);
            }
        }
        private bool loading;
        private int loadGeneration;
        private CancellationTokenSource loadCancellation;
        private Sprite fallbackPlayerSprite;
        private Bounds worldBounds = new(Vector3.zero, new Vector3(10f, 7f, 1f));
        private readonly HashSet<Vector2Int> walkableCells = new();
        private readonly HashSet<Vector2Int> collisionCells = new();
        private readonly HashSet<Vector2Int> blockedPathCells = new();
        private readonly HashSet<Vector2Int> bootGlyphCells = new();
        private readonly List<NeoResolvedObjectInstance> objectInstances = new();

        public bool IsOpen => root != null && root.activeSelf;

        public int RenderedTilemapCount =>
            root == null ? 0 : root.GetComponentsInChildren<Tilemap>(includeInactive: true).Length;

        public void Show(
            HelloWorldNeo neo,
            Action onClose,
            Func<Awaitable> onSave,
            Func<string, Action, bool> onTriggerDialogue,
            Func<bool> onDialogueIsOpen)
        {
            this.neo = neo ?? throw new ArgumentNullException(nameof(neo));
            closeAction = onClose;
            saveAction = onSave;
            triggerDialogue = onTriggerDialogue ?? throw new ArgumentNullException(nameof(onTriggerDialogue));
            dialogueIsOpen = onDialogueIsOpen ?? throw new ArgumentNullException(nameof(onDialogueIsOpen));
            content = neo.Assets.Worlds.OldConsoleLanding.Content;
            saveContent = OldConsoleLandingGridContent.ResolveForSave(
                neo.Client,
                neo.Assets.Worlds.OldConsoleLanding.valueId!);
            if (!neo.Assets.Worlds.OldConsoleLanding.Children.First((check) => check.Name == "Blocked Path").TryWritable(out BlockedPath blocked))
            {
                throw new InvalidCastException($"blocked is invalid type, should be BlockedPath");
            }
            blockedPath = blocked;

            EnsureBuilt();

            barrierOpening = false;
            root.SetActive(true);
            StartLoadWorld();
        }

        public void Tick()
        {
            if (!IsOpen) return;
            if (loading) return;
            SynchronizeBlockedPathState();

            bool isDialogueOpen = dialogueIsOpen?.Invoke() == true;
            SetPromptVisible(!isDialogueOpen);
            if (isDialogueOpen) return;

            if (Input.GetKeyDown(KeyCode.W)) TryMove(Vector2Int.up);
            if (Input.GetKeyDown(KeyCode.S)) TryMove(Vector2Int.down);
            if (Input.GetKeyDown(KeyCode.A)) TryMove(Vector2Int.left);
            if (Input.GetKeyDown(KeyCode.D)) TryMove(Vector2Int.right);
            if (Input.GetKeyDown(KeyCode.E)) Interact();
        }

        public void Hide()
        {
            loadGeneration++;
            CancelLoad();
            loading = false;
            if (root != null) root.SetActive(false);
        }

        public void Dispose()
        {
            loadGeneration++;
            CancelLoad();
            if (root != null)
            {
                SampleUI.DestroyObject(root);
                root = null;
            }
        }

        private void TryMove(Vector2Int delta)
        {
            var target = playerCell + delta;
            if (!CanEnter(target))
            {
                SetStatus("The collision layer catches your boot before the step lands.");
                return;
            }

            MovePlayerTo(target);
            UpdatePrompt();
        }

        private bool CanEnter(Vector2Int cell)
        {
            return walkableCells.Contains(cell) && !collisionCells.Contains(cell);
        }

        private void Interact()
        {
            if (TryFindNearbyBarrier(out _))
            {
                if (!glyphAttuned)
                {
                    TriggerLandingDialogue(
                        LandingDialogueIds.BootGlyphSealLocked,
                        () => SetStatus("Look for a dark boot-glyph floor tile and press E beside it."));
                    return;
                }

                TriggerLandingDialogue(LandingDialogueIds.BootGlyphSealReady, UpdatePrompt);
                return;
            }

            if (!glyphAttuned && TryFindNearbyBootGlyph(out _))
            {
                TriggerLandingDialogue(
                    LandingDialogueIds.BootGlyphAttuned,
                    () =>
                    {
                        SetStatus("Boot trace captured. Take it to the teal blocker or the exit prompt.");
                    });
                return;
            }

            if (TryFindNearbyObject<ExitPromptObject>(out _))
            {
                var barrierClosed = IsBarrierActive();
                TriggerLandingDialogue(
                    barrierClosed && glyphAttuned
                        ? LandingDialogueIds.ExitPromptRelay
                        : LandingDialogueIds.ExitPromptQuiet,
                    barrierClosed && glyphAttuned
                        ? UpdatePrompt
                        : UpdatePrompt);
                return;
            }

            if (TryFindNearbyObject<VaultPlaqueObject>(out _))
            {
                Debug.Log($"IsBarrierActive: {IsBarrierActive()}, glyphAttuned: {glyphAttuned}");
                if (IsBarrierActive() && glyphAttuned)
                {
                    TriggerLandingDialogue(LandingDialogueIds.BootGlyphSealReady, UpdatePrompt);
                    return;
                }

                TriggerLandingDialogue(
                    IsBarrierActive()
                        ? LandingDialogueIds.VaultPlaqueLocked
                        : LandingDialogueIds.VaultPlaqueReward,
                    () =>
                    {
                        SetStatus(IsBarrierActive()
                            ? "Find the glyph trace and open the blocker before claiming the reward."
                            : "The reward path is open. The save has the receipt.");
                    });
                return;
            }

            SetStatus("Nothing answers here. Try a glyph tile, the teal blocker, or the exit prompt.");
        }

        private void SynchronizeBlockedPathState()
        {
            if (content == null || renderer == null) return;

            var currentBlockedTiles = content.Collisions.GetTiles()
                .Where(tile =>
                    tile.SourceKind == NeoTileOutputSourceKind.TileLayerLink &&
                    !string.IsNullOrEmpty(tile.SourceTileLayerLinkId))
                .ToArray();
            var currentBlockedCells = currentBlockedTiles
                .Select(tile => tile.Cell)
                .ToHashSet();
            if (blockedPathCells.SetEquals(currentBlockedCells)) return;

            var wasClosed = blockedPathCells.Any(cell => collisionCells.Contains(cell));
            var removedCells = blockedPathCells.Except(currentBlockedCells).ToArray();

            foreach (var cell in removedCells)
            {
                collisionCells.Remove(cell);
                if (!renderer.TryClearTile(content.Collisions.LayerId, cell))
                {
                    Debug.LogWarning(
                        $"Neo Compose: rendered tilemap for layer '{content.Collisions.LayerId}' was not available for incremental barrier clear.");
                }
            }

            blockedPathCells.Clear();
            foreach (var tile in currentBlockedTiles)
            {
                blockedPathCells.Add(tile.Cell);
                collisionCells.Add(tile.Cell);
            }

            MovePlayerTo(playerCell);
            UpdatePrompt();

            var isOpen = !blockedPathCells.Any(cell => collisionCells.Contains(cell));
            if (wasClosed && isOpen)
            {
                PersistBarrierOpenAsync();
            }
        }

        private async void PersistBarrierOpenAsync()
        {
            if (barrierOpening) return;
            barrierOpening = true;

            try
            {
                neo.Save.Bits += BarrierRewardBits;
                SetStatus($"Barrier removed. +{BarrierRewardBits} bits recovered from the old landing cache.");

                if (saveAction != null)
                {
                    await saveAction();
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(exception);
                SetStatus("The barrier opened locally, but save failed. Check the console.");
            }
            finally
            {
                barrierOpening = false;
            }
        }

        private bool IsBarrierActive() =>
            blockedPath.Tiles.Count > 0;

        private bool TryFindNearbyBarrier(out Vector2Int cell)
        {
            foreach (var barrierCell in blockedPathCells)
            {
                if (!collisionCells.Contains(barrierCell)) continue;
                if (!IsNear(playerCell, barrierCell)) continue;
                cell = barrierCell;
                return true;
            }

            cell = default;
            return false;
        }

        private bool TryFindNearbyBootGlyph(out Vector2Int cell)
        {
            foreach (var bootGlyphCell in bootGlyphCells)
            {
                if (!IsNear(playerCell, bootGlyphCell)) continue;
                cell = bootGlyphCell;
                return true;
            }

            cell = default;
            return false;
        }

        private bool TryFindNearbyObject<T>(out NeoResolvedObjectInstance instance)
            where T : NeoGeneratedCustomValue
        {
            foreach (var obj in objectInstances)
            {
                if (obj.Object is not T) continue;
                IEnumerable<Vector2Int> cells = obj.Footprint.Count == 0
                    ? new[] { obj.Cell }
                    : obj.Footprint;
                if (!cells.Any(cell => IsNear(playerCell, cell))) continue;
                instance = obj;
                return true;
            }

            instance = default;
            return false;
        }

        private static bool IsNear(Vector2Int a, Vector2Int b)
        {
            var distance = Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
            return distance <= 1;
        }

        private Vector2Int FindPlayerSpawnCell()
        {
            foreach (var obj in objectInstances)
            {
                if (obj.Object is PlayerSpawnObject) return obj.Cell;
            }

            return new Vector2Int(0, 4);
        }

        private void MovePlayerTo(Vector2Int cell)
        {
            playerCell = cell;
            EnsurePlayer();
            player.transform.localPosition = new Vector3(cell.x + 0.5f, cell.y + 0.5f, -0.2f);
        }

        private void EnsurePlayer()
        {
            if (player != null) return;

            player = new GameObject("Player");
            player.transform.SetParent(renderer.transform, false);
            var spriteRenderer = player.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = FindRenderedPlayerSprite() ?? CreateFallbackPlayerSprite();
            spriteRenderer.sortingOrder = 20_000;
            ScaleSpriteRendererToCell(spriteRenderer);
        }

        private Sprite FindRenderedPlayerSprite()
        {
            var objectLayer = renderer.transform.Find("Object Layer - Objects");
            if (objectLayer == null) return null;

            var spawn = objectLayer.Find("Object - old-console-object:player-spawn");
            if (spawn == null) return null;

            spawn.gameObject.SetActive(false);
            return spawn.GetComponentsInChildren<SpriteRenderer>(includeInactive: true)
                .Select(spriteRenderer => spriteRenderer.sprite)
                .FirstOrDefault(sprite => sprite != null);
        }

        private void ScaleSpriteRendererToCell(SpriteRenderer spriteRenderer)
        {
            var sprite = spriteRenderer.sprite;
            if (sprite == null) return;

            var width = Mathf.Max(sprite.bounds.size.x, 0.0001f);
            var height = Mathf.Max(sprite.bounds.size.y, 0.0001f);
            spriteRenderer.transform.localScale = new Vector3(1f / width, 1f / height, 1f);
        }

        private Sprite CreateFallbackPlayerSprite()
        {
            if (fallbackPlayerSprite != null) return fallbackPlayerSprite;

            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[16 * 16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(0.28f, 0.35f, 1f, 0f);
            for (int y = 3; y < 13; y++)
            {
                for (int x = 6; x < 10; x++)
                {
                    pixels[y * 16 + x] = new Color(0.31f, 0.25f, 1f, 1f);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            fallbackPlayerSprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16f);
            fallbackPlayerSprite.name = "Fallback landing player";
            return fallbackPlayerSprite;
        }

        private void StartLoadWorld()
        {
            CancelLoad();
            var generation = ++loadGeneration;
            loadCancellation = new CancellationTokenSource();
            loading = true;
            SetPromptVisible(false);
            SetStatus(string.Empty);
            SetLoadingVisible(true, "Loading tile grid...");
            LoadWorldAsync(generation, loadCancellation.Token);
        }

        private async void LoadWorldAsync(int generation, CancellationToken token)
        {
            try
            {
                // Let the loading UI paint before Unity starts building tilemaps.
                await YieldNextFrameAsync(token);
                if (!IsCurrentLoad(generation)) return;

                await RenderWorldAsync(token);
                if (!IsCurrentLoad(generation)) return;

                MovePlayerTo(FindPlayerSpawnCell());
                Frame(worldBounds);
                UpdatePrompt();
                SetStatus("WASD moves. E talks to whatever the old console is whispering through.");
                SetPromptVisible(true);
            }
            catch (Exception exception)
            {
                if (exception is OperationCanceledException) return;
                Debug.LogError(exception);
                if (IsCurrentLoad(generation))
                {
                    SetStatus("The landing grid failed to load. Check the console.");
                }
            }
            finally
            {
                if (IsCurrentLoad(generation))
                {
                    loading = false;
                    SetLoadingVisible(false);
                }
            }
        }

        private bool IsCurrentLoad(int generation) =>
            root != null && root.activeSelf && generation == loadGeneration;

        private static async Awaitable YieldNextFrameAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Application.isPlaying)
            {
                await Awaitable.NextFrameAsync(token);
            }
        }

        private async Awaitable RenderWorldAsync(CancellationToken token)
        {
            await renderer.RenderAsync(content, new NeoTileGridRenderOptions
            {
                MaxTilesPerFrame = 256,
                MaxObjectsPerFrame = 2,
                YieldBeforeRender = false,
                CancellationToken = token,
            });
            BuildInteractionCache();

            player = null;
            EnsurePlayer();
        }

        private void CancelLoad()
        {
            if (loadCancellation == null) return;
            loadCancellation.Cancel();
            loadCancellation.Dispose();
            loadCancellation = null;
        }

        private void BuildInteractionCache()
        {
            var bounds = new CellBoundsAccumulator();
            walkableCells.Clear();
            collisionCells.Clear();
            blockedPathCells.Clear();
            bootGlyphCells.Clear();
            objectInstances.Clear();

            foreach (var tile in content.Background.GetTiles())
            {
                walkableCells.Add(tile.Cell);
                bounds.Include(tile.Cell);
                if (tile.Tile is BootGlyphTile)
                {
                    bootGlyphCells.Add(tile.Cell);
                }
            }

            foreach (var tile in content.Collisions.GetTiles())
            {
                collisionCells.Add(tile.Cell);
                if (tile.SourceKind == NeoTileOutputSourceKind.TileLayerLink &&
                    !string.IsNullOrEmpty(tile.SourceTileLayerLinkId))
                {
                    blockedPathCells.Add(tile.Cell);
                }
                bounds.Include(tile.Cell);
            }

            foreach (var obj in content.Objects.GetObjects())
            {
                objectInstances.Add(obj);
                if (obj.Footprint.Count == 0)
                {
                    bounds.Include(obj.Cell);
                    continue;
                }
                foreach (var cell in obj.Footprint)
                {
                    bounds.Include(cell);
                }
            }
            worldBounds = bounds.ToBounds();
        }

        private void UpdatePrompt()
        {
            if (promptText == null) return;

            if (TryFindNearbyBarrier(out _))
            {
                promptText.text = glyphAttuned
                    ? "E Unblock barrier"
                    : "E Inspect sealed barrier";
                return;
            }

            if (TryFindNearbyBootGlyph(out _) && !glyphAttuned)
            {
                promptText.text = "E Read boot glyph";
                return;
            }

            if (TryFindNearbyObject<ExitPromptObject>(out _))
            {
                promptText.text = IsBarrierActive()
                    ? "E Ask exit prompt"
                    : "E Review launch prompt";
                return;
            }

            if (TryFindNearbyObject<VaultPlaqueObject>(out _))
            {
                promptText.text = rewardClaimed ? "E Read plaque again" : "E Inspect reward plaque";
                return;
            }

            promptText.text = "WASD Move  •  E Interact";
        }

        private void SetStatus(string message)
        {
            if (statusText != null) statusText.text = message ?? string.Empty;
        }

        private void SetPromptVisible(bool visible)
        {
            if (promptPanel != null && promptPanel.activeSelf != visible)
            {
                promptPanel.SetActive(visible);
            }
        }

        private void SetLoadingVisible(bool visible, string message = null)
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

        private void TriggerLandingDialogue(string dialogueId, Action onFinish)
        {
            if (triggerDialogue != null && triggerDialogue(dialogueId, onFinish))
            {
                return;
            }

            Debug.LogWarning($"Neo Compose: landing dialogue '{dialogueId}' could not be triggered.");
            SetStatus("That Neo dialogue flow is missing from the sample export.");
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
            renderer.Lifecycle = new LandingTileGridLifecycle();

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

        private void Frame(Bounds bounds)
        {
            float screenHeight = Mathf.Max(1, Screen.height);
            float aspect = Mathf.Max(1f, Screen.width / screenHeight);
            float width = Mathf.Max(1f, bounds.size.x + 3f);
            float height = Mathf.Max(1f, bounds.size.y + 3f);

            camera.transform.position = new Vector3(bounds.center.x, bounds.center.y, -10f);
            camera.orthographicSize = Mathf.Max(3.5f, height * 0.5f, width / (2f * aspect));
        }

        private static class LandingDialogueIds
        {
            public const string BootGlyphSealLocked = "2a49e84a-ab1f-4468-a9a3-f29796cbf086";
            public const string BootGlyphSealReady = "d755935f-4c3a-4d43-8c40-4ba3f7d28063";
            public const string BootGlyphAttuned = "12729fbc-56a7-4d8f-b04a-ac039604dfe9";
            public const string ExitPromptRelay = "d5a8097d-f02b-41c7-8356-9442a4a29412";
            public const string ExitPromptQuiet = "7a6bcb67-d42a-4eb8-9934-0263d506e85c";
            public const string VaultPlaqueLocked = "da73bce9-0d39-4c27-bb09-32b538f97f61";
            public const string VaultPlaqueReward = "bbda459e-c77e-4084-9047-22b1dfbb0bff";
        }

        private sealed class LandingTileGridLifecycle : NeoTileGridLifecycle
        {
            public override void OnTileLayerCreated(NeoTileLayerContext context)
            {
                if (!string.Equals(context.Layer.DisplayName, "Collisions", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                context.Tilemap.gameObject.AddComponent<TilemapCollider2D>();
            }
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
