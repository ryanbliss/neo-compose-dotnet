// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public partial class NeoOwnedCollectionInsertionTests
    {
        private static JObject SelectionInt(int value) => new()
        {
            ["type"] = "value",
            ["value"] = new JObject
            {
                ["typeInfo"] = new JObject { ["type"] = (int)MemberKind.Int, ["required"] = true },
                ["value"] = value
            }
        };
        private static JObject SelectionVariable(string name) => new() { ["type"] = "variable", ["variableId"] = name };
        private static JObject SelectionCount(JToken pointer) => new()
        {
            ["type"] = "function",
            ["function"] = new JObject
            {
                ["type"] = "count",
                ["info"] = new JObject { ["collectionPointer"] = pointer.DeepClone() }
            }
        };
        private static JObject SelectionArithmetic(string op, params JToken[] pointers) => new()
        {
            ["type"] = "operation",
            ["operation"] = new JObject
            {
                ["type"] = "arithmetic",
                ["arithmetic"] = new JObject
                {
                    ["type"] = op,
                    ["pointers"] = new JArray(pointers.Select(p => p.DeepClone()))
                }
            }
        };
        private static JObject SelectionCounts(params JToken[] pointers)
        {
            JObject value = SelectionCount(pointers[0]);
            foreach (JToken pointer in pointers.Skip(1))
                value = SelectionArithmetic("+", SelectionArithmetic("*", value, SelectionInt(10)), SelectionCount(pointer));
            return new JObject { ["type"] = "return", ["pointer"] = value };
        }

        // Mutate only instructions from the unchanged normal-export fixture.
        // The native is a declared test host boundary, not a descriptor seed.
        private static (NeoClient client, NSFunctionMember function) SelectionBoundary(string mode, bool native, bool finalBoundary = false)
        {
            var schema = NeoTestExport.Read(File.ReadAllText(Root + "paired-export.json"));
            var function = (NSFunctionMember)schema.members.Values.Single(m => m.name == "LocalCollectionReferenceOwnLater");
            var body = JObject.FromObject(function.action);
            var original = (JArray)body["instructions"]!;
            JToken early = original[5]!["target"]!["pointer"]!.DeepClone();
            JToken field = original[4]!["target"]!["pointer"]!.DeepClone();
            JToken second = field.DeepClone();
            second["keyOf"]!["pointer"]!["variableId"] = "p";
            JToken late = early.DeepClone();
            late["pointer"]!["keyOf"]!["key"] = SelectionInt(1);
            JToken fieldReference = early.DeepClone();
            fieldReference["pointer"]!["keyOf"]!["key"] = SelectionInt(2);
            JToken Replace(params int[] values)
            {
                JToken replacement = original[4]!.DeepClone();
                replacement["pointer"] = new JObject
                {
                    ["type"] = "listLiteral",
                    ["typeInfo"] = original[1]!["variable"]!["typeInfo"]!.DeepClone(),
                    ["entries"] = new JArray(values.Select(SelectionInt))
                };
                return replacement;
            }
            JToken Add(JToken pointer)
            {
                JToken call = original[5]!.DeepClone();
                call["target"]!["pointer"] = pointer.DeepClone();
                return call;
            }
            JToken Select(JToken pointer)
            {
                JToken call = original[2]!.DeepClone();
                call["args"] = new JArray(pointer.DeepClone());
                return call;
            }
            JObject boundary = JObject.Parse("{ 'type':'variable', 'variable':{ 'id':'selectionBoundary', 'typeInfo':{ 'type':2, 'required':true }, 'pointer':{ 'type':'callFunction', 'memberId':'selection-native', 'receiver':{ 'kind':'static', 'memberId':'selection-native' }, 'args':[], 'callSiteId':'selection-native-site' } } }");
            var steps = new JArray(original.Take(3).Select(x => x.DeepClone()));
            if (native)
                steps.Add(boundary.DeepClone());
            steps.Add(original[3]!.DeepClone());
            steps.Add(original[4]!.DeepClone());
            switch (mode)
            {
                case "native":
                case "repeat-native":
                    if (mode == "repeat-native")
                    {
                        steps.Add(boundary.DeepClone());
                        steps.Add(boundary.DeepClone());
                    }
                    steps.Add(original[5]!.DeepClone());
                    steps.Add(original[6]!.DeepClone());
                    break;
                case "held-clear-detach":
                    JToken clear = original[2]!.DeepClone();
                    clear["mutation"] = "Clear";
                    clear["args"] = new JArray();
                    steps.Add(clear);
                    steps.Add(Replace(1, 2, 3));
                    steps.Add(SelectionCounts(SelectionVariable("a"), field));
                    break;
                case "early-detach":
                    steps.Add(Replace(1, 2, 3));
                    steps.Add(Add(early));
                    steps.Add(SelectionCounts(SelectionVariable("a"), early, field));
                    break;
                case "early-late-detach":
                case "late-detach":
                    steps.Add(Select(SelectionVariable("a")));
                    steps.Add(Select(field));
                    steps.Add(Replace(1, 2, 3));
                    steps.Add(Add(mode == "late-detach" ? late : SelectionVariable("a")));
                    steps.Add(mode == "late-detach"
                        ? SelectionCounts(SelectionVariable("a"), late, fieldReference, field)
                        : SelectionCounts(SelectionVariable("a"), early, late, fieldReference, field));
                    break;
                case "rebind":
                case "field-copy":
                    JToken create = original[3]!.DeepClone();
                    create["variable"]!["id"] = "p";
                    steps.Add(create);
                    if (mode == "rebind")
                        steps.Add(Replace(1));
                    JToken assign = original[4]!.DeepClone();
                    assign["target"]!["pointer"] = second.DeepClone();
                    if (mode == "field-copy")
                        assign["pointer"] = field.DeepClone();
                    steps.Add(assign);
                    steps.Add(Add(mode == "rebind" ? early : field));
                    steps.Add(mode == "rebind"
                        ? SelectionCounts(field, second, SelectionVariable("a"), early)
                        : SelectionCounts(field, second, early));
                    break;
                default:
                    throw new ArgumentException(mode);
            }
            if (finalBoundary)
                steps.Insert(steps.Count - 1, boundary.DeepClone());
            body["instructions"] = steps;
            function.action = body.ToObject<FunctionWithReturnType>()!;
            schema.members["selection-native"] = new FunctionMember
            {
                id = "selection-native",
                name = "SelectionNative",
                kind = MemberKind.Function,
                Modifier = NeoMemberModifierKind.Static,
                Dispatch = NeoFunctionDispatchKind.Synchronous,
                returnTypeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>()
            };
            schema.classes.Values.Single(c => c.schema.Values.Contains(function.id)).schema["SelectionNative"] = "selection-native";
            if (mode == "held-clear-detach")
            {
                // The Gain control needs an ordinary stored root. An unrooted
                // local holder is deliberately eligible for orphan collection.
                var holder = schema.classes.Values.Single(c => c.name == "Holder");
                var entry = schema.members.Values.OfType<LookupMember>().First(m =>
                    m.declaredTypeInfo is CollectionTypeInfo { type: MemberKind.List, entryTypeInfo: PrimitiveTypeInfo { type: MemberKind.Int } });
                var root = JObject.FromObject(schema.members.Values.OfType<ClassMember>().First(m =>
                    m.TryGetDeclaredClassId(out string id) && id == holder.id));
                root["id"] = "selection-held-root";
                root["name"] = "SelectionHeldRoot";
                root.Remove("valueId");
                root["modifier"] = (int)NeoMemberModifierKind.Static;
                root["storage"] = (int)NeoMemberStorage.Session;
                root["classArguments"] = new JObject
                {
                    [holder.genericParams![0].id] = new JObject { ["kind"] = 1, ["memberId"] = entry.id }
                };
                schema.members["selection-held-root"] = root.ToObject<ClassMember>()!;
                schema.classes.Values.Single(c => c.schema.Values.Contains(function.id)).schema["SelectionHeldRoot"] = "selection-held-root";
            }
            return (NeoTestSaveStack.ClientFromSchema(schema, assumeCurrentSchema: false), function);
        }

        [TestCase("native", 22)]
        [TestCase("repeat-native", 22)]
        [TestCase("early-detach", 223)]
        [TestCase("early-late-detach", 22333)]
        [TestCase("late-detach", 1444)]
        [TestCase("rebind", 1222)]
        [TestCase("field-copy", 212)]
        public void EarlyLocalSelectionPreservesExistingBindingRules(string mode, int expected)
        {
            foreach (bool native in new[] { false, true })
            {
                var setup = SelectionBoundary(mode, native);
                using var client = setup.client;
                int calls = 0;
                client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
                {
                    ["selection-native"] = (c, _, _, _) => { calls++; c.CommitScriptRows(); return 1; }
                });
                Assert.AreEqual(expected, NeoMemberNSFunction.GetOrCreateStatic(client, setup.function.id).InvokeStatic(), "native=" + native);
                Assert.AreEqual((native ? 1 : 0) + (mode == "repeat-native" ? 2 : 0), calls);
            }
        }

        [Test]
        public void PublishedHostReferenceKeepsItsTargetAcrossEarlyLocalBinding()
        {
            var setup = SelectionBoundary("repeat-native", true);
            using var client = setup.client;
            NeoReference<NeoList<int>>? first = null;
            int hash = 0;
            string? firstId = null;
            string? selectedAfterBind = null;
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["selection-native"] = (c, _, _, _) =>
                {
                    // Only this invocation's Lookup entry selects a List row.
                    var row = c.sessionValues.Values.OfType<ArrayMemberValue>().Single(r => r.value?.Length == 1
                        && c.sessionValues.TryGetValue(r.value[0], out MemberValue? target) && target is ArrayMemberValue);
                    var member = c.members.Values.OfType<LookupMember>().First(m =>
                        m.declaredTypeInfo is CollectionTypeInfo { type: MemberKind.List, entryTypeInfo: PrimitiveTypeInfo { type: MemberKind.Int } });
                    if (first is null)
                    {
                        firstId = row.value![0];
                        first = new NeoReference<NeoList<int>>(c, firstId, (LookupMember)member!, row.id);
                        hash = first.GetHashCode();
                    }
                    else
                    {
                        selectedAfterBind = row.value![0];
                        Assert.AreEqual(firstId, first.TargetId);
                        Assert.AreEqual(hash, first.GetHashCode());
                        Assert.AreEqual(first, new NeoReference<NeoList<int>>(c, firstId!, (LookupMember)member!, row.id));
                        Assert.AreEqual(1, first.Value.Count);
                    }
                    return 1;
                }
            });
            Assert.AreEqual(22, NeoMemberNSFunction.GetOrCreateStatic(client, setup.function.id).InvokeStatic());
            Assert.AreNotEqual(firstId, selectedAfterBind);
            Assert.AreEqual(firstId, first!.TargetId);
        }
        [TestCase("native", 22)]
        [TestCase("repeat-native", 22)]
        [TestCase("early-detach", 223)]
        [TestCase("early-late-detach", 22333)]
        [TestCase("late-detach", 1444)]
        [TestCase("rebind", 1222)]
        [TestCase("field-copy", 212)]
        public void PublishedHostCollectionViewsKeepFixedLiveTargets(string mode, int expected)
        {
            var setup = SelectionBoundary(mode, true, finalBoundary: true);
            using var client = setup.client;
            NeoReference<NeoList<int>>? reference = null;
            NeoList<int>? view = null;
            string? id = null;
            int hash = 0;
            int calls = 0;
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["selection-native"] = (c, _, _, _) =>
                {
                    calls++;
                    c.CommitScriptRows();
                    var row = c.sessionValues.Values.OfType<ArrayMemberValue>().First(r => r.value?.Length == 1
                        && c.sessionValues.TryGetValue(r.value[0], out MemberValue? target) && target is ArrayMemberValue);
                    var member = c.members.Values.OfType<LookupMember>().First(m =>
                        m.declaredTypeInfo is CollectionTypeInfo { type: MemberKind.List, entryTypeInfo: PrimitiveTypeInfo { type: MemberKind.Int } });
                    if (reference is null)
                    {
                        var node = (NeoMemberLookup)NeoMember.CreateWritable(c, member, row.id, NeoValueOwnership.Session);
                        reference = NeoGeneratedTypesSupport.ReadReference<NeoList<int>>(c, node)!;
                        // The concrete generated getter uses this selected-node codec.
                        view = NeoGenericBindings.Referenced<NeoList<int>, NeoList<int>>(c, node.GetFirstSelected()!);
                        id = reference.TargetId;
                        hash = reference.GetHashCode();
                    }
                    Assert.AreEqual(id, reference.TargetId);
                    Assert.AreEqual(hash, reference.GetHashCode());
                    Assert.AreEqual(reference, new NeoReference<NeoList<int>>(c, id!, member, row.id));
                    Assert.AreEqual(1, reference.Value.Count);
                    Assert.AreEqual(1, view!.Count);
                    return 1;
                }
            });
            Assert.AreEqual(expected, NeoMemberNSFunction.GetOrCreateStatic(client, setup.function.id).InvokeStatic());
            Assert.GreaterOrEqual(calls, 2);
            Assert.IsFalse(client.sessionValues.ContainsKey(id!));
            Assert.Catch(() => { _ = reference!.Value.Count; });
            Assert.AreEqual(id, reference!.TargetId);
            Assert.AreEqual(hash, reference.GetHashCode());
        }

        [TestCase("normal")]
        [TestCase("readonly")]
        [TestCase("optional")]
        [TestCase("wrong")]
        [TestCase("missing")]
        [TestCase("deleted")]
        [TestCase("open")]
        [TestCase("partial")]
        public void TypedHostCollectionReferenceValidatesTarget(string mode)
        {
            foreach (var ownership in new[] { NeoValueOwnership.Session, NeoValueOwnership.Save })
            {
                var schema = NeoTestExport.Read(File.ReadAllText(Root + "paired-export.json"));
                var ownerClass = schema.classes.Values.Single(c => c.name == "LocalReferenceOwner");
                var list = (ListMember)schema.members[ownerClass.schema["Values"]];
                if (mode == "readonly")
                    list.Mutability = NeoMemberMutabilityKind.ReadOnly;
                if (mode == "optional")
                    list.Requirement = NeoMemberRequirementKind.Optional;
                if (mode == "wrong")
                    list.entryMemberId = schema.members.Values.First(m => m.kind == MemberKind.String
                        && m.Requirement == NeoMemberRequirementKind.Required).id;
                var lookup = schema.members.Values.OfType<LookupMember>().First(m =>
                    m.declaredTypeInfo is CollectionTypeInfo { type: MemberKind.List, entryTypeInfo: PrimitiveTypeInfo { type: MemberKind.Int } });
                if (mode is "open" or "partial")
                {
                    var genericClass = schema.classes.Values.First(c => c.genericParams?.Count > 0);
                    lookup.declaredTypeInfo = new GenericTypeInfo
                    {
                        type = MemberKind.Generic,
                        required = true,
                        ownerClassId = genericClass.id,
                        genericParamId = genericClass.genericParams![0].id
                    };
                }
                using var client = NeoTestSaveStack.ClientFromSchema(schema, assumeCurrentSchema: false);
                string root = Guid.NewGuid().ToString();
                string id = Guid.NewGuid().ToString();
                string child = Guid.NewGuid().ToString();
                string entry = Guid.NewGuid().ToString();
                var plan = new NeoWritePlan(client);
                plan.Set(ownership, new ObjectMemberValue { id = root, classId = ownerClass.id, value = new() { ["Values"] = id } });
                if (mode != "missing")
                {
                    plan.Set(ownership, new ArrayMemberValue { id = id, value = new[] { child } });
                    if (mode == "wrong")
                        plan.Set(ownership, new StringMemberValue { id = child, value = "seven" });
                    else
                        plan.Set(ownership, new NumberMemberValue { id = child, value = 7 });
                }
                plan.Set(ownership, new ArrayMemberValue
                {
                    id = entry,
                    value = new[] { id },
                    genericBindings = mode == "partial" ? new() { ["unrelated"] = list.entryMemberId } : null
                });
                plan.Commit();
                if (mode == "deleted")
                {
                    var removal = new NeoWritePlan(client);
                    removal.Remove(ownership, id);
                    removal.Commit();
                }
                var node = (NeoMemberLookup)NeoMember.CreateWritable(client, lookup, entry, ownership);
                var reference = NeoGeneratedTypesSupport.ReadReference<NeoList<int>>(client, node)!;
                if (mode is "wrong" or "missing" or "deleted" or "optional" or "open" or "partial")
                {
                    Assert.Catch(() => { _ = reference.Value.Count; }, ownership + " " + mode);
                    continue;
                }
                var concrete = NeoGenericBindings.Referenced<NeoReadOnlyList<int>, NeoList<int>>(client, node.GetFirstSelected()!);
                Assert.AreEqual(1, reference.Value.Count);
                Assert.AreEqual(7, reference.Value[0]);
                Assert.AreEqual(1, concrete.Count);
                Assert.AreEqual(id, reference.TargetId);
            }
        }

        [Test]
        public async Task EarlyLocalSelectionSurvivesDeferredNativePublication()
        {
            var setup = SelectionBoundary("native", true);
            using var client = setup.client;
            setup.function.Dispatch = NeoFunctionDispatchKind.Asynchronous;
            ((FunctionMember)client.members["selection-native"]).Dispatch = NeoFunctionDispatchKind.Asynchronous;
            NeoDeferredFunction<int>? pending = null;
            string? selected = null;
            client.RegisterDeferredNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
            {
                ["selection-native"] = (c, _, _, _, deferred) =>
                {
                    c.CommitScriptRows();
                    var row = c.sessionValues.Values.OfType<ArrayMemberValue>().Single(r => r.value?.Length == 1
                        && c.sessionValues.TryGetValue(r.value[0], out MemberValue? target) && target is ArrayMemberValue);
                    selected = row.value![0];
                    pending = NeoGeneratedTypesSupport.ResolveDeferredFunction<NeoDeferredFunction<int>>(deferred, "SelectionNative");
                }
            });
            var invocation = NeoMemberNSFunction.GetOrCreateStatic(client, setup.function.id).InvokeStaticAsync(Array.Empty<object?>());
            Assert.IsNotNull(pending);
            Assert.IsFalse(invocation.IsCompleted);
            Assert.IsTrue(client.sessionValues.ContainsKey(selected!));
            pending!.Complete(1);
            Assert.AreEqual(22, await invocation);
            Assert.IsFalse(client.sessionValues.ContainsKey(selected!));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CapturingAnotherOccurrenceDoesNotRewritePublishedSelectors(bool retirePrevious)
        {
            var setup = SelectionBoundary("native", false);
            using var client = setup.client;
            var context = new NSGetterEvaluator.Context(client, null, null);
            object?[] local = { 7d };
            NSGetterEvaluator.CaptureRuntimeList(local);
            string? previous = null;
            for (int i = 0; i < 32; i++)
            {
                if (retirePrevious && previous is not null)
                {
                    var removal = new NeoWritePlan(client);
                    removal.Remove(NeoValueOwnership.Session, previous);
                    removal.Commit();
                }
                var batch = new NeoWriteBatch(new NeoWritePlan(client), false);
                string id = Guid.NewGuid().ToString();
                batch.Stage(b => StageEarlySelection(b, context, local, id));
                batch.Plan.Commit();
                Assert.AreEqual(i == 0 ? 3 : 1, batch.Plan.Rows.Count, "Only the first publication allocates the target graph.");
                previous = id;
            }
        }

        [Test]
        public void UnchangedEarlySelectionTargetStagesNoRows()
        {
            var setup = SelectionBoundary("native", false);
            using var client = setup.client;
            var context = new NSGetterEvaluator.Context(client, null, null);
            object?[] local = { 7d };
            NSGetterEvaluator.CaptureRuntimeList(local);
            var initial = new NeoWriteBatch(new NeoWritePlan(client), false);
            LocalCollectionSelections.Occurrence? occurrence = null;
            initial.Stage(b => occurrence = StageEarlySelection(b, context, local, Guid.NewGuid().ToString()));
            initial.Plan.Commit();
            var row = occurrence!.Row;
            for (int i = 0; i < 32; i++)
            {
                var plan = new NeoWritePlan(client);
                plan.LocalSelections = new LocalCollectionSelections.Publication(plan);
                plan.LocalSelections.Transition(occurrence.Origin, occurrence.Origin.TargetId,
                    NeoValueOwnership.Session, null);
                plan.Commit();
                Assert.AreEqual(0, plan.Rows.Count);
                Assert.AreSame(row, client.sessionValues[occurrence.Id]);
                Assert.AreSame(row, occurrence.Row);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ObsoleteOccurrenceMetadataRetiresOnlyAfterSuccessfulInstallation(bool fail)
        {
            var setup = SelectionBoundary("native", false);
            using var client = setup.client;
            var context = new NSGetterEvaluator.Context(client, null, null);
            object?[] local = { 7d };
            NSGetterEvaluator.CaptureRuntimeList(local);
            var first = new NeoWriteBatch(new NeoWritePlan(client), false);
            string id = Guid.NewGuid().ToString();
            LocalCollectionSelections.Occurrence? occurrence = null;
            first.Stage(b => occurrence = StageEarlySelection(b, context, local, id));
            first.Plan.Commit();
            var origin = occurrence!.Origin;
            var removal = new NeoWritePlan(client);
            removal.Remove(NeoValueOwnership.Session, id);
            removal.Commit();
            var transition = new NeoWritePlan(client);
            transition.LocalSelections = new LocalCollectionSelections.Publication(transition);
            // Pruning rides an actual backing change; a no-op target request
            // does not traverse otherwise unchanged selectors.
            transition.LocalSelections.Transition(origin, null, origin.Ownership, new object?[] { 7d, 9d });
            if (fail)
            {
                var other = new NeoWritePlan(client);
                other.Set(NeoValueOwnership.Session, new NumberMemberValue { id = Guid.NewGuid().ToString(), value = 1 });
                other.Commit();
                Assert.Throws<InvalidOperationException>(() => transition.Commit());
                Assert.IsTrue(origin.Occurrences.Contains(occurrence));
            }
            else
            {
                transition.Commit();
                Assert.IsFalse(origin.Occurrences.Contains(occurrence));
            }
        }

        [Test]
        public void NativePublicationDoesNotTransferClassChildrenToAnotherOwner()
        {
            var setup = SelectionBoundary("native", true);
            using var client = setup.client;
            int calls = 0;
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["selection-native"] = (c, _, _, _) =>
                {
                    calls++;
                    c.CommitScriptRows();
                    var context = new NSGetterEvaluator.Context(c, null, null);
                    var coin = c.classes.Values.Single(type => type.name == "Coin");
                    var list = c.members.Values.OfType<ListMember>().First(m => m.name == "Type argument"
                        && c.members.TryGetValue(m.entryMemberId, out Member? entry)
                        && entry is ClassMember entryClass && entryClass.TryGetDeclaredClassId(out string classId) && classId == coin.id);
                    // Close a test selection over the existing exported owned-Class descriptor.
                    // No descriptor is added or synthesized for the target graph.
                    var lookup = (LookupMember)c.members.Values.OfType<LookupMember>().First(m =>
                        m.declaredTypeInfo is CollectionTypeInfo { type: MemberKind.List }).ShallowClone();
                    lookup.declaredTypeInfo = NeoReferenceTypes.SlotTypeInfo(c, list)!;
                    string coinId = Guid.NewGuid().ToString();
                    string numberId = Guid.NewGuid().ToString();
                    var initial = new NeoWritePlan(c);
                    initial.Set(NeoValueOwnership.Session, new ObjectMemberValue
                    {
                        id = coinId,
                        classId = coin.id,
                        value = new() { ["Value"] = numberId }
                    });
                    initial.Set(NeoValueOwnership.Session, new NumberMemberValue { id = numberId, value = 7 });
                    initial.Commit();
                    object?[] local = { NSGetterEvaluator.ResolveValueIfId(coinId, context) };
                    NSGetterEvaluator.CaptureRuntimeList(local);
                    string entryId = Guid.NewGuid().ToString();
                    var first = new NeoWritePlan(c);
                    var occurrence = LocalCollectionSelections.Capture(first, NeoValueOwnership.Session, entryId, lookup, local, context, null)!;
                    occurrence.Row = new ArrayMemberValue { id = entryId, value = Array.Empty<string>() };
                    first.Set(NeoValueOwnership.Session, occurrence.Row);
                    first.Commit();
                    string target = occurrence.Origin.TargetId!;
                    Assert.AreEqual(coinId, ((ArrayMemberValue)c.sessionValues[target]).value![0]);
                    var before = c.sessionValues.ToDictionary(pair => pair.Key, pair => pair.Value);
                    var second = new NeoWritePlan(c);
                    var error = Assert.Throws<NSGetterRuntimeError>(() =>
                    {
                        NeoGeneratedTypesSupport.MaterializeLocalSelection(second, list, local, context);
                        second.Commit();
                    });
                    StringAssert.Contains("already owned", error!.Message);
                    CollectionAssert.AreEquivalent(before.Keys, c.sessionValues.Keys);
                    foreach (var pair in before)
                        Assert.AreSame(pair.Value, c.sessionValues[pair.Key]);
                    Assert.AreEqual(target, occurrence.Origin.TargetId);
                    return 1;
                }
            });
            Assert.AreEqual(22, NeoMemberNSFunction.GetOrCreateStatic(client, setup.function.id).InvokeStatic());
            Assert.AreEqual(1, calls);
        }

        [TestCase("gain")]
        [TestCase("reject")]
        [TestCase("remove")]
        public void HeldRemovalDoesNotHideIndependentlyInstalledEarlySelectionTransitions(string completion)
        {
            var setup = SelectionBoundary("held-clear-detach", true, finalBoundary: true);
            using var client = setup.client;
            string? entryId = null;
            string? parentId = null;
            string? originalTarget = null;
            string? previousTarget = null;
            string? latestTarget = null;
            NeoReference<NeoList<int>>? fixedHost = null;
            LookupMember? lookup = null;
            int changes = 0;
            int calls = 0;
            client.OnWritableValuesPublished += (_, plan) =>
            {
                var c = client;
                if (entryId is null || !plan.Rows.ContainsKey((NeoValueOwnership.Session, entryId)))
                    return;
                if (!c.sessionValues.TryGetValue(entryId, out MemberValue? value) || value is not ArrayMemberValue row)
                    return;
                string current = row.value![0];
                if (current == previousTarget)
                    return;
                changes++;
                previousTarget = latestTarget = current;
                var fresh = new NeoReference<NeoList<int>>(c, current, lookup!, entryId);
                Assert.AreEqual(1, fresh.Value.Count, "The new selection is usable inside the publication notification.");
                Assert.AreEqual(originalTarget, fixedHost!.TargetId);
                Assert.AreEqual(1, fixedHost.Value.Count);
            };
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["selection-native"] = (c, _, _, _) =>
                {
                    calls++;
                    c.CommitScriptRows();
                    if (entryId is null)
                    {
                        var row = c.sessionValues.Values.OfType<ArrayMemberValue>().Single(r => r.value?.Length == 1
                            && c.sessionValues.TryGetValue(r.value[0], out MemberValue? target) && target is ArrayMemberValue);
                        entryId = row.id;
                        originalTarget = previousTarget = row.value![0];
                        lookup = c.members.Values.OfType<LookupMember>().First(m =>
                            m.declaredTypeInfo is CollectionTypeInfo { type: MemberKind.List, entryTypeInfo: PrimitiveTypeInfo { type: MemberKind.Int } });
                        fixedHost = new NeoReference<NeoList<int>>(c, originalTarget, lookup, entryId);
                        Assert.IsTrue(c.PendingScriptWrites!.TryGetEntryParent(NeoValueOwnership.Session, entryId, out parentId));
                        var holder = c.sessionValues.Values.OfType<ObjectMemberValue>().Single(r =>
                            r.value?.TryGetValue("Values", out string? valueId) == true && valueId == parentId);
                        var root = new NeoWritePlan(c);
                        root.Bind(NeoValueOwnership.Session, "selection-held-root", true, holder.id);
                        root.Commit();
                        return 1;
                    }
                    Assert.AreEqual(2, changes, "Bind and detach both changed the still-installed selection.");
                    Assert.AreEqual(latestTarget, ((ArrayMemberValue)c.sessionValues[entryId]).value![0]);
                    var held = c.PendingScriptWrites!;
                    Assert.IsNotNull(held);
                    Assert.IsTrue(held.RemovesReference(NeoValueOwnership.Session, entryId));
                    if (completion == "gain")
                    {
                        var context = new NSGetterEvaluator.Context(c, null, null);
                        held.Stage(b =>
                        {
                            b.List(NeoValueOwnership.Session, parentId!, context, null).Insert(0, entryId, NeoTimestamp.Now());
                            b.Gain(NeoValueOwnership.Session, entryId, parentId!);
                        });
                        Assert.IsFalse(held.RemovesReference(NeoValueOwnership.Session, entryId));
                        c.CommitScriptWrites();
                        Assert.AreEqual(latestTarget, ((ArrayMemberValue)c.sessionValues[entryId]).value![0]);
                    }
                    else if (completion == "reject")
                    {
                        // Reject the completely prepared removal against a newer
                        // committed revision. CommitScriptWrites rebases by design;
                        // direct prepared-plan commit exercises its stale guard.
                        held.Prepare();
                        var competitor = new NeoWritePlan(c);
                        competitor.Set(NeoValueOwnership.Session, new NumberMemberValue { id = Guid.NewGuid().ToString(), value = 1 });
                        competitor.Commit();
                        var before = c.sessionValues.ToDictionary(pair => pair.Key, pair => pair.Value);
                        Assert.Throws<InvalidOperationException>(() => held.Plan.Commit());
                        Assert.IsFalse(held.Plan.Installed);
                        CollectionAssert.AreEquivalent(before.Keys, c.sessionValues.Keys);
                        foreach (var pair in before)
                            Assert.AreSame(pair.Value, c.sessionValues[pair.Key]);
                        Assert.AreEqual(latestTarget, ((ArrayMemberValue)c.sessionValues[entryId]).value![0]);
                    }
                    else
                    {
                        Assert.AreSame(held, c.PendingScriptWrites, "Independent transitions must not force the held batch.");
                    }
                    Assert.AreEqual(originalTarget, fixedHost!.TargetId);
                    Assert.AreEqual(1, fixedHost.Value.Count);
                    return 1;
                }
            });
            Assert.AreEqual(13, NeoMemberNSFunction.GetOrCreateStatic(client, setup.function.id).InvokeStatic());
            Assert.AreEqual(2, calls);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SamePlanRemovalAndTransitionPublishesOrRollsBackTogether(bool reject)
        {
            var setup = SelectionBoundary("native", false);
            using var client = setup.client;
            var context = new NSGetterEvaluator.Context(client, null, null);
            object?[] local = { 7d };
            NSGetterEvaluator.CaptureRuntimeList(local);
            string id = Guid.NewGuid().ToString();
            var first = new NeoWriteBatch(new NeoWritePlan(client), false);
            LocalCollectionSelections.Occurrence? occurrence = null;
            first.Stage(b => occurrence = StageEarlySelection(b, context, local, id));
            first.Plan.Commit();
            var origin = occurrence!.Origin;
            string old = origin.TargetId!;
            var plan = new NeoWritePlan(client);
            plan.Remove(NeoValueOwnership.Session, id);
            plan.LocalSelections = new LocalCollectionSelections.Publication(plan);
            plan.LocalSelections.Transition(origin, null, NeoValueOwnership.Session, new object?[] { 7d, 9d });
            if (reject)
            {
                var competitor = new NeoWritePlan(client);
                competitor.Set(NeoValueOwnership.Session, new NumberMemberValue { id = Guid.NewGuid().ToString(), value = 3 });
                competitor.Commit();
                var before = client.sessionValues.ToDictionary(pair => pair.Key, pair => pair.Value);
                Assert.Throws<InvalidOperationException>(() => plan.Commit());
                Assert.AreEqual(old, origin.TargetId);
                Assert.IsTrue(origin.Occurrences.Contains(occurrence));
                CollectionAssert.AreEquivalent(before.Keys, client.sessionValues.Keys);
                foreach (var pair in before)
                    Assert.AreSame(pair.Value, client.sessionValues[pair.Key]);
            }
            else
            {
                plan.Commit();
                Assert.IsFalse(client.sessionValues.ContainsKey(id));
                Assert.IsFalse(origin.Occurrences.Contains(occurrence));
                Assert.AreNotEqual(old, origin.TargetId);
                Assert.AreEqual(2, ((ArrayMemberValue)client.sessionValues[origin.TargetId!]).value!.Length);
            }
        }

        [TestCase("replace", 1)]
        [TestCase("replace", 16)]
        [TestCase("replace", 32)]
        [TestCase("replace", 64)]
        [TestCase("replace", 128)]
        [TestCase("ABA", 32)]
        [TestCase("stage-fail", 32)]
        [TestCase("plan-fail", 32)]
        [TestCase("cross-plan", 32)]
        [TestCase("unchanged", 32)]
        public void ReplacingEarlySelectionCellPreservesInstalledAndStagedOrigins(string mode, int n)
        {
            var schema = NeoTestExport.Read(File.ReadAllText(Root + "paired-export.json"));
            using var c = NeoTestSaveStack.ClientFromSchema(schema, assumeCurrentSchema: false);
            var ctx = new NSGetterEvaluator.Context(c, null, null);
            var lookup = c.members.Values.OfType<LookupMember>().First(m => m.declaredTypeInfo is CollectionTypeInfo { type: MemberKind.List, entryTypeInfo: PrimitiveTypeInfo { type: MemberKind.Int } });
            object?[] a = { 7d }, b = { 8d, 9d };
            NSGetterEvaluator.CaptureRuntimeList(a);
            NSGetterEvaluator.CaptureRuntimeList(b);
            string id = Guid.NewGuid().ToString();
            LocalCollectionSelections.Occurrence Capture(NeoWriteBatch batch, object?[] v)
            {
                var o = LocalCollectionSelections.Capture(batch.Plan, NeoValueOwnership.Session, id, lookup, v, ctx, null)!;
                o.Row = new ArrayMemberValue { id = id, value = Array.Empty<string>() };
                batch.Plan.Set(NeoValueOwnership.Session, o.Row);
                return o;
            }
            void Check(bool ok, string msg)
            {
                Assert.IsTrue(ok, mode + " " + n + ": " + msg);
            }
            var first = new NeoWriteBatch(new NeoWritePlan(c), false);
            LocalCollectionSelections.Occurrence? initial = null;
            first.Stage(x => initial = Capture(x, a));
            first.Plan.Commit();
            var state = ctx.allocationTracker.LocalSelections!;
            var originA = initial!.Origin;
            string fixedId = originA.TargetId!;
            var host = new NeoReference<NeoList<int>>(c, fixedId, lookup, id);
            long rows = 0;
            int callbacks = 0;
            c.OnWritableValuesPublished += (_, p) =>
            {
                callbacks++;
                if (p.Rows.TryGetValue((NeoValueOwnership.Session, id), out var row) && row is ArrayMemberValue arr)
                {
                    Check(state.Matches(NeoValueOwnership.Session, id, ReferenceEquals(arr, initial.Row) ? a : b, new NeoWritePlan(c))
                        || state.Matches(NeoValueOwnership.Session, id, a, new NeoWritePlan(c)), "notification metadata agrees");
                }
            };
            for (int i = 0; i < n; i++)
            {
                var currentRow = c.sessionValues[id];
                var batch = new NeoWriteBatch(new NeoWritePlan(c), false);
                LocalCollectionSelections.Occurrence? replacement = null;
                if (mode == "stage-fail")
                {
                    try
                    {
                        batch.Stage(x =>
                        {
                            replacement = Capture(x, b);
                            throw new InvalidOperationException("stage");
                        });
                        throw new Exception("accepted stage");
                    }
                    catch (InvalidOperationException e)
                    {
                        Check(e.Message == "stage", "primary");
                    }
                    Check(ReferenceEquals(c.sessionValues[id], currentRow), "stage changed installed row");
                    Check(state.Matches(NeoValueOwnership.Session, id, a, batch.Plan), "stage restored old selector");
                }
                batch.Stage(x => replacement = Capture(x, mode == "unchanged" ? a : b));
                Check(state.Matches(NeoValueOwnership.Session, id, mode == "unchanged" ? a : b, batch.Plan), "staged-row precedence");
                Check(state.Matches(NeoValueOwnership.Session, id, a, new NeoWritePlan(c)), "installed occurrence changed before commit");
                if (mode == "ABA")
                    batch.Stage(x => replacement = Capture(x, a));
                if (mode == "cross-plan")
                {
                    var transition = new NeoWritePlan(c);
                    transition.LocalSelections = new LocalCollectionSelections.Publication(transition);
                    transition.LocalSelections.Transition(originA, null, NeoValueOwnership.Session, new object?[] { 7d, 4d });
                    transition.Commit();
                    Check(((ArrayMemberValue)c.sessionValues[id]).value![0] == originA.TargetId, "old installed selector follows cross-plan transition");
                    Check(state.Matches(NeoValueOwnership.Session, id, b, batch.Plan), "cross-plan overwrote staged replacement");
                    batch.Plan.Rebase();
                }
                if (mode == "plan-fail")
                {
                    var other = new NeoWritePlan(c);
                    other.Set(NeoValueOwnership.Session, new NumberMemberValue { id = Guid.NewGuid().ToString(), value = 1 });
                    other.Commit();
                    try
                    {
                        batch.Plan.Commit();
                        throw new Exception("accepted stale plan");
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    Check(ReferenceEquals(c.sessionValues[id], currentRow), "failed plan changed installed selector");
                    Check(state.Matches(NeoValueOwnership.Session, id, a, new NeoWritePlan(c)), "failed plan lost old occurrence");
                    batch = new NeoWriteBatch(new NeoWritePlan(c), false);
                    batch.Stage(x => replacement = Capture(x, b));
                }
                batch.Plan.Commit();
                rows += batch.Plan.Rows.Count;
                object?[] expected = mode is "ABA" or "unchanged" ? a : b;
                Check(state.Matches(NeoValueOwnership.Session, id, expected, new NeoWritePlan(c)), "installed replacement mismatch");
                Check(replacement!.Origin.Occurrences.Count == 1, "duplicate current occurrences");
                Check(host.TargetId == fixedId && host.Value.Count == 1, "fixed host reference changed");
                if (!ReferenceEquals(expected, a))
                {
                    Check(originA.Occurrences.Count == 0, "old origin still owns selector");
                    var back = new NeoWriteBatch(new NeoWritePlan(c), false);
                    back.Stage(x => replacement = Capture(x, a));
                    back.Plan.Commit();
                    rows += back.Plan.Rows.Count;
                }
            }
            Check(originA.Occurrences.Count == 1, "final A occurrence count");
            Assert.LessOrEqual(rows, 2 * n + 3, "Only the first new target adds its scalar rows; replacement work stays per changed cell.");
        }

        private static LocalCollectionSelections.Occurrence StageEarlySelection(NeoWriteBatch batch,
            NSGetterEvaluator.Context context, object value, string id)
        {
            var lookup = context.client.members.Values.OfType<LookupMember>().First(m =>
                m.declaredTypeInfo is CollectionTypeInfo { type: MemberKind.List, entryTypeInfo: PrimitiveTypeInfo { type: MemberKind.Int } });
            var occurrence = LocalCollectionSelections.Capture(batch.Plan, NeoValueOwnership.Session, id,
                lookup, value, context, null)!;
            Assert.IsNotNull(occurrence);
            occurrence.Row = new ArrayMemberValue
            {
                id = id,
                value = Array.Empty<string>(),
                createdAt = NeoTimestamp.Now(),
                updatedAt = NeoTimestamp.Now()
            };
            batch.Plan.Set(NeoValueOwnership.Session, occurrence.Row);
            return occurrence;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EarlySelectionMetadataPrecedesPublicationEvents(bool throwEvent)
        {
            var setup = SelectionBoundary("native", false);
            using var client = setup.client;
            var context = new NSGetterEvaluator.Context(client, null, null);
            object?[] local = { 7d };
            NSGetterEvaluator.CaptureRuntimeList(local);
            var batch = new NeoWriteBatch(new NeoWritePlan(client), false);
            string entry = Guid.NewGuid().ToString();
            batch.Stage(b => StageEarlySelection(b, context, local, entry));
            int called = 0;
            var failure = new InvalidOperationException("installed selection event");
            client.OnWritableValuesPublished += (_, plan) =>
            {
                if (!ReferenceEquals(plan, batch.Plan))
                    return;
                called++;
                Assert.IsTrue(plan.Installed);
                var origin = context.allocationTracker.LocalSelections!.Find(local)!;
                Assert.IsNotNull(origin.TargetId);
                Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Session, entry, out ArrayMemberValue? occurrence));
                Assert.AreEqual(origin.TargetId, occurrence!.value![0]);
                Assert.AreEqual(origin.TargetId, NSGetterEvaluator.FindRowReference(local, context)!.valueId);
                // A reentrant read sees the installed target, never the marker.
                Assert.AreEqual(1, ((object?[])NSGetterEvaluator.RuntimeCollection(local, context)!).Length);
                if (throwEvent)
                    throw failure;
            };
            if (throwEvent)
                Assert.AreSame(failure, Assert.Throws<InvalidOperationException>(() => batch.Plan.Commit()));
            else
                batch.Plan.Commit();
            Assert.AreEqual(1, called);
            Assert.IsTrue(batch.Plan.Installed);
            Assert.IsNotNull(context.allocationTracker.LocalSelections!.Find(local)!.TargetId);
            var retry = new NeoWriteBatch(new NeoWritePlan(client), false);
            string next = Guid.NewGuid().ToString();
            retry.Stage(b => StageEarlySelection(b, context, local, next));
            retry.Plan.Commit();
            Assert.AreEqual(((ArrayMemberValue)client.sessionValues[entry]).value![0], ((ArrayMemberValue)client.sessionValues[next]).value![0]);
        }

        [Test]
        public void EarlySelectionFailedStageKeepsEarlierOccurrence()
        {
            var setup = SelectionBoundary("native", false);
            using var client = setup.client;
            var context = new NSGetterEvaluator.Context(client, null, null);
            object?[] first = { 7d }, rejected = { 9d };
            NSGetterEvaluator.CaptureRuntimeList(first);
            NSGetterEvaluator.CaptureRuntimeList(rejected);
            var batch = new NeoWriteBatch(new NeoWritePlan(client), false);
            string firstId = Guid.NewGuid().ToString(), rejectedId = Guid.NewGuid().ToString();
            batch.Stage(b => StageEarlySelection(b, context, first, firstId));
            Assert.Throws<InvalidOperationException>(() => batch.Stage(b =>
            {
                StageEarlySelection(b, context, rejected, rejectedId);
                throw new InvalidOperationException("late selection stage");
            }));
            Assert.IsNull(context.allocationTracker.LocalSelections!.Find(rejected));
            batch.Plan.Commit();
            Assert.IsTrue(client.sessionValues.ContainsKey(firstId));
            Assert.IsFalse(client.sessionValues.ContainsKey(rejectedId));
            Assert.IsNotNull(NSGetterEvaluator.FindRowReference(first, context));
            Assert.IsNull(NSGetterEvaluator.FindRowReference(rejected, context));
        }

        [Test]
        public void RejectedSelectionPlanPublishesNoOriginAndRetrySucceeds()
        {
            var setup = SelectionBoundary("native", false);
            using var client = setup.client;
            var context = new NSGetterEvaluator.Context(client, null, null);
            object?[] local = { 7d };
            NSGetterEvaluator.CaptureRuntimeList(local);
            var batch = new NeoWriteBatch(new NeoWritePlan(client), false);
            string id = Guid.NewGuid().ToString();
            batch.Stage(b => StageEarlySelection(b, context, local, id));
            var other = new NeoWritePlan(client);
            other.Set(NeoValueOwnership.Session, new NumberMemberValue { id = Guid.NewGuid().ToString(), value = 3 });
            other.Commit();
            int roots = client.sessionValues.Count;
            Assert.Throws<InvalidOperationException>(() => batch.Plan.Commit());
            Assert.IsFalse(batch.Plan.Installed);
            Assert.AreEqual(roots, client.sessionValues.Count);
            Assert.IsNull(context.allocationTracker.LocalSelections!.Find(local));
            Assert.IsNull(NSGetterEvaluator.FindRowReference(local, context));
            var retry = new NeoWriteBatch(new NeoWritePlan(client), false);
            retry.Stage(b => StageEarlySelection(b, context, local, id));
            retry.Plan.Commit();
            Assert.IsNotNull(NSGetterEvaluator.FindRowReference(local, context));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RemovedOrReplacedEarlyOccurrenceDoesNotPublishItsOrigin(bool replaced)
        {
            var setup = SelectionBoundary("native", false);
            using var client = setup.client;
            var context = new NSGetterEvaluator.Context(client, null, null);
            object?[] local = { 7d };
            NSGetterEvaluator.CaptureRuntimeList(local);
            var batch = new NeoWriteBatch(new NeoWritePlan(client), false);
            string id = Guid.NewGuid().ToString();
            batch.Stage(b => StageEarlySelection(b, context, local, id));
            if (replaced)
                batch.Plan.Set(NeoValueOwnership.Session, new ArrayMemberValue { id = id, value = Array.Empty<string>() });
            else
                batch.Plan.Remove(NeoValueOwnership.Session, id);
            int before = client.sessionValues.Count;
            batch.Plan.Commit();
            Assert.IsNull(context.allocationTracker.LocalSelections!.Find(local)!.TargetId);
            Assert.IsNull(NSGetterEvaluator.FindRowReference(local, context));
            Assert.AreEqual(before + (replaced ? 1 : 0), client.sessionValues.Count);
        }


        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        public void PublicationOriginUsesExistingCrossCallerEscape(bool sharedCache, bool partial, bool returns)
        {
            var setup = SelectionBoundary("native", false);
            using var client = setup.client;
            var first = sharedCache ? client.CreateGetterContext(NeoValueOwnership.Session) : new NSGetterEvaluator.Context(client, null, null);
            var second = sharedCache ? client.CreateGetterContext(NeoValueOwnership.Session) : new NSGetterEvaluator.Context(client, null, null);
            object?[] local = { 7d };
            NSGetterEvaluator.CaptureRuntimeList(local);
            string entry = Guid.NewGuid().ToString();
            string? target = null;
            client.EnterScriptWrites();
            try
            {
                var batch = client.ScriptWriteBatch("selection-holder")!;
                batch.Stage(b => StageEarlySelection(b, first, local, entry));
                object returned = local;
                if (partial)
                {
                    client.CommitScriptRows();
                    target = ((ArrayMemberValue)client.sessionValues[entry]).value![0];
                    returned = NSGetterEvaluator.ResolveValueIfId(target, second)!;
                }
                FinishHeldFrame(client, first, null);
                FinishHeldFrame(client, second, returns ? returned : null);
                batch.AfterPublication(() => target = ((ArrayMemberValue)client.sessionValues[entry]).value![0]);
            }
            finally { client.ExitScriptWrites(); }
            Assert.IsNotNull(target);
            Assert.AreEqual(returns, client.sessionValues.ContainsKey(target!));
            Assert.IsFalse(first.allocationTracker.HasDeferredCompletion);
            Assert.IsFalse(second.allocationTracker.HasDeferredCompletion);
        }
    }
}
