// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// The base of every generated user client (<c>MyGameUserNeo</c>), beside
    /// <see cref="NeoProjectClient"/> (P104 §4.1). The current
    /// <see cref="NeoProjectStore"/> owns the client underneath, so this has
    /// no <c>Dispose</c>.
    /// </summary>
    public abstract class NeoUserClient
    {
        /// <param name="client">The store's user client.</param>
        /// <param name="readOnlyFactories">The generated read-only view factories, by class id.</param>
        /// <param name="writableFactories">The generated writable view factories, by class id.</param>
        protected NeoUserClient(
            NeoClient client,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory> readOnlyFactories,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableClassFactory> writableFactories)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
            client.RegisterGeneratedClassFactories(readOnlyFactories, writableFactories);
        }

        public NeoClient Client
        {
            get;
        }

        /// <inheritdoc cref="NeoClient.CommitAsync"/>
        public Awaitable CommitAsync(bool replaceSnapshot = false, bool forceCapture = false) =>
            Client.CommitAsync(replaceSnapshot, forceCapture);

        /// <inheritdoc cref="NeoClient.RunTransaction"/>
        public void RunTransaction(Action transaction) => Client.RunTransaction(transaction);

        /// <summary>The current store's generated user client, built by <paramref name="create"/> on first use.</summary>
        protected static T RequireLoadedClient<T>(Func<NeoClient, T> create)
            where T : NeoUserClient
        {
            var store = NeoProjectStore.Current;
            if (store?.LoadedUserClient == null)
            {
                throw new InvalidOperationException(
                    "The user file hasn't loaded. Load a `NeoProjectStore`, or await " +
                    $"`{typeof(T).Name}.WhenLoadedAsync()`.");
            }
            return store.UserClientView(create);
        }

        /// <summary>
        /// Completes once a current store has loaded the user file. Only
        /// <paramref name="cancellationToken"/> ends the wait.
        /// </summary>
        protected static async Awaitable<T> WhenClientLoadedAsync<T>(
            Func<NeoClient, T> create,
            CancellationToken cancellationToken)
            where T : NeoUserClient
        {
            await NeoProjectStore.WhenUserClientLoadedAsync(cancellationToken);
            return RequireLoadedClient(create);
        }
    }
}
