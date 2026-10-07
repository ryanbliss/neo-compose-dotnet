// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P104 §4: the current store loads the player's user file, its user
    /// client owns the User layer, and save clients read that layer in place.
    /// </summary>
    public partial class NeoUserFileTests
    {
        private const string Channel = NeoSaveTestSupport.TargetChannel;
        private const string VolumeId = "v-volume";
        private const string ScoreId = "v-score";

        // Assets, Save, Session and User roots: Save holds Score (10), User holds Volume (5).
        private static readonly string Corpus = new JObject
        {
            ["metadata"] = new JObject
            {
                ["schemaVersion"] = NeoProjectExportContract.CurrentSchemaVersion,
                ["projectId"] = "project-1",
                ["versionId"] = "v1",
                ["semver"] = new JObject { ["label"] = "1.0" },
            },
            ["project"] = new JObject
            {
                ["id"] = "project-1",
                ["name"] = "User File Tests",
                ["rootAssetsMemberId"] = "root-assets",
                ["rootSaveFileMemberId"] = "root-save",
                ["rootSessionMemberId"] = "root-session",
                ["rootUserMemberId"] = "root-user",
                ["createdAt"] = 0,
                ["updatedAt"] = 0,
            },
            ["members"] = new JObject
            {
                ["root-assets"] = RootMember("root-assets", "Assets", "class-assets", "v-root-assets", null),
                ["root-save"] = RootMember("root-save", "Save", "class-save", "v-root-save", NeoMemberStorage.Save),
                ["root-session"] = RootMember("root-session", "Session", "class-session", "v-root-session", NeoMemberStorage.Session),
                ["root-user"] = RootMember("root-user", "User", "class-user", "v-root-user", NeoMemberStorage.User),
                ["member-score"] = IntMember("member-score", "Score"),
                ["member-volume"] = IntMember("member-volume", "Volume"),
            },
            ["classes"] = new JObject
            {
                ["class-assets"] = Class("class-assets", "Assets", new JObject()),
                ["class-save"] = Class("class-save", "Save", new JObject { ["Score"] = "member-score" }),
                ["class-session"] = Class("class-session", "Session", new JObject()),
                ["class-user"] = Class("class-user", "User", new JObject { ["Volume"] = "member-volume" }),
            },
            ["enums"] = new JObject(),
            ["interfaces"] = new JObject(),
            ["constructors"] = new JObject(),
            ["variants"] = new JObject(),
            ["variantFolders"] = new JObject(),
            ["internalRecordRelations"] = new JObject(),
            ["values"] = new JObject
            {
                ["v-root-assets"] = ObjectRow("v-root-assets", "class-assets", new JObject()),
                ["v-root-save"] = ObjectRow("v-root-save", "class-save", new JObject { ["Score"] = ScoreId }),
                ["v-root-session"] = ObjectRow("v-root-session", "class-session", new JObject()),
                ["v-root-user"] = ObjectRow("v-root-user", "class-user", new JObject { ["Volume"] = VolumeId }),
                [ScoreId] = new JObject { ["id"] = ScoreId, ["value"] = 10, ["createdAt"] = 0, ["updatedAt"] = 0 },
                [VolumeId] = new JObject { ["id"] = VolumeId, ["value"] = 5, ["createdAt"] = 0, ["updatedAt"] = 0 },
            },
        }.ToString(Newtonsoft.Json.Formatting.None);

        private readonly List<NeoProjectStore> stores = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var store in stores)
                store.Dispose();
            stores.Clear();
        }

        [Test]
        public async Task Load_WithoutCloud_LoadsDefaultsAndBecomesCurrent()
        {
            var store = await LoadStoreAsync(new NeoInMemoryLocalSaveStore());

            Assert.That(NeoProjectStore.Current, Is.SameAs(store));
            Assert.That(store.LoadedUserClient, Is.Not.Null);
            Assert.That(Volume(store.LoadedUserClient!), Is.EqualTo(5));
            Assert.That(store.User.Kind, Is.EqualTo(NeoSaveFileKind.User));
        }

        [Test]
        public async Task SecondAndToolingStores_LoadNoUserFile()
        {
            var first = await LoadStoreAsync(new NeoInMemoryLocalSaveStore());
            var second = await LoadStoreAsync(new NeoInMemoryLocalSaveStore());
            var tooling = await LoadStoreAsync(new NeoInMemoryLocalSaveStore(), loadUserFile: false);

            Assert.That(NeoProjectStore.Current, Is.SameAs(first));
            Assert.That(second.LoadedUserClient, Is.Null);
            Assert.That(tooling.LoadedUserClient, Is.Null);
            Assert.Throws<InvalidOperationException>(() => _ = tooling.User);
        }

        [Test]
        public async Task UserWrite_ReachesAttachedSaveClientAsExternalChange()
        {
            var store = await LoadStoreAsync(new NeoInMemoryLocalSaveStore());
            var save = await LoadSaveAsync(store);
            var heard = new List<(string id, NeoChangeSource source)>();
            save.OnWritableValueChanged += (_, id) => heard.Add((id, save.CurrentChangeSource));

            SetVolume(store.LoadedUserClient!, 7);

            Assert.That(Volume(save), Is.EqualTo(7));
            Assert.That(heard, Is.EqualTo(new[] { (VolumeId, NeoChangeSource.External) }));
        }

        [Test]
        public async Task SaveClient_CannotWriteUserData()
        {
            var store = await LoadStoreAsync(new NeoInMemoryLocalSaveStore());
            var save = await LoadSaveAsync(store);

            var error = Assert.Throws<InvalidOperationException>(() => SetVolume(save, 7));

            Assert.That(error!.Message, Is.EqualTo(NeoClient.UserDataWriteError));
            Assert.That(Volume(save), Is.EqualTo(5));
            Assert.That(Volume(store.LoadedUserClient!), Is.EqualTo(5));
        }

        [Test]
        public async Task Load_Throws_WithoutCurrentStore_OrFromAnotherStore()
        {
            var current = await LoadStoreAsync(new NeoInMemoryLocalSaveStore());
            var other = await LoadStoreAsync(new NeoInMemoryLocalSaveStore());

            var fromOther = await CatchAsync(() => LoadSaveAsync(other));
            Assert.That(fromOther?.Message, Does.Contain("isn't `NeoProjectStore.Current`"));

            NeoProjectStore.ResetCurrent();
            var noCurrent = await CatchAsync(() => LoadSaveAsync(current));
            Assert.That(noCurrent?.Message, Does.Contain("Load a `NeoProjectStore` before loading a save"));
        }

        [Test]
        public async Task ToolingStoreSave_ReadsAuthoredUserDefaultsDetached()
        {
            var store = await LoadStoreAsync(new NeoInMemoryLocalSaveStore(), loadUserFile: false);

            var save = await LoadSaveAsync(store);

            Assert.That(save.AttachedUserClient, Is.Null);
            Assert.That(Volume(save), Is.EqualTo(5));
        }

        [Test]
        public async Task Dispose_ClearsCurrent_AndSaveClientKeepsLastUserValues()
        {
            var store = await LoadStoreAsync(new NeoInMemoryLocalSaveStore());
            var save = await LoadSaveAsync(store);
            SetVolume(store.LoadedUserClient!, 7);

            store.Dispose();

            Assert.That(NeoProjectStore.Current, Is.Null);
            Assert.That(store.LoadedUserClient, Is.Null);
            Assert.That(Volume(save), Is.EqualTo(7));
        }

        [Test]
        public async Task UserCommit_PersistsUnderUserKey_SeparatelyFromSaves()
        {
            var local = new NeoInMemoryLocalSaveStore();
            var store = await LoadStoreAsync(local);
            var save = await LoadSaveAsync(store);
            SetVolume(store.LoadedUserClient!, 7);
            save.SetWritableValue(NeoValueOwnership.Save, new NumberMemberValue { id = ScoreId, value = 11 });

            await store.LoadedUserClient!.CommitAsync();

            Assert.That(await local.ListSaveIdsAsync(), Is.Empty);
            Assert.That(await local.LoadUserAsync(NeoSaveSynchronizer.UserKey), Does.Contain(VolumeId));

            await save.CommitAsync();

            Assert.That(await local.ListSaveIdsAsync(), Is.EqualTo(new[] { "save-1" }));
            Assert.That(await local.LoadSaveAsync("save-1"), Does.Not.Contain(VolumeId));

            store.Dispose();
            var reloaded = await LoadStoreAsync(local);
            Assert.That(Volume(reloaded.LoadedUserClient!), Is.EqualTo(7));
        }

        [Test]
        public async Task UnparseableLocalCopy_IsSetAside_AndDefaultsLoad()
        {
            var local = new NeoInMemoryLocalSaveStore();
            await local.CommitUserAsync(NeoSaveSynchronizer.UserKey, "{not json");
            LogAssert.Expect(LogType.Warning, new Regex("could not be parsed"));

            var store = await LoadStoreAsync(local);

            Assert.That(Volume(store.LoadedUserClient!), Is.EqualTo(5));
            Assert.That(await local.LoadUserAsync(NeoSaveSynchronizer.UnreadableUserKey), Is.EqualTo("{not json"));
        }

        [Test]
        public async Task CloudOff_AnotherChannelsCopy_LoadsDefaultsUnderAFreshIdentity()
        {
            var local = new NeoInMemoryLocalSaveStore();
            await local.CommitUserAsync(
                NeoSaveSynchronizer.UserKey,
                UserFileJson(9, channel: "channel-other", serverId: "server-other"));

            var store = await LoadStoreAsync(local);
            await store.LoadedUserClient!.CommitAsync();

            Assert.That(Volume(store.LoadedUserClient!), Is.EqualTo(5));
            Assert.That(store.User.CustomId, Is.Not.EqualTo("user-1"));
            var written = JObject.Parse((await local.LoadUserAsync(NeoSaveSynchronizer.UserKey))!);
            Assert.That(written.Value<string>("releaseChannelId"), Is.EqualTo(Channel));
            Assert.That(written.Value<string>("serverId"), Is.Null.Or.Empty);
        }

        [Test]
        public async Task CloudOn_AnotherChannelsCopy_LoadsTheCloudFileWithoutConflict()
        {
            var local = new NeoInMemoryLocalSaveStore();
            await local.CommitUserAsync(
                NeoSaveSynchronizer.UserKey,
                UserFileJson(9, channel: "channel-other", serverId: "server-other"));
            var api = new FakeApiClient { userFile = RemoteUserFile(3) };

            var store = await LoadStoreAsync(local, api, failOnConflict: true);

            Assert.That(Volume(store.LoadedUserClient!), Is.EqualTo(3));
            Assert.That(store.User.CustomId, Is.EqualTo("user-cloud"));
        }

        [Test]
        public async Task NotFound_DropsASyncedCopy_AndKeepsANeverUploadedOne()
        {
            var synced = new NeoInMemoryLocalSaveStore();
            await synced.CommitUserAsync(NeoSaveSynchronizer.UserKey, UserFileJson(9, serverId: "server-user"));
            var droppedStore = await LoadStoreAsync(synced, new FakeApiClient());

            Assert.That(Volume(droppedStore.LoadedUserClient!), Is.EqualTo(5));
            var dropped = JObject.Parse((await synced.LoadUserAsync(NeoSaveSynchronizer.UserKey))!);
            Assert.That(dropped.Value<string>("serverId"), Is.Null.Or.Empty);
            Assert.That(dropped.Value<string>("customId"), Is.EqualTo(droppedStore.User.CustomId));

            droppedStore.Dispose();
            NeoProjectStore.ResetCurrent();
            var localOnly = new NeoInMemoryLocalSaveStore();
            await localOnly.CommitUserAsync(NeoSaveSynchronizer.UserKey, UserFileJson(9));
            var keptStore = await LoadStoreAsync(localOnly, new FakeApiClient());

            Assert.That(Volume(keptStore.LoadedUserClient!), Is.EqualTo(9));
        }

        [Test]
        public async Task AnotherServerIdInTheCloud_ReplacesTheLocalCopyWithoutConflict()
        {
            var local = new NeoInMemoryLocalSaveStore();
            await local.CommitUserAsync(
                NeoSaveSynchronizer.UserKey,
                UserFileJson(9, serverId: "server-previous-account", updatedAt: 999));
            var api = new FakeApiClient { userFile = RemoteUserFile(3) };

            var store = await LoadStoreAsync(local, api, failOnConflict: true);

            Assert.That(Volume(store.LoadedUserClient!), Is.EqualTo(3));
            var written = JObject.Parse((await local.LoadUserAsync(NeoSaveSynchronizer.UserKey))!);
            Assert.That(written.Value<string>("serverId"), Is.EqualTo("server-user-cloud"));
        }

        [TestCase(200, 3)]
        [TestCase(50, 9)]
        public async Task ConflictWithoutHandler_KeepsTheNewerHead(long remoteUpdatedAt, int expected)
        {
            var local = new NeoInMemoryLocalSaveStore();
            await local.CommitUserAsync(
                NeoSaveSynchronizer.UserKey,
                UserFileJson(9, serverId: "server-user-cloud", customId: "user-cloud", snapshotId: "snap-old", updatedAt: 100));
            var remote = RemoteUserFile(3);
            remote.snapshotId = "snap-new";
            remote.snapshotRevision = 2;
            remote.updatedAt = remoteUpdatedAt;
            var api = new FakeApiClient { userFile = remote };

            var store = await LoadStoreAsync(local, api);

            Assert.That(Volume(store.LoadedUserClient!), Is.EqualTo(expected));
            Assert.That(store.User.CustomId, Is.EqualTo("user-cloud"));
        }

        [Test]
        public async Task SignIn_AppliesTheAccountsFile_AsAnExternalChange()
        {
            var api = new FakeApiClient { userFileThrows = new NeoComposeNotSignedInException("Signed out.") };
            var store = await LoadStoreAsync(new NeoInMemoryLocalSaveStore(), api);
            var save = await LoadSaveAsync(store);
            var user = store.LoadedUserClient!;
            SetVolume(user, 7);
            var heard = new List<NeoChangeSource>();
            save.OnWritableValueChanged += (_, id) =>
            {
                if (id == VolumeId)
                    heard.Add(save.CurrentChangeSource);
            };
            api.userFileThrows = null;
            api.userFile = RemoteUserFile(3);

            await store.User.ReconcileSignInAsync();

            Assert.That(store.LoadedUserClient, Is.SameAs(user));
            Assert.That(Volume(user), Is.EqualTo(3));
            Assert.That(Volume(save), Is.EqualTo(3));
            Assert.That(heard, Is.EqualTo(new[] { NeoChangeSource.External }));
            Assert.That(store.User.CustomId, Is.EqualTo("user-cloud"));

            // The next commit diffs against the account's head, not the replaced file.
            api.commitResults.Enqueue(NeoCommitResult.Committed(RemoteUserFile(4)));
            SetVolume(user, 4);
            await user.CommitAsync();

            Assert.That(api.commits, Is.Empty);
            Assert.That(api.sparseCommits, Has.Count.EqualTo(1));
            Assert.That(api.sparseCommits[0].customId, Is.EqualTo("user-cloud"));
        }

        [Test]
        public async Task SignIn_WithNoCloudFile_KeepsANeverUploadedCopyAndUploadsIt()
        {
            var api = new FakeApiClient { userFileThrows = new NeoComposeNotSignedInException("Signed out.") };
            var local = new NeoInMemoryLocalSaveStore();
            var store = await LoadStoreAsync(local, api);
            var user = store.LoadedUserClient!;
            SetVolume(user, 7);
            api.commitThrows.Enqueue(new NeoComposeNotSignedInException("Signed out."));
            await user.CommitAsync();
            SetVolume(user, 8);
            api.userFileThrows = null;

            await store.User.ReconcileSignInAsync();

            Assert.That(Volume(user), Is.EqualTo(8));
            api.commitResults.Enqueue(NeoCommitResult.Committed(RemoteUserFile(8)));
            await user.CommitAsync();
            Assert.That(api.commits, Has.Count.EqualTo(2));
            Assert.That(api.commits[1].request.kind, Is.EqualTo(NeoSaveSynchronizer.UserFileKind));
            Assert.That(Newtonsoft.Json.JsonConvert.SerializeObject(api.commits[1].request.values), Does.Contain("8"));
        }

        [Test]
        public async Task SignIn_KeepsANewerNeverUploadedCopy_OverTheAccountsOlderFile()
        {
            var api = new FakeApiClient { userFileThrows = new NeoComposeNotSignedInException("Signed out.") };
            var store = await LoadStoreAsync(new NeoInMemoryLocalSaveStore(), api);
            var user = store.LoadedUserClient!;
            SetVolume(user, 7);
            api.commitThrows.Enqueue(new NeoComposeNotSignedInException("Signed out."));
            await user.CommitAsync();
            api.userFileThrows = null;
            api.userFile = RemoteUserFile(3);

            await store.User.ReconcileSignInAsync();

            Assert.That(Volume(user), Is.EqualTo(7));
            Assert.That(store.User.CustomId, Is.EqualTo("user-cloud"));
        }

        [Test]
        public async Task CreateConflict_AdoptsTheExistingFile()
        {
            var api = new FakeApiClient();
            var store = await LoadStoreAsync(new NeoInMemoryLocalSaveStore(), api);
            var user = store.LoadedUserClient!;
            var existing = RemoteUserFile(3);
            api.commitThrows.Enqueue(new NeoUserFileExistsException(existing));
            api.getResult = existing;
            api.commitResults.Enqueue(NeoCommitResult.Committed(RemoteUserFile(7)));
            SetVolume(user, 7);

            await user.CommitAsync();

            Assert.That(store.User.CustomId, Is.EqualTo("user-cloud"));
            Assert.That(api.sparseCommits, Has.Count.EqualTo(1));
            Assert.That(api.sparseCommits[0].customId, Is.EqualTo("user-cloud"));
        }

        [Test]
        public async Task CommitToADeletedFile_LoadsDefaultsAsAnExternalChange()
        {
            var local = new NeoInMemoryLocalSaveStore();
            var api = new FakeApiClient { userFile = RemoteUserFile(9) };
            var store = await LoadStoreAsync(local, api);
            var save = await LoadSaveAsync(store);
            var user = store.LoadedUserClient!;
            var heard = new List<NeoChangeSource>();
            save.OnWritableValueChanged += (_, id) =>
            {
                if (id == VolumeId)
                    heard.Add(save.CurrentChangeSource);
            };
            api.commitThrows.Enqueue(new NeoComposeNotFoundException("Gone."));
            api.getThrows = new NeoComposeNotFoundException("Gone.");
            SetVolume(user, 8);
            heard.Clear();

            await user.CommitAsync();
            await WaitUntilAsync(() => Volume(user) == 5);

            Assert.That(Volume(save), Is.EqualTo(5));
            Assert.That(heard, Does.Contain(NeoChangeSource.External));
            Assert.That(store.User.CustomId, Is.Not.EqualTo("user-cloud"));
            var written = JObject.Parse((await local.LoadUserAsync(NeoSaveSynchronizer.UserKey))!);
            Assert.That(written.Value<string>("serverId"), Is.Null.Or.Empty);
            Assert.That(written.Value<string>("customId"), Is.EqualTo(store.User.CustomId));
        }

        [Test]
        public async Task UserClient_ThrowsBeforeLoad_AndWhenLoadedAsyncCompletesOnLoad()
        {
            var error = Assert.Throws<InvalidOperationException>(() => _ = TestUserNeo.Instance);
            Assert.That(error!.Message, Does.Contain("await `TestUserNeo.WhenLoadedAsync()`"));
            var waiting = TestUserNeo.WhenLoadedAsync();

            var store = await LoadStoreAsync(new NeoInMemoryLocalSaveStore());
            var loaded = await waiting;

            Assert.That(loaded, Is.SameAs(TestUserNeo.Instance));
            Assert.That(loaded.Client, Is.SameAs(store.LoadedUserClient));

            store.Dispose();
            NeoProjectStore.ResetCurrent();
            var next = await LoadStoreAsync(new NeoInMemoryLocalSaveStore());
            Assert.That(TestUserNeo.Instance, Is.Not.SameAs(loaded));
            Assert.That(TestUserNeo.Instance.Client, Is.SameAs(next.LoadedUserClient));
        }

        [Test]
        public async Task UserTransaction_ReachesTheSaveClientOnceAtItsEnd()
        {
            var store = await LoadStoreAsync(new NeoInMemoryLocalSaveStore());
            var save = await LoadSaveAsync(store);
            var heard = new List<double?>();
            save.OnWritableValueChanged += (_, id) => heard.Add(Volume(save));

            TestUserNeo.Instance.RunTransaction(() =>
            {
                SetVolume(store.LoadedUserClient!, 6);
                SetVolume(store.LoadedUserClient!, 7);
                Assert.That(heard, Is.Empty);
            });

            Assert.That(heard, Is.EqualTo(new double?[] { 7 }));
        }

        [Test]
        public async Task UserFile_RejectsArchiveAndClone()
        {
            var store = await LoadStoreAsync(new NeoInMemoryLocalSaveStore(), new FakeApiClient());

            var archive = await CatchAwaitableAsync(() => store.ArchiveAsync(store.User.CustomId));
            var archiveDirect = await CatchAwaitableAsync(() => store.User.ArchiveAsync());

            Assert.That(archive?.Message, Is.EqualTo("The user file can't be archived."));
            Assert.That(archiveDirect?.Message, Is.EqualTo("The user file can't be archived."));
        }

        private sealed class TestUserNeo : NeoUserClient
        {
            private TestUserNeo(NeoClient client)
                : base(
                    client,
                    new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>(),
                    new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>())
            {
            }

            public static TestUserNeo Instance => RequireLoadedClient(client => new TestUserNeo(client));

            public static async Task<TestUserNeo> WhenLoadedAsync() =>
                await WhenClientLoadedAsync(client => new TestUserNeo(client), default);
        }

        private async Task<NeoProjectStore> LoadStoreAsync(
            INeoLocalSaveStore local,
            FakeApiClient? api = null,
            bool loadUserFile = true,
            bool failOnConflict = false,
            string? corpus = null,
            NeoAuthentication? authentication = null)
        {
            var store = new NeoProjectStore(
                dataSource: NeoTestExport.Source(corpus ?? Corpus),
                localStore: local,
                apiClient: api,
                targetReleaseChannelId: Channel,
                options: new NeoSaveOptions { LiveSessionsEnabled = false },
                authentication: authentication,
                loadUserFile: loadUserFile);
            stores.Add(store);
            if (failOnConflict && loadUserFile)
                store.User.OnConflict += (_, _) => Assert.Fail("The user file raised a conflict.");
            await store.LoadAsync();
            return store;
        }

        private static async Task<NeoClient> LoadSaveAsync(NeoProjectStore store) =>
            await new NeoLoader().Load(store.Open("save-1"));

        private static async Task<Exception?> CatchAsync(Func<Task> action)
        {
            try
            {
                await action();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static Task<Exception?> CatchAwaitableAsync(Func<Awaitable> action) =>
            CatchAsync(async () => await action());

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                    Assert.Fail("Timed out waiting for the user file.");
                await Task.Delay(10);
            }
        }

        private static double? Volume(NeoClient client) =>
            client.TryGetValue<NumberMemberValue>(VolumeId, out var row) ? row.value : null;

        private static void SetVolume(NeoClient client, double value) =>
            client.SetWritableValue(NeoValueOwnership.User, new NumberMemberValue { id = VolumeId, value = value });

        private static RemoteGameSave RemoteUserFile(double volume)
        {
            var remote = NeoSaveTestSupport.Remote("user-cloud", "snap-user");
            remote.name = NeoSaveSynchronizer.UserFileName;
            remote.values = NeoSaveValues.FromTypedValues(new Dictionary<string, MemberValue>
            {
                [VolumeId] = new NumberMemberValue { id = VolumeId, value = volume },
            });
            return remote;
        }

        private static string UserFileJson(
            double volume,
            string channel = Channel,
            string? serverId = null,
            string customId = "user-1",
            string snapshotId = "snap-local",
            long updatedAt = 2) =>
            new JObject
            {
                ["customId"] = customId,
                ["releaseChannelId"] = channel,
                ["name"] = NeoSaveSynchronizer.UserFileName,
                ["projectId"] = "project-1",
                ["version"] = new JObject { ["id"] = "v1", ["label"] = "1.0" },
                ["serverId"] = serverId,
                ["snapshotId"] = serverId == null ? null : snapshotId,
                ["snapshotRevision"] = serverId == null ? 0 : 1,
                ["values"] = new JObject
                {
                    [VolumeId] = new JObject { ["id"] = VolumeId, ["value"] = volume },
                },
                ["createdAt"] = 1,
                ["updatedAt"] = updatedAt,
            }.ToString(Newtonsoft.Json.Formatting.None);

        private static JObject RootMember(string id, string name, string classId, string valueId, NeoMemberStorage? storage)
        {
            var member = new JObject
            {
                ["id"] = id,
                ["name"] = name,
                ["kind"] = (int)MemberKind.Class,
                ["projectId"] = "project-1",
                ["classId"] = classId,
                ["valueId"] = valueId,
                ["requirement"] = 1,
                ["createdAt"] = 0,
                ["updatedAt"] = 0,
            };
            if (storage is { } declared)
                member["storage"] = (int)declared;
            return member;
        }

        private static JObject IntMember(string id, string name) => new()
        {
            ["id"] = id,
            ["name"] = name,
            ["kind"] = (int)MemberKind.Int,
            ["projectId"] = "project-1",
            ["createdAt"] = 0,
            ["updatedAt"] = 0,
        };

        private static JObject Class(string id, string name, JObject schema) => new()
        {
            ["id"] = id,
            ["name"] = name,
            ["schema"] = schema,
            ["projectId"] = "project-1",
            ["createdAt"] = 0,
            ["updatedAt"] = 0,
        };

        private static JObject ObjectRow(string id, string classId, JObject value) => new()
        {
            ["id"] = id,
            ["classId"] = classId,
            ["value"] = value,
            ["createdAt"] = 0,
            ["updatedAt"] = 0,
        };
    }
}
