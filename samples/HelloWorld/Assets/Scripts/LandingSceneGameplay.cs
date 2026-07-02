// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HelloWorld.Assets.Scripts.Neo;
using NeoCompose.Runtime;
using UnityEngine;

namespace HelloWorld.Assets.Scripts
{
    /// <summary>
    /// SDK-facing gameplay model for the Old Console Landing sample. It owns
    /// generated Neo values, save-backed mutations, dialogue triggers, collision
    /// state, and prompts; <see cref="LandingSceneUI"/> owns Unity presentation.
    /// </summary>
    internal sealed class LandingSceneGameplay : IDisposable
    {
        private const int CacheRewardBits = 25;

        private readonly HelloWorldNeo neo;
        private readonly LandingSceneUI ui = new();
        private readonly Action closeRequested;
        private readonly Func<Awaitable> saveAction;
        private readonly Func<string, Action, bool> triggerDialogue;
        private readonly Func<bool> dialogueIsOpen;
        private readonly BlockedPath blockedPath;
        private IDisposable collisionSubscription;
        private IDisposable barrierSubscription;
        private bool barrierOpening;
        private bool barrierWasActive;
        private bool loading;
        private int loadGeneration;
        private CancellationTokenSource loadCancellation;
        private bool disposed;

        public LandingSceneGameplay(
            HelloWorldNeo neo,
            Action onCloseRequested,
            Func<Awaitable> onSave,
            Func<string, Action, bool> onTriggerDialogue,
            Func<bool> onDialogueIsOpen)
        {
            this.neo = neo ?? throw new ArgumentNullException(nameof(neo));
            closeRequested = onCloseRequested ?? throw new ArgumentNullException(nameof(onCloseRequested));
            saveAction = onSave;
            triggerDialogue = onTriggerDialogue ?? throw new ArgumentNullException(nameof(onTriggerDialogue));
            dialogueIsOpen = onDialogueIsOpen ?? throw new ArgumentNullException(nameof(onDialogueIsOpen));
            Content = neo.Assets.Worlds.OldConsoleLanding.Content;

            var blockedGroup = neo.Assets.Worlds.OldConsoleLanding.Children
                .First(check => check.Name == "Blocked Path");
            if (!blockedGroup.TryWritable(out BlockedPath blocked))
            {
                throw new InvalidCastException("Old Console Landing 'Blocked Path' must resolve as BlockedPath.");
            }
            blockedPath = blocked;
            barrierWasActive = IsBarrierActive();

            ui.MoveRequested += OnMoveRequested;
            ui.InteractRequested += OnInteractRequested;
            ui.CloseRequested += RequestClose;

            Open();
        }

        public bool IsOpen => ui.IsOpen;
        public ReadOnlyOldConsoleLandingGridContent Content { get; }
        public Vector2Int PlayerCell { get; private set; }
        public Bounds WorldBounds { get; private set; } =
            new Bounds(Vector3.zero, new Vector3(10f, 7f, 1f));
        public string PromptText { get; private set; } = "WASD Move  •  E Interact";
        public string StatusText { get; private set; } = string.Empty;
        public bool DialogueIsOpen => dialogueIsOpen.Invoke();

        private bool GlyphAttuned =>
            neo.Dialogues.HasVisited(LandingDialogueIds.BootGlyphAttuned);

        private bool RewardClaimed =>
            neo.Dialogues.HasVisited(LandingDialogueIds.BootGlyphSealReady);

        private bool CacheClaimed =>
            neo.Dialogues.HasVisited(LandingDialogueIds.RecoveryCache);

        public void Open()
        {
            barrierOpening = false;
            SetStatus(string.Empty);
            UpdatePrompt();
            ui.Show();
            StartLoadWorld();
        }

        public void Close()
        {
            loadGeneration++;
            CancelLoad();
            DisposeContentSubscriptions();
            loading = false;
            ui.Hide();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            loadGeneration++;
            CancelLoad();
            DisposeContentSubscriptions();
            ui.MoveRequested -= OnMoveRequested;
            ui.InteractRequested -= OnInteractRequested;
            ui.CloseRequested -= RequestClose;
            ui.Dispose();
        }

        public void Tick()
        {
            if (!IsOpen || loading) return;

            RenderChrome();
            ui.SetPromptVisible(!DialogueIsOpen);
            if (DialogueIsOpen) return;

            ui.Tick();
        }

        private void OnMoveRequested(Vector2Int delta)
        {
            bool moved = TryMove(delta);
            if (moved) ui.MovePlayerTo(PlayerCell);
            RenderChrome();
        }

        private void OnInteractRequested()
        {
            Interact();
            RenderChrome();
        }

        private void RequestClose()
        {
            closeRequested.Invoke();
        }

        private void StartLoadWorld()
        {
            CancelLoad();
            int generation = ++loadGeneration;
            loadCancellation = new CancellationTokenSource();
            loading = true;
            ui.SetPromptVisible(false);
            ui.RenderChrome(string.Empty, string.Empty);
            ui.SetLoadingVisible(true, "Loading tile grid...");
            LoadWorldAsync(generation, loadCancellation.Token);
        }

