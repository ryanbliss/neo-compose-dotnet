// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P127 §12.1 and §12.2: the one placement walk the SDK writes and
    /// validates with, ported from the compose
    /// <c>type-source-walk.test.ts</c>, plus a member default's template rows
    /// and the one-hop readers.
    /// </summary>
    public sealed class P127TypeSourceTests
    {
        private const string ProjectId = "p127-type-source";
        private const string ChildOverridesEntryMemberId = "system_86688965-6b26-529e-95f3-a4070f022582";

        private static readonly TypeBinding Int = TestTypeBindings.Of(MemberKind.Int);

        private static TypeBinding Open(string classId, string param) => TestTypeBindings.Open(classId, param);

        private static TypeBinding ListOf(TypeBinding entry, bool required = true) => TestTypeBindings.List(entry, required);

        private static TypeBinding HolderOf(TypeBinding t, bool required = true) =>
            TestTypeBindings.Class("holder", required, ("T", t));

        private static Dictionary<string, TypeBinding> Stamped(TypeBinding t) => TestTypeBindings.Stamp(("T", t));

        // ------------------------------------------------------------------
        // walk
        // ------------------------------------------------------------------

        [Test]
        public void StampsAClassRowOnlyAtABindingNode()
        {
            using NeoClient client = BuildClient();
            var anything = Place(client, HolderRows(Int, Number("h.payload", 4)), Root(client, "h", "anything"));
            Assert.That(anything["h"].StampParams, Is.EqualTo(new[] { "T" }));
            Assert.That(anything["h.p"].StampParams, Is.Null);

            var declared = Place(
                client,
                new MemberValue[]
                {
                    Obj("r", new() { ["Closed"] = "c" }),
                    Obj("c", new() { ["Payload"] = "c.payload", ["Items"] = "c.items", ["P"] = "c.p" }),
                    Number("c.payload", 4),
                    Arr("c.items"),
                    Obj("c.p", new() { ["As"] = "c.p.as" }),
                    Arr("c.p.as"),
                },
                Root(client, "r", "root"));
            Assert.That(declared["c"].StampParams, Is.Null);
            // Declarations close every type under a Holder<int> field; each
            // row whose declared type names a param stores it closed.
            Assert.That(declared["c"].TypeSource, Is.EqualTo(NeoTypeSource.OfType(HolderOf(Int, required: false))));
            Assert.That(declared["c.items"].TypeSource, Is.EqualTo(NeoTypeSource.OfType(ListOf(Int, required: false))));
            Assert.That(declared["c.p.as"].TypeSource, Is.EqualTo(NeoTypeSource.OfType(ListOf(Int, required: false))));
            Assert.That(declared["c.payload"].TypeSource, Is.Null);

            var ownerless = Place(client, HolderRows(Int, Number("h.payload", 4)), NeoTypeSources.OwnerlessPosition("h"));
            Assert.That(ownerless["h"].StampParams, Is.EqualTo(new[] { "T" }));
        }

        [Test]
        public void SpellsHoldersOwnListOverItsBindingNodesParam()
        {
            using NeoClient client = BuildClient();
            var placed = Place(client, HolderRows(Int, Number("h.payload", 4)), Root(client, "h", "anything"));
            Assert.That(
                placed["h.items"].TypeSource,
                Is.EqualTo(NeoTypeSource.OfType(ListOf(Open("holder", "T"), required: false), "h")));
            // A scalar reads its type from its owner.
            Assert.That(placed["h.payload"].TypeSource, Is.Null);
        }

        [Test]
        public void ProjectsATBoundToListOfListOfIntWithBindingKeyPaths()
        {
            using NeoClient client = BuildClient();
            var placed = Place(
                client,
                HolderRows(ListOf(ListOf(Int)), Arr("h.payload", "h.inner")).Append(Arr("h.inner")),
                Root(client, "h", "anything"));
            Assert.That(placed["h.payload"].TypeSource, Is.EqualTo(NeoTypeSource.AtPath("h", new[] { "T" })));
            Assert.That(placed["h.inner"].TypeSource, Is.EqualTo(NeoTypeSource.AtPath("h", new[] { "T", "0" })));
        }

        [Test]
        public void StoresTheTypeOverTheSourcesParamsBehindADeclaredClassSlot()
        {
            using NeoClient client = BuildClient();
            var placed = Place(client, HolderRows(Int, Number("h.payload", 4)), Root(client, "h", "anything"));
            Assert.That(
                placed["h.p"].TypeSource,
                Is.EqualTo(NeoTypeSource.OfType(TestTypeBindings.Class("pair", false, ("A", Open("holder", "T")), ("B", Int)), "h")));
            Assert.That(
                placed["h.p.as"].TypeSource,
                Is.EqualTo(NeoTypeSource.OfType(ListOf(Open("holder", "T"), required: false), "h")));
        }

        [Test]
        public void ResolvesABoxedListOfListsInnerListsThroughTheBoxRow()
        {
            using NeoClient client = BuildClient();
            ArrayMemberValue box = Arr("b", "b.0");
            box.valueType = ListOf(ListOf(Int));
            var placed = Place(client, new MemberValue[] { box, Arr("b.0") }, Root(client, "b", "anything"));
            Assert.That(placed["b"].TypeSource, Is.Null);
            Assert.That(placed["b.0"].TypeSource, Is.EqualTo(NeoTypeSource.AtPath("b", new[] { "0" })));
        }

        // ------------------------------------------------------------------
        // refusals
        // ------------------------------------------------------------------

        [Test]
        public void RefusesMalformedTypeSourceShapes()
        {
            JObject Json(TypeBinding binding) => JObject.FromObject(binding);
            Assert.That(
                NeoTypeSource.Violation(new JObject { ["valueId"] = "h", ["path"] = new JArray("T"), ["type"] = Json(Int) }),
                Is.EqualTo("typeSource stores both path and type; they are exclusive."));
            Assert.That(
                NeoTypeSource.Violation(new JObject { ["valueId"] = "h", ["type"] = Json(Int) }),
                Is.EqualTo("typeSource.type is closed but names a valueId."));
            Assert.That(
                NeoTypeSource.Violation(new JObject { ["valueId"] = "h" }),
                Is.EqualTo("typeSource stores neither path nor type."));
            Assert.That(
                NeoTypeSource.Violation(new JObject { ["path"] = new JArray("T") }),
                Is.EqualTo("typeSource.path names no valueId."));
            // A member default's template row stores its type open, with no
            // row to name (§12.1); only its placement can judge it.
            Assert.That(NeoTypeSource.Violation(new JObject { ["type"] = Json(ListOf(Open("holder", "T"))) }), Is.Null);
        }

        [Test]
        public void RefusesATargetThatDoesntHoldTheBinding()
        {
            ObjectMemberValue holder = Obj("h", new(), "holder", Stamped(ListOf(Int)));
            Assert.That(
                NeoTypeSources.TargetViolation("v", NeoTypeSource.AtPath("gone", new[] { "T" }), null),
                Is.EqualTo("Value \"v\" typeSource names missing row \"gone\"."));
            Assert.That(
                NeoTypeSources.TargetViolation("v", NeoTypeSource.AtPath("h", new[] { "T" }), Obj("h", new())),
                Is.EqualTo("Value \"v\" typeSource names row \"h\", which holds no stamp or valueType."));
            Assert.That(
                NeoTypeSources.TargetViolation("v", NeoTypeSource.AtPath("h", new[] { "T", "0", "0" }), holder),
                Is.EqualTo("Value \"v\" typeSource.path [\"T\",\"0\",\"0\"] isn't bound by row \"h\"."));
            Assert.That(NeoTypeSources.TargetViolation("v", NeoTypeSource.AtPath("h", new[] { "T", "0" }), holder), Is.Null);
            Assert.That(
                NeoTypeSources.TargetViolation("v", NeoTypeSource.OfType(ListOf(Open("pair", "A")), "h"), holder),
                Is.EqualTo("Value \"v\" typeSource.type names param \"A\", which row \"h\" doesn't stamp."));
            Assert.That(NeoTypeSources.TargetViolation("v", NeoTypeSource.OfType(ListOf(Open("holder", "T")), "h"), holder), Is.Null);
        }

        // ------------------------------------------------------------------
        // place and validate
        // ------------------------------------------------------------------

        [Test]
        public void MovesStampsToBindingNodesAndWritesEachRowsTypeSource()
        {
            using NeoClient client = BuildClient();
            var graph = new Graph(
                Obj("r", new()
                {
                    ["Closed"] = "c"
                }),
                Obj("c", new()
                {
                    ["Payload"] = "c.payload",
                    ["Items"] = "c.items",
                    ["P"] = "c.p"
                }, stamp: Stamped(Int)),
                Number("c.payload", 4),
                Arr("c.items", stamp: Stamped(Int)),
                Obj("c.p", new()
                {
                    ["As"] = "c.p.as"
                }),
                Arr("c.p.as"));
            NeoTypeSourcePosition? position = Root(client, "r", "root");
            Assert.That(NeoTypeSources.Violations(client, graph, position), Is.EqualTo(new[]
            {
                "Value \"c\" stores a stamp but isn't a binding node.",
                "Value \"c.p\" stores no typeSource, but its declared type names a type parameter.",
                "Value \"c.p.as\" stores no typeSource, but its declared type names a type parameter.",
                "Value \"c.items\" stores a stamp but isn't a binding node.",
            }));
            NeoTypeSources.Place(client, graph, position);
            Assert.That(NeoTypeSources.Violations(client, graph, position), Is.Empty);
            Assert.That(graph["c"].genericBindings, Is.Null);
            Assert.That(graph["c.items"].genericBindings, Is.Null);
            Assert.That(graph["c.items"].typeSource, Is.EqualTo(NeoTypeSource.OfType(ListOf(Int, required: false))));
        }

        [Test]
        public void RefusesABindingNodeWithNoStampItsSlotCantSpell()
        {
            using NeoClient client = BuildClient();
            MemberValue[] rows = HolderRows(Int, Number("h.payload", 4));
            rows[0].genericBindings = null;
            var graph = new Graph(rows);
            NeoTypeSourcePosition? position = Root(client, "h", "anything");
            Assert.That(NeoTypeSources.Violations(client, graph, position)[0], Is.EqualTo("Value \"h\" is a binding node and stores no stamp."));
            var error = Assert.Throws<InvalidOperationException>(() => NeoTypeSources.Place(client, graph, position));
            Assert.That(error!.Message, Is.EqualTo("Binding node \"h\" stores no stamp, and its slot doesn't spell one."));
        }

        [Test]
        public void StoresTheClassOfABindingNodeWhoseSlotNamesNone()
        {
            using NeoClient client = BuildClient();
            var graph = new Graph(HolderRows(TestTypeBindings.Class("plain"), Obj("h.payload", new())));
            NeoTypeSourcePosition? position = Root(client, "h", "anything");
            Assert.That(NeoTypeSources.Violations(client, graph, position), Does.Contain("Value \"h.payload\" is a binding node and stores no classId."));
            NeoTypeSources.Place(client, graph, position);
            MemberValue payload = graph["h.payload"];
            Assert.That(payload.classId, Is.EqualTo("plain"));
            Assert.That(payload.genericBindings, Is.Null);
            Assert.That(payload.typeSource, Is.Null);
        }

        [Test]
        public void RefusesAnExtraForeignOrStaleTypeSource()
        {
            using NeoClient client = BuildClient();
            var graph = new Graph(HolderRows(ListOf(ListOf(Int)), Arr("h.payload", "h.inner"))
                .Append(Arr("h.inner"))
                .Append(Arr("h.items.0"))
                .ToArray());
            NeoTypeSourcePosition? position = Root(client, "h", "anything");
            NeoTypeSources.Place(client, graph, position);
            graph["h.inner"].typeSource = NeoTypeSource.AtPath("h", new[] { "T" });
            graph["h.items"].typeSource = NeoTypeSource.OfType(ListOf(Open("holder", "T")), "other");
            graph["h.p.as"].typeSource = NeoTypeSource.OfType(ListOf(Int, required: false), "h");
            graph["h.payload"].typeSource = NeoTypeSource.OfType(Int);
            Assert.That(NeoTypeSources.Violations(client, graph, position), Is.EqualTo(new[]
            {
                "Value \"h.p.as\" typeSource.type is stale; its placement derives "
                    + NeoTypeSources.Key(ListOf(Open("holder", "T"), required: false)) + ".",
                "Value \"h.items\" typeSource names \"other\", but its nearest binding node or box row is \"h\".",
                "Value \"h.payload\" typeSource names no row, but its placement reads it from \"h\".",
                "Value \"h.inner\" typeSource.path [\"T\"] is stale; its placement derives [\"T\",\"0\"].",
            }));
        }

        [Test]
        public void RewritesPsRowsWhenItsDeclarationChangesToPairOfIntAndT()
        {
            using NeoClient client = BuildClient();
            var graph = new Graph(HolderRows(Int, Number("h.payload", 4)));
            NeoTypeSources.Place(client, graph, Root(client, "h", "anything"));

            using NeoClient edited = BuildClient(data =>
                ((ClassMember)data.members["p"]).classArguments = TestTypeBindings.Arguments(("A", Int), ("B", Open("holder", "T"))));
            NeoTypeSourcePosition? position = Root(edited, "h", "anything");
            Assert.That(NeoTypeSources.Violations(edited, graph, position), Has.Count.EqualTo(2));
            NeoTypeSources.Place(edited, graph, position);
            Assert.That(graph["h.p.as"].typeSource, Is.EqualTo(NeoTypeSource.OfType(ListOf(Int, required: false))));
        }

        // ------------------------------------------------------------------
        // readers
        // ------------------------------------------------------------------

        [Test]
        public void ResolvesTheIntEntriesOfAStampedHoldersAsInOneHop()
        {
            using NeoClient client = BuildClient();
            var graph = new Graph(HolderRows(Int, Number("h.payload", 4)));
            NeoTypeSources.Place(client, graph, Root(client, "h", "anything"));
            MemberValue a = graph["h.p.as"];
            var visited = new List<string>();
            MemberValueBase? Lookup(string id)
            {
                visited.Add(id);
                return graph.Row(id);
            }

            Assert.That(NeoTypeSources.PlacedRowType(client, a.id, a, MemberOf(client, "as"), Lookup), Is.EqualTo(ListOf(Int, required: false)));
            Dictionary<string, TypeBinding> stamp = NeoTypeSources.PlacedRowStamp(client, a.id, a, MemberOf(client, "as"), Lookup);
            Assert.That(stamp["A"], Is.EqualTo(Int));
            Assert.That(visited, Is.EqualTo(new[] { "h", "h" }));
        }

        [Test]
        public void ReadsADeclaredSlotClassRowsEnvFromItsClosedTypeSource()
        {
            using NeoClient client = BuildClient();
            ObjectMemberValue row = Obj("c", new());
            row.typeSource = NeoTypeSource.OfType(HolderOf(Int, required: false));
            Dictionary<string, TypeBinding> stamp = NeoTypeSources.PlacedRowStamp(client, "c", row, MemberOf(client, "closed"), _ => null);
            Assert.That(stamp["T"], Is.EqualTo(Int));
        }

        [Test]
        public void RefusesARowWhoseMemberNamesAParamAndStoresNoTypeSource()
        {
            using NeoClient client = BuildClient();
            var error = Assert.Throws<InvalidOperationException>(() =>
                NeoTypeSources.PlacedRowType(client, "h.items", Arr("h.items"), MemberOf(client, "items"), _ => null));
            Assert.That(error!.Message, Is.EqualTo("Value \"h.items\" under member \"items\" is missing typeSource."));
        }

        // ------------------------------------------------------------------
        // edge placements
        // ------------------------------------------------------------------

        [Test]
        public void SkipsAFieldNoTypeSpellsSuchAsListOfFunctionRef()
        {
            using NeoClient client = BuildClient(data =>
            {
                data.members["action"] = new FunctionRefMember { id = "action", projectId = ProjectId, name = "Entry", kind = MemberKind.FunctionRef };
                data.members["actions"] = List("actions", "Actions", "action");
                data.classes["frame"] = Class("frame", "Frame", new()
                {
                    ["Actions"] = "actions"
                });
            });
            var graph = new Graph(Obj("f", new()
            {
                ["Actions"] = "f.actions"
            }, "frame"), Arr("f.actions"));
            var visited = new List<string>();
            NeoTypeSources.Walk(client, graph, NeoTypeSources.OwnerlessPosition("f"), placed => visited.Add(placed.RowId));
            Assert.That(visited, Is.EqualTo(new[] { "f" }));
        }

        [Test]
        public void ClosesAStaticOverrideRootThroughItsDeclaringClassesExtendsBindings()
        {
            using NeoClient client = BuildClient(data =>
            {
                data.members["frames-entry"] = Generic("frames-entry", "S");
                data.members["frames"] = List("frames", "Frames", "frames-entry");
                ListMember overrideFrames = List("override-frames", "Frames", "frames-entry");
                overrideFrames.extendsMemberId = "frames";
                data.members["override-frames"] = overrideFrames;
                NeoSchemaClass open = Class("open-segment", "OpenSegment", new()
                {
                    ["Frames"] = "frames"
                }, "S");
                NeoSchemaClass closed = Class("int-segment", "IntSegment", new()
                {
                    ["Frames"] = "override-frames"
                });
                closed.extendsClassId = open.id;
                closed.extendsGenericBindings = TestTypeBindings.Arguments(("S", Int));
                data.classes[open.id] = open;
                data.classes[closed.id] = closed;
            });
            var graph = new Graph(Arr("s", stamp: TestTypeBindings.Stamp(("S", Int))));
            NeoTypeSourcePosition? position = NeoTypeSources.RootPosition(client, "s", MemberOf(client, "override-frames"), "int-segment");
            Assert.That(NeoTypeSources.Violations(client, graph, position), Is.EqualTo(new[] { "Value \"s\" stores a stamp but isn't a binding node." }));
            NeoTypeSources.Place(client, graph, position);
            Assert.That(graph["s"].genericBindings, Is.Null);
            Assert.That(graph["s"].typeSource, Is.EqualTo(NeoTypeSource.OfType(ListOf(Int, required: false))));
        }

        [Test]
        public void KeepsTheStampOfAClassRowInASlotThatAdmitsANarrowerConstruction()
        {
            // The frame ChildOverrides entry declares Holder<int>; each row
            // may narrow it, so its placement can't spell the row's type.
            using NeoClient client = BuildClient(data =>
            {
                ClassMember entry = ClassSlot("entry-override", "Entry", "holder", TestTypeBindings.Arguments(("T", Int)));
                entry.extendsMemberId = ChildOverridesEntryMemberId;
                data.members[entry.id] = entry;
                data.members[ChildOverridesEntryMemberId] = ClassSlot(ChildOverridesEntryMemberId, "Entry", "holder", TestTypeBindings.Arguments(("T", Int)));
            });
            var graph = new Graph(HolderRows(ListOf(Int), Arr("h.payload")));
            NeoTypeSourcePosition? position = Root(client, "h", "entry-override");
            NeoTypeSources.Place(client, graph, position);
            Assert.That(NeoTypeSources.Violations(client, graph, position), Is.Empty);
            Assert.That(graph["h"].genericBindings, Is.EqualTo(Stamped(ListOf(Int))));
            Assert.That(graph["h.payload"].typeSource, Is.EqualTo(NeoTypeSource.AtPath("h", new[] { "T" })));
        }

        [Test]
        public void TemplateDefaultRowsStoreTheirTypeOpenWithNoRow()
        {
            // Holder<T>'s P defaults to a Pair<T, int>: no instance closes T,
            // so each row under the default names Holder's T with no valueId.
            using NeoClient client = BuildClient();
            var graph = new Graph(Obj("d", new()
            {
                ["As"] = "d.as"
            }), Arr("d.as"));
            NeoTypeSourcePosition? position = NeoTypeSources.DefaultRootPosition(client, "d", MemberOf(client, "p"), "holder");
            NeoTypeSources.Place(client, graph, position);
            Assert.That(NeoTypeSources.Violations(client, graph, position), Is.Empty);
            Assert.That(graph["d"].genericBindings, Is.Null);
            Assert.That(
                graph["d"].typeSource,
                Is.EqualTo(NeoTypeSource.OfType(TestTypeBindings.Class("pair", false, ("A", Open("holder", "T")), ("B", Int)))));
            Assert.That(graph["d.as"].typeSource, Is.EqualTo(NeoTypeSource.OfType(ListOf(Open("holder", "T"), required: false))));

            // The default root itself stores its type when it names T.
            var items = new Graph(Arr("e"));
            NeoTypeSources.Place(client, items, NeoTypeSources.DefaultRootPosition(client, "e", MemberOf(client, "items"), "holder"));
            Assert.That(items["e"].typeSource, Is.EqualTo(NeoTypeSource.OfType(ListOf(Open("holder", "T"), required: false))));
        }

        [Test]
        public void RefusesAnOpenTypeWithNoRowOutsideItsTemplate()
        {
            // §12.1: only a member default's descendants store an open type
            // with no valueId, and only over their owning class's params.
            using NeoClient client = BuildClient();
            var template = new Graph(Obj("d", new()
            {
                ["As"] = "d.as"
            }), Arr("d.as"));
            NeoTypeSourcePosition? defaultRoot = NeoTypeSources.DefaultRootPosition(client, "d", MemberOf(client, "p"), "holder");
            NeoTypeSources.Place(client, template, defaultRoot);
            template["d.as"].typeSource = NeoTypeSource.OfType(ListOf(Open("pair", "A"), required: false));
            Assert.That(NeoTypeSources.Violations(client, template, defaultRoot), Is.EqualTo(new[]
            {
                "Value \"d.as\" typeSource.type names param \"A\" of another class, but its template default belongs to class \"holder\".",
            }));

            var instance = new Graph(HolderRows(Int, Number("h.payload", 4)));
            NeoTypeSourcePosition? root = Root(client, "h", "anything");
            NeoTypeSources.Place(client, instance, root);
            instance["h.items"].typeSource = NeoTypeSource.OfType(ListOf(Open("holder", "T")));
            Assert.That(NeoTypeSources.Violations(client, instance, root), Is.EqualTo(new[]
            {
                "Value \"h.items\" typeSource.type is open but names no valueId outside a member default.",
            }));
        }

        // Compose's "template member defaults" case, through the SDK's
        // production default writer: a node with no row reads its member's
        // inline default, placed where the server's walk places it.
        [Test]
        public void MemberDefaultRootsTakeTheirDefaultsTypeSource()
        {
            using NeoClient client = BuildClient(data =>
            {
                ((ClassMember)data.members["p"]).defaultValue = new ObjectMemberValueBase
                {
                    classId = "pair",
                    value = new Dictionary<string, string> { ["As"] = "d.as" },
                };
                ((ListMember)data.members["items"]).defaultValue = new ArrayMemberValueBase { value = Array.Empty<string>() };
                ((ClassMember)data.members["closed"]).defaultValue = new ObjectMemberValueBase
                {
                    classId = "holder",
                    value = new Dictionary<string, string>(),
                };
            });
            Assert.That(
                DefaultRoot(client, "p").typeSource,
                Is.EqualTo(NeoTypeSource.OfType(TestTypeBindings.Class("pair", false, ("A", Open("holder", "T")), ("B", Int)))));
            Assert.That(DefaultRoot(client, "items").typeSource, Is.EqualTo(NeoTypeSource.OfType(ListOf(Open("holder", "T"), required: false))));

            // A closed default's type is closed.
            var closed = (NeoMemberClass)NeoMember.CreateWritable(client, MemberOf(client, "closed"), null, NeoValueOwnership.Session);
            Assert.That(closed.value!.typeSource, Is.EqualTo(NeoTypeSource.OfType(HolderOf(Int, required: false))));
            Assert.That(closed.value.genericBindings, Is.Null);
        }

        // ------------------------------------------------------------------
        // creation-time placement and one-hop readers
        // ------------------------------------------------------------------

        // A write places the rows it creates when it stages them, so a staged
        // entry reads its type before the plan commits.
        [Test]
        public void PlacesACreatedEntryWhenTheWriteStagesIt()
        {
            using NeoClient client = BuildClient();
            ObjectMemberValue holder = Obj("h", new()
            {
                ["Items"] = "h.items"
            }, "holder", Stamped(ListOf(Int)));
            ArrayMemberValue items = Arr("h.items");
            items.typeSource = NeoTypeSource.OfType(ListOf(Open("holder", "T")), "h");
            client.SetWritableValue(NeoValueOwnership.Session, holder);
            client.SetWritableValue(NeoValueOwnership.Session, items);

            var plan = new NeoWritePlan(client);
            ArrayMemberValue entry = Arr("e");
            plan.Set(NeoValueOwnership.Session, entry);
            NeoTypeSources.PlaceCreatedUnder(client, plan, "h.items", items, MemberOf(client, "items"), "e");
            Assert.That(entry.typeSource, Is.EqualTo(NeoTypeSource.AtPath("h", new[] { "T" })));
            Assert.That(entry.genericBindings, Is.Null);
        }

        [Test]
        public void ReadersRefuseARowThatStoresNeitherItsStampNorItsTypeSource()
        {
            using NeoClient client = BuildClient();
            var entries = Assert.Throws<InvalidOperationException>(() =>
                NeoTypeSources.EntryStamp(client, Arr("h.items"), MemberOf(client, "items")));
            Assert.That(entries!.Message, Is.EqualTo("Value \"h.items\" under member \"items\" is missing typeSource."));

            var owner = Assert.Throws<InvalidOperationException>(() =>
                NeoTypeSources.OwnStamp(client, "c", Obj("c", new(), "holder"), MemberOf(client, "closed"), _ => null));
            Assert.That(owner!.Message, Is.EqualTo("Class row \"c\" under member \"closed\" stores neither a stamp nor a typeSource."));
        }

        [Test]
        public void TypeSourceRoundTripsThroughItsConverter()
        {
            var row = new ArrayMemberValue
            {
                id = "v",
                value = Array.Empty<string>(),
                typeSource = NeoTypeSource.OfType(ListOf(Open("holder", "T")), "h"),
            };
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(row);
            var read = Newtonsoft.Json.JsonConvert.DeserializeObject<ArrayMemberValue>(json)!;
            Assert.That(read.typeSource, Is.EqualTo(row.typeSource));
            var path = new ArrayMemberValue { id = "w", value = Array.Empty<string>(), typeSource = NeoTypeSource.AtPath("h", new[] { "T", "0" }) };
            Assert.That(
                Newtonsoft.Json.JsonConvert.DeserializeObject<ArrayMemberValue>(Newtonsoft.Json.JsonConvert.SerializeObject(path))!.typeSource,
                Is.EqualTo(path.typeSource));
            var error = Assert.Throws<Newtonsoft.Json.JsonSerializationException>(() =>
                Newtonsoft.Json.JsonConvert.DeserializeObject<ArrayMemberValue>("{\"id\":\"x\",\"value\":[],\"typeSource\":{\"path\":[\"T\"]}}"));
            StringAssert.Contains("typeSource.path names no valueId.", error!.Message);
        }

        // ------------------------------------------------------------------
        // constructor arguments
        // ------------------------------------------------------------------

        [Test]
        public void PlacesAGenericConstructorArgumentUnderItsParameterClosedThroughTheRow()
        {
            using NeoClient client = BuildClient(WithConstructors);
            // Box<int>(List<T> items) in a declared Box<int> slot: the list
            // stores its type closed.
            var declared = Place(client, BoxRows(stamp: null), Root(client, "b", "box-closed"));
            Assert.That(declared["b"].TypeSource, Is.EqualTo(NeoTypeSource.OfType(TestTypeBindings.Class("box", false, ("T", Int)))));
            Assert.That(declared["b.items"].TypeSource, Is.EqualTo(NeoTypeSource.OfType(ListOf(Int))));

            // The same Box at a binding node: the list names the node's T.
            var bound = Place(client, BoxRows(Stamped(Int)), Root(client, "b", "anything"));
            Assert.That(bound["b"].StampParams, Is.EqualTo(new[] { "T" }));
            Assert.That(bound["b.items"].TypeSource, Is.EqualTo(NeoTypeSource.OfType(ListOf(Open("box", "T")), "b")));
        }

        [Test]
        public void PlacesABaseClassConstructorArgumentsFramesThroughItsExtendsBindings()
        {
            using NeoClient client = BuildClient(WithConstructors);
            var placed = Place(client, DirectionalRows(), Root(client, "d", "directional-slot"));
            // ThreeFrame closes Segment<F>'s F to int, so its rows need no
            // typeSource, but each Frames list, declared List<F>, stores its
            // closed type.
            foreach (string segment in new[] { "d.v", "d.h" })
            {
                Assert.That(placed[segment].StampParams, Is.Null);
                Assert.That(placed[segment].TypeSource, Is.Null);
                Assert.That(placed[$"{segment}.frames"].TypeSource, Is.EqualTo(NeoTypeSource.OfType(ListOf(Int, required: false))));
            }
            Assert.That(
                NeoTypeSources.Violations(client, new Graph(DirectionalRows().ToArray()), Root(client, "d", "directional-slot")),
                Is.EqualTo(new[]
                {
                    "Value \"d.h.frames\" stores no typeSource, but its declared type names a type parameter.",
                    "Value \"d.v.frames\" stores no typeSource, but its declared type names a type parameter.",
                }));
        }

        [Test]
        public void MakesAClassConstructorArgumentInAnInterfaceOrParamSlotABindingNode()
        {
            using NeoClient client = BuildClient(WithConstructors);
            var carried = Place(
                client,
                new MemberValue[]
                {
                    Constructed("c", "carrier", "carrier-ctor", new() { ["__arg_0__"] = "c.payload" }),
                    Obj("c.payload", new() { ["Items"] = "c.payload.items" }, "holder", Stamped(Int)),
                    Arr("c.payload.items"),
                },
                Root(client, "c", "carrier-slot"));
            Assert.That(carried["c.payload"].StampParams, Is.EqualTo(new[] { "T" }));
            Assert.That(carried["c.payload"].BindingClassId, Is.EqualTo("holder"));
            Assert.That(carried["c.payload.items"].TypeSource, Is.EqualTo(NeoTypeSource.OfType(ListOf(Open("holder", "T"), required: false), "c.payload")));

            // Wrap<Holder<int>>(W item): the param slot spells the stamp.
            var wrapped = Place(
                client,
                new MemberValue[]
                {
                    Constructed("w", "wrap", "wrap-ctor", new() { ["__arg_0__"] = "w.item" }),
                    Obj("w.item", new(), "holder"),
                },
                Root(client, "w", "wrap-slot"));
            Assert.That(wrapped["w.item"].StampParams, Is.EqualTo(new[] { "T" }));
            Assert.That(wrapped["w.item"].SpelledStamp, Is.EqualTo(Stamped(Int)));
        }

        [Test]
        public void PlacesAConstructionsConstructorArgumentRowsUnderItsOwnerlessRoot()
        {
            using NeoClient client = BuildClient(WithConstructors);
            MemberValue[] rows = BoxRows(Stamped(Int));
            Assert.That(NeoTypeSources.PlaceConstructed(client, (ObjectMemberValue)rows[0], rows), Is.Null);
            Assert.That(rows[1].typeSource, Is.EqualTo(NeoTypeSource.OfType(ListOf(Open("box", "T")), "b")));
        }

        [Test]
        public void PlacesARowAWriteLinksByItsFieldAloneNotByAConstructorArgument()
        {
            using NeoClient client = BuildClient(WithConstructors);
            ObjectMemberValue box = Constructed("b", "box", "box-ctor", new()
            {
                ["__arg_0__"] = "b.items"
            }, Stamped(Int));
            var plan = new NeoWritePlan(client);
            ArrayMemberValue items = Arr("b.items");
            plan.Set(NeoValueOwnership.Session, box);
            plan.Set(NeoValueOwnership.Session, items);

            // A write links a child into its parent's value; the parent's
            // constructor arguments are other rows.
            NeoTypeSources.PlaceCreatedUnder(client, plan, "b", box, MemberOf(client, "anything"), "b.items");
            Assert.That(items.typeSource, Is.Null);

            // The Items field, unlike the required parameter, is nullable.
            box.value!["Items"] = "b.items";
            NeoTypeSources.PlaceCreatedUnder(client, plan, "b", box, MemberOf(client, "anything"), "b.items");
            Assert.That(items.typeSource, Is.EqualTo(NeoTypeSource.OfType(ListOf(Open("box", "T"), required: false), "b")));
        }

        [Test]
        public void ObjectConstructorArgumentClassRowIsABindingNode()
        {
            using NeoClient client = BuildClient(data =>
            {
                WithConstructors(data);
                AddConstructor(data, "carrier-ctor", "carrier", new FunctionArgumentTypeInfo
                {
                    name = "payload",
                    type = MemberKind.Object,
                    required = true,
                });
            });
            var placed = Place(client, new MemberValue[]
            {
                Constructed("c", "carrier", "carrier-ctor", new() { ["__arg_0__"] = "h" }),
                Obj("h", new() { ["Items"] = "h.items" }, "holder", Stamped(Int)),
                Arr("h.items"),
            }, Root(client, "c", "carrier-slot"));
            Assert.That(placed["h"].BindingClassId, Is.EqualTo("holder"));
            Assert.That(placed["h"].StampParams, Is.EqualTo(new[] { "T" }));
            Assert.That(placed["h.items"].TypeSource,
                Is.EqualTo(NeoTypeSource.OfType(ListOf(Open("holder", "T"), false), "h")));
        }

        [TestCase(MemberKind.Int)]
        [TestCase(MemberKind.String)]
        [TestCase(MemberKind.Sprite)]
        public void ClosedNonRowGenericConstructorArgumentStaysInline(MemberKind kind)
        {
            using NeoClient client = BuildClient(WithConstructors);
            ObjectMemberValue root = Constructed("w", "wrap", "wrap-ctor", new()
            {
                ["__arg_0__"] = "not-a-row"
            },
                new()
                {
                    ["W"] = TestTypeBindings.Of(kind)
                });
            var placed = Place(client, new MemberValue[] { root, Arr("not-a-row") }, NeoTypeSources.OwnerlessPosition("w"));
            Assert.That(placed.ContainsKey("not-a-row"), Is.False);
            Assert.That(root.constructorArgs!["__arg_0__"]!.Value<string>(), Is.EqualTo("not-a-row"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InlineRowStoringConstructorArgumentIsRefused(bool generic)
        {
            using NeoClient client = BuildClient(WithConstructors);
            ObjectMemberValue root = generic
                ? Constructed("w", "wrap", "wrap-ctor", new(), new()
                {
                    ["W"] = ListOf(Int)
                })
                : Constructed("w", "box", "box-ctor", new(), Stamped(Int));
            root.constructorArgs!["__arg_0__"] = new JArray(1, 2);
            Assert.That(() => Place(client, new[] { root }, NeoTypeSources.OwnerlessPosition("w")),
                Throws.InvalidOperationException.With.Message.EqualTo(
                    "Value \"w\" stores constructor argument \"__arg_0__\" inline, but its type stores a row."));
        }

        /// <summary>
        /// A variant-class constructor argument stores its selection token
        /// <c>{classId, variantId, rowValueId}</c>. A selection naming a row
        /// places that row like any argument; one naming only an authored
        /// variant has no row to place. Any other class still stores a row.
        /// </summary>
        [TestCase("row")]
        [TestCase("rowless")]
        [TestCase("not-variant")]
        public void VariantSelectionConstructorArgumentPlacesOnlyTheRowItNames(string mode)
        {
            using NeoClient client = BuildClient(data =>
            {
                WithConstructors(data);
                NeoSchemaClass tint = Class("tint", "Tint", new());
                if (mode != "not-variant")
                    tint.system = new JObject { ["worldKind"] = "variant" };
                data.classes["tint"] = tint;
            });
            ObjectMemberValue root = Constructed("w", "wrap", "wrap-ctor", new(), new()
            {
                ["W"] = TestTypeBindings.Class("tint")
            });
            root.constructorArgs!["__arg_0__"] = new JObject
            {
                ["classId"] = "tint",
                ["variantId"] = "red",
                ["rowValueId"] = mode == "row" ? "w.item" : null,
            };
            var rows = new List<MemberValue> { root };
            if (mode == "row")
                rows.Add(Obj("w.item", new(), "tint"));
            if (mode == "not-variant")
            {
                Assert.That(() => Place(client, rows, NeoTypeSources.OwnerlessPosition("w")),
                    Throws.InvalidOperationException.With.Message.EqualTo(
                        "Value \"w\" stores constructor argument \"__arg_0__\" inline, but its type stores a row."));
                return;
            }
            var placed = Place(client, rows, NeoTypeSources.OwnerlessPosition("w"));
            Assert.That(placed.Keys, Is.EquivalentTo(mode == "row" ? new[] { "w", "w.item" } : new[] { "w" }));
        }

        [Test]
        public void ConstructorParameterIdUsesActionId()
        {
            using NeoClient client = BuildClient(WithConstructors);
            client.constructors["box-ctor"].action.parameters[2].id = "items-param";
            var root = Constructed("b", "box", "box-ctor", new()
            {
                ["items-param"] = "items"
            }, Stamped(Int));
            var placed = Place(client, new MemberValue[] { root, Arr("items") }, NeoTypeSources.OwnerlessPosition("b"));
            Assert.That(placed["items"].TypeSource, Is.EqualTo(NeoTypeSource.OfType(ListOf(Open("box", "T")), "b")));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TupleConstructorArgumentWritesRowsAndReplaysFromSerializedRows(bool generic)
        {
            ProjectData schema = null!;
            using (NeoClient client = BuildClient(data =>
            {
                schema = data;
                WithConstructors(data);
                var pair = new FunctionArgumentTypeInfo
                {
                    name = "pair",
                    type = MemberKind.Tuple,
                    required = true,
                    elements = new[]
                    {
                        new TupleElementTypeInfo { typeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true } },
                        new TupleElementTypeInfo { typeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true } },
                    },
                };
                data.classes["pairy"] = Class("pairy", "Pairy", new()
                {
                    ["Pair"] = "pair-field"
                }, generic ? new[] { "P" } : Array.Empty<string>());
                data.members["pair-field"] = generic
                    ? Generic("pair-field", "P", "Pair")
                    : new TupleMember
                    {
                        id = "pair-field",
                        projectId = ProjectId,
                        name = "Pair",
                        kind = MemberKind.Tuple,
                        elementMemberIds = new[] { "pair-int", "pair-string" },
                    };
                data.members["pair-int"] = new IntMember { id = "pair-int", projectId = ProjectId, name = "Int", kind = MemberKind.Int, Requirement = NeoMemberRequirementKind.Required };
                data.members["pair-string"] = new StringMember { id = "pair-string", projectId = ProjectId, name = "String", kind = MemberKind.String, Requirement = NeoMemberRequirementKind.Required };
                var parameter = generic ? new FunctionArgumentTypeInfo
                {
                    name = "pair",
                    type = MemberKind.Generic,
                    required = true,
                    ownerClassId = "pairy",
                    genericParamId = "P",
                } : pair;
                AddConstructor(data, "pairy-ctor", "pairy", parameter);
                data.constructors["pairy-ctor"].action.instructions = new[] { AssignArgument("Pair", parameter) };
                data.members["pairy-slot"] = ClassSlot("pairy-slot", "Pairy", "pairy", generic
                    ? new()
                    {
                        ["P"] = NeoTypeBindings.ArgumentOf(pair)
                    } : null);
                data.classes["root-class"].schema["Pairy"] = "pairy-slot";
            }))
            {
                var arguments = ((ClassMember)schema.members["pairy-slot"]).classArguments;
                var created = NeoGeneratedTypesSupport.EvaluateStoredDeclaredConstructor(client, "pairy", "pairy-ctor",
                    new[] { new NeoDeclaredConstructorArgument("pair", (1, "a")) }, arguments, arguments);
                string tupleId = created.value!.constructorArgs!["__arg_0__"]!.Value<string>()!;
                Assert.That(client.TryGetValue(tupleId, out ArrayMemberValue? tuple), Is.True);
                Assert.That(tuple!.value, Has.Length.EqualTo(2));
                // Keep only the recipe and its inputs. No materialized field can mask a replay defect.
                var root = Constructed("stored-pairy", "pairy", "pairy-ctor", new()
                {
                    ["__arg_0__"] = tupleId
                });
                if (generic)
                    root.typeSource = NeoTypeSource.OfType(TestTypeBindings.Class("pairy", false, ("P", arguments!["P"])));
                schema.values[root.id] = root;
                schema.values[tupleId] = tuple;
                foreach (string id in tuple.value!)
                {
                    Assert.That(client.TryGetValue(id, out MemberValue? element), Is.True);
                    schema.values[id] = element!;
                }
                if (generic)
                    tuple.typeSource = NeoTypeSource.OfType(arguments!["P"]);
                ((ObjectMemberValue)schema.values["root-save-value"]).value!["Pairy"] = root.id;
            }
            string serialized = Newtonsoft.Json.JsonConvert.SerializeObject(schema.values);
            schema.values = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, MemberValue>>(serialized)!;
            using NeoClient reopened = NeoTestSaveStack.ClientFromSchema(schema);
            var pairNode = reopened.save.Get<NeoMemberClassWritable>("Pairy").Get<NeoMemberTuple>("Pair");
            var result = (NeoCompose.Runtime.NeoScript.RuntimeTuple)NeoGeneratedTypesSupport.ReadTupleValue(pairNode)!;
            Assert.That(result[0], Is.EqualTo(1));
            Assert.That(result[1], Is.EqualTo("a"));
        }

        [Test]
        public void ConstructorOnlyClassRowReadsBoxListIntEnvironmentFromTypeSource()
        {
            using NeoClient client = BuildClient(data =>
            {
                WithConstructors(data);
                data.constructors["box-ctor"].action.instructions = new[] { AssignArgument("Items", data.constructors["box-ctor"].argumentTypes[0]) };
                AddConstructor(data, "carrier-ctor", "carrier", new FunctionArgumentTypeInfo
                {
                    name = "payload",
                    type = MemberKind.Class,
                    required = true,
                    classId = "box",
                    typeArguments = new() { ["T"] = new CollectionTypeInfo { type = MemberKind.List, required = true, entryTypeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true } } },
                });
                data.classes["root-class"].schema["Carrier"] = "carrier-slot";
                var carrier = Constructed("c", "carrier", "carrier-ctor", new()
                {
                    ["__arg_0__"] = "b"
                });
                var box = Constructed("b", "box", "box-ctor", new()
                {
                    ["__arg_0__"] = "items"
                });
                box.typeSource = NeoTypeSource.OfType(TestTypeBindings.Class("box", true, ("T", ListOf(Int))));
                var items = Arr("items", "inner");
                items.typeSource = NeoTypeSource.OfType(ListOf(ListOf(Int)));
                data.values["c"] = carrier;
                data.values["b"] = box;
                data.values["items"] = items;
                data.values["inner"] = Arr("inner", "number");
                data.values["number"] = Number("number", 7);
                ((ObjectMemberValue)data.values["root-save-value"]).value!["Carrier"] = "c";
            });
            Assert.That(client.TryGetValue("b", out ObjectMemberValue? boxRow), Is.True);
            Assert.That(boxRow!.genericBindings, Is.Null);
            var ctx = new NeoCompose.Runtime.NeoScript.NSGetterEvaluator.Context(client, null, null, NeoValueOwnership.Save);
            object receiver = NeoCompose.Runtime.NeoScript.NSGetterEvaluator.UnwrapRow(boxRow, ctx, NeoValueOwnership.Save)!;
            var env = NeoNSFunctionRuntime.ResolveReceiverGenericEnv(client, receiver, ctx, "Box argument");
            Assert.That(NeoGenericResolution.SubstituteTypeInfo(client,
                new GenericTypeInfo { type = MemberKind.Generic, ownerClassId = "box", genericParamId = "T", required = true }, env).type,
                Is.EqualTo(MemberKind.List));
            using var box = new NeoMemberClassWritable(client, ClassSlot("box-view", "Box", "box", null), "b", NeoValueOwnership.Save);
            var list = box.Get<NeoMemberList>("Items");
            Assert.That(((NeoMemberInt)((NeoMemberList)list[0])[0]).value!.value, Is.EqualTo(7));
        }

        [TestCase(MemberKind.Class)]
        [TestCase(MemberKind.Interface)]
        [TestCase(MemberKind.Object)]
        public void ConstructorRejectsNonNullAggregateWithoutBackingRowAtWriteTime(MemberKind kind)
        {
            using NeoClient client = BuildClient(data =>
            {
                WithConstructors(data);
                AddConstructor(data, "carrier-ctor", "carrier", new FunctionArgumentTypeInfo
                {
                    name = "payload",
                    type = kind,
                    required = true,
                    classId = kind == MemberKind.Class ? "holder" : null,
                    typeArguments = kind == MemberKind.Class ? new() { ["T"] = new PrimitiveTypeInfo { type = MemberKind.Int, required = true } } : null,
                    interfaceId = kind == MemberKind.Interface ? "iholder" : null,
                });
            });
            var resolved = ResolveCarrier(client);
            var ctx = new NeoCompose.Runtime.NeoScript.NSGetterEvaluator.Context(client, null, null, NeoValueOwnership.Session);
            int before = client.sessionValues.Count;
            Exception error = Assert.Catch(() => NeoGeneratedTypesSupport.ConstructDeclaredClassValueData(
                resolved, new object?[] { "abc" }, Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>(), ctx))!;
            Assert.That(error.Message, Is.EqualTo("Constructor aggregate argument '__arg_0__' has no backing value row."));
            Assert.That(client.sessionValues.Count, Is.EqualTo(before));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DetachedLiteralTuplePublishesOnlyWhenAttached(bool attach)
        {
            var tupleType = new TupleTypeInfo
            {
                type = MemberKind.Tuple,
                required = true,
                elements = new[]
                {
                    new TupleElementTypeInfo { typeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true } },
                    new TupleElementTypeInfo { typeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true } },
                },
            };
            using NeoClient client = BuildClient(data =>
            {
                WithConstructors(data);
                AddConstructor(data, "carrier-ctor", "carrier", new FunctionArgumentTypeInfo
                {
                    name = "payload",
                    type = MemberKind.Tuple,
                    required = true,
                    elements = tupleType.elements,
                });
            });
            int before = client.sessionValues.Count;
            var ctx = new NeoCompose.Runtime.NeoScript.NSGetterEvaluator.Context(client, null, null, NeoValueOwnership.Session);
            var detached = NeoGeneratedTypesSupport.ConstructDetachedDeclared(ResolveCarrier(client),
                NeoGeneratedTypesSupport.ResolveDetachedClassPlan(client, "carrier")!,
                new object?[] { new NeoCompose.Runtime.NeoScript.RuntimeTuple(new object?[] { 7, "a" }, tupleType) },
                Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>(), ctx, null);
            Assert.That(detached.attachedId, Is.Null);
            Assert.That(client.sessionValues.Count, Is.EqualTo(before), "Recording a tuple must not publish its snapshot.");
            if (!attach)
                return;
            string rootId = NeoGeneratedTypesSupport.MaterializeDetached(detached, ctx, ctx);
            Assert.That(client.TryGetValue(rootId, out ObjectMemberValue? root), Is.True);
            string tupleId = root!.constructorArgs!["__arg_0__"]!.Value<string>()!;
            Assert.That(client.TryGetValue(tupleId, out ArrayMemberValue? tuple), Is.True);
            Assert.That(tuple!.value, Has.Length.EqualTo(2));
            Assert.That(client.sessionValues.Count, Is.EqualTo(before + 4));
        }

        [Test]
        public void TupleConstructorArgumentChecksSingleOwnershipBeforePublishing()
        {
            var classType = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = "three" };
            var tupleType = new TupleTypeInfo
            {
                type = MemberKind.Tuple,
                required = true,
                elements = new[]
                {
                    new TupleElementTypeInfo { typeInfo = classType },
                    new TupleElementTypeInfo { typeInfo = classType },
                },
            };
            using NeoClient client = BuildClient(data =>
            {
                WithConstructors(data);
                AddConstructor(data, "carrier-ctor", "carrier", new FunctionArgumentTypeInfo
                {
                    name = "payload",
                    type = MemberKind.Tuple,
                    required = true,
                    elements = tupleType.elements,
                });
            });
            using var child = NeoGeneratedTypesSupport.CreateWritableClassValue(client, "three", new(), Array.Empty<MemberValue>());
            var ctx = new NeoCompose.Runtime.NeoScript.NSGetterEvaluator.Context(client, null, null, NeoValueOwnership.Session);
            object value = NeoCompose.Runtime.NeoScript.NSGetterEvaluator.UnwrapRow(child.value!, ctx, NeoValueOwnership.Session)!;
            var tuple = new NeoCompose.Runtime.NeoScript.RuntimeTuple(new[] { value, value }, tupleType);
            int before = client.sessionValues.Count;
            Exception error = Assert.Catch(() => NeoGeneratedTypesSupport.ConstructDeclaredClassValueData(
                ResolveCarrier(client), new object?[] { tuple },
                Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>(), ctx))!;
            StringAssert.Contains("would have two owned parents", error.Message);
            Assert.That(client.sessionValues.Count, Is.EqualTo(before));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ConstructorRecordsArgumentWithoutSecondFieldPlacementWalk(bool aggregate)
        {
            using NeoClient client = BuildClient(data =>
            {
                WithConstructors(data);
                if (aggregate)
                    data.classes["carrier"].genericParams = new List<GenericParamDeclaration>
                    {
                        new() { id = "T", name = "T" },
                    };
                AddConstructor(data, "carrier-ctor", "carrier", new FunctionArgumentTypeInfo
                {
                    name = "payload",
                    type = aggregate ? MemberKind.List : MemberKind.Int,
                    required = true,
                    entryTypeInfo = aggregate ? new GenericTypeInfo
                    {
                        type = MemberKind.Generic,
                        required = true,
                        ownerClassId = "carrier",
                        genericParamId = "T",
                    } : null,
                });
                data.classes["carrier"].schema["Child"] = "carrier-child";
                data.members["carrier-child"] = ClassSlot("carrier-child", "Child", "three", null);
            });
            object input = aggregate ? new object[] { 7 } : 7;
            var arguments = aggregate ? Stamped(Int) : null;
            var created = NeoGeneratedTypesSupport.EvaluateStoredDeclaredConstructor(client, "carrier", "carrier-ctor",
                new[] { new NeoDeclaredConstructorArgument("payload", input) }, arguments, arguments);
            JToken argument = created.value!.constructorArgs!["__arg_0__"]!;
            ArrayMemberValue? argumentRow = null;
            if (aggregate)
            {
                Assert.That(client.TryGetValue(argument.Value<string>()!, out argumentRow), Is.True);
                // Remove its first placement so re-recording must actively place the argument.
                argumentRow!.typeSource = null;
                client.SetWritableValue(NeoValueOwnership.Session, argumentRow);
            }
            else
            {
                Assert.That(argument.Value<int>(), Is.EqualTo(7));
                argument = new JValue(9);
            }
            // A trap in an unrelated field proves this path never visits it.
            // A root walk would try to resolve this missing class and throw.
            var child = Obj("placement-trap", new(), "missing-class");
            client.SetWritableValue(NeoValueOwnership.Session, child);
            created.value.value!["Child"] = child.id;
            Assert.DoesNotThrow(() => client.StampLiveConstructionProvenance(created.value, "carrier-ctor",
                new Dictionary<string, JToken?> { ["__arg_0__"] = argument }));
            Assert.That(created.value.constructorArgs!["__arg_0__"], Is.EqualTo(argument));
            if (aggregate)
            {
                Assert.That(client.TryGetValue(argumentRow!.id, out argumentRow), Is.True);
                Assert.That(argumentRow!.typeSource,
                    Is.EqualTo(NeoTypeSource.OfType(ListOf(Open("carrier", "T")), created.value.id)));
            }
            Assert.That(child.classId, Is.EqualTo("missing-class"));
        }

        [Test]
        public void LiveInterfaceConstructorArgumentKeepsConcreteFieldPlacement()
        {
            using NeoClient client = BuildClient(data =>
            {
                WithConstructors(data);
                data.classes["holder"].implementsInterfaces = new List<InterfaceConstruction>
                {
                    new() { interfaceId = "iholder" },
                };
                data.classes["carrier"].schema["Child"] = "carrier-child";
                data.members["carrier-child"] = ClassSlot("carrier-child", "Child", "holder",
                    TestTypeBindings.Arguments(("T", Int)));
            });
            using var created = NeoGeneratedTypesSupport.CreateWritableClassValue(client, "carrier",
                new()
                {
                    ["Child"] = "h"
                }, HolderRows(Int, Number("h.payload", 7)));
            var expected = NeoTypeSource.OfType(HolderOf(Int, required: false));
            Assert.That(client.TryGetValue("h", out ObjectMemberValue? child), Is.True);
            Assert.That(child!.typeSource, Is.EqualTo(expected));
            Assert.That(child.genericBindings, Is.Null);

            // A second indexed parent refers to the same row only through its recipe.
            // Neither that recipe nor the live interface argument may replace the field placement.
            client.SetWritableValue(NeoValueOwnership.Session,
                Constructed("other-carrier", "carrier", "carrier-ctor", new()
                {
                    ["__arg_0__"] = "h"
                }));
            client.StampLiveConstructionProvenance(created.value!, "carrier-ctor",
                new Dictionary<string, JToken?> { ["__arg_0__"] = new JValue("h") });

            Assert.That(created.value!.constructorArgs!["__arg_0__"]!.Value<string>(), Is.EqualTo("h"));
            Assert.That(created.value.value!["Child"], Is.EqualTo("h"));
            Assert.That(client.TryGetValue("h", out child), Is.True);
            Assert.That(child!.typeSource, Is.EqualTo(expected));
            Assert.That(child.genericBindings, Is.Null);
        }

        [Test]
        public void CandidateInterfaceConstructorArgumentKeepsConcreteFieldPlacement()
        {
            using NeoClient client = BuildClient(data =>
            {
                WithConstructors(data);
                data.classes["holder"].implementsInterfaces = new List<InterfaceConstruction>
                {
                    new() { interfaceId = "iholder" },
                };
                data.classes["carrier"].schema["Child"] = "carrier-child";
                data.members["carrier-child"] = ClassSlot("carrier-child", "Child", "holder",
                    TestTypeBindings.Arguments(("T", Int)));
            });
            MemberValue[] rows = HolderRows(Int, Number("h.payload", 7));
            NeoTypeSources.PlaceConstructed(client, (ObjectMemberValue)rows[0], rows);
            client.SetWritableValues(NeoValueOwnership.Session, rows);
            var root = Constructed("carrier", "carrier", "carrier-ctor", new()
            {
                ["__arg_0__"] = "h"
            });
            client.SetWritableValue(NeoValueOwnership.Session, root);

            var plan = new NeoWritePlan(client);
            var parent = Obj("field-parent", new()
            {
                ["Child"] = "h"
            }, "carrier");
            plan.Set(NeoValueOwnership.Session, parent);
            NeoTypeSources.PlaceCreatedUnder(client, plan, parent.id, parent, MemberOf(client, "carrier-slot"), "h");

            Assert.That(client.TryGetValue(parent.id, out ObjectMemberValue? _), Is.False);
            using (client.ReadCandidate(plan))
            {
                Assert.That(client.TryGetValue("h", out ObjectMemberValue? child), Is.True);
                Assert.That(child!.typeSource, Is.EqualTo(NeoTypeSource.OfType(HolderOf(Int, required: false))));
                Assert.That(child.genericBindings, Is.Null);
                Assert.That(NeoTypeSources.PlaceConstructorArguments(client, root), Is.Null);
            }
        }

        [Test]
        public void TemplateDefaultCloneRemapsOpenGenericConstructorArgumentInDestinationEnvironment()
        {
            using NeoClient client = BuildClient(data =>
            {
                WithConstructors(data);
                // Holder<T>.P has a template default containing Box<T> B.
                data.classes["pair"].schema["B"] = "template-b-slot";
                data.members["template-b-slot"] = ClassSlot("template-b-slot", "B", "box",
                    TestTypeBindings.Arguments(("T", Open("pair", "A"))));
                data.members["box-items"] = Generic("box-items", "T", "Items");
                AddConstructor(data, "box-ctor", "box", new FunctionArgumentTypeInfo
                {
                    name = "items",
                    type = MemberKind.Generic,
                    required = true,
                    ownerClassId = "box",
                    genericParamId = "T",
                });
                ((ClassMember)data.members["p"]).defaultValue = new ObjectMemberValueBase
                {
                    classId = "pair",
                    value = new() { ["B"] = "template-b" },
                };
                var box = Constructed("template-b", "box", "box-ctor", new()
                {
                    ["__arg_0__"] = "template-items"
                });
                box.value!["Items"] = "template-items";
                box.typeSource = NeoTypeSource.OfType(TestTypeBindings.Class("box", false, ("T", Open("holder", "T"))));
                var items = Arr("template-items");
                items.typeSource = NeoTypeSource.OfType(Open("holder", "T"));
                data.values[box.id] = box;
                data.values[items.id] = items;
            });
            var arguments = Stamped(ListOf(Int));
            var holder = NeoGeneratedTypesSupport.EvaluateStoredDeclaredConstructor(client, "holder", null,
                Array.Empty<NeoDeclaredConstructorArgument>(), arguments, arguments);
            using var holderWrapper = new NeoMemberClassWritable(client, holder.member, holder.value.id, NeoValueOwnership.Session);
            var box = holderWrapper.Get<NeoMemberClassWritable>("P").Get<NeoMemberClassWritable>("B");
            string clonedInput = box.value!.constructorArgs!["__arg_0__"]!.Value<string>()!;
            Assert.That(clonedInput, Is.Not.EqualTo("template-items"));
            Assert.That(clonedInput, Is.EqualTo(box.value.value!["Items"]));
            Assert.That(client.TryGetValue(clonedInput, out ArrayMemberValue? clonedItems), Is.True);
            Assert.That(clonedItems!.typeSource, Is.EqualTo(NeoTypeSource.AtPath(holder.value!.id, new[] { "T" })));
            Assert.That(client.TryGetValue("template-b", out ObjectMemberValue? template), Is.True);
            Assert.That(template!.constructorArgs!["__arg_0__"]!.Value<string>(), Is.EqualTo("template-items"));
        }

        private static NeoGeneratedTypesSupport.NeoResolvedDeclaredConstructor ResolveCarrier(NeoClient client) =>
            NeoGeneratedTypesSupport.ResolveDeclaredConstructor(client,
                new ClassTypeInfo { type = MemberKind.Class, required = true, classId = "carrier" },
                "carrier-ctor", new[] { "payload" }, Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>());

        private static AssignInstruction AssignArgument(string field, NeoCompose.Runtime.Json.TypeInfo type) => new()
        {
            type = InstructionKind.Assign,
            operatorValue = "=",
            target = new WriteTarget
            {
                writability = WritabilityKind.Session,
                typeInfo = type,
                pointer = new KeyOfPointer
                {
                    type = PointerKind.KeyOf,
                    keyOf = new KeyOf
                    {
                        pointer = new VariablePointer { type = PointerKind.Variable, variableId = "__this__" },
                        key = new ValuePointer { type = PointerKind.Value, value = new Value { typeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true }, value = new JValue(field) } },
                    },
                },
            },
            pointer = new VariablePointer { type = PointerKind.Variable, variableId = "__arg_0__" },
        };

        // class Box<T> { List<T> Items; Box(List<T> items) }
        // class Segment<F> { List<F> Frames; }  class ThreeFrame : Segment<int>
        // class Directional { Directional(ThreeFrame vertical, ThreeFrame horizontal) }
        // interface IHolder {}  class Carrier { Carrier(IHolder payload) }  class Wrap<W> { Wrap(W item) }
        // members: Box<int> BoxClosed; Directional DirectionalSlot; Carrier CarrierSlot; Wrap<Holder<int>> WrapSlot;
        private static void WithConstructors(ProjectData data)
        {
            data.members["box-items"] = List("box-items", "Items", "items-entry-box");
            data.members["items-entry-box"] = Generic("items-entry-box", "T");
            data.members["frames"] = List("frames", "Frames", "frames-entry");
            data.members["frames-entry"] = Generic("frames-entry", "F");
            data.members["box-closed"] = ClassSlot("box-closed", "BoxClosed", "box", TestTypeBindings.Arguments(("T", Int)));
            data.members["directional-slot"] = ClassSlot("directional-slot", "DirectionalSlot", "directional", null);
            data.members["carrier-slot"] = ClassSlot("carrier-slot", "CarrierSlot", "carrier", null);
            data.members["wrap-slot"] = ClassSlot("wrap-slot", "WrapSlot", "wrap", TestTypeBindings.Arguments(("W", HolderOf(Int))));
            data.classes["box"] = Class("box", "Box", new()
            {
                ["Items"] = "box-items"
            }, "T");
            data.classes["segment"] = Class("segment", "Segment", new()
            {
                ["Frames"] = "frames"
            }, "F");
            NeoSchemaClass three = Class("three", "ThreeFrame", new());
            three.extendsClassId = "segment";
            three.extendsGenericBindings = TestTypeBindings.Arguments(("F", Int));
            data.classes["three"] = three;
            data.classes["directional"] = Class("directional", "Directional", new());
            data.classes["carrier"] = Class("carrier", "Carrier", new());
            data.classes["wrap"] = Class("wrap", "Wrap", new(), "W");
            AddConstructor(data, "box-ctor", "box", new FunctionArgumentTypeInfo
            {
                name = "items",
                type = MemberKind.List,
                required = true,
                entryTypeInfo = new GenericTypeInfo { type = MemberKind.Generic, required = true, ownerClassId = "box", genericParamId = "T" },
            });
            FunctionArgumentTypeInfo Segment(string name) => new()
            {
                name = name,
                type = MemberKind.Class,
                required = true,
                classId = "three"
            };
            AddConstructor(data, "directional-ctor", "directional", Segment("vertical"), Segment("horizontal"));
            data.interfaces["iholder"] = new Interface
            {
                id = "iholder",
                projectId = ProjectId,
                name = "IHolder",
                members = new Dictionary<string, InterfaceMember>(),
                createdAt = "x",
                updatedAt = "x",
            };
            AddConstructor(data, "carrier-ctor", "carrier", new FunctionArgumentTypeInfo
            {
                name = "payload",
                type = MemberKind.Interface,
                required = true,
                interfaceId = "iholder",
            });
            AddConstructor(data, "wrap-ctor", "wrap", new FunctionArgumentTypeInfo
            {
                name = "item",
                type = MemberKind.Generic,
                required = true,
                ownerClassId = "wrap",
                genericParamId = "W",
            });
        }

        private static ConstructorRecord Constructor(string id, string classId, params FunctionArgumentTypeInfo[] arguments) => new()
        {
            id = id,
            projectId = ProjectId,
            classId = classId,
            argumentTypes = arguments,
            code = "",
            action = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = arguments
                    .Select((argument, index) => new Variable { id = $"__arg_{index}__", typeInfo = argument })
                    .Prepend(new Variable { id = "__root__", typeInfo = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = "root-class" } })
                    .Prepend(new Variable { id = "__this__", typeInfo = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = classId } })
                    .ToArray(),
                typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                instructions = Array.Empty<Instruction>(),
            },
        };

        private static void AddConstructor(ProjectData data, string id, string classId, params FunctionArgumentTypeInfo[] arguments)
        {
            data.constructors[id] = Constructor(id, classId, arguments);
            data.classes[classId].constructorIds = new[] { id };
        }

        private static ObjectMemberValue Constructed(
            string id,
            string classId,
            string constructorId,
            Dictionary<string, string> arguments,
            Dictionary<string, TypeBinding>? stamp = null)
        {
            ObjectMemberValue row = Obj(id, new(), classId, stamp);
            row.instanceConstructorId = constructorId;
            row.constructorArgs = arguments.ToDictionary(pair => pair.Key, pair => (JToken?)new JValue(pair.Value));
            return row;
        }

        /// <summary>A Box whose Items only its constructor argument names.</summary>
        private static MemberValue[] BoxRows(Dictionary<string, TypeBinding>? stamp) => new MemberValue[]
        {
            Constructed("b", "box", "box-ctor", new() { ["__arg_0__"] = "b.items" }, stamp),
            Arr("b.items"),
        };

        private static IEnumerable<MemberValue> DirectionalRows() => new MemberValue[]
        {
            Constructed("d", "directional", "directional-ctor", new() { ["__arg_0__"] = "d.v", ["__arg_1__"] = "d.h" }),
            Obj("d.v", new() { ["Frames"] = "d.v.frames" }, "three"),
            Arr("d.v.frames"),
            Obj("d.h", new() { ["Frames"] = "d.h.frames" }, "three"),
            Arr("d.h.frames"),
        };

        // ------------------------------------------------------------------
        // fixture
        // ------------------------------------------------------------------

        // class Holder<T> { T Payload; List<T> Items; Pair<T, int> P; }
        // class Pair<A, B> { List<A> As; }
        // class Plain { Holder<int> Closed; }
        // members: object Anything; Plain Root;
        private static NeoClient BuildClient(Action<ProjectData>? edit = null)
        {
            var data = new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    _id = ProjectId,
                    name = "Type Sources",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = new Dictionary<string, JsonMember>
                {
                    ["root-assets"] = RootMember("root-assets", "root-assets-value"),
                    ["root-save"] = RootMember("root-save", "root-save-value"),
                    ["root-session"] = RootMember("root-session", "root-session-value"),
                    ["payload"] = Generic("payload", "T", "Payload"),
                    ["items-entry"] = Generic("items-entry", "T"),
                    ["items"] = List("items", "Items", "items-entry"),
                    ["p"] = ClassSlot("p", "P", "pair", TestTypeBindings.Arguments(("A", Open("holder", "T")), ("B", Int))),
                    ["as-entry"] = Generic("as-entry", "A"),
                    ["as"] = List("as", "As", "as-entry"),
                    ["closed"] = ClassSlot("closed", "Closed", "holder", TestTypeBindings.Arguments(("T", Int))),
                    ["anything"] = new ObjectMember { id = "anything", projectId = ProjectId, name = "Anything", kind = MemberKind.Object },
                    ["root"] = ClassSlot("root", "Root", "plain", null),
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["root-assets-value"] = Obj("root-assets-value", new(), "root-class"),
                    ["root-save-value"] = Obj("root-save-value", new(), "root-class"),
                    ["root-session-value"] = Obj("root-session-value", new(), "root-class"),
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    ["root-class"] = Class("root-class", "Root", new()),
                    ["holder"] = Class("holder", "Holder", new() { ["Payload"] = "payload", ["Items"] = "items", ["P"] = "p" }, "T"),
                    ["pair"] = Class("pair", "Pair", new() { ["As"] = "as" }, "A", "B"),
                    ["plain"] = Class("plain", "Plain", new() { ["Closed"] = "closed" }),
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            }.WithUserRoot();
            edit?.Invoke(data);
            return NeoTestSaveStack.ClientFromSchema(data);
        }

        private static ClassMember RootMember(string id, string valueId) => new()
        {
            id = id,
            projectId = ProjectId,
            name = id,
            kind = MemberKind.Class,
            Requirement = NeoMemberRequirementKind.Required,
            valueId = valueId,
            DeclaredClassId = "root-class",
        };

        private static GenericMember Generic(string id, string param, string name = "Entry") => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            kind = MemberKind.Generic,
            genericParamId = param,
        };

        private static ListMember List(string id, string name, string entryMemberId) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            kind = MemberKind.List,
            entryMemberId = entryMemberId,
        };

        private static ClassMember ClassSlot(string id, string name, string classId, Dictionary<string, TypeBinding>? arguments) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            kind = MemberKind.Class,
            DeclaredClassId = classId,
            classArguments = arguments,
        };

        private static NeoSchemaClass Class(string id, string name, Dictionary<string, string> schema, params string[] parameters) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            schema = schema,
            genericParams = parameters.Length == 0
                ? null
                : parameters.Select(param => new GenericParamDeclaration { id = param, name = param }).ToList(),
        };

        private static JsonMember MemberOf(NeoClient client, string id) =>
            client.TryGetMember(id, out JsonMember? member) ? member : throw new ArgumentException($"No member {id}.");

        private static MemberValue DefaultRoot(NeoClient client, string memberId)
        {
            JsonMember member = MemberOf(client, memberId);
            MemberValue root = MemberValueFactory.CreateFromDefault(member, "d", default, default)!;
            root.typeSource = NeoTypeSources.DefaultRootTypeSource(client, member, root);
            return root;
        }

        private static NeoTypeSourcePosition? Root(NeoClient client, string valueId, string memberId) =>
            NeoTypeSources.RootPosition(client, valueId, MemberOf(client, memberId));

        private static ObjectMemberValue Obj(
            string id,
            Dictionary<string, string> value,
            string? classId = null,
            Dictionary<string, TypeBinding>? stamp = null) => new()
            {
                id = id,
                classId = classId,
                genericBindings = stamp,
                value = value,
            };

        private static ArrayMemberValue Arr(string id, params string[] entries) => new() { id = id, value = entries };

        private static ArrayMemberValue Arr(string id, Dictionary<string, TypeBinding> stamp) => new()
        {
            id = id,
            value = Array.Empty<string>(),
            genericBindings = stamp,
        };

        private static NumberMemberValue Number(string id, double value) => new() { id = id, value = value };

        /// <summary>A Holder row in the <c>object</c> member, with every field present.</summary>
        private static MemberValue[] HolderRows(TypeBinding t, MemberValue payload) => new MemberValue[]
        {
            Obj("h", new() { ["Payload"] = "h.payload", ["Items"] = "h.items", ["P"] = "h.p" }, "holder", Stamped(t)),
            payload,
            Arr("h.items"),
            Obj("h.p", new() { ["As"] = "h.p.as" }),
            Arr("h.p.as"),
        };

        private static Dictionary<string, NeoTypeSourceRequirement> Place(
            NeoClient client,
            IEnumerable<MemberValue> rows,
            NeoTypeSourcePosition? position)
        {
            var placed = new Dictionary<string, NeoTypeSourceRequirement>(StringComparer.Ordinal);
            NeoTypeSources.Walk(client, new Graph(rows.ToArray()), position, requirement => placed[requirement.RowId] = requirement);
            return placed;
        }

        private sealed class Graph : INeoTypeSourceGraph
        {
            private readonly Dictionary<string, MemberValue> rows = new(StringComparer.Ordinal);

            internal Graph(params MemberValue[] rows)
            {
                foreach (MemberValue row in rows)
                    this.rows[row.id] = row;
            }

            internal MemberValue this[string id] => rows[id];

            public MemberValueBase? Row(string valueId) => rows.TryGetValue(valueId, out MemberValue? row) ? row : null;

            public IEnumerable<string> UnorderedEntryIds(string containerId) => Enumerable.Empty<string>();
        }
    }
}
