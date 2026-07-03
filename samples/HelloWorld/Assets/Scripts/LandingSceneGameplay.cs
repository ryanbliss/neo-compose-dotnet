// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Linq;
using HelloWorld.Assets.Scripts.Neo;
using NeoCompose.Runtime;
using UnityEngine;

namespace HelloWorld.Assets.Scripts
{
    /// <summary>
    /// What the landing scene needs from the screen that opened it: dialogue
    /// presentation, save plumbing, and a way to hand control back.
    /// </summary>
    public interface ILandingSceneHost
    {
        bool DialogueIsOpen { get; }
        void CloseLandingScene();
        Awaitable SaveProgressAsync();
        bool TryTriggerDialogue(string dialogueId, Action onFinish);
    }

    /// <summary>
    /// SDK-facing gameplay model for the Old Console Landing sample. It owns
    /// generated Neo values, save-backed mutations, dialogue triggers, collision
    /// state, and prompts; <see cref="LandingSceneUI"/> owns Unity presentation.
    /// </summary>
    public sealed class LandingSceneGameplay : IDisposable
    {
        private const int CacheRewardBits = 25;

        /// <summary>The player's interaction reach: their own cell plus the four neighbors.</summary>
        private static readonly NeoCellPattern WithinReachPattern = NeoCellPattern.Cross(1);

        private readonly HelloWorldNeo neo;
        private readonly LandingSceneUI ui = new();
        private readonly ILandingSceneHost host;
        private readonly BlockedPath blockedPath;
        private IDisposable collisionSubscription;
        private bool barrierOpening;
        private bool barrierWasActive;
        private bool loading;
        private bool disposed;
        private IReadOnlyOldConsoleLandingGrid Grid;
        // Resolved once: each Grid.Content access resolves a fresh content
        // instance with its own primitive, and change events only flow through
        // the instance the renderer live-syncs — so render, queries, and
        // subscriptions must share this one.
        private readonly ReadOnlyOldConsoleLandingGridContent content;

        public LandingSceneGameplay(HelloWorldNeo neo, ILandingSceneHost host)
        {
            this.neo = neo ?? throw new ArgumentNullException(nameof(neo));
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            Grid = neo.Assets.Worlds.OldConsoleLanding;
            content = Grid.Content;
            blockedPath = neo.Assets.Worlds.OldConsoleLanding.GetRequiredChild<BlockedPath>();
            barrierWasActive = IsBarrierActive();

            ui.MoveRequested += OnMoveRequested;
            ui.InteractRequested += OnInteractRequested;
            ui.CloseRequested += RequestClose;

            Open();
        }

        public bool IsOpen => ui.IsOpen;
        public Vector2Int PlayerCell { get; private set; }
        public Bounds WorldBounds { get; private set; } =
            new Bounds(Vector3.zero, new Vector3(10f, 7f, 1f));
        public string PromptText { get; private set; } = "WASD Move  •  E Interact";
        public string StatusText { get; private set; } = string.Empty;
        public bool DialogueIsOpen => host.DialogueIsOpen;

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
            LoadWorldAsync();
        }

        public void Close()
        {
            DisposeContentSubscriptions();
            loading = false;
            ui.Hide();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            DisposeContentSubscriptions();
            ui.MoveRequested -= OnMoveRequested;
            ui.InteractRequested -= OnInteractRequested;
            ui.CloseRequested -= RequestClose;
            // Destroying the UI tears down the renderer, which cancels any
            // still-running RenderGridAsync for us.
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
            host.CloseLandingScene();
        }

        private async void LoadWorldAsync()
        {
            loading = true;
            ui.SetPromptVisible(false);
            ui.RenderChrome(string.Empty, string.Empty);
            ui.SetLoadingVisible(true, "Loading tile grid...");

            try
            {
                await ui.RenderGridAsync(content);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer render or torn down mid-load; whoever
                // cancelled us owns the UI state now.
                return;
            }
            catch (Exception exception)
            {
                Debug.LogError(exception);
                if (disposed) return;
                loading = false;
                ui.SetLoadingVisible(false);
                ui.RenderChrome(PromptText, "The landing grid failed to load. Check the console.");
                return;
            }

            if (disposed || !IsOpen) return;

            loading = false;
            ui.SetLoadingVisible(false);
            WorldBounds = ComputeWorldBounds();
            SubscribeContentChanges();
            PlayerCell = FindPlayerSpawnCell();
            UpdatePrompt();
            SetStatus("WASD moves. E talks to whatever the old console is whispering through.");
            ui.MovePlayerTo(PlayerCell);
            ui.Frame(WorldBounds);
            RenderChrome();
            ui.SetPromptVisible(true);
        }