        private async void LoadWorldAsync(int generation, CancellationToken token)
        {
            try
            {
                await YieldNextFrameAsync(token);
                if (!IsCurrentLoad(generation)) return;

                await ui.RenderGridAsync(Content, token);
                if (!IsCurrentLoad(generation)) return;

                BuildWorldBounds();
                SubscribeContentChanges();
                CompleteWorldLoad();
                ui.MovePlayerTo(PlayerCell);
                ui.Frame(WorldBounds);
                RenderChrome();
                ui.SetPromptVisible(true);
            }
            catch (Exception exception)
            {
                if (exception is OperationCanceledException) return;
                Debug.LogError(exception);
                if (IsCurrentLoad(generation))
                {
                    ui.RenderChrome(PromptText, "The landing grid failed to load. Check the console.");
                }
            }
            finally
            {
                if (IsCurrentLoad(generation))
                {
                    loading = false;
                    ui.SetLoadingVisible(false);
                }
            }
        }

        private void SubscribeContentChanges()
        {
            DisposeContentSubscriptions();
            collisionSubscription = Content.Collisions.OnChanged(_ =>
            {
                UpdatePrompt();
                RenderChrome();
            });
            barrierWasActive = IsBarrierActive();
            barrierSubscription = blockedPath.Tiles.OnChanged((_, __) =>
            {
                bool isActive = IsBarrierActive();
                if (barrierWasActive && !isActive)
                {
                    PersistBarrierOpenAsync();
                }
                barrierWasActive = isActive;
                UpdatePrompt();
                RenderChrome();
            });
        }

        private void DisposeContentSubscriptions()
        {
            collisionSubscription?.Dispose();
            collisionSubscription = null;
            barrierSubscription?.Dispose();
            barrierSubscription = null;
        }

        private bool IsCurrentLoad(int generation) =>
            IsOpen && generation == loadGeneration && !disposed;

