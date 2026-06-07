// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime;
using UnityEngine;

namespace HelloWorld.Assets.Scripts
{
    /// <summary>
    /// The save-file menu: owns the <see cref="NeoProjectStore"/> and authentication,
    /// renders the browse / create / archive / clone UI (<see cref="MenuUI"/>) plus an
    /// optional cloud sign-in cell, and hands the selected save's
    /// <see cref="NeoSaveSynchronizer"/> to a freshly-spawned
    /// <see cref="HelloWorldGameplay"/>.
    /// </summary>
    /// <remarks>
    /// Transitions are object-lifetime clean: entering gameplay spawns a child
    /// GameObject, leaving it destroys that GameObject (which disposes the client in
    /// <see cref="HelloWorldGameplay.OnDestroy"/>). The menu never touches the client
    /// directly. The synchronizer's conflict / migration / clone events are wired to
    /// menu dialogs, since resolving them is a menu (UI) concern.
    /// </remarks>
    public sealed class HelloWorldMenu : MonoBehaviour
    {
        private MenuUI menu;
        private NeoProjectStore store;
        private HelloWorldGameplay gameplay;
        private bool authBusy;
        private string pendingUserCode = "";
        private string pendingVerificationUri = "";

        internal void Awake()
        {
            ShowMenu();
        }

        internal void OnDestroy()
        {
            menu?.Dispose();
            menu = null;
        }

        /// <summary>
        /// Brings up (or returns to) the save-file menu. Fire-and-forget: loading the
        /// list can hit the cloud (a web request), so it must run on the Unity loop —
        /// never block the main thread on it.
        /// </summary>
        public void ShowMenu()
        {
            Run(ShowMenuAsync());
        }

        private async Awaitable ShowMenuAsync()
        {
            menu ??= new MenuUI();
            menu.SetMenuVisible(true);
            RenderMenu(loading: true);

            if (store == null)
            {
                // Everything (schema, save store, cloud per `enableOAuthCloudSync`) is
                // inferred from the project's NeoComposeConfig in Resources.
                store = new NeoProjectStore();
                await store.LoadAsync();
            }
            else
            {
                await store.RefreshSavesAsync();
            }

            if (this == null) return;
            RenderMenu(loading: false);
        }

