// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NUnit.Framework;

namespace HelloWorld.Assets.Tests
{
    /// <summary>
    /// Round-trips the default file-backed <c>INeoLocalSaveStore</c>: write, list,
    /// read, and delete saves by <c>customId</c> over <c>save-{customId}.json</c> files.
    /// </summary>
    public class NeoFileLocalSaveStoreTests
    {
        private string directory;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "neo-folder-store-" + Path.GetRandomFileName());
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }

        [Test]
        public void CommitListLoadDelete_RoundTripsByCustomId()
        {
            var store = new NeoFileLocalSaveStore(directory);

            Assert.IsEmpty(store.ListSaveIdsAsync().GetAwaiter().GetResult());
            Assert.IsNull(store.LoadSaveAsync("alpha").GetAwaiter().GetResult(), "Absent save reads as null.");

            store.CommitSaveAsync("alpha", "{\"name\":\"alpha\"}").GetAwaiter().GetResult();
            store.CommitSaveAsync("beta", "{\"name\":\"beta\"}").GetAwaiter().GetResult();

            CollectionAssert.AreEquivalent(
                new[] { "alpha", "beta" },
                store.ListSaveIdsAsync().GetAwaiter().GetResult().ToArray());
            Assert.AreEqual(
                "{\"name\":\"alpha\"}",
                store.LoadSaveAsync("alpha").GetAwaiter().GetResult());

            // Overwrite in place.
            store.CommitSaveAsync("alpha", "{\"name\":\"alpha-2\"}").GetAwaiter().GetResult();
            Assert.AreEqual(
                "{\"name\":\"alpha-2\"}",
                store.LoadSaveAsync("alpha").GetAwaiter().GetResult());

            store.DeleteSaveAsync("alpha").GetAwaiter().GetResult();
            CollectionAssert.AreEquivalent(
                new[] { "beta" },
                store.ListSaveIdsAsync().GetAwaiter().GetResult().ToArray());
            Assert.IsNull(store.LoadSaveAsync("alpha").GetAwaiter().GetResult());
        }

        [Test]
        public void Files_AreNamedBySaveCustomIdConvention()
        {
            var store = new NeoFileLocalSaveStore(directory);
            store.CommitSaveAsync("hero-42", "{}").GetAwaiter().GetResult();

            Assert.IsTrue(
                File.Exists(Path.Combine(directory, "save-hero-42.json")),
                "Each save is persisted as save-{customId}.json.");
        }
    }
}