        private static async Awaitable YieldNextFrameAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Application.isPlaying)
            {
                await Awaitable.NextFrameAsync(token);
            }
        }

        private void CancelLoad()
        {
            if (loadCancellation == null) return;
            loadCancellation.Cancel();
            loadCancellation.Dispose();
            loadCancellation = null;
        }

        private void RenderChrome()
        {
            ui.RenderChrome(PromptText, StatusText);
        }

        private void CompleteWorldLoad()
        {
            PlayerCell = FindPlayerSpawnCell();
            UpdatePrompt();
            SetStatus("WASD moves. E talks to whatever the old console is whispering through.");
        }

        private bool TryMove(Vector2Int delta)
        {
            var target = PlayerCell + delta;
            if (!CanEnter(target))
            {
                SetStatus("Hull plating. It does not negotiate.");
                return false;
            }

            PlayerCell = target;
            UpdatePrompt();
            return true;
        }

        private void Interact()
        {
            if (TryFindNearbyBarrier(out _))
            {
                if (!GlyphAttuned)
                {
                    TriggerLandingDialogue(
                        LandingDialogueIds.BootGlyphSealLocked,
                        () => SetStatus("The seal wants a boot trace. Find the glowing glyph in the south chamber and press E beside it."));
                    return;
                }

                TriggerLandingDialogue(LandingDialogueIds.BootGlyphSealReady, UpdatePrompt);
                return;
            }

            if (!GlyphAttuned && TryFindNearbyBootGlyph(out _))
            {
                TriggerLandingDialogue(
                    LandingDialogueIds.BootGlyphAttuned,
                    () =>
                    {
                        SetStatus("Boot trace captured. Take it to the vault seal — or let the ship's console relay it.");
                    });
                return;
            }

            if (TryFindNearbyObject<RecoveryCacheObject>(out _))
            {
                if (CacheClaimed)
                {
                    SetStatus("The cache is empty. The receipt, however, is eternal.");
                    return;
                }

                TriggerLandingDialogue(
                    LandingDialogueIds.RecoveryCache,
                    () => ClaimCacheRewardAsync());
                return;
            }

            if (TryFindNearbyObject<ExitPromptObject>(out _))
            {
                var barrierClosed = IsBarrierActive();
                TriggerLandingDialogue(
                    barrierClosed && GlyphAttuned
                        ? LandingDialogueIds.ExitPromptRelay
                        : LandingDialogueIds.ExitPromptQuiet,
                    UpdatePrompt);
                return;
            }

            if (TryFindNearbyObject<VaultPlaqueObject>(out _))
            {
                if (IsBarrierActive() && GlyphAttuned)
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
                            ? "Find the boot glyph and release the seal before claiming the cache."
                            : "The vault stands open. The save has the receipt.");
                    });
                return;
            }

            SetStatus("Nothing answers here. Try the boot glyph, the vault seal, or your ship's console.");
        }

        private async void PersistBarrierOpenAsync()
        {
            if (barrierOpening) return;
            barrierOpening = true;

            try
            {
                SetStatus("Seal released. The vault corridor stands open.");

                if (saveAction != null)
                {
                    await saveAction();
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(exception);
                SetStatus("The seal opened locally, but save failed. Check the console.");
            }
            finally
            {
                barrierOpening = false;
            }
        }

        private async void ClaimCacheRewardAsync()
        {
            try
            {
                neo.Save.Bits += CacheRewardBits;
                SetStatus($"+{CacheRewardBits} bits recovered from the cache.");
                UpdatePrompt();

                if (saveAction != null)
                {
                    await saveAction();
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(exception);
                SetStatus("The cache opened locally, but save failed. Check the console.");
            }
        }

        private void BuildWorldBounds()
        {
            var bounds = new CellBoundsAccumulator();

            foreach (var tile in Content.Background.GetTiles())
            {
                bounds.Include(tile.Cell);
            }

            foreach (var tile in Content.Collisions.GetTiles())
            {
                bounds.Include(tile.Cell);
            }

            foreach (var obj in Content.Objects.GetObjects())
            {
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

            WorldBounds = bounds.ToBounds();
        }

        private bool IsBlockedPathCollision(NeoResolvedTileInstance tile) =>
            tile.SourceKind == NeoTileOutputSourceKind.TileLayerLink &&
            !string.IsNullOrEmpty(tile.SourceTileLayerLinkId) &&
            string.Equals(tile.SourceTileLayerLinkId, blockedPath.valueId, StringComparison.Ordinal);

        private bool CanEnter(Vector2Int cell)
        {
            return Content.Background.GetTile(cell) != null &&
                Content.Collisions.GetTile(cell) == null;
        }

        private bool IsBarrierActive() =>
            blockedPath.Tiles.Count > 0;

        private bool TryFindNearbyBarrier(out Vector2Int cell)
        {
            foreach (var candidate in NearbyCells(PlayerCell))
            {
                var tile = Content.Collisions.GetTile(candidate);
                if (tile == null || !IsBlockedPathCollision(tile)) continue;
                cell = candidate;
                return true;
            }

            cell = default;
            return false;
        }

        private bool TryFindNearbyBootGlyph(out Vector2Int cell)
        {
            foreach (var candidate in NearbyCells(PlayerCell))
            {
                var tile = Content.Background.GetTile(candidate);
                if (tile == null || !tile.TryGetTile<BootGlyphTile>(out _)) continue;
                cell = candidate;
                return true;
            }

            cell = default;
            return false;
        }

        private bool TryFindNearbyObject<T>(out NeoResolvedObjectInstance instance)
            where T : NeoGeneratedCustomValue
        {
            foreach (var cell in NearbyCells(PlayerCell))
            {
                foreach (var obj in Content.Objects.GetObjects(cell))
                {
                    if (!obj.TryGetObject<T>(out _)) continue;
                    instance = obj;
                    return true;
                }
            }

            instance = default;
            return false;
        }

        private static IEnumerable<Vector2Int> NearbyCells(Vector2Int cell)
        {
            yield return cell;
            yield return cell + Vector2Int.up;
            yield return cell + Vector2Int.down;
            yield return cell + Vector2Int.left;
            yield return cell + Vector2Int.right;
        }

        private Vector2Int FindPlayerSpawnCell()
        {
            foreach (var obj in Content.Objects.GetObjects())
            {
                if (obj.TryGetObject<PlayerSpawnObject>(out _)) return obj.Cell;
            }

            return new Vector2Int(-7, 2);
        }

        private void UpdatePrompt()
        {
            if (TryFindNearbyBarrier(out _))
            {
                PromptText = GlyphAttuned
                    ? "E Release vault seal"
                    : "E Inspect vault seal";
                return;
            }

            if (TryFindNearbyBootGlyph(out _) && !GlyphAttuned)
            {
                PromptText = "E Read boot glyph";
                return;
            }

            if (TryFindNearbyObject<ExitPromptObject>(out _))
            {
                PromptText = IsBarrierActive()
                    ? "E Ask launch console"
                    : "E Review launch console";
                return;
            }

            if (TryFindNearbyObject<RecoveryCacheObject>(out _))
            {
                PromptText = CacheClaimed ? "E Cache (claimed)" : "E Open recovery cache";
                return;
            }

            if (TryFindNearbyObject<VaultPlaqueObject>(out _))
            {
                PromptText = RewardClaimed ? "E Read plaque again" : "E Inspect reward plaque";
                return;
            }

            PromptText = "WASD Move  •  E Interact";
        }

        private void SetStatus(string message)
        {
            StatusText = message ?? string.Empty;
        }

        private void TriggerLandingDialogue(string dialogueId, Action onFinish)
        {
            if (triggerDialogue(dialogueId, onFinish))
            {
                return;
            }

            Debug.LogWarning($"Neo Compose: landing dialogue '{dialogueId}' could not be triggered.");
            SetStatus("That Neo dialogue flow is missing from the sample export.");
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
            public const string RecoveryCache = "cb0ac79c-f3b4-4c96-b968-8c4173c1f712";
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
