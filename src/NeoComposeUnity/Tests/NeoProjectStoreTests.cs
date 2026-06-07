// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Threading.Tasks;
using NeoCompose.Runtime;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class NeoProjectStoreTests
    {
        [Test]
        public async Task LoadAsync_GoesLoadingThenReady_AndGatesOpenUntilReady()
        {
            var source = new ControllableProjectDataSource();
            var store = new NeoProjectStore(
                dataSource: source,
                localStore: new NeoInMemoryLocalSaveStore(),
                targetReleaseChannelId: NeoSaveTestSupport.TargetChannel);

            Assert.That(store.State, Is.EqualTo(NeoProjectStoreState.Idle));
            Assert.Throws<System.InvalidOperationException>(() => store.Open("save-1"));

            var loadTask = store.LoadAsync();
            // The async source has not completed yet: the store is mid-load and
            // still rejects Open.
            Assert.That(store.State, Is.EqualTo(NeoProjectStoreState.Loading));
            Assert.Throws<System.InvalidOperationException>(() => store.Open("save-1"));

            source.Complete(NeoSaveTestSupport.ProjectJson);
            await loadTask;

            Assert.That(store.State, Is.EqualTo(NeoProjectStoreState.Ready));
            Assert.That(store.Schema, Is.Not.Null);
            Assert.DoesNotThrow(() => store.Open("save-1"));
        }

        [Test]
        public async Task CreateNew_IsLocalOnlyUntilCommit_ThenListsTheSave()
        {
            var local = new NeoInMemoryLocalSaveStore();
            var store = new NeoProjectStore(
                dataSource: new NeoJsonProjectDataSource(NeoSaveTestSupport.ProjectJson),
                localStore: local,
                targetReleaseChannelId: NeoSaveTestSupport.TargetChannel);
            await store.LoadAsync();

            var listChanges = 0;
            store.OnListChanged += () => listChanges++;

            var sync = store.CreateNew("save-1", "My Save");

            // Ready immediately, nothing persisted, nothing listed.
            Assert.That(sync.State, Is.EqualTo(NeoSaveSynchronizerState.Ready));
            Assert.That(store.Saves, Is.Empty);
            Assert.That(await local.LoadSaveAsync("save-1"), Is.Null);
            Assert.That(await sync.LoadSaveContentAsync(), Is.Null, "A new draft has nothing to load.");

            await sync.CommitSaveContentAsync(NeoSaveTestSupport.SaveContent("My Save"), replaceSnapshot: false);

            // First commit persists locally and surfaces in the list.
            Assert.That(await local.LoadSaveAsync("save-1"), Is.Not.Null);
            Assert.That(store.Saves, Has.Count.EqualTo(1));
            Assert.That(store.Saves[0].customId, Is.EqualTo("save-1"));
            Assert.That(store.Saves[0].isLocalOnly, Is.True);
            Assert.That(listChanges, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public async Task Commit_ThroughSynchronizer_KeepsListInSync()
        {
            var local = new NeoInMemoryLocalSaveStore();
            await local.CommitSaveAsync("save-1", NeoSaveTestSupport.SaveContent("Original"));
            var store = new NeoProjectStore(
                dataSource: new NeoJsonProjectDataSource(NeoSaveTestSupport.ProjectJson),
                localStore: local,
                targetReleaseChannelId: NeoSaveTestSupport.TargetChannel);
            await store.LoadAsync();

            Assert.That(store.Saves[0].name, Is.EqualTo("Original"));

            var sync = store.Open("save-1");
            await sync.CommitSaveContentAsync(NeoSaveTestSupport.SaveContent("Renamed"), replaceSnapshot: false);

            // The list the project store exposes reflects the active-file commit.
            Assert.That(store.Saves, Has.Count.EqualTo(1));
            Assert.That(store.Saves[0].name, Is.EqualTo("Renamed"));
        }

        [Test]
        public void Open_BeforeLoad_Throws()
        {
            var store = new NeoProjectStore(
                dataSource: new NeoJsonProjectDataSource(NeoSaveTestSupport.ProjectJson),
                localStore: new NeoInMemoryLocalSaveStore());

            Assert.Throws<System.InvalidOperationException>(() => store.Open("save-1"));
            Assert.Throws<System.InvalidOperationException>(() => store.CreateNew());
        }
    }
}
