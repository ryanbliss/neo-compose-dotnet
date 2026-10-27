// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P127 §1.2. A Lookup or class slot with a closed generic class type
    /// judges a row by its closed type: its class, closed by the slot it's
    /// placed in, or with its own params as wildcards when that slot leaves
    /// it open (the server's <c>closedClassTargetViolation</c>). The runtime
    /// doesn't judge an object target (the TypeScript runtime's
    /// <c>assertReferenceFits</c>). <c>DerivedBox&lt;T&gt; : Box&lt;T&gt;</c>,
    /// <c>SeedBox : Box&lt;Seed&gt;</c>, <c>DerivedPair&lt;T, U&gt; : Box&lt;T&gt;</c>
    /// and <c>Weird&lt;T&gt; : Box&lt;Tool&gt;</c>. <c>Boxed</c>, a
    /// <c>DerivedBox&lt;Seed&gt;</c> slot, and <c>Loose</c>, a
    /// <c>Box&lt;Seed&gt;</c> slot, both close their <c>DerivedBox</c> row as
    /// <c>DerivedBox&lt;Seed&gt;</c>; <c>Paired</c>, a <c>Box&lt;Seed&gt;</c>
    /// slot, leaves its <c>DerivedPair</c> row's <c>U</c> open, and
    /// <c>ToolBoxed</c>, a <c>Box&lt;Tool&gt;</c> slot, its <c>Weird</c> row's
    /// <c>T</c>.
    /// </summary>
    public class P127ClosedClassTargetTests
    {
        private const string SeedId = "class-seed";
        private const string ToolId = "class-tool";
        private const string BoxId = "class-box";
        private const string BoxT = "class-box-t";
        private const string DerivedBoxId = "class-derived-box";
        private const string DerivedBoxT = "class-derived-box-t";
        private const string SeedBoxId = "class-seed-box";
        private const string DerivedPairId = "class-derived-pair";
        private const string DerivedPairT = "class-derived-pair-t";
        private const string WeirdId = "class-weird";
        private const string ListBoxId = "class-list-box";
        private const string Box2Id = "class-box2";
        private const string TwinId = "class-twin";
        private const string SeedPairId = "class-seed-pair";
        private const string NarrowId = "class-narrow";
        private const string HoldsId = "iface-holds";
        private const string HoldsT = "iface-holds-t";
        private const string HoldId = "class-hold";
        private const string HoldT = "class-hold-t";
        private const string CallbackId = "class-callback";
        private const string CallbackT = "class-callback-t";
        private const string NullableBoxId = "class-nullable-box";
        private const string HoldingId = "class-holding";
        private const string HoldingD = "class-holding-d";
        private const string ListHoldingId = "class-list-holding";
        private const string ListHoldingT = "class-list-holding-t";
        private const string ActionBoxId = "class-action-box";
        private const string HoldListId = "class-hold-list";
        private const string HoldListT = "class-hold-list-t";

        [TestCase("derived-row")]
        // A base slot closes its row by working the row's arguments back.
        [TestCase("loose-row")]
        public void AnOpenDerivedRowProjectsItsSlotArgumentsOntoTheBase(string rowId)
        {
            using NeoClient client = LoadClient();

            Assert.IsTrue(Lookup(client, "Seeds").Add(rowId));
            var error = Assert.Throws<InvalidOperationException>(() => Lookup(client, "Tools").Add(rowId));
            Assert.AreEqual(
                $"lookup member Tools target {rowId} is DerivedBox<Seed>, but the member declares Box<Tool>. Point it at a Box<Tool> row.",
                error!.Message);
        }

        [Test]
        public void AClosedDerivedClassIsJudgedByTheBaseItExtends()
        {
            using NeoClient client = LoadClient();

            Assert.IsTrue(Lookup(client, "Seeds").Add("seed-box-row"));
            var error = Assert.Throws<InvalidOperationException>(() => Lookup(client, "Tools").Add("seed-box-row"));
            Assert.AreEqual(
                "lookup member Tools target seed-box-row is SeedBox, but the member declares Box<Tool>. Point it at a Box<Tool> row.",
                error!.Message);
        }

        [Test]
        public void AnUnclosedRowIsJudgedByWhatItPassesItsBases()
        {
            using NeoClient client = LoadClient();

            // DerivedPair passes Box its own T, which matches any argument.
            Assert.IsTrue(Lookup(client, "Tools").Add("pair-row"));
            Assert.IsTrue(Lookup(client, "Tools").Add("weird-row"));
            var error = Assert.Throws<InvalidOperationException>(() => Lookup(client, "Seeds").Add("weird-row"));
            Assert.AreEqual(
                "lookup member Seeds target weird-row is Weird, but the member declares Box<Seed>. Point it at a Box<Seed> row.",
                error!.Message);
        }

        [Test]
        public void AnUnclosedRowInfersItsParamsFromTheTarget()
        {
            using NeoClient client = LoadClient();

            // ListBox<T> : Box<List<T>>, held in a Box<List<Seed>> slot.
            Assert.IsTrue(Lookup(client, "Lists").Add("list-row"));
            var listError = Assert.Throws<InvalidOperationException>(() => Lookup(client, "Seeds").Add("list-row"));
            Assert.AreEqual(
                "lookup member Seeds target list-row is ListBox, but the member declares Box<Seed>. Point it at a Box<Seed> row.",
                listError!.Message);
            // Twin<T> : Box2<T, T> binds T once.
            Assert.IsTrue(Lookup(client, "Same").Add("twin-row"));
            var twinError = Assert.Throws<InvalidOperationException>(() => Lookup(client, "Mixed").Add("twin-row"));
            Assert.AreEqual(
                "lookup member Mixed target twin-row is Twin, but the member declares Box2<Seed, Tool>. Point it at a Box2<Seed, Tool> row.",
                twinError!.Message);
        }

        [Test]
        public void ABaseSlotClosesARowByUnification()
        {
            using NeoClient client = LoadClient();

            // Listed, a Box<List<Seed>> slot, closes its ListBox<T> : Box<List<T>> row as ListBox<Seed>.
            var error = Assert.Throws<InvalidOperationException>(() => Lookup(client, "ToolLists").Add("list-row"));
            Assert.AreEqual(
                "lookup member ToolLists target list-row is ListBox<Seed>, but the member declares Box<List<Tool>>. Point it at a Box<List<Tool>> row.",
                error!.Message);
        }

        [Test]
        public void AnInterfaceLookupSelectsARowThatImplementsItsConstruction()
        {
            using NeoClient client = LoadClient();

            // Box<T> implements IHolds<T>, so both rows hold Seed.
            Assert.IsTrue(Lookup(client, "SeedHolders").IsSelectableId("seed-box-row"));
            Assert.IsTrue(Lookup(client, "SeedHolders").IsSelectableId("loose-row"));
            Assert.IsFalse(Lookup(client, "ToolHolders").IsSelectableId("seed-box-row"));
            Assert.IsFalse(Lookup(client, "ToolHolders").IsSelectableId("loose-row"));
            // IHolds doesn't determine DerivedPair's U.
            Assert.IsFalse(Lookup(client, "SeedHolders").IsSelectableId("pair-row"));
        }

        [TestCase("SeedHolders", "seed-row", "Holder.SeedHolders can't reference 'seed-row', because Seed doesn't implement IHolds.")]
        [TestCase("ToolHolders", "seed-box-row", "Holder.ToolHolders can't reference 'seed-box-row', because SeedBox implements IHolds<Seed>, not IHolds<Tool>.")]
        // Loose closes its row as DerivedBox<Seed>.
        [TestCase("ToolHolders", "loose-row", "Holder.ToolHolders can't reference 'loose-row', because DerivedBox implements IHolds<Seed>, not IHolds<Tool>.")]
        [TestCase("ToolHolders", "pair-row", "Holder.ToolHolders can't reference 'pair-row', because IHolds doesn't determine DerivedPair's parameter U.")]
        // SeedPair : DerivedPair<Seed, Tool> closes it.
        [TestCase(
            "SeedHolders",
            "pair-row",
            "Holder.SeedHolders can't reference 'pair-row', because IHolds doesn't determine DerivedPair's parameter U. Reference a class that binds it, such as SeedPair.")]
        [TestCase(
            "SeedHolders",
            "narrow-row",
            "Holder.SeedHolders can't reference 'narrow-row', because Narrow's T extends Seed, and IHolds's T is unconstrained.")]
        public void AnInterfaceLookupRefusesARowInTheServersWords(string lookup, string rowId, string message)
        {
            using NeoClient client = LoadClient();

            var error = Assert.Throws<InvalidOperationException>(() => Lookup(client, lookup).Add(rowId));
            Assert.AreEqual(message, error!.Message);
        }

        [Test]
        public void ARowInAClosedConstructionReadsItsSlotThroughItsOwner()
        {
            using NeoClient client = LoadClient();

            // Held is a Hold<Seed>, so its Box<T> Inner holds a DerivedBox<Seed>.
            Assert.IsTrue(Lookup(client, "Seeds").IsSelectableId("held-inner-row"));
            Assert.IsTrue(Lookup(client, "SeedHolders").IsSelectableId("held-inner-row"));
            var classError = Assert.Throws<InvalidOperationException>(() => Lookup(client, "Tools").Add("held-inner-row"));
            Assert.AreEqual(
                "lookup member Tools target held-inner-row is DerivedBox<Seed>, but the member declares Box<Tool>. Point it at a Box<Tool> row.",
                classError!.Message);
            var interfaceError = Assert.Throws<InvalidOperationException>(() => Lookup(client, "ToolHolders").Add("held-inner-row"));
            Assert.AreEqual(
                "Holder.ToolHolders can't reference 'held-inner-row', because DerivedBox implements IHolds<Seed>, not IHolds<Tool>.",
                interfaceError!.Message);
        }

        [Test]
        public void ATupleArgumentNamingAParamIsClosedByItsOwner()
        {
            using NeoClient client = LoadClient();

            // Hold<Seed>'s Box<(T, Seed)> Pair holds a Box<(Seed, Seed)>.
            Assert.IsTrue(Lookup(client, "TupleSeeds").IsSelectableId("held-pair-row"));
            var error = Assert.Throws<InvalidOperationException>(() => Lookup(client, "TupleTools").Add("held-pair-row"));
            Assert.AreEqual(
                "lookup member TupleTools target held-pair-row is Box<(Seed, Seed)>, but the member declares Box<(Tool, Seed)>. Point it at a Box<(Tool, Seed)> row.",
                error!.Message);
        }

        [Test]
        public void AnUnclosedRowUnifiesThroughADelegateArgument()
        {
            using NeoClient client = LoadClient();

            // Callback<T> : Box<NeoDelegate<T>(T)> binds T from the return, then checks the parameter.
            Assert.IsTrue(Lookup(client, "Makers").IsSelectableId("callback-row"));
            Assert.IsFalse(Lookup(client, "MixedMakers").IsSelectableId("callback-row"));
            var error = Assert.Throws<InvalidOperationException>(() => Lookup(client, "Seeds").Add("callback-row"));
            Assert.AreEqual(
                "lookup member Seeds target callback-row is Callback, but the member declares Box<Seed>. Point it at a Box<Seed> row.",
                error!.Message);
        }

        // Holding<D> : Box<D> { Hold<D> Held } in a Box<Seed> slot is a
        // Holding<Seed>, so its Held is a Hold<Seed> whose Inner closes too.
        // ListHolding<T> : Box<List<T>> in a Box<List<Seed>> slot binds T the same way.
        [TestCase("b")]
        [TestCase("l")]
        public void ADerivedRowsOwnParamsCloseTheRowsBelowIt(string prefix)
        {
            using NeoClient client = LoadClient();

            Assert.IsTrue(Lookup(client, "HeldSeeds").Add($"{prefix}-held"));
            var heldError = Assert.Throws<InvalidOperationException>(() => Lookup(client, "HeldTools").Add($"{prefix}-held"));
            Assert.AreEqual(
                $"lookup member HeldTools target {prefix}-held is Hold<Seed>, but the member declares Hold<Tool>. Point it at a Hold<Tool> row.",
                heldError!.Message);
            Assert.IsTrue(Lookup(client, "Seeds").Add($"{prefix}-inner"));
            var innerError = Assert.Throws<InvalidOperationException>(() => Lookup(client, "Tools").Add($"{prefix}-inner"));
            Assert.AreEqual(
                $"lookup member Tools target {prefix}-inner is DerivedBox<Seed>, but the member declares Box<Tool>. Point it at a Box<Tool> row.",
                innerError!.Message);
        }

        // An open param substituted away leaves DerivedPair<_, Seed>, which isn't closed.
        [Test]
        public void ARowTypeMissingAClassArgumentStaysOpen()
        {
            using NeoClient client = LoadClient();
            ClassMember slot = ClassSlot("member-half-pair", "HalfPair", DerivedPairId, ("class-derived-pair-u", Seed));

            Assert.IsNull(NeoReferenceTypes.ClassRowTypeInfo(client, DerivedPairId, slot));
        }

        [Test]
        public void AClosedRowKeepsANestedNullableActionArgument()
        {
            using NeoClient client = LoadClient();

            // ActionBox : Box<NeoAction<string?>> isn't a Box<NeoAction<string>>.
            Assert.IsTrue(Lookup(client, "NullableActions").Add("action-box-row"));
            var error = Assert.Throws<InvalidOperationException>(() => Lookup(client, "Actions").Add("action-box-row"));
            Assert.AreEqual(
                "lookup member Actions target action-box-row is ActionBox, but the member declares Box<NeoAction<string>>. Point it at a Box<NeoAction<string>> row.",
                error!.Message);
        }

        // The server's NSAction type-argument arm: never nullable, each argument keeping its `?`.
        [Test]
        public void AnActionArgumentSignatureKeepsItsArgumentsNullability()
        {
            using NeoClient client = LoadClient();
            ClassMember nullableSlot = ClassSlot("member-nullable-action-slot", "NullableActionSlot", BoxId, (BoxT, NullableStringAction));
            ClassMember slot = ClassSlot("member-action-slot", "ActionSlot", BoxId, (BoxT, StringActionArgument));

            Assert.IsTrue(NeoGenericResolution.ConstructedSlotAccepts(client, nullableSlot, ActionBoxId, NeoGenericResolution.EmptyEnv).ok);
            Assert.IsFalse(NeoGenericResolution.ConstructedSlotAccepts(client, slot, ActionBoxId, NeoGenericResolution.EmptyEnv).ok);
            var action = (ActionTypeInfo)NeoGenericResolution.TypeInfoFromArgumentSignature(
                NeoGenericResolution.SignatureOfMember(client, NeoTypeBindingViews.View(NullableStringAction), NeoGenericResolution.EmptyEnv));
            Assert.IsTrue(action.required);
            Assert.IsFalse(action.argumentTypes[0].required);
        }

        [Test]
        public void AVoidReturnBindsNoRowParam()
        {
            // Callback<T> : Box<NeoDelegate<T>()> against Box<NeoDelegate<void>()>: T against void stays T.
            ProjectData data = BuildProjectData();
            data.classes[CallbackId].extendsGenericBindings![BoxT] = TypeBinding.Closed(
                new TypeHead(MemberKind.NSDelegate, true),
                TestTypeBindings.Arguments(("0", Forward(CallbackT))));
            using NeoClient client = Load(data);

            Assert.IsFalse(Lookup(client, "VoidMakers").IsSelectableId("callback-row"));
        }

        [Test]
        public void AClosedRowKeepsANestedNullableArgument()
        {
            using NeoClient client = LoadClient();

            // NullableBox : Box<string?> isn't a Box<string>.
            Assert.IsTrue(Lookup(client, "NullableStrings").Add("nullable-box-row"));
            var error = Assert.Throws<InvalidOperationException>(() => Lookup(client, "Strings").Add("nullable-box-row"));
            Assert.AreEqual(
                "lookup member Strings target nullable-box-row is NullableBox, but the member declares Box<string>. Point it at a Box<string> row.",
                error!.Message);
        }

        [Test]
        public void AnInPlaceSchemaEditReachesTheClassPairMemo()
        {
            using NeoClient client = LoadClient();
            Assert.IsFalse(Lookup(client, "Seeds").IsSelectableId("weird-row"));
            Assert.IsTrue(client.TryGetClass(WeirdId, out NeoSchemaClass? weird));

            weird!.extendsGenericBindings![BoxT] = Seed;
            client.InvalidateSchemaResolutionCaches();

            Assert.IsTrue(Lookup(client, "Seeds").IsSelectableId("weird-row"));
        }

        [TestCase("derived-row")]
        [TestCase("loose-row")]
        [TestCase("seed-box-row")]
        public void AHostRebindDoesNotJudgeAnObjectTarget(string rowId)
        {
            using NeoClient client = LoadClient();

            CollectionAssert.AreEqual(
                new[] { rowId },
                NeoGeneratedTypesSupport.ReferenceSelection(client, Lookup(client, "Tools"), Reference(client, rowId)));
        }

        [Test]
        public void AReferenceByIdReadsTheRowWithoutJudgingIt()
        {
            using NeoClient client = LoadClient();

            // Loose is a Box<Seed> slot, so this is a downcast.
            Assert.AreEqual(6, Convert.ToInt32(ReadK(client, "loose-row", Boxed(DerivedBoxId, (DerivedBoxT, SeedId)))));
            Assert.AreEqual(7, Convert.ToInt32(ReadK(client, "pair-row", Boxed(DerivedPairId, (DerivedPairT, SeedId), ("class-derived-pair-u", ToolId)))));
        }

        // A write into a constructed slot is judged as the server's entry
        // write judges it: a SeedBox fits Box<Seed> but not Box<Tool>, and an
        // open DerivedBox takes the slot's arguments.
        [TestCase("SeedBoxed", SeedBoxId, null)]
        [TestCase("ToolBoxed", SeedBoxId, "Type \"class-seed-box\" is not admissible in constructed slot \"ToolBoxed\": class-argument signature mismatch for param \"class-box-t\" (arguments compare invariantly).")]
        [TestCase("ToolBoxed", DerivedBoxId, null)]
        public void AClassSlotWriteRefusesAClosedDescendantWithOtherArguments(string key, string classId, string? refused)
        {
            using NeoClient client = LoadClient();
            client.SetSaveValue(Row("written-row", classId));

            TestDelegate write = () => client.save.SetSerializedValue(key, NeoValueWritePayload.FromValueReference("written-row", null));

            if (refused is null)
                Assert.DoesNotThrow(write);
            else
                Assert.AreEqual(refused, Assert.Throws<InvalidOperationException>(write)!.Message);
        }

        [TestCase(BoxId, null)]
        // An unstamped DerivedBox<T> : Box<T> row in that Box<Tool> slot is a DerivedBox<Tool>.
        [TestCase(DerivedBoxId, null)]
        // Shelf's Shelved is a Hold<Tool>, so its Box<T> Inner is a Box<Tool>.
        [TestCase(SeedBoxId, "Constructed Class field 'class-shelf.Shelved.Inner' rejects class \"SeedBox\": class-argument signature mismatch for param \"class-box-t\" (arguments compare invariantly).")]
        public void AConstructedClassFieldReadsItsSlotThroughItsOwner(string innerClassId, string? refused)
        {
            using NeoClient client = LoadClient();
            var rows = new List<MemberValue>
            {
                Row("new-held", HoldId, new Dictionary<string, string> { ["Inner"] = "new-inner", ["Pair"] = "new-pair" }),
                Row("new-inner", innerClassId),
                Row("new-pair", BoxId),
            };

            TestDelegate construct = () => NeoGeneratedTypesSupport.CreateWritableClassValue(
                client,
                "class-shelf",
                new Dictionary<string, string> { ["Shelved"] = "new-held" },
                rows);
            if (refused is null)
                Assert.DoesNotThrow(construct);
            else
                Assert.AreEqual(refused, Assert.Throws<InvalidOperationException>(construct)!.Message);
        }

        // Shelf's Shelved is a Hold<Tool>, whose Box<(T, Seed)> Pair names T:
        // the slot closes Box's param to (T, Seed) under {T: Tool}, so a Box
        // there is a Box<(Tool, Seed)>, stamped or not.
        [TestCase(true)]
        [TestCase(false)]
        public void AConstructedSlotWhoseArgumentNamesItsOwnersParamReadsItThroughTheOwner(bool stamped)
        {
            using NeoClient client = LoadClient();
            ObjectMemberValue pair = stamped ? ToolSeedPair() : Row("new-pair", BoxId);
            var rows = new List<MemberValue>
            {
                Row("new-held", HoldId, new Dictionary<string, string> { ["Inner"] = "new-inner", ["Pair"] = "new-pair" }),
                Row("new-inner", BoxId),
                pair,
            };

            Assert.DoesNotThrow(() => NeoGeneratedTypesSupport.CreateWritableClassValue(
                client,
                "class-shelf",
                new Dictionary<string, string> { ["Shelved"] = "new-held" },
                rows));
        }

        // HoldList<Seed>'s Box<List<T>> Listed reads List<Seed> through its
        // owner, so a ListHolding<L> : Box<List<L>> row in it closes L to Seed
        // and its Hold<L> Held is a Hold<Seed>.
        [Test]
        public void ARowClosesItsParamsThroughItsSlotsOwner()
        {
            using NeoClient client = LoadClient();
            ClassMember seedHoldList = ClassSlot("member-seed-hold-list", "SeedHoldList", HoldListId, (HoldListT, Seed));

            Member? listed = client.TryResolveOwnedChildMember(Row("hold-list-row", HoldListId), seedHoldList, "Listed");
            var held = client.TryResolveOwnedChildMember(Row("list-holding-row", ListHoldingId), listed, "Held") as ClassMember;

            Assert.IsNotNull(held);
            TypeBinding argument = held!.TypeArguments![HoldT];
            Assert.IsFalse(argument.IsOpen);
            Assert.AreEqual(Seed, argument);
        }

        // ListShelf's Shelved is a HoldList<Tool>, so the ListHolding in its
        // Listed holds a Hold<Tool>, whose Box<T> Inner is a Box<Tool>.
        [TestCase(BoxId, null)]
        [TestCase(SeedBoxId, "Constructed Class field 'class-list-shelf.Shelved.Listed.Held.Inner' rejects class \"SeedBox\": class-argument signature mismatch for param \"class-box-t\" (arguments compare invariantly).")]
        public void AConstructedRowClosesItsParamsThroughItsSlotsOwner(string innerClassId, string? refused)
        {
            using NeoClient client = LoadClient();
            var rows = new List<MemberValue>
            {
                Row("new-hold-list", HoldListId, new Dictionary<string, string> { ["Listed"] = "new-listed" }),
                Row("new-listed", ListHoldingId, new Dictionary<string, string> { ["Held"] = "new-held" }),
                Row("new-held", HoldId, new Dictionary<string, string> { ["Inner"] = "new-inner", ["Pair"] = "new-pair" }),
                Row("new-inner", innerClassId),
                Row("new-pair", BoxId),
            };

            TestDelegate construct = () => NeoGeneratedTypesSupport.CreateWritableClassValue(
                client,
                "class-list-shelf",
                new Dictionary<string, string> { ["Shelved"] = "new-hold-list" },
                rows);
            if (refused is null)
                Assert.DoesNotThrow(construct);
            else
                Assert.AreEqual(refused, Assert.Throws<InvalidOperationException>(construct)!.Message);
        }

        // HoldList's Listed is a Box<List<T>>. Where T is open, any row fits;
        // where it's Tool, the row must hold a List<Tool>.
        [TestCase(SeedBoxId, null, null)]
        [TestCase(SeedBoxId, ToolId, "class-argument signature mismatch for param \"class-box-t\" (arguments compare invariantly)")]
        [TestCase(ListHoldingId, ToolId, null)]
        [TestCase(BoxId, null, "class \"Box\" is open (generic param \"class-box-t\" is unbound) and cannot be instantiated")]
        public void AConstructedSlotJudgesARowWhereItsContextClosesTheArgument(string valueClassId, string? holdListArgument, string? refused)
        {
            using NeoClient client = LoadClient();
            var arguments = holdListArgument is null
                ? null
                : TestTypeBindings.Arguments((HoldListT, TestTypeBindings.Class(holdListArgument)));
            IReadOnlyDictionary<string, NeoGenericEnvEntry> context = NeoGenericResolution.ResolveInstanceEnv(client, HoldListId, arguments);
            Assert.IsTrue(client.TryGetMember("member-hold-list-listed", out Member? listed));

            NeoSlotAdmission admission = NeoGenericResolution.ConstructedSlotAccepts(client, (ClassMember)listed!, valueClassId, context);

            Assert.AreEqual(refused, admission.ok ? null : admission.reason);
        }

        // A stored HoldList<Seed>'s ListHolding<L> row in its Box<List<T>>
        // Listed is placed in that slot read in HoldList<Seed>, so L is Seed:
        // the row loads with its omitted defaults, and its Hold<L> Held is a
        // Hold<Seed>, whether read by inference or through the node tree.
        [Test]
        public void AStoredRowClosesItsParamsThroughItsInferredSlot()
        {
            using NeoClient client = LoadSeedHoldListClient();
            Assert.IsTrue(client.TryInferMemberForValueId("hl-listed", out Member? listed));
            Assert.IsTrue(client.TryGetValue("hl-listed", out MemberValue? row));

            var held = client.TryResolveOwnedChildMember(row!, listed, "Held") as ClassMember;
            var node = client.save.Get<NeoMemberClass>("SeedHoldList").Get<NeoMemberClass>("Listed").Get<NeoMemberClass>("Held");

            Assert.AreEqual(Seed, held!.TypeArguments![HoldT]);
            Assert.AreEqual(Seed, NeoTypeBindingViews.BindingOf(node.GenericEnv[HoldT].memberId!));
            LogAssert.NoUnexpectedReceived();
        }

        // A method on that ListHolding row reads L as Seed.
        [Test]
        public void AMethodReceiverReadsItsParamsThroughItsInferredSlot()
        {
            using NeoClient client = LoadSeedHoldListClient();
            var ctx = new NSGetterEvaluator.Context(client, thisValue: null, rootValue: null, valueOwnership: NeoValueOwnership.Save);
            Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Save, "hl-listed", out MemberValue? row));
            object receiver = NSGetterEvaluator.UnwrapRow(row!, ctx, NeoValueOwnership.Save)!;

            IReadOnlyDictionary<string, NeoGenericEnvEntry> env = NeoNSFunctionRuntime.ResolveReceiverGenericEnv(client, receiver, ctx, "Probe");

            Assert.AreEqual(Seed, NeoTypeBindingViews.BindingOf(env[ListHoldingT].memberId!));
        }

        // HoldList's List<Box<List<T>>> Shelf: a ListHolding<L> entry closes L
        // through the list's environment, its stamp or, unstamped, the
        // closed type its typeSource stores (P127 §12.1).
        [TestCase(true)]
        [TestCase(false)]
        public void AListEntryClosesItsParamsThroughItsList(bool stamped)
        {
            using NeoClient client = LoadClient();
            ClassMember seedHoldList = ClassSlot("member-seed-hold-list", "SeedHoldList", HoldListId, (HoldListT, Seed));
            Assert.IsTrue(client.TryGetMember("member-hold-list-shelf", out Member? schemaShelf));
            Member? shelf = stamped
                ? schemaShelf
                : client.TryResolveOwnedChildMember(Row("hold-list-row", HoldListId), seedHoldList, "Shelf");
            var shelfRow = new ArrayMemberValue
            {
                id = "shelf-row",
                createdAt = "x",
                updatedAt = "x",
                value = new[] { "lh" },
                genericBindings = stamped ? TestTypeBindings.Stamp((HoldListT, TestTypeBindings.Class(SeedId))) : null,
                typeSource = stamped
                    ? null
                    : NeoTypeSource.OfType(TestTypeBindings.List(
                        TestTypeBindings.Class(BoxId, true, (BoxT, TestTypeBindings.List(TestTypeBindings.Class(SeedId)))))),
            };

            Member? entry = client.TryResolveCollectionEntryMember(shelf, shelfRow);
            var held = client.TryResolveOwnedChildMember(Row("lh", ListHoldingId), entry, "Held") as ClassMember;

            Assert.AreEqual(Seed, held!.TypeArguments![HoldT]);
        }

        // The stored HoldList<Seed>'s unstamped Shelf reads its ListHolding
        // entry's L as Seed through the node tree too.
        [Test]
        public void AStoredListEntryClosesItsParamsThroughItsList()
        {
            using NeoClient client = LoadSeedHoldListClient();

            var shelf = client.save.Get<NeoMemberClass>("SeedHoldList").Get<NeoMemberList>("Shelf");
            var held = ((NeoMemberClass)shelf[0]).Get<NeoMemberClass>("Held");

            Assert.AreEqual(Seed, NeoTypeBindingViews.BindingOf(held.GenericEnv[HoldT].memberId!));
        }

        // HoldList's (T, Seed) Both: an unstamped row's T element reads the
        // HoldList<Seed> the tuple was read in.
        [Test]
        public void ATupleElementReadsItsParamsThroughItsTuple()
        {
            using NeoClient client = LoadSeedHoldListClient();
            Assert.IsTrue(client.TryGetValue("hl-row", out MemberValue? holdList));
            Assert.IsTrue(client.TryGetValue("hl-both", out MemberValue? both));
            ClassMember seedHoldList = ClassSlot("member-seed-hold-list", "SeedHoldList", HoldListId, (HoldListT, Seed));

            var tuple = (TupleMember)client.TryResolveOwnedChildMember(holdList!, seedHoldList, "Both")!;
            var element = client.TryResolveTupleElementMember(tuple, both, 0) as ClassMember;

            Assert.AreEqual(SeedId, element!.DeclaredClassId);
        }

        // ListShelf's Shelved defaults to a HoldList<Tool> whose Listed holds
        // a ListHolding<L>: the default and the clone of its Listed row read L
        // as Tool.
        [Test]
        public void AClassDefaultClosesItsParamsThroughItsSlotsOwner()
        {
            ProjectData data = BuildProjectData();
            ((ClassMember)data.members["member-list-shelved"]).defaultValue = new ObjectMemberValueBase
            {
                classId = HoldListId,
                value = new Dictionary<string, string> { ["Listed"] = "def-listed" },
            };
            // A default's rows store every field.
            foreach (MemberValue row in new MemberValue[]
            {
                Row("def-listed", ListHoldingId, new Dictionary<string, string> { ["Held"] = "def-held", ["k"] = "def-listed-k" }),
                new NumberMemberValue { id = "def-listed-k", value = 1d },
                Row("def-held", HoldId, new Dictionary<string, string> { ["Inner"] = "def-inner", ["Pair"] = "def-pair" }),
                Row("def-inner", BoxId, new Dictionary<string, string> { ["k"] = "def-inner-k" }),
                new NumberMemberValue { id = "def-inner-k", value = 2d },
                Row("def-pair", BoxId, new Dictionary<string, string> { ["k"] = "def-pair-k" }),
                new NumberMemberValue { id = "def-pair-k", value = 3d },
            })
                data.values[row.id] = row;
            using NeoClient client = Load(data);

            Assert.DoesNotThrow(() => NeoGeneratedTypesSupport.CreateWritableClassValue(
                client,
                "class-list-shelf",
                new Dictionary<string, string>(),
                new List<MemberValue>()));
        }

        // Shelf's Shelved defaults to a Hold<Tool> that omits its Box<T>
        // Inner, whose default is a SeedBox: refused in the server's words
        // for a default in a constructed slot.
        [Test]
        public void AClassDefaultInAConstructedSlotIsJudgedInItsOwner()
        {
            ProjectData data = BuildProjectData();
            ((ClassMember)data.members["member-shelved"]).defaultValue = new ObjectMemberValueBase
            {
                classId = HoldId,
                value = new Dictionary<string, string>(),
            };
            ((ClassMember)data.members["member-hold-inner"]).defaultValue = new ObjectMemberValueBase
            {
                classId = SeedBoxId,
                value = new Dictionary<string, string>(),
            };
            using NeoClient client = Load(data);

            var error = Assert.Throws<InvalidOperationException>(() => NeoGeneratedTypesSupport.CreateWritableClassValue(
                client,
                "class-shelf",
                new Dictionary<string, string>(),
                new List<MemberValue>()))!;

            Assert.AreEqual(
                "Cannot create a value of class \"SeedBox\" in constructed slot \"Inner\": class-argument signature mismatch for param \"class-box-t\" (arguments compare invariantly).",
                error.Message);
        }

        /// <summary><c>Reference&lt;target&gt;(id: rowId).k</c>.</summary>
        private static object? ReadK(NeoClient client, string rowId, TypeInfo target)
        {
            var reference = new RowOfPointer
            {
                type = PointerKind.RowOf,
                place = new ReferencePointer { type = PointerKind.Reference, valueId = rowId },
                typeInfo = target,
            };
            var function = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = new Variable[0],
                typeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = new KeyOfPointer
                        {
                            type = PointerKind.KeyOf,
                            keyOf = new KeyOf
                            {
                                pointer = new DerefPointer { type = PointerKind.Deref, pointer = reference },
                                key = new ValuePointer
                                {
                                    type = PointerKind.Value,
                                    value = new Value
                                    {
                                        typeInfo = new PrimitiveTypeInfo { type = MemberKind.String, required = true },
                                        value = JToken.FromObject("k"),
                                    },
                                },
                            },
                        },
                    },
                },
            };
            return NSGetterEvaluator.Evaluate(function, new NSGetterEvaluator.Context(client, thisValue: null, rootValue: null));
        }

        private static ClassTypeInfo Boxed(string classId, params (string Param, string ClassId)[] arguments)
        {
            var typeArguments = new Dictionary<string, TypeInfo>();
            foreach ((string param, string argumentClassId) in arguments)
                typeArguments[param] = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = argumentClassId };
            return new ClassTypeInfo { type = MemberKind.Class, required = true, classId = classId, typeArguments = typeArguments };
        }

        private static NeoMemberLookupWritable Lookup(NeoClient client, string name) =>
            client.save.Get<NeoMemberLookupWritable>(name);

        private static NeoReference<object> Reference(NeoClient client, string targetId) =>
            new NeoReference<object>(client, targetId, null, null);

        private static NeoClient LoadClient() => Load(BuildProjectData());

        private static NeoClient Load(ProjectData data)
        {
            data.WithUserRoot();
            return NeoTestSaveStack.ClientFromSchema(data.PlaceAuthored());
        }

        /// <summary>
        /// The fixture with a stored <c>HoldList&lt;Seed&gt;</c>,
        /// <c>SeedHoldList</c>: a <c>ListHolding</c> in its <c>Listed</c> and
        /// in its unstamped <c>Shelf</c>, each holding a <c>Hold</c> whose
        /// <c>DerivedBox</c> <c>Inner</c> omits its field, and an unstamped
        /// <c>Both</c>.
        /// </summary>
        private static NeoClient LoadSeedHoldListClient()
        {
            ProjectData data = BuildProjectData();
            data.classes["class-holder"].schema["SeedHoldList"] = "member-seed-hold-list";
            data.members["member-seed-hold-list"] = ClassSlot("member-seed-hold-list", "SeedHoldList", HoldListId, (HoldListT, Seed));
            ((ObjectMemberValue)data.values["root-save-value"]).value!["SeedHoldList"] = "hl-row";
            foreach (MemberValue row in new MemberValue[]
            {
                Row("hl-row", HoldListId, new Dictionary<string, string>
                {
                    ["Listed"] = "hl-listed",
                    ["Shelf"] = "hl-shelf",
                    ["Both"] = "hl-both",
                }),
                Row("hl-listed", ListHoldingId, new Dictionary<string, string> { ["Held"] = "hl-held" }),
                Row("hl-held", HoldId, new Dictionary<string, string> { ["Inner"] = "hl-inner", ["Pair"] = "hl-pair" }),
                Row("hl-inner", DerivedBoxId),
                // An unstamped Box<(T, Seed)> row stays open, so it stores its field.
                Row("hl-pair", DerivedBoxId, new Dictionary<string, string> { ["k"] = "hl-pair-k" }),
                new NumberMemberValue { id = "hl-pair-k", value = 1d },
                new ArrayMemberValue { id = "hl-shelf", createdAt = "x", updatedAt = "x", value = new[] { "hl-shelved" } },
                Row("hl-shelved", ListHoldingId, new Dictionary<string, string> { ["Held"] = "hl-shelved-held" }),
                Row("hl-shelved-held", HoldId, new Dictionary<string, string> { ["Inner"] = "hl-shelved-inner", ["Pair"] = "hl-shelved-pair" }),
                Row("hl-shelved-inner", DerivedBoxId),
                Row("hl-shelved-pair", DerivedBoxId, new Dictionary<string, string> { ["k"] = "hl-shelved-pair-k" }),
                new NumberMemberValue { id = "hl-shelved-pair-k", value = 2d },
                new ArrayMemberValue { id = "hl-both", createdAt = "x", updatedAt = "x", value = new[] { "hl-both-first", "hl-both-second" } },
                Row("hl-both-first", SeedId),
                Row("hl-both-second", SeedId),
            })
                data.values[row.id] = row;
            return Load(data);
        }

        private static ProjectData BuildProjectData()
        {
            return new ProjectData
            {
                project = new Project
                {
                    id = "project",
                    _id = "project",
                    name = "Closed Class Targets",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    ["class-root"] = Class("class-root", "Root"),
                    ["class-holder"] = Class("class-holder", "Holder", new Dictionary<string, string>
                    {
                        ["Boxed"] = "member-boxed",
                        ["SeedBoxed"] = "member-seed-boxed",
                        ["ToolBoxed"] = "member-tool-boxed",
                        ["Loose"] = "member-loose",
                        ["Paired"] = "member-paired",
                        ["Seeds"] = "member-seeds",
                        ["Tools"] = "member-tools",
                        ["DerivedSeeds"] = "member-derived-seeds",
                        ["SeedHolders"] = "member-seed-holders",
                        ["ToolHolders"] = "member-tool-holders",
                        ["Listed"] = "member-listed",
                        ["Twinned"] = "member-twinned",
                        ["Lists"] = "member-lists",
                        ["ToolLists"] = "member-tool-lists",
                        ["Same"] = "member-same",
                        ["Mixed"] = "member-mixed",
                        ["Planted"] = "member-planted",
                        ["Narrowed"] = "member-narrowed",
                        ["Held"] = "member-held",
                        ["Called"] = "member-called",
                        ["TupleSeeds"] = "member-tuple-seeds",
                        ["TupleTools"] = "member-tuple-tools",
                        ["Makers"] = "member-makers",
                        ["MixedMakers"] = "member-mixed-makers",
                        ["VoidMakers"] = "member-void-makers",
                        ["NullableBoxed"] = "member-nullable-boxed",
                        ["Strings"] = "member-strings",
                        ["NullableStrings"] = "member-nullable-strings",
                        ["HoldingBoxed"] = "member-holding-boxed",
                        ["ListHoldingBoxed"] = "member-list-holding-boxed",
                        ["HeldSeeds"] = "member-held-seeds",
                        ["HeldTools"] = "member-held-tools",
                        ["ActionBoxed"] = "member-action-boxed",
                        ["Actions"] = "member-actions",
                        ["NullableActions"] = "member-nullable-actions",
                    }),
                    [SeedId] = Class(SeedId, "Seed"),
                    [ToolId] = Class(ToolId, "Tool"),
                    [BoxId] = Implementing(Class(BoxId, "Box", new Dictionary<string, string> { ["k"] = "member-k" }, (BoxT, "T"))),
                    [DerivedBoxId] = Extending(
                        Class(DerivedBoxId, "DerivedBox", genericParam: (DerivedBoxT, "T")),
                        Forward(DerivedBoxT)),
                    [SeedBoxId] = Extending(Class(SeedBoxId, "SeedBox"), Seed),
                    [DerivedPairId] = Extending(
                        Class(DerivedPairId, "DerivedPair", genericParam: (DerivedPairT, "T"), secondParam: ("class-derived-pair-u", "U")),
                        Forward(DerivedPairT)),
                    [WeirdId] = Extending(Class(WeirdId, "Weird", genericParam: ("class-weird-t", "T")), Tool),
                    [ListBoxId] = Extending(Class(ListBoxId, "ListBox", genericParam: ("class-list-box-t", "T")), ListOfListBoxT),
                    [Box2Id] = Class(Box2Id, "Box2", genericParam: ("class-box2-a", "A"), secondParam: ("class-box2-b", "B")),
                    [TwinId] = Twin(),
                    [SeedPairId] = SeedPair(),
                    [NarrowId] = Narrow(),
                    [HoldId] = Class(HoldId, "Hold", new Dictionary<string, string>
                    {
                        ["Inner"] = "member-hold-inner",
                        ["Pair"] = "member-hold-pair",
                    }, (HoldT, "T")),
                    [CallbackId] = Extending(Class(CallbackId, "Callback", genericParam: (CallbackT, "T")), CallbackArgument),
                    ["class-shelf"] = Class("class-shelf", "Shelf", new Dictionary<string, string> { ["Shelved"] = "member-shelved" }),
                    [HoldListId] = Class(HoldListId, "HoldList", new Dictionary<string, string>
                    {
                        ["Listed"] = "member-hold-list-listed",
                        ["Shelf"] = "member-hold-list-shelf",
                        ["Both"] = "member-hold-list-both",
                    }, (HoldListT, "T")),
                    ["class-list-shelf"] = Class("class-list-shelf", "ListShelf", new Dictionary<string, string> { ["Shelved"] = "member-list-shelved" }),
                    [NullableBoxId] = Extending(Class(NullableBoxId, "NullableBox"), NullableString),
                    [HoldingId] = Extending(
                        Class(HoldingId, "Holding", new Dictionary<string, string> { ["Held"] = "member-holding-held" }, (HoldingD, "D")),
                        Forward(HoldingD)),
                    [ListHoldingId] = Extending(
                        Class(ListHoldingId, "ListHolding", new Dictionary<string, string> { ["Held"] = "member-list-holding-held" }, (ListHoldingT, "T")),
                        ListOfListHoldingT),
                    [ActionBoxId] = Extending(Class(ActionBoxId, "ActionBox"), NullableStringAction),
                },
                members = new Dictionary<string, Member>
                {
                    ["root-assets"] = RootMember("root-assets", "class-root"),
                    ["root-save"] = RootMember("root-save", "class-holder"),
                    ["root-session"] = RootMember("root-session", "class-root"),
                    ["member-listed"] = ClassSlot("member-listed", "Listed", BoxId, (BoxT, SeedList)),
                    ["member-twinned"] = TwinnedSlot(),
                    ["member-planted"] = ClassSlot("member-planted", "Planted", SeedId),
                    ["member-narrowed"] = ClassSlot("member-narrowed", "Narrowed", BoxId, (BoxT, Seed)),
                    ["member-lists"] = BoxLookup("member-lists", "Lists", BoxId, BoxT, SeedId, list: true),
                    ["member-tool-lists"] = BoxLookup("member-tool-lists", "ToolLists", BoxId, BoxT, ToolId, list: true),
                    ["member-same"] = Box2Lookup("member-same", "Same", ToolOrSeed(false)),
                    ["member-mixed"] = Box2Lookup("member-mixed", "Mixed", ToolOrSeed(true)),
                    ["member-k"] = new IntMember { id = "member-k", projectId = "project", name = "k", kind = MemberKind.Int },
                    ["member-boxed"] = ClassSlot("member-boxed", "Boxed", DerivedBoxId, (DerivedBoxT, Seed)),
                    ["member-seed-boxed"] = ClassSlot("member-seed-boxed", "SeedBoxed", BoxId, (BoxT, Seed)),
                    ["member-tool-boxed"] = ClassSlot("member-tool-boxed", "ToolBoxed", BoxId, (BoxT, Tool)),
                    ["member-loose"] = ClassSlot("member-loose", "Loose", BoxId, (BoxT, Seed)),
                    ["member-paired"] = ClassSlot("member-paired", "Paired", BoxId, (BoxT, Seed)),
                    ["member-seeds"] = BoxLookup("member-seeds", "Seeds", BoxId, BoxT, SeedId),
                    ["member-tools"] = BoxLookup("member-tools", "Tools", BoxId, BoxT, ToolId),
                    ["member-derived-seeds"] = BoxLookup("member-derived-seeds", "DerivedSeeds", DerivedBoxId, DerivedBoxT, SeedId),
                    ["member-seed-holders"] = HoldsLookup("member-seed-holders", "SeedHolders", SeedId),
                    ["member-tool-holders"] = HoldsLookup("member-tool-holders", "ToolHolders", ToolId),
                    ["member-hold-inner"] = HoldInner(),
                    ["member-hold-pair"] = ClassSlot("member-hold-pair", "Pair", BoxId, (BoxT, HoldTuple)),
                    ["member-hold-tuple-seed"] = ClassSlot("member-hold-tuple-seed", "Item2", SeedId),
                    ["member-held"] = ClassSlot("member-held", "Held", HoldId, (HoldT, Seed)),
                    ["member-shelved"] = ClassSlot("member-shelved", "Shelved", HoldId, (HoldT, Tool)),
                    ["member-hold-list-listed"] = ClassSlot("member-hold-list-listed", "Listed", BoxId, (BoxT, ListOfHoldListT)),
                    ["member-hold-list-shelf-entry"] = ClassSlot("member-hold-list-shelf-entry", "Entry", BoxId, (BoxT, ListOfHoldListT)),
                    ["member-hold-list-shelf"] = new ListMember
                    {
                        id = "member-hold-list-shelf",
                        projectId = "project",
                        name = "Shelf",
                        kind = MemberKind.List,
                        Requirement = NeoMemberRequirementKind.Optional,
                        entryMemberId = "member-hold-list-shelf-entry",
                    },
                    ["member-hold-list-both"] = new TupleMember
                    {
                        id = "member-hold-list-both",
                        projectId = "project",
                        name = "Both",
                        kind = MemberKind.Tuple,
                        Requirement = NeoMemberRequirementKind.Optional,
                        elementMemberIds = new[] { "member-hold-list-both-t", "member-hold-tuple-seed" },
                    },
                    ["member-hold-list-both-t"] = new GenericMember
                    {
                        id = "member-hold-list-both-t",
                        projectId = "project",
                        name = "Item1",
                        kind = MemberKind.Generic,
                        Requirement = NeoMemberRequirementKind.Required,
                        genericParamId = HoldListT,
                    },
                    ["member-list-shelved"] = ClassSlot("member-list-shelved", "Shelved", HoldListId, (HoldListT, Tool)),
                    ["member-called"] = ClassSlot("member-called", "Called", BoxId),
                    ["member-tuple-seeds"] = BoxOf("member-tuple-seeds", "TupleSeeds", SeedTuple(SeedId)),
                    ["member-tuple-tools"] = BoxOf("member-tuple-tools", "TupleTools", SeedTuple(ToolId)),
                    ["member-makers"] = BoxOf("member-makers", "Makers", Maker(SeedId)),
                    ["member-mixed-makers"] = BoxOf("member-mixed-makers", "MixedMakers", Maker(ToolId)),
                    ["member-void-makers"] = BoxOf("member-void-makers", "VoidMakers", new DelegateTypeInfo
                    {
                        type = MemberKind.NSDelegate,
                        required = true,
                        returnTypeInfo = new VoidTypeInfo { type = MemberKind.Void, required = true },
                        argumentTypes = Array.Empty<TypeInfo>(),
                    }),
                    ["member-nullable-boxed"] = ClassSlot("member-nullable-boxed", "NullableBoxed", BoxId, (BoxT, NullableString)),
                    ["member-holding-held"] = ForwardSlot("member-holding-held", "Held", HoldId, HoldT, HoldingD),
                    ["member-list-holding-held"] = ForwardSlot("member-list-holding-held", "Held", HoldId, HoldT, ListHoldingT),
                    ["member-holding-boxed"] = ClassSlot("member-holding-boxed", "HoldingBoxed", BoxId, (BoxT, Seed)),
                    ["member-list-holding-boxed"] = ClassSlot("member-list-holding-boxed", "ListHoldingBoxed", BoxId, (BoxT, SeedList)),
                    ["member-held-seeds"] = BoxLookup("member-held-seeds", "HeldSeeds", HoldId, HoldT, SeedId),
                    ["member-held-tools"] = BoxLookup("member-held-tools", "HeldTools", HoldId, HoldT, ToolId),
                    ["member-action-boxed"] = ClassSlot("member-action-boxed", "ActionBoxed", BoxId, (BoxT, NullableStringAction)),
                    ["member-actions"] = BoxOf("member-actions", "Actions", StringAction(required: true)),
                    ["member-nullable-actions"] = BoxOf("member-nullable-actions", "NullableActions", StringAction(required: false)),
                    ["member-strings"] = BoxOf("member-strings", "Strings", new PrimitiveTypeInfo { type = MemberKind.String, required = true }),
                    ["member-nullable-strings"] = BoxOf("member-nullable-strings", "NullableStrings", new PrimitiveTypeInfo { type = MemberKind.String, required = false }),
                },
                interfaces = new Dictionary<string, Interface>
                {
                    [HoldsId] = new Interface
                    {
                        id = HoldsId,
                        projectId = "project",
                        name = "IHolds",
                        members = new Dictionary<string, InterfaceMember>(),
                        genericParams = new List<GenericParamDeclaration> { new() { id = HoldsT, name = "T" } },
                    },
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["root-assets-value"] = Row("root-assets-value", "class-root"),
                    ["root-save-value"] = Row("root-save-value", "class-holder", new Dictionary<string, string>
                    {
                        ["Boxed"] = "derived-row",
                        ["SeedBoxed"] = "seed-box-row",
                        ["Loose"] = "loose-row",
                        ["Paired"] = "pair-row",
                        ["ToolBoxed"] = "weird-row",
                        ["Listed"] = "list-row",
                        ["Twinned"] = "twin-row",
                        ["Planted"] = "seed-row",
                        ["Narrowed"] = "narrow-row",
                        ["Held"] = "held-row",
                        ["Called"] = "callback-row",
                        ["NullableBoxed"] = "nullable-box-row",
                        ["HoldingBoxed"] = "b-row",
                        ["ListHoldingBoxed"] = "l-row",
                        ["ActionBoxed"] = "action-box-row",
                    }),
                    ["root-session-value"] = Row("root-session-value", "class-root"),
                    ["derived-row"] = Row("derived-row", DerivedBoxId),
                    ["seed-box-row"] = Row("seed-box-row", SeedBoxId),
                    ["nullable-box-row"] = Row("nullable-box-row", NullableBoxId),
                    ["action-box-row"] = Row("action-box-row", ActionBoxId),
                    ["b-row"] = Row("b-row", HoldingId, new Dictionary<string, string> { ["Held"] = "b-held" }),
                    ["b-held"] = Row("b-held", HoldId, new Dictionary<string, string> { ["Inner"] = "b-inner" }),
                    ["b-inner"] = Row("b-inner", DerivedBoxId),
                    ["l-row"] = Row("l-row", ListHoldingId, new Dictionary<string, string> { ["Held"] = "l-held" }),
                    ["l-held"] = Row("l-held", HoldId, new Dictionary<string, string> { ["Inner"] = "l-inner" }),
                    ["l-inner"] = Row("l-inner", DerivedBoxId),
                    ["loose-row"] = Row("loose-row", DerivedBoxId, new Dictionary<string, string> { ["k"] = "loose-k" }),
                    ["pair-row"] = Row("pair-row", DerivedPairId, new Dictionary<string, string> { ["k"] = "pair-k" }),
                    ["weird-row"] = Row("weird-row", WeirdId, new Dictionary<string, string> { ["k"] = "weird-k" }),
                    ["list-row"] = Row("list-row", ListBoxId, new Dictionary<string, string> { ["k"] = "list-k" }),
                    ["twin-row"] = Row("twin-row", TwinId),
                    ["seed-row"] = Row("seed-row", SeedId),
                    ["narrow-row"] = Row("narrow-row", NarrowId, new Dictionary<string, string> { ["k"] = "narrow-k" }),
                    ["narrow-k"] = new NumberMemberValue { id = "narrow-k", value = 10d },
                    ["held-row"] = Row("held-row", HoldId, new Dictionary<string, string>
                    {
                        ["Inner"] = "held-inner-row",
                        ["Pair"] = "held-pair-row",
                    }),
                    ["held-inner-row"] = Row("held-inner-row", DerivedBoxId, new Dictionary<string, string> { ["k"] = "held-inner-k" }),
                    ["held-pair-row"] = Row("held-pair-row", BoxId, new Dictionary<string, string> { ["k"] = "held-pair-k" }),
                    ["callback-row"] = Row("callback-row", CallbackId, new Dictionary<string, string> { ["k"] = "callback-k" }),
                    ["held-inner-k"] = new NumberMemberValue { id = "held-inner-k", value = 11d },
                    ["held-pair-k"] = new NumberMemberValue { id = "held-pair-k", value = 12d },
                    ["callback-k"] = new NumberMemberValue { id = "callback-k", value = 15d },
                    ["weird-k"] = new NumberMemberValue { id = "weird-k", value = 8d },
                    ["list-k"] = new NumberMemberValue { id = "list-k", value = 9d },
                    ["loose-k"] = new NumberMemberValue { id = "loose-k", value = 6d },
                    ["pair-k"] = new NumberMemberValue { id = "pair-k", value = 7d },
                },
            };
        }

        private static readonly TypeBinding Seed = TestTypeBindings.Class(SeedId);
        private static readonly TypeBinding Tool = TestTypeBindings.Class(ToolId);
        private static readonly TypeBinding SeedList = TestTypeBindings.List(Seed);
        private static readonly TypeBinding NullableString = TestTypeBindings.Of(MemberKind.String, required: false);
        private static readonly TypeBinding NullableStringAction = Action(NullableString);
        private static readonly TypeBinding StringActionArgument = Action(TestTypeBindings.Of(MemberKind.String));
        private static readonly TypeBinding HoldTuple = TestTypeBindings.Tuple(Forward(HoldT), Seed);
        private static readonly TypeBinding ListOfHoldListT = TestTypeBindings.List(Forward(HoldListT));
        private static readonly TypeBinding ListOfListBoxT = TestTypeBindings.List(Forward("class-list-box-t"));
        private static readonly TypeBinding ListOfListHoldingT = TestTypeBindings.List(Forward(ListHoldingT));
        private static readonly TypeBinding CallbackArgument = TypeBinding.Closed(
            new TypeHead(MemberKind.NSDelegate, true),
            TestTypeBindings.Arguments(("0", Forward(CallbackT)), ("1", Forward(CallbackT))));

        private static TypeBinding Action(TypeBinding argument) =>
            TypeBinding.Closed(new TypeHead(MemberKind.NSAction, true), TestTypeBindings.Arguments(("0", argument)));

        /// <summary>
        /// The class or interface declaring <paramref name="paramId"/>'s own
        /// parameter, forwarded: each fixture parameter id is its owner's id
        /// with a parameter suffix.
        /// </summary>
        private static TypeBinding Forward(string paramId) =>
            paramId == HoldsT
                ? TestTypeBindings.OpenInterface(HoldsId, paramId)
                : TestTypeBindings.Open(paramId.Substring(0, paramId.LastIndexOf('-')), paramId);

        /// <summary><c>Box&lt;T&gt; : IHolds&lt;T&gt;</c>.</summary>
        private static NeoSchemaClass Implementing(NeoSchemaClass box)
        {
            box.implementsInterfaces = new List<InterfaceConstruction>
            {
                new()
                {
                    interfaceId = HoldsId,
                    arguments = new Dictionary<string, TypeBinding>
                    {
                        [HoldsT] = Forward(BoxT),
                    },
                },
            };
            return box;
        }

        private static NeoSchemaClass Extending(NeoSchemaClass schemaClass, TypeBinding boxArgument)
        {
            schemaClass.extendsClassId = BoxId;
            schemaClass.extendsGenericBindings = new Dictionary<string, TypeBinding> { [BoxT] = boxArgument };
            return schemaClass;
        }

        private static NeoSchemaClass Class(
            string id,
            string name,
            Dictionary<string, string>? schema = null,
            (string Id, string Name)? genericParam = null,
            (string Id, string Name)? secondParam = null)
        {
            var schemaClass = new NeoSchemaClass
            {
                id = id,
                projectId = "project",
                name = name,
                schema = schema ?? new Dictionary<string, string>(),
            };
            if (genericParam is { } param)
                schemaClass.genericParams = new List<GenericParamDeclaration> { new() { id = param.Id, name = param.Name } };
            if (secondParam is { } second)
                schemaClass.genericParams!.Add(new GenericParamDeclaration { id = second.Id, name = second.Name });
            return schemaClass;
        }

        private static ClassMember ClassSlot(
            string id,
            string name,
            string classId,
            (string Param, TypeBinding Binding)? argument = null,
            NeoMemberRequirementKind requirement = NeoMemberRequirementKind.Required)
        {
            var slot = new ClassMember
            {
                id = id,
                projectId = "project",
                name = name,
                kind = MemberKind.Class,
                Requirement = requirement,
                DeclaredClassId = classId,
            };
            if (argument is { } bound)
                slot.classArguments = new Dictionary<string, TypeBinding> { [bound.Param] = bound.Binding };
            return slot;
        }

        private static LookupMember BoxLookup(string id, string name, string classId, string param, string argumentClassId, bool list = false) =>
            new()
            {
                id = id,
                projectId = "project",
                name = name,
                kind = MemberKind.Lookup,
                Requirement = NeoMemberRequirementKind.Optional,
                Selection = NeoMemberSelectionKind.Single,
                declaredTypeInfo = new ClassTypeInfo
                {
                    type = MemberKind.Class,
                    required = true,
                    classId = classId,
                    typeArguments = new Dictionary<string, TypeInfo>
                    {
                        [param] = list
                            ? new CollectionTypeInfo { type = MemberKind.List, required = true, entryTypeInfo = ClassType(argumentClassId) }
                            : ClassType(argumentClassId),
                    },
                },
            };

        /// <summary><c>Twin&lt;T&gt; : Box2&lt;T, T&gt;</c>.</summary>
        private static NeoSchemaClass Twin()
        {
            NeoSchemaClass twin = Class(TwinId, "Twin", genericParam: ("class-twin-t", "T"));
            twin.extendsClassId = Box2Id;
            twin.extendsGenericBindings = new Dictionary<string, TypeBinding>
            {
                ["class-box2-a"] = Forward("class-twin-t"),
                ["class-box2-b"] = Forward("class-twin-t"),
            };
            return twin;
        }

        /// <summary><c>SeedPair : DerivedPair&lt;Seed, Tool&gt;</c>.</summary>
        private static NeoSchemaClass SeedPair()
        {
            NeoSchemaClass seedPair = Class(SeedPairId, "SeedPair");
            seedPair.extendsClassId = DerivedPairId;
            seedPair.extendsGenericBindings = new Dictionary<string, TypeBinding>
            {
                [DerivedPairT] = Seed,
                ["class-derived-pair-u"] = Tool,
            };
            return seedPair;
        }

        /// <summary><c>Narrow&lt;T extends Seed&gt; : Box&lt;T&gt;</c>.</summary>
        private static NeoSchemaClass Narrow()
        {
            NeoSchemaClass narrow = Extending(
                Class(NarrowId, "Narrow", genericParam: ("class-narrow-t", "T")),
                Forward("class-narrow-t"));
            narrow.genericParams![0].constraints = new List<GenericParamConstraint>
            {
                new() { kind = NeoGenericParamConstraintKind.Class, classId = SeedId },
            };
            return narrow;
        }

        /// <summary>
        /// A Box in a Hold&lt;Tool&gt;'s <c>Box&lt;(T, Seed)&gt; Pair</c>, stamped
        /// as stored: no argument binds a param to a member naming one.
        /// </summary>
        private static ObjectMemberValue ToolSeedPair()
        {
            ObjectMemberValue row = Row("new-pair", BoxId);
            row.genericBindings = TestTypeBindings.Stamp((BoxT, TestTypeBindings.Tuple(TestTypeBindings.Class(ToolId), TestTypeBindings.Class(SeedId))));
            return row;
        }

        /// <summary>Hold's <c>Box&lt;T&gt; Inner</c>.</summary>
        private static ClassMember HoldInner()
        {
            ClassMember slot = ClassSlot("member-hold-inner", "Inner", BoxId);
            slot.classArguments = new Dictionary<string, TypeBinding>
            {
                [BoxT] = Forward(HoldT),
            };
            return slot;
        }

        /// <summary><c>(first, Seed)</c>.</summary>
        private static TupleTypeInfo SeedTuple(string firstClassId) =>
            new()
            {
                type = MemberKind.Tuple,
                required = true,
                elements = new[]
                {
                    new TupleElementTypeInfo { typeInfo = ClassType(firstClassId) },
                    new TupleElementTypeInfo { typeInfo = ClassType(SeedId) },
                },
            };

        /// <summary>A delegate returning Seed that takes a <paramref name="parameterClassId"/>.</summary>
        private static DelegateTypeInfo Maker(string parameterClassId) =>
            new()
            {
                type = MemberKind.NSDelegate,
                required = true,
                returnTypeInfo = ClassType(SeedId),
                argumentTypes = new TypeInfo[] { ClassType(parameterClassId) },
            };

        private static LookupMember BoxOf(string id, string name, TypeInfo argument) =>
            new()
            {
                id = id,
                projectId = "project",
                name = name,
                kind = MemberKind.Lookup,
                Requirement = NeoMemberRequirementKind.Optional,
                Selection = NeoMemberSelectionKind.Single,
                declaredTypeInfo = new ClassTypeInfo
                {
                    type = MemberKind.Class,
                    required = true,
                    classId = BoxId,
                    typeArguments = new Dictionary<string, TypeInfo> { [BoxT] = argument },
                },
            };

        /// <summary>A <c>Box2&lt;Seed, Seed&gt;</c> slot.</summary>
        private static ClassMember TwinnedSlot()
        {
            ClassMember slot = ClassSlot("member-twinned", "Twinned", Box2Id);
            slot.classArguments = new Dictionary<string, TypeBinding>
            {
                ["class-box2-a"] = Seed,
                ["class-box2-b"] = Seed,
            };
            return slot;
        }

        /// <summary>A <paramref name="classId"/> slot passing <paramref name="param"/> its owner's <paramref name="forwardParamId"/>.</summary>
        private static ClassMember ForwardSlot(string id, string name, string classId, string param, string forwardParamId)
        {
            ClassMember slot = ClassSlot(id, name, classId);
            slot.classArguments = new Dictionary<string, TypeBinding>
            {
                [param] = Forward(forwardParamId),
            };
            return slot;
        }

        /// <summary><c>NeoAction&lt;string&gt;</c>, or <c>NeoAction&lt;string?&gt;</c>.</summary>
        private static ActionTypeInfo StringAction(bool required) =>
            new()
            {
                type = MemberKind.NSAction,
                required = true,
                argumentTypes = new TypeInfo[] { new PrimitiveTypeInfo { type = MemberKind.String, required = required } },
            };

        private static ListMember ListOf(string id, string entryMemberId) =>
            new()
            {
                id = id,
                projectId = "project",
                name = "T",
                kind = MemberKind.List,
                Requirement = NeoMemberRequirementKind.Required,
                entryMemberId = entryMemberId,
            };

        /// <summary><c>Box2&lt;Seed, Seed&gt;</c>, or <c>Box2&lt;Seed, Tool&gt;</c> when <paramref name="mixed"/>.</summary>
        private static Dictionary<string, TypeInfo> ToolOrSeed(bool mixed) =>
            new()
            {
                ["class-box2-a"] = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = SeedId },
                ["class-box2-b"] = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = mixed ? ToolId : SeedId },
            };

        private static LookupMember Box2Lookup(string id, string name, Dictionary<string, TypeInfo> typeArguments) =>
            new()
            {
                id = id,
                projectId = "project",
                name = name,
                kind = MemberKind.Lookup,
                Requirement = NeoMemberRequirementKind.Optional,
                Selection = NeoMemberSelectionKind.Single,
                declaredTypeInfo = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = Box2Id, typeArguments = typeArguments },
            };

        private static LookupMember HoldsLookup(string id, string name, string argumentClassId) =>
            new()
            {
                id = id,
                projectId = "project",
                name = name,
                kind = MemberKind.Lookup,
                Requirement = NeoMemberRequirementKind.Optional,
                Selection = NeoMemberSelectionKind.Single,
                declaredTypeInfo = new InterfaceTypeInfo
                {
                    type = MemberKind.Interface,
                    required = true,
                    interfaceId = HoldsId,
                    typeArguments = new Dictionary<string, TypeInfo>
                    {
                        [HoldsT] = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = argumentClassId },
                    },
                },
            };

        private static ClassTypeInfo ClassType(string classId) =>
            new()
            {
                type = MemberKind.Class,
                required = true,
                classId = classId
            };

        private static ClassMember RootMember(string id, string classId) =>
            new()
            {
                id = id,
                projectId = "project",
                name = id,
                kind = MemberKind.Class,
                Requirement = NeoMemberRequirementKind.Required,
                valueId = id + "-value",
                DeclaredClassId = classId,
            };

        private static ObjectMemberValue Row(string id, string classId, Dictionary<string, string>? value = null) =>
            new()
            {
                id = id,
                classId = classId,
                createdAt = "x",
                updatedAt = "x",
                value = value ?? new Dictionary<string, string>(),
            };
    }
}
