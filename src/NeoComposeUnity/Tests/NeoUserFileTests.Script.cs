// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using JsonMember = NeoCompose.Runtime.Json.Member;
using Pointer = NeoCompose.Runtime.Json.Pointer;
using TypeInfo = NeoCompose.Runtime.Json.TypeInfo;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P104 §12 runtime cases over NeoScript, delegates, listeners, and
    /// effects. The script corpus extends the base one:
    /// <code>
    /// class Item { int Level; }
    /// class User { int Volume; action OnChange(); Func&lt;int&gt; Reader = Read; Func&lt;int&gt; Handler;
    ///   List&lt;Item&gt; Items; int Runs; int Mirror;
    ///   void Raise() { this.Volume = this.Volume + 1; }  int Read() => this.Volume;
    ///   void Fire() { this.OnChange(); }
    ///   void StoreClosure() { this.Handler = (41 captured) => capture + 1; }
    ///   @effect void Track() { this.Runs = this.Runs + 1; this.Mirror = this.Volume; }
    ///   native void UserHeard(int? next); }
    /// class Save { int Score; Func&lt;int&gt; Callback; void Ping(); void RaiseUser() { root.User.Raise(); }
    ///   void FireUser() { root.User.Fire(); }  void StoreCapture() { root.Save.Callback = (root.User captured) => 1; }
    ///   void StoreSessionCapture() { root.Session.Callback = (root.User captured) => 1; }
    ///   native void SaveHeard(int? next); }
    /// class Session { Func&lt;int&gt; Callback; Item? Pick (lookup over root.User.Items); }
    /// </code>
    /// </summary>
    public partial class NeoUserFileTests
    {
        private static readonly Lazy<string> ScriptCorpusSource = new(BuildScriptCorpus);

        private static string ScriptCorpus => ScriptCorpusSource.Value;

        [Test]
        public async Task UserAction_TakesOnlyUserListeners()
        {
            var store = await LoadStoreAsync(new NeoInMemoryLocalSaveStore(), corpus: ScriptCorpus);
            var save = await LoadSaveAsync(store);
            var user = store.LoadedUserClient!;
            var onChange = UserRoot(user).Get<NeoMemberActionWritable>("OnChange").Bind();
            var saveObject = new SaveRootView(save, SaveRoot(save));
            var userObject = new UserRootView(user, UserRoot(user));

            var error = Assert.Throws<InvalidOperationException>(() => onChange.AddListener(saveObject.Ping));
            onChange.AddListener(userObject.Raise);

            Assert.That(error!.Message, Is.EqualTo(NeoClient.UserDelegateProvenanceError));
            Assert.That(onChange.Listeners.Select(listener => listener.memberId), Is.EqualTo(new[] { "member-raise" }));
        }

        [Test]
        public async Task UserDelegate_TakesOnlyADelegateTheUserClientRead()
        {
            var store = await LoadStoreAsync(new NeoInMemoryLocalSaveStore(), corpus: ScriptCorpus);
            var save = await LoadSaveAsync(store);
            var user = store.LoadedUserClient!;
            var handler = UserRoot(user).Get<NeoMemberDelegateWritable>("Handler");
            var readBySave = new NeoMemberClass(save, "root-user", "v-root-user", NeoValueOwnership.User)
                .Get<NeoMemberDelegate>("Reader").Bind<long>(value => Convert.ToInt64(value));
            var readByUser = UserRoot(user).Get<NeoMemberDelegate>("Reader").Bind<long>(value => Convert.ToInt64(value));

            var error = Assert.Throws<InvalidOperationException>(() => handler.Set(readBySave));
            handler.Set(readByUser);

            Assert.That(error!.Message, Is.EqualTo(NeoClient.UserDelegateProvenanceError));
            Assert.That(Convert.ToInt64(UserRoot(user).Get<NeoMemberDelegate>("Handler").Invoke()), Is.EqualTo(5));
        }

        [Test]
        public async Task SaveDelegate_RejectsAUserTargetOrCapture_AndSessionAcceptsIt()
        {
            var store = await LoadStoreAsync(new NeoInMemoryLocalSaveStore(), corpus: ScriptCorpus);
            var save = await LoadSaveAsync(store);
            var reader = new NeoMemberClass(save, "root-user", "v-root-user", NeoValueOwnership.User)
                .Get<NeoMemberDelegate>("Reader").Bind<long>(value => Convert.ToInt64(value));

            var target = Assert.Throws<InvalidOperationException>(
                () => SaveRoot(save).Get<NeoMemberDelegateWritable>("Callback").Set(reader));
            var capture = Assert.Catch(() => InvokeSave(save, "StoreCapture"));
            InvokeSave(save, "StoreSessionCapture");

            Assert.That(target!.Message, Is.EqualTo(NeoClient.SaveHoldsUserReferenceError));
            Assert.That(capture!.Message, Does.Contain(NeoClient.SaveHoldsUserReferenceError));
            Assert.That(SaveRoot(save).Get<NeoMemberDelegate>("Callback").value?.value, Is.Null);
            var session = new NeoMemberClassWritable(save, "root-session", "v-root-session", NeoValueOwnership.Session);
            Assert.That(Convert.ToInt64(session.Get<NeoMemberDelegate>("Callback").Invoke()), Is.EqualTo(1));
        }

        [TestCase("RaiseUser")]
        [TestCase("FireUser")]
        public async Task UserFunctionThatWrites_FailsFromSaveCode_AndKeepsUserValues(string function)
        {
            var store = await LoadStoreAsync(new NeoInMemoryLocalSaveStore(), corpus: ScriptCorpus);
            var save = await LoadSaveAsync(store);
            var user = store.LoadedUserClient!;
            var onChange = UserRoot(user).Get<NeoMemberActionWritable>("OnChange");
            onChange.AddListener(new NeoDelegateValue { memberId = "member-raise" });

            var error = Assert.Catch(() => InvokeSave(save, function));

            Assert.That(error!.Message, Does.Contain(NeoClient.UserDataWriteError));
            Assert.That(Volume(save), Is.EqualTo(5));
            Assert.That(Volume(user), Is.EqualTo(5));
        }

        [Test]
        public async Task UserClosure_PersistsAndRunsAfterReload()
        {
            var local = new NeoInMemoryLocalSaveStore();
            var store = await LoadStoreAsync(local, corpus: ScriptCorpus);
            InvokeUser(store.LoadedUserClient!, "StoreClosure");
            await store.LoadedUserClient!.CommitAsync();
            store.Dispose();

            var reloaded = await LoadStoreAsync(local, corpus: ScriptCorpus);

            var handler = UserRoot(reloaded.LoadedUserClient!).Get<NeoMemberDelegate>("Handler");
            Assert.That(handler.value?.value?.IsClosure, Is.True);
            Assert.That(Convert.ToInt64(handler.Invoke()), Is.EqualTo(42));
        }

        [Test]
        public async Task UserToUserListener_PersistsInTheUserFile()
        {
            var local = new NeoInMemoryLocalSaveStore();
            var store = await LoadStoreAsync(local, corpus: ScriptCorpus);
            store.LoadedUserClient!.EditMemberChangeListener(
                "v-root-user", NeoValueOwnership.User, "member-volume", OptionalIntType(),
                new NeoDelegateValue { memberId = "member-user-heard" }, add: true);
            await store.LoadedUserClient!.CommitAsync();
            store.Dispose();

            var reloaded = await LoadStoreAsync(local, corpus: ScriptCorpus);
            var heard = Hear(reloaded.LoadedUserClient!, "member-user-heard");
            SetVolume(reloaded.LoadedUserClient!, 9);

            Assert.That(heard, Is.EqualTo(new[] { 9.0 }));
        }

        [Test]
        public async Task SaveToUserListener_IsSessionTier()
        {
            var local = new NeoInMemoryLocalSaveStore();
            var store = await LoadStoreAsync(local, corpus: ScriptCorpus);
            var save = await LoadSaveAsync(store);
            var user = store.LoadedUserClient!;
            var heard = Hear(save, "member-save-heard");
            save.EditMemberChangeListener(
                "v-root-user", NeoValueOwnership.User, "member-volume", OptionalIntType(),
                new NeoDelegateValue { memberId = "member-save-heard", valueId = "v-root-save" }, add: true);

            SetVolume(user, 8);
            save.SetWritableValue(NeoValueOwnership.Save, new NumberMemberValue { id = ScoreId, value = 11 });
            await save.CommitAsync();
            save.Dispose();
            var reloaded = await LoadSaveAsync(store);
            var heardAfterReload = Hear(reloaded, "member-save-heard");
            SetVolume(user, 9);

            Assert.That(heard, Is.EqualTo(new[] { 8.0 }));
            Assert.That(await local.LoadSaveAsync("save-1"), Does.Not.Contain("member-save-heard"));
            Assert.That(heardAfterReload, Is.Empty);
        }

        [Test]
        public async Task UserEffect_RunsOncePerChange_OnlyInTheUserClient()
        {
            var store = await LoadStoreAsync(new NeoInMemoryLocalSaveStore(), corpus: ScriptCorpus);
            var user = store.LoadedUserClient!;
            var save = await LoadSaveAsync(store);
            // The generated wrappers start each client's script runtime.
            user.StartScriptRuntime();
            save.StartScriptRuntime();
            double runs = Number(user, "v-runs")!.Value;

            SetVolume(user, 7);

            Assert.That(Number(user, "v-runs"), Is.EqualTo(runs + 1));
            Assert.That(Number(save, "v-runs"), Is.EqualTo(runs + 1));
            Assert.That(Number(save, "v-mirror"), Is.EqualTo(7));
        }

        [Test]
        public async Task RolledBackUserWrite_ReachesNeitherClient()
        {
            var store = await LoadStoreAsync(new NeoInMemoryLocalSaveStore(), corpus: ScriptCorpus);
            var save = await LoadSaveAsync(store);
            var user = store.LoadedUserClient!;
            var heard = new List<string>();
            save.OnWritableValueChanged += (_, id) => heard.Add(id);
            var plan = new NeoWritePlan(user);
            var checkpoint = plan.Open();
            plan.Set(NeoValueOwnership.User, new NumberMemberValue { id = VolumeId, value = 6 });
            plan.Rollback(checkpoint);
            plan.Close();
            plan.Set(NeoValueOwnership.User, new NumberMemberValue { id = "v-runs", value = 1 });

            plan.Commit();

            Assert.That(Volume(user), Is.EqualTo(5));
            Assert.That(Volume(save), Is.EqualTo(5));
            Assert.That(heard, Is.EqualTo(new[] { "v-runs" }));
        }

        [Test]
        public async Task SavePlanPreparedBeforeAUserWrite_ThrowsWhenItCommits()
        {
            var store = await LoadStoreAsync(new NeoInMemoryLocalSaveStore());
            var save = await LoadSaveAsync(store);
            var plan = new NeoWritePlan(save);
            plan.Set(NeoValueOwnership.Save, new NumberMemberValue { id = ScoreId, value = 11 });

            SetVolume(store.LoadedUserClient!, 7);

            var error = Assert.Throws<InvalidOperationException>(() => plan.Commit());
            Assert.That(error!.Message, Is.EqualTo("The data graph changed while this write was being prepared."));
        }

        [Test]
        public async Task SaveClient_SeesUserCollectionChanges_ThroughLookupParentAndNewNodes()
        {
            var store = await LoadStoreAsync(new NeoInMemoryLocalSaveStore(), corpus: ScriptCorpus);
            var save = await LoadSaveAsync(store);
            var user = store.LoadedUserClient!;
            var pick = new NeoMemberClassWritable(save, "root-session", "v-root-session", NeoValueOwnership.Session)
                .Get<NeoMemberLookupWritable>("Pick");

            user.SetWritableValues(NeoValueOwnership.User, new MemberValue[]
            {
                new NumberMemberValue { id = "item-b-level", value = 2 },
                Item("item-b"),
                new ArrayMemberValue { id = "v-items", value = new[] { "item-a", "item-b" } },
            });

            Assert.That(pick.IsSelectableId("item-b"), Is.True);
            Assert.That(save.TryFindOwnedParent(NeoValueOwnership.User, "item-b", out string? parent), Is.True);
            Assert.That(parent, Is.EqualTo("v-items"));
            Assert.That(Number(save, "item-b-level"), Is.EqualTo(2));

            new NeoMemberListWritable(user, "member-items", "v-items", NeoValueOwnership.User).RemoveById("item-b");

            // The removed row lingers until reclamation, in both clients alike.
            Assert.That(pick.IsSelectableId("item-b"), Is.False);
            Assert.That(save.TryFindOwnedParent(NeoValueOwnership.User, "item-b", out string? saveParent),
                Is.EqualTo(user.TryFindOwnedParent(NeoValueOwnership.User, "item-b", out string? userParent)));
            Assert.That(saveParent, Is.EqualTo(userParent));
            Assert.That(new NeoMemberList(save, "member-items", "v-items", NeoValueOwnership.User).Count, Is.EqualTo(1));
        }

        [Test]
        public async Task FileStore_KeepsUserKeysBesideSaves_AndListsOnlySaves()
        {
            string directory = Path.Combine(Path.GetTempPath(), "neo-user-file-" + Guid.NewGuid().ToString("N"));
            try
            {
                var local = new NeoFileLocalSaveStore(directory);
                await local.CommitSaveAsync("save-1", "{}");
                await local.CommitUserAsync(NeoSaveSynchronizer.UserKey, "user");
                await local.CommitUserAsync(NeoSaveSynchronizer.UnreadableUserKey, "unreadable");

                Assert.That(File.ReadAllText(Path.Combine(directory, "user.json")), Is.EqualTo("user"));
                Assert.That(File.ReadAllText(Path.Combine(directory, "user.unreadable.json")), Is.EqualTo("unreadable"));
                Assert.That(await local.LoadUserAsync(NeoSaveSynchronizer.UserKey), Is.EqualTo("user"));
                Assert.That(await local.ListSaveIdsAsync(), Is.EqualTo(new[] { "save-1" }));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task SignInMidLoad_RerunsTheCloudStep()
        {
            var tokens = new NeoRealtimeSaveTests.TestTokenStore();
            var authentication = new NeoAuthentication(
                new NeoAuthenticationOptions("https://api.example", "project-1", "client-1", "openid"),
                tokens,
                now: () => DateTimeOffset.FromUnixTimeSeconds(0));
            var api = new FakeApiClient { userFile = RemoteUserFile(3) };
            var local = new GatedLocalStore(new NeoInMemoryLocalSaveStore());
            local.onUserRead = () =>
            {
                tokens.token = new NeoComposeStoredToken(
                    "access-token", long.MaxValue, new[] { "openid" },
                    "https://api.example", "Ada Lovelace", "ada@example.test");
                authentication.RefreshState();
            };

            var store = await LoadStoreAsync(local, api, authentication: authentication);
            await WaitUntilAsync(() => Volume(store.LoadedUserClient!) == 3);

            Assert.That(store.User.CustomId, Is.EqualTo("user-cloud"));
        }

        [Test]
        public async Task SubsystemRegistrationReset_LeavesNoCurrentStoreOrUserClient()
        {
            var reset = typeof(NeoProjectStore).GetMethod(
                nameof(NeoProjectStore.ResetCurrent),
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
            var stamp = reset.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
            await LoadStoreAsync(new NeoInMemoryLocalSaveStore());
            Assert.That(TestUserNeo.Instance, Is.Not.Null);

            reset.Invoke(null, null);

            Assert.That(stamp?.loadType, Is.EqualTo(RuntimeInitializeLoadType.SubsystemRegistration));
            Assert.That(NeoProjectStore.Current, Is.Null);
            Assert.Throws<InvalidOperationException>(() => _ = TestUserNeo.Instance);
        }

        [Test]
        public void GeneratedProjects_HaveExactlyOneProjectClientBesideTheirUserClient()
        {
            var types = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly =>
                {
                    try
                    {
                        return assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException exception)
                    {
                        return exception.Types.Where(type => type != null).Cast<Type>().ToArray();
                    }
                })
                .Where(type => !type.IsAbstract && type.Namespace != typeof(NeoUserFileTests).Namespace)
                .ToArray();
            var userNamespaces = types
                .Where(type => typeof(NeoUserClient).IsAssignableFrom(type))
                .Select(type => type.Namespace)
                .Distinct()
                .ToArray();

            Assert.That(userNamespaces, Is.Not.Empty, "HelloWorld generates a user client.");
            foreach (string? generatedNamespace in userNamespaces)
            {
                Assert.That(
                    types.Count(type => type.Namespace == generatedNamespace && typeof(NeoProjectClient).IsAssignableFrom(type)),
                    Is.EqualTo(1),
                    generatedNamespace);
            }
        }

        [Test]
        public async Task SaveLoadedWhileTheCurrentStoreLoads_WaitsForItsUserClient()
        {
            var local = new GatedLocalStore(new NeoInMemoryLocalSaveStore());
            var release = new TaskCompletionSource<bool>();
            var reading = new TaskCompletionSource<bool>();
            local.userReadGate = release.Task;
            local.onUserRead = () => reading.TrySetResult(true);
            var store = new NeoProjectStore(
                dataSource: NeoTestExport.Source(Corpus),
                localStore: local,
                targetReleaseChannelId: Channel,
                options: new NeoSaveOptions { LiveSessionsEnabled = false });
            stores.Add(store);
            var loading = store.LoadAsync();
            await reading.Task;

            var saving = LoadCustomAsync(new SaveLoader(store.Schema!));
            await Task.Delay(50);
            Assert.That(saving.IsCompleted, Is.False);
            release.SetResult(true);
            await loading;
            var save = await saving;

            Assert.That(save.AttachedUserClient, Is.SameAs(store.LoadedUserClient));
            SetVolume(store.LoadedUserClient!, 7);
            Assert.That(Volume(save), Is.EqualTo(7));
        }

        private static async Task<NeoClient> LoadCustomAsync(INeoSaveLoader loader) =>
            await new NeoLoader().Load(loader);

        private static NeoMemberClassWritable UserRoot(NeoClient client) =>
            new(client, "root-user", "v-root-user", NeoValueOwnership.User);

        private static NeoMemberClassWritable SaveRoot(NeoClient client) =>
            new(client, "root-save", "v-root-save", NeoValueOwnership.Save);

        private static void InvokeUser(NeoClient client, string function) =>
            UserRoot(client).Get<NeoMemberNSFunction>(function).Invoke("v-root-user", Array.Empty<object?>());

        private static void InvokeSave(NeoClient client, string function) =>
            SaveRoot(client).Get<NeoMemberNSFunction>(function).Invoke("v-root-save", Array.Empty<object?>());

        private static double? Number(NeoClient client, string id) =>
            client.TryGetValue<NumberMemberValue>(id, out var row) ? row.value : null;

        private static List<double> Hear(NeoClient client, string memberId)
        {
            var heard = new List<double>();
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                [memberId] = (_, _, args) =>
                {
                    heard.Add(Convert.ToDouble(args[0]));
                    return null;
                },
            });
            return heard;
        }

        private static ObjectMemberValue Item(string id) => new()
        {
            id = id,
            classId = "class-item",
            containerId = "v-items",
            value = new Dictionary<string, string> { ["Level"] = id + "-level" },
        };

        private static PrimitiveTypeInfo IntType() => new() { type = MemberKind.Int, required = true };

        // The base corpus's Volume is optional.
        private static PrimitiveTypeInfo OptionalIntType() => new() { type = MemberKind.Int };

        private static string BuildScriptCorpus()
        {
            JObject corpus = JObject.Parse(Corpus);
            var members = (JObject)corpus["members"]!;
            var classes = (JObject)corpus["classes"]!;
            var values = (JObject)corpus["values"]!;
            void Place(string classId, string key, JsonMember member)
            {
                member.projectId = "project-1";
                members[member.id] = JObject.Parse(JsonConvert.SerializeObject(member));
                classes[classId]!["schema"]![key] = member.id;
            }

            var delegateType = new DelegateTypeInfo
            {
                type = MemberKind.NSDelegate,
                required = false,
                returnTypeInfo = IntType(),
                argumentTypes = Array.Empty<TypeInfo>(),
            };
            Pointer root = Variable("__root__");
            Pointer self = Variable("__this__");
            Pointer userRoot = KeyOf(root, "User");

            Place("class-user", "OnChange", new ActionMember
            {
                id = "member-on-change",
                name = "OnChange",
                kind = MemberKind.NSAction,
                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
            });
            Place("class-user", "Reader", Delegate("member-reader", "Reader"));
            Place("class-user", "Handler", Delegate("member-handler", "Handler"));
            Place("class-user", "Runs", new IntMember { id = "member-runs", name = "Runs", kind = MemberKind.Int });
            Place("class-user", "Mirror", new IntMember { id = "member-mirror", name = "Mirror", kind = MemberKind.Int });
            Place("class-user", "Items", new ListMember
            {
                id = "member-items",
                name = "Items",
                kind = MemberKind.List,
                entryMemberId = "member-item-entry",
                Requirement = NeoMemberRequirementKind.Required,
            });
            members["member-item-entry"] = JObject.Parse(JsonConvert.SerializeObject(new ClassMember
            {
                id = "member-item-entry",
                projectId = "project-1",
                name = "Item",
                kind = MemberKind.Class,
                classId = "class-item",
                Requirement = NeoMemberRequirementKind.Required,
            }));
            classes["class-item"] = Class("class-item", "Item", new JObject());
            Place("class-item", "Level", new IntMember { id = "member-level", name = "Level", kind = MemberKind.Int });
            Place("class-user", "Raise", Function("member-raise", "Raise", null,
                Assign(KeyOf(self, "Volume"), Plus(KeyOf(self, "Volume"), Literal(1)), IntType(), WritabilityKind.Runtime)));
            Place("class-user", "Read", Function("member-read", "Read", IntType(),
                new ReturnInstruction { type = InstructionKind.Return, pointer = KeyOf(self, "Volume") }));
            Place("class-user", "Fire", Function("member-fire", "Fire", null, new FunctionCallInstruction
            {
                type = InstructionKind.FunctionCall,
                call = new CallActionPointer
                {
                    type = PointerKind.CallAction,
                    action = KeyOf(self, "OnChange"),
                    args = Array.Empty<Pointer>(),
                    callSiteId = "fire",
                },
            }));
            Place("class-user", "StoreClosure", Function("member-store-closure", "StoreClosure", null,
                Assign(KeyOf(self, "Handler"), Closure(delegateType, Literal(41), capturesInt: true), delegateType, WritabilityKind.Runtime)));
            var track = Function("member-track", "Track", null,
                Assign(KeyOf(self, "Runs"), Plus(KeyOf(self, "Runs"), Literal(1)), IntType(), WritabilityKind.Runtime),
                Assign(KeyOf(self, "Mirror"), KeyOf(self, "Volume"), IntType(), WritabilityKind.Runtime));
            track.Effect = NeoEffectKind.Auto;
            Place("class-user", "Track", track);
            Place("class-user", "UserHeard", Native("member-user-heard", "UserHeard"));

            Place("class-save", "Ping", Function("member-ping", "Ping", null));
            Place("class-save", "Callback", Delegate("member-callback", "Callback"));
            Place("class-save", "RaiseUser", Function("member-raise-user", "RaiseUser", null, Call("member-raise", userRoot)));
            Place("class-save", "FireUser", Function("member-fire-user", "FireUser", null, Call("member-fire", userRoot)));
            Place("class-save", "StoreCapture", Function("member-store-capture", "StoreCapture", null,
                Assign(KeyOf(KeyOf(root, "Save"), "Callback"), Closure(delegateType, userRoot, capturesInt: false), delegateType)));
            Place("class-save", "StoreSessionCapture", Function("member-store-session-capture", "StoreSessionCapture", null,
                Assign(KeyOf(KeyOf(root, "Session"), "Callback"), Closure(delegateType, userRoot, capturesInt: false), delegateType,
                    WritabilityKind.Session)));
            Place("class-save", "SaveHeard", Native("member-save-heard", "SaveHeard"));

            Place("class-session", "Callback", Delegate("member-session-callback", "Callback"));
            Place("class-session", "Pick", new LookupMember
            {
                id = "member-pick",
                name = "Pick",
                kind = MemberKind.Lookup,
                collectionMemberId = "member-items",
                Selection = NeoMemberSelectionKind.Single,
            });

            var userRow = (JObject)values["v-root-user"]!["value"]!;
            userRow["OnChange"] = "v-on-change";
            userRow["Reader"] = "v-reader";
            userRow["Runs"] = "v-runs";
            userRow["Mirror"] = "v-mirror";
            userRow["Items"] = "v-items";
            values["v-on-change"] = Row("v-on-change", new JObject { ["listeners"] = new JArray() });
            values["v-reader"] = Row("v-reader", new JObject { ["memberId"] = "member-read", ["valueId"] = "v-root-user" });
            values["v-runs"] = Row("v-runs", 0);
            values["v-mirror"] = Row("v-mirror", 0);
            values["v-items"] = Row("v-items", new JArray("item-a"));
            var item = ObjectRow("item-a", "class-item", new JObject { ["Level"] = "item-a-level" });
            item["containerId"] = "v-items";
            values["item-a"] = item;
            values["item-a-level"] = Row("item-a-level", 1);
            return corpus.ToString(Formatting.None);
        }

        private static DelegateMember Delegate(string id, string name) => new()
        {
            id = id,
            name = name,
            kind = MemberKind.NSDelegate,
            returnTypeInfo = IntType(),
            argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
        };

        /// <summary>A native listener taking the observed optional int.</summary>
        private static FunctionMember Native(string id, string name) => new()
        {
            id = id,
            name = name,
            kind = MemberKind.Function,
            returnTypeInfo = new VoidTypeInfo { type = MemberKind.Void, required = true },
            argumentTypes = new[] { new FunctionArgumentTypeInfo { name = "next", type = MemberKind.Int } },
            Dispatch = NeoFunctionDispatchKind.Synchronous,
        };

        private static NSFunctionMember Function(string id, string name, PrimitiveTypeInfo? returns, params Instruction[] instructions) => new()
        {
            id = id,
            name = name,
            kind = MemberKind.NSFunction,
            code = "compiled test function",
            returnTypeInfo = returns ?? (TypeInfo)new VoidTypeInfo { type = MemberKind.Void, required = true },
            argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
            Dispatch = NeoFunctionDispatchKind.Synchronous,
            action = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = new[] { Parameter("__this__"), Parameter("__root__") },
                instructions = instructions,
                typeInfo = returns ?? new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
            },
        };

        /// <summary>A closure over an int capture returning capture + 1, or over a User capture returning 1.</summary>
        private static DelegateClosurePointer Closure(DelegateTypeInfo typeInfo, Pointer capture, bool capturesInt) => new()
        {
            type = PointerKind.DelegateClosure,
            typeInfo = typeInfo,
            code = "() => capture",
            captures = new[] { capture },
            action = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = new[]
                {
                    Parameter("__this__"),
                    Parameter("__root__"),
                    Parameter("__capture_0_0__", capturesInt
                        ? IntType()
                        : new ClassTypeInfo { type = MemberKind.Class, classId = "class-user", required = true }),
                },
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = capturesInt ? Plus(Variable("__capture_0_0__"), Literal(1)) : Literal(1),
                    },
                },
                typeInfo = IntType(),
            },
        };

        private static FunctionCallInstruction Call(string memberId, Pointer receiver) => new()
        {
            type = InstructionKind.FunctionCall,
            call = new CallFunctionPointer
            {
                type = PointerKind.CallFunction,
                memberId = memberId,
                receiver = CallReceiver.Instance(receiver),
                args = Array.Empty<Pointer>(),
                callSiteId = "call-" + memberId,
            },
        };

        private static AssignInstruction Assign(KeyOfPointer target, Pointer value, TypeInfo typeInfo, string writability = WritabilityKind.Save) => new()
        {
            type = InstructionKind.Assign,
            operatorValue = "=",
            target = new WriteTarget { pointer = target, typeInfo = typeInfo, writability = writability },
            pointer = value,
        };

        private static Variable Parameter(string id, TypeInfo? typeInfo = null) => new()
        {
            id = id,
            typeInfo = typeInfo ?? new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
            pointer = Variable(id),
        };

        private static VariablePointer Variable(string id) => new() { type = PointerKind.Variable, variableId = id };

        private static KeyOfPointer KeyOf(Pointer receiver, string key) => new()
        {
            type = PointerKind.KeyOf,
            keyOf = new KeyOf
            {
                pointer = receiver,
                key = new ValuePointer
                {
                    type = PointerKind.Value,
                    value = new Value
                    {
                        typeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true },
                        value = JToken.FromObject(key),
                    },
                },
            },
        };

        private static ValuePointer Literal(int value) => new()
        {
            type = PointerKind.Value,
            value = new Value { typeInfo = IntType(), value = JToken.FromObject(value) },
        };

        private static OperationPointer Plus(Pointer left, Pointer right) => new()
        {
            type = PointerKind.Operation,
            operation = new ArithmeticOperation
            {
                type = OperationKind.Arithmetic,
                arithmetic = new ArithmeticOpInfo { type = ArithmeticOpKind.Addition, pointers = new[] { left, right } },
            },
        };

        private static JObject Row(string id, JToken value) => new()
        {
            ["id"] = id,
            ["value"] = value,
            ["createdAt"] = 0,
            ["updatedAt"] = 0,
        };

        /// <summary>The generated wrapper of the Save root: <c>saveObject.Ping</c>.</summary>
        private sealed class SaveRootView : NeoGeneratedClassValue
        {
            public SaveRootView(NeoClient client, NeoMemberClassWritable node)
                : base(client, node, "class-save", isReadOnly: false, NeoValueOwnership.Save)
            {
            }

            [NeoMemberMethod("member-ping")]
            public void Ping()
            {
            }
        }

        /// <summary>The generated wrapper of the User root: <c>user.Raise</c>.</summary>
        private sealed class UserRootView : NeoGeneratedClassValue
        {
            public UserRootView(NeoClient client, NeoMemberClassWritable node)
                : base(client, node, "class-user", isReadOnly: false, NeoValueOwnership.User)
            {
            }

            [NeoMemberMethod("member-raise")]
            public void Raise()
            {
            }
        }

        /// <summary>A custom loader over a store's schema.</summary>
        private sealed class SaveLoader : INeoSaveLoader
        {
            public SaveLoader(ProjectData schema)
            {
                Schema = schema;
            }

            public ProjectData Schema
            {
                get;
            }

            public string CustomId => "custom-save";

            public Awaitable<string?> LoadSaveContentAsync() => NeoAwaitable.FromResult<string?>(null);

            public Awaitable CommitSaveContentAsync(string content, bool replaceSnapshot) => NeoAwaitable.Completed();
        }

        /// <summary>A local store whose user-file read runs a hook and can wait on a gate.</summary>
        private sealed class GatedLocalStore : INeoLocalSaveStore
        {
            private readonly INeoLocalSaveStore inner;
            internal Action? onUserRead;
            internal Task? userReadGate;

            public GatedLocalStore(INeoLocalSaveStore inner)
            {
                this.inner = inner;
            }

            public Awaitable<IReadOnlyList<string>> ListSaveIdsAsync() => inner.ListSaveIdsAsync();

            public Awaitable<string?> LoadSaveAsync(string customId) => inner.LoadSaveAsync(customId);

            public Awaitable CommitSaveAsync(string customId, string content) => inner.CommitSaveAsync(customId, content);

            public Awaitable DeleteSaveAsync(string customId) => inner.DeleteSaveAsync(customId);

            public async Awaitable<string?> LoadUserAsync(string key)
            {
                onUserRead?.Invoke();
                if (userReadGate != null)
                    await userReadGate;
                return await inner.LoadUserAsync(key);
            }

            public Awaitable CommitUserAsync(string key, string content) => inner.CommitUserAsync(key, content);
        }
    }
}
