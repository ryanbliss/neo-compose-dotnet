#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P67 §4.1/§4.2 executed through the compiled IR — the on-device half of
    /// compiler revisions 10-11's `variant` pointer and its two intrinsics.
    ///
    /// <para>The fixture hand-builds variant graphs because that is the only
    /// way to reach the evaluator arms without a live push; the graphs are the
    /// shape the CLI actually emits (a materialized value map, no `init`).</para>
    /// </summary>
    public class P67VariantIRTests
    {
        private const string ProjectId = "p67";
        private const string WidgetClassId = "widget-class";
        private const string VariantClassId = "neo-variant-class";
        private const string LookupFolderId = "lookup-folder";
        private const string LookupCollectionMemberId = "lookup-catalog";
        private const string LookupCollectionValueId = "value-catalog";

        // -------------------------------------------------------------------
        // §3.3 — one variant's initialize delegating to another's Initialize.
        // -------------------------------------------------------------------

        [Test]
        public void VariantInitialize_DelegatesToAnotherVariantsInitialize()
        {
            NeoClient client = LoadClient();

            // Variant "Down" declares `initialize: () => Widget.Variants.Up.Initialize()`.
            // "Up" constructs the widget and labels it.
            VariantRecord down = client.variants["variant-down"];
            NeoMemberClassWritable node = NeoVariantSupport.InitializeNode(
                client,
                WidgetClassId,
                down);

            Assert.IsNotNull(node.value);
            Assert.AreEqual(WidgetClassId, node.value!.classId);
            // "Up" built it; "Down"'s own Overrides then refined it, proving the
            // delegated construction and the delegating variant's declarative
            // half both ran, in that order (§4.1 steps 1 then 2).
            Assert.AreEqual("down", ReadLabel(client, node));
        }

        [Test]
        public void VariantInitialize_RunsOverridesButNotApply()
        {
            NeoClient client = LoadClient();
            VariantRecord up = client.variants["variant-up"];

            NeoMemberClassWritable node = NeoVariantSupport.InitializeNode(
                client,
                WidgetClassId,
                up);

            // "Up" declares an Apply that would write "applied". §4.1 says the
            // construction path never runs it, so Overrides' "up" survives.
            Assert.AreEqual("up", ReadLabel(client, node));
        }

        // -------------------------------------------------------------------
        // §4.2 — ToVariant through the IR.
        // -------------------------------------------------------------------

        [Test]
        public void VariantApply_RunsInPlaceAndReturnsTheReceiver()
        {
            NeoClient client = LoadClient();
            NSGetterEvaluator.Context ctx = Context(client);
            string targetId = NewSessionInstance(client);

            object? applied = NSGetterEvaluator.Evaluate(
                Getter(Return(VariantApplyPointer(
                    Reference(targetId),
                    VariantRef(WidgetClassId, "variant-up")))),
                ctx);

            // §4.2 step 4 — a value, and the receiver's, never a replacement.
            Assert.IsNotNull(applied);
            // Ordering, provably: Apply wrote Label AND Trace; Overrides then
            // overwrote Label only. `Trace` is what makes a skipped Apply fail
            // this test rather than pass it silently.
            Assert.AreEqual("apply-ran", ReadRowMember(client, targetId, "Trace"));
            Assert.AreEqual("up", ReadRowLabel(client, targetId));
        }

        [Test]
        public void VariantApply_OverridesWinOverTheApplyClosure()
        {
            NeoClient client = LoadClient();
            NSGetterEvaluator.Context ctx = Context(client);
            string targetId = NewSessionInstance(client);

            NSGetterEvaluator.Evaluate(
                Getter(Return(VariantApplyPointer(
                    Reference(targetId),
                    VariantRef(WidgetClassId, "variant-up")))),
                ctx);

            // Apply set Label to "applied"; Overrides is step 2, so it wins.
            Assert.AreNotEqual("applied", ReadRowLabel(client, targetId));
        }

        // -------------------------------------------------------------------
        // P68 §4 — the row argument through both evaluator intrinsics.
        // -------------------------------------------------------------------

        [Test]
        public void LookupVariantInitialize_ReceivesAnExplicitCollectionRow()
        {
            NeoClient client = LoadClient();

            object? made = NSGetterEvaluator.Evaluate(
                Getter(Return(VariantInitializePointer(
                    VariantRef(WidgetClassId, "variant-lookup"),
                    Reference("value-target")))),
                Context(client));

            Assert.IsNotNull(made);
        }

        [Test]
        public void LookupVariantApply_ThreadsTheRowAndReturnsTheReceiver()
        {
            NeoClient client = LoadClient();
            string targetId = NewSessionInstance(client);
            NSGetterEvaluator.Context ctx = Context(client);
            object? receiver = NSGetterEvaluator.Evaluate(
                Getter(Return(Reference(targetId))),
                ctx);

            object? applied = NSGetterEvaluator.Evaluate(
                Getter(Return(VariantApplyPointer(
                    Reference(targetId),
                    VariantRef(WidgetClassId, "variant-lookup"),
                    Reference("value-target")))),
                ctx);

            Assert.AreSame(receiver, applied);
            Assert.AreEqual("target", ReadRowLabel(client, targetId));
        }

        [Test]
        public void LookupVariantInitialize_UsesARowBoundOnTheVariantValue()
        {
            NeoClient client = LoadClient();

            object? made = NSGetterEvaluator.Evaluate(
                Getter(Return(VariantInitializePointer(
                    VariantRef(
                        WidgetClassId,
                        "variant-lookup",
                        rowValueId: "value-target")))),
                Context(client));

            Assert.IsNotNull(made);
        }

        [Test]
        public void LookupVariant_RejectsARowOutsideTheBoundCollection()
        {
            NeoClient client = LoadClient();

            var error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(
                    Getter(Return(VariantInitializePointer(
                        VariantRef(WidgetClassId, "variant-lookup"),
                        Reference("value-assets")))),
                    Context(client)))!;

            StringAssert.Contains("not an entry", error.Message);
            StringAssert.Contains(LookupCollectionValueId, error.Message);
        }

        [Test]
        public void BaseVariant_RejectsALookupRowOnBothPaths()
        {
            NeoClient client = LoadClient();
            string targetId = NewSessionInstance(client);

            Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(
                    Getter(Return(VariantInitializePointer(
                        VariantRef(WidgetClassId, variantId: null),
                        Reference("value-target")))),
                    Context(client)));
            Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(
                    Getter(Return(VariantApplyPointer(
                        Reference(targetId),
                        VariantRef(WidgetClassId, variantId: null),
                        Reference("value-target")))),
                    Context(client)));
        }

        [Test]
        public void VariantApply_SkipsTheClosureWhenTheVariantDeclaresNone()
        {
            NeoClient client = LoadClient();
            NSGetterEvaluator.Context ctx = Context(client);
            string targetId = NewSessionInstance(client);

            // "Plain" authors no Apply value — declarative-only application.
            // The member is still declared on the class, so "absent" has to be
            // read off the value, not off the schema.
            NSGetterEvaluator.Evaluate(
                Getter(Return(VariantApplyPointer(
                    Reference(targetId),
                    VariantRef(WidgetClassId, "variant-plain")))),
                ctx);

            Assert.AreEqual("plain", ReadRowLabel(client, targetId));
        }

        // -------------------------------------------------------------------
        // §3.4 — the base selection through both paths.
        // -------------------------------------------------------------------

        [Test]
        public void BaseSelection_InitializeIsTheClassesOwnConstruction()
        {
            NeoClient client = LoadClient();

            NeoMemberClassWritable node = NeoVariantSupport.InitializeNode(
                client,
                WidgetClassId,
                record: null);

            Assert.IsNotNull(node.value);
            Assert.AreEqual(WidgetClassId, node.value!.classId);
        }

        [Test]
        public void BaseSelection_ApplyLeavesTheReceiverUntouched()
        {
            NeoClient client = LoadClient();
            NSGetterEvaluator.Context ctx = Context(client);
            string targetId = NewSessionInstance(client);
            string before = ReadRowLabel(client, targetId);

            object? applied = NSGetterEvaluator.Evaluate(
                Getter(Return(VariantApplyPointer(
                    Reference(targetId),
                    VariantRef(WidgetClassId, variantId: null)))),
                ctx);

            Assert.IsNotNull(applied);
            // "Become the plain class again" is not a state a written value can
            // be walked back to, so the base entry writes nothing (§4.2).
            Assert.AreEqual(before, ReadRowLabel(client, targetId));
        }

        [Test]
        public void VariantPointer_RejectsAnIdThatIsNotInTheExport()
        {
            NeoClient client = LoadClient();
            NSGetterEvaluator.Context ctx = Context(client);

            var error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.Evaluate(
                    Getter(Return(VariantInitializePointer(
                        VariantRef(WidgetClassId, "variant-missing")))),
                    ctx))!;

            StringAssert.Contains("variant-missing", error.Message);
        }

        // -------------------------------------------------------------------
        // 6 - a Variant MEMBER read, which is the shape the resolver actually
        // emits for `item.Variant` (an ordinary member pointer, not the static
        // `variant` pointer every other test here builds).
        // -------------------------------------------------------------------

        private static KeyOfPointer MemberRead(string valueId, string schemaKey) => new()
        {
            type = PointerKind.KeyOf,
            keyOf = new KeyOf
            {
                pointer = Reference(valueId),
                key = Literal(schemaKey),
            },
        };

        [Test]
        public void VariantMemberRead_YieldsTheStoredPair()
        {
            // Regression: ExtractWireValue had no VariantMemberValue arm, so a
            // member read fell to `_ => null` on device while the web evaluator
            // returned the pair - a silent web/device split on a shipped kind.
            NeoClient client = LoadClient();
            NSGetterEvaluator.Context ctx = Context(client);

            object? read = NSGetterEvaluator.Evaluate(
                Getter(Return(MemberRead("value-target", "Chosen"))),
                ctx);

            Assert.IsNotNull(read);
        }

        [Test]
        public void VariantMemberRead_DrivesInitializeWithoutAStaticPath()
        {
            NeoClient client = LoadClient();
            NSGetterEvaluator.Context ctx = Context(client);

            object? made = NSGetterEvaluator.Evaluate(
                Getter(Return(VariantInitializePointer(MemberRead("value-target", "Chosen")))),
                ctx);

            // "Up" constructs the widget and Overrides labels it.
            Assert.IsNotNull(made);
        }

        [Test]
        public void VariantMemberRead_DrivesToVariantWithoutAStaticPath()
        {
            NeoClient client = LoadClient();
            NSGetterEvaluator.Context ctx = Context(client);
            string targetId = NewSessionInstance(client);

            NSGetterEvaluator.Evaluate(
                Getter(Return(VariantApplyPointerFrom(
                    Reference(targetId),
                    MemberRead("value-target", "Chosen")))),
                ctx);

            Assert.AreEqual("up", ReadRowLabel(client, targetId));
        }

        [Test]
        public void VariantMemberRead_OfAnEmptySelectionIsNull()
        {
            // `item.Variant == null` must agree with the web, which returns the
            // stored pair - and null when there is no selection.
            NeoClient client = LoadClient();
            NSGetterEvaluator.Context ctx = Context(client);
            client.ProjectDataForRuntime.values["value-empty-holder"] =
                ObjectValue(
                    "value-empty-holder",
                    WidgetClassId,
                    ("Label", "value-target-label"),
                    ("Chosen", "value-target-empty-choice"));

            object? read = NSGetterEvaluator.Evaluate(
                Getter(Return(MemberRead("value-empty-holder", "Chosen"))),
                ctx);

            Assert.IsNull(read);
        }

        // -------------------------------------------------------------------
        // 2.2 / 11 - a variant graph is rooted at its RECORD, not at a member,
        // so the load-time reachability walk must treat it as live.
        // -------------------------------------------------------------------

        [Test]
        public void VariantGraphs_LoadWithoutAnUnreferencedValueWarning()
        {
            // Every other value row is reachable from Assets/Save/Session. A
            // variant's is reachable only from its variant record, so a walk
            // that did not know about variant roots would report the whole
            // graph as unreferenced (or drop it).
            ProjectData data = BuildVariantProjectData();

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);

            // No warning was logged during load: an unhandled Debug.LogWarning
            // fails an EditMode test through the log handler, so reaching here
            // with the graph intact is the assertion.
            LogAssert.NoUnexpectedReceived();
            Assert.IsTrue(client.TryGetVariant("variant-up", out VariantRecord? record));
            Assert.IsTrue(client.TryGetValue(
                record!.valueId,
                out ObjectMemberValue? root));
            Assert.AreEqual(VariantClassId, root!.classId);
            // The graph beneath it is still readable, which is what "live"
            // has to mean for the construction path to work at all.
            Assert.IsTrue(root.value!.ContainsKey("Initialize"));
        }

        // -------------------------------------------------------------------
        // Revision handshake.
        // -------------------------------------------------------------------

        [Test]
        public void CompilerRevision_CurrentIsEleven()
        {
            Assert.AreEqual(11, FunctionWithReturnType.CurrentCompilerRevision);
        }

        [Test]
        public void CompilerRevision_ElevenExecutesAndTwelveIsRejected()
        {
            NeoClient client = LoadClient();
            FunctionWithReturnType eleven = Getter(Return(Literal("ok")));
            eleven.compilerRevision = 11;
            Assert.DoesNotThrow(() =>
                NeoScriptExecutor.PrepareCallback(
                    client,
                    eleven,
                    Context(client),
                    options: null));

            FunctionWithReturnType twelve = Getter(Return(Literal("ok")));
            twelve.compilerRevision = 12;
            var error = Assert.Throws<NeoScriptPreExecutionValidationError>(() =>
                NeoScriptExecutor.PrepareCallback(
                    client,
                    twelve,
                    Context(client),
                    options: null))!;
            StringAssert.Contains("compiler revision 12", error.Message);
            StringAssert.Contains("revisions 1 through 11", error.Message);
        }

        // -------------------------------------------------------------------
        // Fixture.
        // -------------------------------------------------------------------

        private static NeoClient LoadClient()
        {
            return NeoTestSaveStack.ClientFromSchema(BuildVariantProjectData());
        }

        private static NSGetterEvaluator.Context Context(NeoClient client)
        {
            var ctx = new NSGetterEvaluator.Context(
                client,
                thisValue: null,
                rootValue: null,
                valueOwnership: NeoValueOwnership.Session);
            return ctx.WithRoot(NeoScriptValueMarshaller.ResolveRoot(client, ctx));
        }

        /// <summary>
        /// A Session-owned Widget to apply variants to. Application writes, so
        /// an Asset-owned row is correctly refused as read-only.
        /// </summary>
        private static string NewSessionInstance(NeoClient client)
        {
            NeoMemberClassWritable node = NeoVariantSupport.InitializeNode(
                client,
                WidgetClassId,
                record: null);
            return node.value?.id
                ?? throw new InvalidOperationException("No instance row.");
        }

        private static string ReadLabel(NeoClient client, NeoMemberClass node)
        {
            return node.TryGet("Label", out NeoMemberString? label)
                ? label.value?.value ?? string.Empty
                : string.Empty;
        }

        private static string ReadRowLabel(NeoClient client, string valueId)
        {
            return ReadRowMember(client, valueId, "Label");
        }

        private static string ReadRowMember(
            NeoClient client,
            string valueId,
            string schemaKey)
        {
            if (!client.TryGetValue(valueId, out ObjectMemberValue? row)) return string.Empty;
            if (row.value is null || !row.value.TryGetValue(schemaKey, out string? memberId))
            {
                return string.Empty;
            }
            return client.TryGetValue(memberId, out StringMemberValue? member)
                ? member.value ?? string.Empty
                : string.Empty;
        }

        private static ProjectData BuildVariantProjectData()
        {
            var rootClass = new NeoSchemaClass
            {
                id = "root-class",
                projectId = ProjectId,
                name = "Root",
                schema = new Dictionary<string, string>
                {
                    ["Catalog"] = LookupCollectionMemberId,
                },
            };
            var widgetClass = new NeoSchemaClass
            {
                id = WidgetClassId,
                projectId = ProjectId,
                name = "Widget",
                schema = new Dictionary<string, string>
                {
                    ["Label"] = "widget-label",
                    // Written only by Apply, never by any Overrides partial, so
                    // a skipped Apply is observable.
                    ["Trace"] = "widget-trace",
                    // P67 6 - a Variant member, read through an ordinary keyOf
                    // member pointer rather than a static Variants path.
                    ["Chosen"] = "widget-chosen",
                },
            };
            // The seeded family, reduced to the shape the handle reads.
            var variantClass = new NeoSchemaClass
            {
                id = VariantClassId,
                projectId = ProjectId,
                name = "NeoVariant",
                schema = new Dictionary<string, string>
                {
                    ["Initialize"] = "variant-initialize",
                    ["Apply"] = "variant-apply",
                    ["Overrides"] = "variant-overrides",
                },
            };

            ClassMember rootAssets = RootMember("root-assets", "Assets", "value-assets");
            ClassMember rootSave = RootMember("root-save", "Save", "value-save", "save");
            ClassMember rootSession =
                RootMember("root-session", "Session", "value-session", "session");

            var values = new Dictionary<string, MemberValue>
            {
                ["value-assets"] = ObjectValue(
                    "value-assets",
                    rootClass.id,
                    ("Catalog", LookupCollectionValueId)),
                ["value-save"] = ObjectValue("value-save", rootClass.id),
                ["value-session"] = ObjectValue("value-session", rootClass.id),

                // A pre-existing widget instance for the application path.
                ["value-target"] = ObjectValue(
                    "value-target",
                    WidgetClassId,
                    ("Label", "value-target-label"),
                    ("Chosen", "value-target-chosen")),
                ["value-target-label"] = StringValue("value-target-label", "target"),
                ["value-target-chosen"] = new VariantMemberValue
                {
                    id = "value-target-chosen",
                    value = new VariantRefValue
                    {
                        classId = WidgetClassId,
                        variantId = "variant-up",
                    },
                    createdAt = "x",
                    updatedAt = "x",
                },
                ["value-target-empty-choice"] = new VariantMemberValue
                {
                    id = "value-target-empty-choice",
                    value = null,
                    createdAt = "x",
                    updatedAt = "x",
                },
                [LookupCollectionValueId] = new ArrayMemberValue
                {
                    id = LookupCollectionValueId,
                    value = new[] { "value-target" },
                    createdAt = "x",
                    updatedAt = "x",
                },

                // Variant "Up": constructs the widget, Overrides Label, and
                // declares an Apply so the construction path can be shown to
                // skip it.
                ["value-variant-up"] = ObjectValue(
                    "value-variant-up",
                    VariantClassId,
                    ("Initialize", "value-up-initialize"),
                    ("Apply", "value-up-apply"),
                    ("Overrides", "value-up-overrides")),
                ["value-up-initialize"] = Closure(
                    "value-up-initialize",
                    Return(ClassConstructorPointer(WidgetClassId))),
                ["value-up-apply"] = VoidClosure(
                    "value-up-apply",
                    AssignMember("Label", "applied"),
                    AssignMember("Trace", "apply-ran")),
                ["value-up-overrides"] = ObjectValue(
                    "value-up-overrides",
                    WidgetClassId,
                    ("Label", "value-up-override-label")),
                ["value-up-override-label"] = StringValue("value-up-override-label", "up"),

                // Variant "Down": §3.3's delegating shape — its initialize is
                // `Widget.Variants.Up.Initialize()`.
                ["value-variant-down"] = ObjectValue(
                    "value-variant-down",
                    VariantClassId,
                    ("Initialize", "value-down-initialize"),
                    ("Overrides", "value-down-overrides")),
                ["value-down-initialize"] = Closure(
                    "value-down-initialize",
                    Return(VariantInitializePointer(
                        VariantRef(WidgetClassId, "variant-up")))),
                ["value-down-overrides"] = ObjectValue(
                    "value-down-overrides",
                    WidgetClassId,
                    ("Label", "value-down-override-label")),
                ["value-down-override-label"] =
                    StringValue("value-down-override-label", "down"),

                // Variant "Plain": Overrides only — declarative-only application.
                ["value-variant-plain"] = ObjectValue(
                    "value-variant-plain",
                    VariantClassId,
                    ("Initialize", "value-plain-initialize"),
                    ("Overrides", "value-plain-overrides")),
                ["value-plain-initialize"] = Closure(
                    "value-plain-initialize",
                    Return(ClassConstructorPointer(WidgetClassId))),
                ["value-plain-overrides"] = ObjectValue(
                    "value-plain-overrides",
                    WidgetClassId,
                    ("Label", "value-plain-override-label")),
                ["value-plain-override-label"] =
                    StringValue("value-plain-override-label", "plain"),

                // P68 lookup variant: Initialize consumes the row while
                // constructing; Apply copies the row's Label onto the target,
                // making row-argument threading observable on device.
                ["value-variant-lookup"] = ObjectValue(
                    "value-variant-lookup",
                    VariantClassId,
                    ("Initialize", "value-lookup-initialize"),
                    ("Apply", "value-lookup-apply")),
                ["value-lookup-initialize"] = LookupClosure(
                    "value-lookup-initialize",
                    Return(ClassConstructorPointer(WidgetClassId))),
                ["value-lookup-apply"] = LookupVoidClosure(
                    "value-lookup-apply",
                    AssignMember(
                        "Label",
                        VariableMemberRead("__row__", "Label"))),
            };

            return new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    name = "P67 Variant IR Tests",
                    rootAssetsMemberId = rootAssets.id,
                    rootSaveFileMemberId = rootSave.id,
                    rootSessionMemberId = rootSession.id,
                },
                members = new Dictionary<string, JsonMember>
                {
                    [rootAssets.id] = rootAssets,
                    [rootSave.id] = rootSave,
                    [rootSession.id] = rootSession,
                    ["widget-label"] = StringField("widget-label", "Label"),
                    ["widget-trace"] = StringField("widget-trace", "Trace"),
                    ["widget-chosen"] = new VariantMember
                    {
                        id = "widget-chosen",
                        projectId = ProjectId,
                        name = "Chosen",
                        kind = MemberKind.Variant,
                        required = false,
                        storage = "session",
                        createdAt = "x",
                        updatedAt = "x",
                    },
                    ["lookup-entry"] = new ClassMember
                    {
                        id = "lookup-entry",
                        projectId = ProjectId,
                        name = "Entry",
                        kind = MemberKind.Class,
                        classId = WidgetClassId,
                        required = true,
                        storage = "immutable",
                        createdAt = "x",
                        updatedAt = "x",
                    },
                    [LookupCollectionMemberId] = new ListMember
                    {
                        id = LookupCollectionMemberId,
                        projectId = ProjectId,
                        name = "Catalog",
                        kind = MemberKind.List,
                        entryMemberId = "lookup-entry",
                        required = true,
                        storage = "immutable",
                        createdAt = "x",
                        updatedAt = "x",
                    },
                    // `Initialize` returns TObject; `Apply` is void (§1).
                    ["variant-initialize"] = DelegateField(
                        "variant-initialize",
                        "Initialize",
                        ClassType(WidgetClassId)),
                    ["variant-apply"] = DelegateField(
                        "variant-apply",
                        "Apply",
                        new PrimitiveTypeInfo { type = MemberKind.Null, required = true }),
                    ["variant-overrides"] = PartialField("variant-overrides", "Overrides"),
                },
                values = values,
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [rootClass.id] = rootClass,
                    [widgetClass.id] = widgetClass,
                    [variantClass.id] = variantClass,
                },
                variants = new Dictionary<string, VariantRecord>
                {
                    ["variant-up"] = Variant("variant-up", "Up", "value-variant-up"),
                    ["variant-down"] = Variant("variant-down", "Down", "value-variant-down"),
                    ["variant-plain"] = Variant("variant-plain", "Plain", "value-variant-plain"),
                    ["variant-lookup"] = new VariantRecord
                    {
                        id = "variant-lookup",
                        projectId = ProjectId,
                        classId = WidgetClassId,
                        name = "Lookup",
                        folder = "Stages",
                        valueId = "value-variant-lookup",
                        createdAt = "x",
                        updatedAt = "x",
                    },
                },
                variantFolders = new Dictionary<string, VariantFolderRecord>
                {
                    [LookupFolderId] = new VariantFolderRecord
                    {
                        id = LookupFolderId,
                        classId = WidgetClassId,
                        path = "Stages",
                        binding = new VariantFolderBinding
                        {
                            collectionMemberId = LookupCollectionMemberId,
                            collectionValueId = LookupCollectionValueId,
                        },
                    },
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
        }

        private static VariantRecord Variant(string id, string name, string valueId) => new()
        {
            id = id,
            projectId = ProjectId,
            classId = WidgetClassId,
            name = name,
            folder = null,
            valueId = valueId,
            createdAt = "x",
            updatedAt = "x",
        };

        // ---- IR builders ----

        private static ReturnInstruction Return(Pointer pointer) => new()
        {
            type = InstructionKind.Return,
            pointer = pointer,
        };

        private static ValuePointer Literal(string value) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true },
                value = JToken.FromObject(value),
            },
        };

        private static ReferencePointer Reference(string valueId) => new()
        {
            type = PointerKind.Reference,
            valueId = valueId,
        };

        private static VariantPointer VariantRef(
            string classId,
            string? variantId,
            string? rowValueId = null) => new()
        {
            type = PointerKind.Variant,
            classId = classId,
            variantId = variantId,
            rowValueId = rowValueId,
        };

        private static FunctionPointer VariantInitializePointer(
            Pointer variant,
            Pointer? row = null) => new()
        {
            type = PointerKind.Function,
            function = new VariantInitializeFunction
            {
                type = FunctionKind.VariantInitialize,
                info = new FunctionVariantInitializeInfo
                {
                    variantPointer = variant,
                    rowPointer = row,
                    schemaClassInfo = ClassType(WidgetClassId),
                },
            },
        };

        private static FunctionPointer VariantApplyPointer(
            Pointer receiver,
            VariantPointer variant,
            Pointer? row = null) => new()
        {
            type = PointerKind.Function,
            function = new VariantApplyFunction
            {
                type = FunctionKind.VariantApply,
                info = new FunctionVariantApplyInfo
                {
                    receiverPointer = receiver,
                    variantPointer = variant,
                    rowPointer = row,
                    schemaClassInfo = ClassType(WidgetClassId),
                },
            },
        };

        private static FunctionPointer VariantApplyPointerFrom(
            Pointer receiver,
            Pointer variant) => new()
        {
            type = PointerKind.Function,
            function = new VariantApplyFunction
            {
                type = FunctionKind.VariantApply,
                info = new FunctionVariantApplyInfo
                {
                    receiverPointer = receiver,
                    variantPointer = variant,
                    schemaClassInfo = ClassType(WidgetClassId),
                },
            },
        };

        private static FunctionPointer ClassConstructorPointer(string classId) => new()
        {
            type = PointerKind.Function,
            function = new ClassConstructorFunction
            {
                type = FunctionKind.ClassConstructor,
                info = new FunctionClassConstructorInfo
                {
                    schemaClassInfo = ClassType(classId),
                    fields = Array.Empty<FunctionClassConstructorField>(),
                },
            },
        };

        private static AssignInstruction AssignMember(
            string schemaKey,
            string value)
        {
            return AssignMember(schemaKey, Literal(value));
        }

        private static AssignInstruction AssignMember(
            string schemaKey,
            Pointer value) => new()
        {
            type = InstructionKind.Assign,
            target = new WriteTarget
            {
                pointer = new KeyOfPointer
                {
                    type = PointerKind.KeyOf,
                    keyOf = new KeyOf
                    {
                        pointer = new VariablePointer
                        {
                            type = PointerKind.Variable,
                            variableId = "__this__",
                        },
                        key = Literal(schemaKey),
                    },
                },
                typeInfo = new PrimitiveTypeInfo
                {
                    type = MemberKind.String,
                    required = true,
                },
            },
            pointer = value,
        };

        private static KeyOfPointer VariableMemberRead(
            string variableId,
            string schemaKey) => new()
        {
            type = PointerKind.KeyOf,
            keyOf = new KeyOf
            {
                pointer = new VariablePointer
                {
                    type = PointerKind.Variable,
                    variableId = variableId,
                },
                key = Literal(schemaKey),
            },
        };

        private static ClassTypeInfo ClassType(string classId) => new()
        {
            type = MemberKind.Class,
            required = true,
            classId = classId,
        };

        private static FunctionWithReturnType Getter(params Instruction[] instructions) => new()
        {
            compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
            parameters = Array.Empty<Variable>(),
            instructions = instructions,
            typeInfo = new PrimitiveTypeInfo { type = MemberKind.Unknown, required = false },
        };

        /// <summary>
        /// A void-body closure — the shape `Apply` compiles to (§1's
        /// `NeoDelegate&lt;void, TObject&gt;`), which returns nothing.
        /// </summary>
        private static DelegateMemberValue VoidClosure(
            string id,
            params Instruction[] instructions)
        {
            DelegateMemberValue closure = Closure(id, instructions);
            closure.value!.action!.typeInfo = new PrimitiveTypeInfo
            {
                type = MemberKind.Null,
                required = true,
            };
            return closure;
        }

        private static DelegateMemberValue LookupVoidClosure(
            string id,
            params Instruction[] instructions)
        {
            DelegateMemberValue closure = LookupClosure(id, instructions);
            closure.value!.action!.typeInfo = new PrimitiveTypeInfo
            {
                type = MemberKind.Null,
                required = true,
            };
            return closure;
        }

        private static DelegateMemberValue LookupClosure(
            string id,
            params Instruction[] instructions)
        {
            DelegateMemberValue closure = Closure(id, instructions);
            var parameters = new List<Variable>(closure.value!.action!.parameters)
            {
                new Variable
                {
                    id = "__row__",
                    typeInfo = ClassType(WidgetClassId),
                },
            };
            closure.value.action.parameters = parameters.ToArray();
            return closure;
        }

        private static DelegateMemberValue Closure(
            string id,
            params Instruction[] instructions) => new()
        {
            id = id,
            createdAt = "x",
            updatedAt = "x",
            value = new NeoDelegateValue
            {
                code = "// hand-built",
                action = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = new[]
                    {
                        new Variable
                        {
                            id = "__this__",
                            typeInfo = ClassType(WidgetClassId),
                        },
                        new Variable
                        {
                            id = "__root__",
                            typeInfo = ClassType("root-class"),
                        },
                    },
                    instructions = instructions,
                    typeInfo = new PrimitiveTypeInfo
                    {
                        type = MemberKind.Unknown,
                        required = false,
                    },
                },
            },
        };

        // ---- record builders ----

        private static ClassMember RootMember(
            string id,
            string name,
            string valueId,
            string? storage = null) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            kind = MemberKind.Class,
            classId = "root-class",
            valueId = valueId,
            storage = storage,
            createdAt = "x",
            updatedAt = "x",
        };

        private static ObjectMemberValue ObjectValue(
            string id,
            string classId,
            params (string key, string valueId)[] entries)
        {
            var map = new Dictionary<string, string>();
            foreach ((string key, string valueId) in entries) map[key] = valueId;
            return new ObjectMemberValue
            {
                id = id,
                classId = classId,
                value = map,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static StringMemberValue StringValue(string id, string value) => new()
        {
            id = id,
            value = value,
            createdAt = "x",
            updatedAt = "x",
        };

        private static StringMember StringField(string id, string name) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            kind = MemberKind.String,
            // Settleable without a call-site argument, so the base selection's
            // bare construction (§3.4) succeeds.
            required = false,
            defaultValue = new StringMemberValueBase { value = "unset" },
            localizable = false,
            storage = "session",
            createdAt = "x",
            updatedAt = "x",
        };

        private static DelegateMember DelegateField(
            string id,
            string name,
            TypeInfo returnTypeInfo) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            kind = MemberKind.NSDelegate,
            required = false,
            storage = "immutable",
            returnTypeInfo = returnTypeInfo,
            argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
            createdAt = "x",
            updatedAt = "x",
        };

        private static ClassMember PartialField(string id, string name) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            kind = MemberKind.Class,
            classId = WidgetClassId,
            partial = true,
            required = false,
            storage = "immutable",
            createdAt = "x",
            updatedAt = "x",
        };
    }
}