        private void SubscribeContentChanges()
        {
            DisposeContentSubscriptions();
            barrierWasActive = IsBarrierActive();
            // Barrier tiles project onto the Collisions layer, so this single
            // subscription hears both direct edits and blocked-path changes —
            // the args say which source caused each one.
            collisionSubscription = content.Collisions.OnChanged(OnCollisionsChanged);
        }

        private void OnCollisionsChanged(NeoTileLayerChangedArgs args)
        {
            bool barrierChanged =
                args.SourceKind == NeoTileGridChangeSourceKind.TileLayerLink &&
                string.Equals(args.SourceId, blockedPath.valueId, StringComparison.Ordinal);
            bool isActive = IsBarrierActive();
            if (barrierChanged && barrierWasActive && !isActive)
            {
                PersistBarrierOpenAsync();
            }
            barrierWasActive = isActive;
            UpdatePrompt();
            RenderChrome();
        }

        private void DisposeContentSubscriptions()
        {
            collisionSubscription?.Dispose();
            collisionSubscription = null;
        }

        private void RenderChrome()
        {
            ui.RenderChrome(PromptText, StatusText);
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
            if (NearBarrier())
            {
                if (!GlyphAttuned)
                {
                    TriggerLandingDialogue(
                        LandingDialogueIds.BootGlyphSealLocked,
                        () => SetStatus("The seal wants a boot trace. Find the glowing glyph in the south chamber and press E beside it.")
                    );
                    return;
                }

                TriggerLandingDialogue(LandingDialogueIds.BootGlyphSealReady, UpdatePrompt);
                return;
            }

            if (!GlyphAttuned && NearBootGlyph())
            {
                TriggerLandingDialogue(
                    LandingDialogueIds.BootGlyphAttuned,
                    () =>
                    {
                        SetStatus("Boot trace captured. Take it to the vault seal — or let the ship's console relay it.");
                    }
                );
                return;
            }

            if (NearObject<RecoveryCacheObject>())
            {
                if (CacheClaimed)
                {
                    SetStatus("The cache is empty. The receipt, however, is eternal.");
                    return;
                }

                TriggerLandingDialogue(
                    LandingDialogueIds.RecoveryCache,
                    () => ClaimCacheRewardAsync()
                );
                return;
            }

            if (NearObject<ExitPromptObject>())
            {
                var barrierClosed = IsBarrierActive();
                TriggerLandingDialogue(
                    barrierClosed && GlyphAttuned
                        ? LandingDialogueIds.ExitPromptRelay
                        : LandingDialogueIds.ExitPromptQuiet,
                    UpdatePrompt
                );
                return;
            }

            if (NearObject<VaultPlaqueObject>())
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
                    }
                );
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
                await host.SaveProgressAsync();
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
                await host.SaveProgressAsync();
            }
            catch (Exception exception)
            {
                Debug.LogError(exception);
                SetStatus("The cache opened locally, but save failed. Check the console.");
            }
        }

        private Bounds ComputeWorldBounds()
        {
            var cellBounds = content.ComputeCellBounds();
            if (cellBounds.size == Vector3Int.zero)
            {
                return new Bounds(Vector3.zero, new Vector3(10f, 7f, 1f));
            }

            return new Bounds(cellBounds.center, cellBounds.size);
        }

        private bool CanEnter(Vector2Int cell)
        {
            return content.Background.GetTile(cell) != null &&
                content.Collisions.GetTile(cell) == null;
        }

        private bool IsBarrierActive() =>
            blockedPath.Tiles.Count > 0;

        private bool NearBarrier() =>
            blockedPath.GetTile(PlayerCell, WithinReachPattern) is not null;

        private bool NearBootGlyph() =>
            content.Background.GetTile<BootGlyphTile>(PlayerCell, WithinReachPattern) is not null;

        private bool NearObject<T>()
            where T : class, INeoValueReference
        {
            return content.Objects.GetObject<T>(PlayerCell, WithinReachPattern) is not null;
        }

        private Vector2Int FindPlayerSpawnCell()
        {
            var spawn = content.Objects.GetObjects<PlayerSpawnObject>().FirstOrDefault();
            return spawn?.Cell ?? new Vector2Int(-7, 2);
        }

        private void UpdatePrompt()
        {
            if (NearBarrier())
            {
                PromptText = GlyphAttuned
                    ? "E Release vault seal"
                    : "E Inspect vault seal";
                return;
            }

            if (NearBootGlyph() && !GlyphAttuned)
            {
                PromptText = "E Read boot glyph";
                return;
            }

            if (NearObject<ExitPromptObject>())
            {
                PromptText = IsBarrierActive()
                    ? "E Ask launch console"
                    : "E Review launch console";
                return;
            }

            if (NearObject<RecoveryCacheObject>())
            {
                PromptText = CacheClaimed ? "E Cache (claimed)" : "E Open recovery cache";
                return;
            }

            if (NearObject<VaultPlaqueObject>())
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
            if (host.TryTriggerDialogue(dialogueId, onFinish))
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
    }
}
