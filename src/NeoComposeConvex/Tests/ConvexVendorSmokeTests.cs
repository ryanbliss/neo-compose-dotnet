// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Reactive.Linq;
using System.Text.Json;
using Convex.Client;
using Convex.Client.Features.Security.Authentication;
using NUnit.Framework;

namespace NeoCompose.Convex.Tests
{
    /// <summary>
    /// Phase-1 smoke coverage for the vendored Convex client: the vendor
    /// assembly compiles, its public surface is reachable from a consuming
    /// assembly, and the bundled dependency DLLs (System.Reactive,
    /// System.Text.Json) actually load and execute under Unity's runtime.
    /// </summary>
    public sealed class ConvexVendorSmokeTests
    {
        /// <summary>
        /// Pins the surface specs/convex-realtime-sync.md:194-197 marks as
        /// must-remain. Nothing else in this repository references these
        /// symbols, so a re-vendor trim would otherwise drop them unnoticed.
        /// </summary>
        [Test]
        public void VendoredMustRemainSurfaceIsPresent()
        {
            Assert.That(typeof(IConvexClient).IsInterface, Is.True);
            Assert.That(typeof(IConvexClient).IsAssignableFrom(typeof(ConvexClient)), Is.True);
            Assert.That(typeof(IConvexClientBuilder).IsInterface, Is.True);
            Assert.That(typeof(IConvexAuthentication).IsInterface, Is.True);
        }

        [Test]
        public void SystemReactiveExecutes()
        {
            var observed = Observable.Return(42).FirstAsync().Wait();
            Assert.That(observed, Is.EqualTo(42));
        }

        [Test]
        public void SystemTextJsonExecutes()
        {
            var json = JsonSerializer.Serialize(new SmokePayload { Value = 7 });
            var parsed = JsonSerializer.Deserialize<SmokePayload>(json);
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed!.Value, Is.EqualTo(7));
        }

        private sealed class SmokePayload
        {
            public int Value { get; set; }
        }
    }
}