        /// <summary>
        /// Fire-and-forget a UI-triggered task on the Unity loop without blocking the
        /// main thread (so cloud network calls can progress), logging any failure.
        /// </summary>
        private static async void Run(Awaitable task)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                // Expected when the developer stops Play mode mid-request — not an error.
            }
            catch (Exception exception)
            {
                Debug.LogError(exception);
            }
        }

        private void RenderMenu(bool loading)
        {
            IReadOnlyList<NeoSaveListEntry> activeSaves = store == null
                ? Array.Empty<NeoSaveListEntry>()
                : store.Saves.Where(save => !save.IsArchived).ToList();
            menu.Render(
                loading,
                activeSaves,
                BuildAuthInfo(),
                onCreateNew: OnCreateNew,
                onContinue: OnContinue,
                onClone: OnClone,
                onDelete: OnDelete,
                onSignIn: OnSignIn);
        }

        private MenuAuthInfo BuildAuthInfo()
        {
            var auth = store?.Authentication;
            string identity = auth == null
                ? ""
                : auth.DisplayName.Length > 0 ? auth.DisplayName : auth.DisplayEmail;
            return new MenuAuthInfo(
                cloudEnabled: auth != null,
                signedIn: auth?.IsSignedIn ?? false,
                busy: authBusy,
                identity: identity,
                userCode: pendingUserCode,
                verificationUri: pendingVerificationUri);
        }

        // ── Save selection / creation ─────────────────────────────────────────

        private void OnContinue(string customId) => Run(EnterGameplayAsync(store.Open(customId)));

        private void OnCreateNew() => Run(EnterGameplayAsync(store.CreateNew()));

        /// <summary>
        /// "Delete" always removes the local save file. Confirmation copy is the
        /// sample's responsibility (the SDK won't prompt): signed-in players can
        /// recover from the cloud, signed-out players are warned the data is gone.
        /// </summary>
        private void OnDelete(string customId)
        {
            bool signedIn = store?.Authentication?.IsSignedIn == true;
            string subtitle = signedIn
                ? "Your save file will be recoverable at app.neocompose.com"
                : "You will not be able to recover your data unless you log in prior to deleting your account.";
            menu.ShowPrompt(
                "Are you sure you want to do this?",
                subtitle,
                new (string, Action)[]
                {
                    ("Delete", () => Run(DeleteAsync(customId))),
                    ("Cancel", () => { }),
                });
        }

        private async Awaitable DeleteAsync(string customId)
        {
            // ArchiveAsync deletes the local file and (when signed in) archives the
            // cloud copy so it stays recoverable from the web app.
            await store.ArchiveAsync(customId);
            if (this == null) return;
            RenderMenu(loading: false);
        }

        private void OnClone(string customId) => Run(CloneAsync(customId));

        private async Awaitable CloneAsync(string customId)
        {
            await store.CloneAsync(customId);
            await store.RefreshSavesAsync();
            if (this == null) return;
            RenderMenu(loading: false);
        }

        // ── Authentication ────────────────────────────────────────────────────

        private async void OnSignIn()
        {
            var auth = store?.Authentication;
            if (auth == null || authBusy) return;

            authBusy = true;
            auth.OnDeviceAuthorizationPrompt += OnDevicePrompt;
            RenderMenu(loading: false);
            try
            {
                await auth.SignInAsync();
            }
            catch (OperationCanceledException)
            {
                // Expected when the developer stops Play mode mid sign-in — not an error.
            }
            catch (Exception exception)
            {
                Debug.LogError(exception);
            }
            finally
            {
                auth.OnDeviceAuthorizationPrompt -= OnDevicePrompt;
                authBusy = false;
                pendingUserCode = "";
                pendingVerificationUri = "";
                if (this != null)
                {
                    if (store.Authentication?.IsSignedIn == true)
                    {
                        await store.RefreshSavesAsync();
                    }
                    if (this != null) RenderMenu(loading: false);
                }
            }
        }

        private void OnDevicePrompt(NeoComposeDeviceCodeResponse code)
        {
            if (this == null) return;
            pendingUserCode = code.userCode;
            pendingVerificationUri = code.verificationUri;
            RenderMenu(loading: false);
        }

        // ── Gameplay transitions ──────────────────────────────────────────────

        private async Awaitable EnterGameplayAsync(NeoSaveSynchronizer synchronizer)
        {
            menu.SetMenuVisible(false);
            // Cover the transition with a loading overlay — loading a cloud save can
            // hit the network, and any lifecycle prompt renders above it.
            menu.ShowLoadingOverlay("Loading…");
            WireLifecyclePrompts(synchronizer);

            var go = new GameObject("HelloWorld Gameplay");
            go.transform.SetParent(transform, worldPositionStays: false);
            gameplay = go.AddComponent<HelloWorldGameplay>();
            gameplay.OnExitToMenu += ReturnToMenu;
            try
            {
                await gameplay.EnterAsync(synchronizer);
            }
            finally
            {
                if (this != null) menu.HideLoadingOverlay();
            }
        }

        private void ReturnToMenu()
        {
            if (gameplay != null)
            {
                gameplay.OnExitToMenu -= ReturnToMenu;
                DestroyGameObject(gameplay.gameObject);
                gameplay = null;
            }

            ShowMenu();
        }

        /// <summary>
        /// Wires the synchronizer's lifecycle events to menu dialogs — resolving a
        /// conflict / migration / cross-channel clone is a menu (UI) concern, so the
        /// gameplay screen stays focused on the client.
        /// </summary>
        private void WireLifecyclePrompts(NeoSaveSynchronizer synchronizer)
        {
            synchronizer.OnConflict += (conflict, continuation) =>
                menu.ShowPrompt(
                    "Cloud save conflict",
                    $"\"{conflict.Local.name}\" changed both locally and in the cloud. " +
                    "Keeping local writes a new cloud snapshot, so neither copy is lost.",
                    new (string, Action)[]
                    {
                        ("Keep local", () => continuation.KeepLocal()),
                        ("Keep cloud", () => continuation.KeepRemote()),
                    });

            synchronizer.OnMigrationRequired += (migration, continuation) =>
                menu.ShowPrompt(
                    "Save needs migration",
                    $"\"{migration.CustomId}\" can't be read by this version of the game. " +
                    "Skip loading it for now — it stays in your list.",
                    new (string, Action)[]
                    {
                        ("Skip", () => continuation.Skip()),
                    });

            synchronizer.OnSelectedSaveRequiringClone += (request, continuation) =>
                menu.ShowPrompt(
                    "Clone to this channel",
                    "This cloud save belongs to a different release channel. " +
                    "Clone it here to continue?",
                    new (string, Action)[]
                    {
                        ("Clone & play", () => continuation.Approve()),
                        ("Cancel", () => continuation.Deny()),
                    });
        }

        private static void DestroyGameObject(GameObject target)
        {
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
