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
        bool TryTriggerDialogue(NeoDialogueReference dialogueReference, Action onFinish);
    }

    /// <summary>
    /// Gameplay for the Old Console Landing sample. It owns generated Neo values,
    /// save-backed mutations, dialogue triggers, collision state, and prompts;
    /// <see cref="LandingSceneUI"/> owns Unity presentation. Spawn it with
    /// <see cref="Open"/>; destroy its GameObject to close the scene.
    /// </summary>
    public sealed class LandingSceneGameplay : MonoBehaviour
    {
        private const int CacheRewardBits = 25;

        /// <summary>The player's interaction reach: their own cell plus the four neighbors.</summary>
        private static readonly NeoCellPattern WithinReachPattern = NeoCellPattern.Cross(1);

        private readonly LandingSceneUI ui = new();
        private HelloWorldNeo neo;
        private ILandingSceneHost host;
        private BlockedPath blockedPath;
        private IDisposable collisionSubscription;
        private bool barrierWasActive;
        private bool loading;
        // Dialogue ids resolved once at load for the HasVisited flags, whose
        // owning tile/object isn't necessarily within the player's reach.
        private string bootGlyphAttunedId = "";
        private string recoveryCacheDialogueId = "";
        // Resolved once: each Grid.Content access resolves a fresh content
        // instance with its own primitive, and change events only flow through
        // the instance the renderer live-syncs — so render, queries, and
        // subscriptions must share this one.
        private IReadOnlyOldConsoleLandingGrid Grid;
        private ReadOnlyOldConsoleLandingGridContent content;

        public static LandingSceneGameplay Open(HelloWorldNeo neo, ILandingSceneHost host)
        {
            var gameplay = new GameObject("Old Console Landing Gameplay")
                .AddComponent<LandingSceneGameplay>();
            gameplay.Initialize(neo, host);
            return gameplay;
        }

        private void Initialize(HelloWorldNeo neo, ILandingSceneHost host)
        {
            this.neo = neo;
            this.host = host;
            Grid = neo.Assets.Worlds.OldConsoleLanding;
            content = Grid.Content;
            blockedPath = Grid.GetRequiredChild<BlockedPath>();
            barrierWasActive = IsBarrierActive();

            ui.MoveRequested += OnMoveRequested;
            ui.InteractRequested += OnInteractRequested;
            ui.CloseRequested += host.CloseLandingScene;

            UpdatePrompt();
            ui.Show();
            LoadWorldAsync();
        }

        public Vector2Int PlayerCell { get; private set; }
        public string PromptText { get; private set; } = "WASD Move  •  E Interact";
        public string StatusText { get; private set; } = string.Empty;

        private bool GlyphAttuned =>
            bootGlyphAttunedId.Length > 0 &&
            neo.Dialogues.HasVisited(bootGlyphAttunedId);

        // The seal dialogues live on the barrier link (`blockedPath`), which is
        // already resolved — read the reference off it directly.
        private bool RewardClaimed =>
            neo.Dialogues.HasVisited(blockedPath.BootGlyphSealReady.Id);

        private bool CacheClaimed =>
            recoveryCacheDialogueId.Length > 0 &&
            neo.Dialogues.HasVisited(recoveryCacheDialogueId);

        private void Update()
        {
            if (loading) return;

            RenderChrome();
            ui.SetPromptVisible(!host.DialogueIsOpen);
            if (host.DialogueIsOpen) return;

            ui.Tick();
        }

        private void OnDestroy()
        {
            collisionSubscription?.Dispose();
            // Destroying the UI tears down the renderer, which cancels any
            // still-running RenderGridAsync for us.
            ui.Dispose();
        }

        private void OnMoveRequested(Vector2Int delta)
        {
            if (TryMove(delta)) ui.MovePlayerTo(PlayerCell);
            RenderChrome();
        }

        private void OnInteractRequested()
        {
            Interact();
            RenderChrome();
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
                // Torn down mid-load; whoever cancelled us owns the UI now.
                return;
            }

            loading = false;
            ui.SetLoadingVisible(false);
            // Barrier tiles project onto the Collisions layer, so this single
            // subscription hears both direct edits and blocked-path changes —
            // the args say which source caused each one.
            collisionSubscription = content.Collisions.OnChanged(OnCollisionsChanged);
            PlayerCell = content.Objects.GetObjects<PlayerSpawnObject>().First().Cell;
            // Resolve the flag dialogues once (their owners aren't under the
            // player): the boot-glyph tile and the recovery cache carry their
            // DialogueLookup references as generated NeoDialogueReferences.
            bootGlyphAttunedId =
                content.Background.GetTiles<BootGlyphTile>().FirstOrDefault()?.Info
                    ?.BootGlyphAttuned.Id ?? "";
            recoveryCacheDialogueId =
                content.Objects.GetObjects<RecoveryCacheObject>().FirstOrDefault()?.Info
                    ?.RecoveryCache.Id ?? "";
            UpdatePrompt();
            StatusText = "WASD moves. E talks to whatever the old console is whispering through.";
            ui.MovePlayerTo(PlayerCell);
            var cellBounds = content.ComputeCellBounds();
            ui.Frame(new Bounds(cellBounds.center, cellBounds.size));
            RenderChrome();
            ui.SetPromptVisible(true);
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

        private void RenderChrome()
        {
            ui.RenderChrome(PromptText, StatusText);
        }

        private bool TryMove(Vector2Int delta)
        {
            var target = PlayerCell + delta;
            if (!CanEnter(target))
            {
                StatusText = "Hull plating. It does not negotiate.";
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
                        blockedPath.BootGlyphSealLocked,
                        () => StatusText = "The seal wants a boot trace. Find the glowing glyph in the south chamber and press E beside it."
                    );
                    return;
                }

                TriggerLandingDialogue(blockedPath.BootGlyphSealReady, UpdatePrompt);
                return;
            }

            if (!GlyphAttuned && NearBootGlyph() is BootGlyphTile glyph)
            {
                TriggerLandingDialogue(
                    glyph.BootGlyphAttuned,
                    () => StatusText = "Boot trace captured. Take it to the vault seal — or let the ship's console relay it."
                );
                return;
            }

            if (NearObject<RecoveryCacheObject>() is RecoveryCacheObject cache)
            {
                if (CacheClaimed)
                {
                    StatusText = "The cache is empty. The receipt, however, is eternal.";
                    return;
                }

                TriggerLandingDialogue(cache.RecoveryCache, () => ClaimCacheRewardAsync());
                return;
            }

            if (NearObject<ExitPromptObject>() is ExitPromptObject exitPrompt)
            {
                var barrierClosed = IsBarrierActive();
                TriggerLandingDialogue(
                    barrierClosed && GlyphAttuned
                        ? exitPrompt.ExitPromptRelay
                        : exitPrompt.ExitPromptQuiet,
                    UpdatePrompt
                );
                return;
            }

            if (NearObject<VaultPlaqueObject>() is VaultPlaqueObject plaque)
            {
                if (IsBarrierActive() && GlyphAttuned)
                {
                    TriggerLandingDialogue(blockedPath.BootGlyphSealReady, UpdatePrompt);
                    return;
                }

                TriggerLandingDialogue(
                    IsBarrierActive()
                        ? plaque.VaultPlaqueLocked
                        : plaque.VaultPlaqueReward,
                    () =>
                    {
                        StatusText = IsBarrierActive()
                            ? "Find the boot glyph and release the seal before claiming the cache."
                            : "The vault stands open. The save has the receipt.";
                    }
                );
                return;
            }

            StatusText = "Nothing answers here. Try the boot glyph, the vault seal, or your ship's console.";
        }

        private async void PersistBarrierOpenAsync()
        {
            try
            {
                StatusText = "Seal released. The vault corridor stands open.";
                await host.SaveProgressAsync();
            }
            catch (Exception exception)
            {
                Debug.LogError(exception);
                StatusText = "The seal opened locally, but save failed. Check the console.";
            }
        }

        private async void ClaimCacheRewardAsync()
        {
            try
            {
                neo.Save.Bits += CacheRewardBits;
                StatusText = $"+{CacheRewardBits} bits recovered from the cache.";
                UpdatePrompt();
                await host.SaveProgressAsync();
            }
            catch (Exception exception)
            {
                Debug.LogError(exception);
                StatusText = "The cache opened locally, but save failed. Check the console.";
            }
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

        private BootGlyphTile NearBootGlyph() =>
            content.Background.GetTile<BootGlyphTile>(PlayerCell, WithinReachPattern)?.Info;

        private T NearObject<T>()
            where T : class, INeoValueReference
        {
            return content.Objects.GetObject<T>(PlayerCell, WithinReachPattern)?.Info;
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

            if (NearBootGlyph() is not null && !GlyphAttuned)
            {
                PromptText = "E Read boot glyph";
                return;
            }

            if (NearObject<ExitPromptObject>() is not null)
            {
                PromptText = IsBarrierActive()
                    ? "E Ask launch console"
                    : "E Review launch console";
                return;
            }

            if (NearObject<RecoveryCacheObject>() is not null)
            {
                PromptText = CacheClaimed ? "E Cache (claimed)" : "E Open recovery cache";
                return;
            }

            if (NearObject<VaultPlaqueObject>() is not null)
            {
                PromptText = RewardClaimed ? "E Read plaque again" : "E Inspect reward plaque";
                return;
            }

            PromptText = "WASD Move  •  E Interact";
        }

        // Takes the resolved DialogueLookup reference off an interactable and
        // triggers it by id through the host. The dialogue ids are no longer
        // hardcoded here — they live on the objects/tiles as DialogueLookup
        // attributes and arrive as generated NeoDialogueReferences.
        private void TriggerLandingDialogue(NeoDialogueReference reference, Action onFinish)
        {
            if (host.TryTriggerDialogue(reference, onFinish))
            {
                return;
            }

            Debug.LogWarning($"Neo Compose: landing dialogue '{reference.Id}' could not be triggered.");
            StatusText = "That Neo dialogue flow is missing from the sample export.";
        }
    }
}
