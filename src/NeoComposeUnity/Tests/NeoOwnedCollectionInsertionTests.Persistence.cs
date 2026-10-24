#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public partial class NeoOwnedCollectionInsertionTests
    {
        private const string PersistHolder = "test-save-early-holder";
        private const string PersistOwner = "test-save-early-owner";
        private static (ProjectData schema, string function, string lookup) PersistenceSchema(bool temporary)
        {
            var schema = NeoTestExport.Read(File.ReadAllText(Root + "paired-export.json"));
            var function = (NSFunctionMember)schema.members.Values.Single(m => m.name == "LocalCollectionReferenceOwnLater");
            var holder = schema.classes.Values.Single(c => c.name == "Holder");
            var owner = schema.classes.Values.Single(c => c.name == "LocalReferenceOwner");
            var lookup = schema.members.Values.OfType<LookupMember>().First(m =>
                m.declaredTypeInfo is CollectionTypeInfo
                {
                    type: MemberKind.List, required: true,
                    entryTypeInfo: PrimitiveTypeInfo { type: MemberKind.Int, required: true }
                });
            var probe = schema.classes.Values.Single(c => c.schema.Values.Contains(function.id));
            ClassMember RootMember(string id, string classId, JObject? arguments) => JObject.FromObject(new
            {
                id,
                name = id,
                kind = (int)MemberKind.Class,
                requirement = (int)NeoMemberRequirementKind.Required,
                modifier = (int)NeoMemberModifierKind.Static,
                storage = (int)NeoMemberStorage.Save,
                classId,
                classArguments = arguments
            }).ToObject<ClassMember>()!;
            schema.members[PersistHolder] = RootMember(PersistHolder, holder.id, new JObject
            {
                [holder.genericParams![0].id] = new JObject { ["kind"] = 1, ["memberId"] = lookup.id }
            });
            schema.members[PersistOwner] = RootMember(PersistOwner, owner.id, null);
            probe.schema["PersistenceHolder"] = PersistHolder;
            probe.schema["PersistenceOwner"] = PersistOwner;
            var action = JObject.FromObject(function.action);
            var instructions = (JArray)action["instructions"]!;
            instructions[0]!["variable"]!["pointer"] = new JObject { ["type"] = "staticMember", ["memberId"] = PersistHolder };
            instructions[3]!["variable"]!["pointer"] = new JObject { ["type"] = "staticMember", ["memberId"] = PersistOwner };
            var native = new FunctionMember
            {
                id = "save-selection-native",
                name = "SaveSelectionNative",
                kind = MemberKind.Function,
                Modifier = NeoMemberModifierKind.Static,
                Dispatch = NeoFunctionDispatchKind.Synchronous,
                returnTypeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>()
            };
            schema.members[native.id] = native;
            probe.schema[native.name] = native.id;
            instructions.Insert(3, JObject.Parse("{'type':'variable','variable':{'id':'boundary','typeInfo':{'type':2,'required':true},'pointer':{'type':'callFunction','memberId':'save-selection-native','receiver':{'kind':'static','memberId':'save-selection-native'},'args':[],'callSiteId':'save-selection-native-site'}}}"));
            if (temporary)
            {
                while (instructions.Count > 4)
                    instructions.RemoveAt(4);
                instructions.Add(new JObject { ["type"] = "return", ["pointer"] = SelectionInt(1) });
            }
            foreach (var value in action.Descendants().OfType<JProperty>().Where(p => p.Name == "writability" && (string?)p.Value == "session").ToArray())
                value.Value = "save";
            function.action = action.ToObject<FunctionWithReturnType>()!;
            return (schema, function.id, lookup.id);
        }
        private static (string holder, string owner) SeedPersistenceRoots(NeoClient client, string lookup)
        {
            var holderClass = client.classes.Values.Single(c => c.name == "Holder");
            var ownerClass = client.classes.Values.Single(c => c.name == "LocalReferenceOwner");
            string h = Guid.NewGuid().ToString(), o = Guid.NewGuid().ToString();
            string hl = Guid.NewGuid().ToString(), ol = Guid.NewGuid().ToString();
            var plan = new NeoWritePlan(client);
            plan.Set(NeoValueOwnership.Save, new ObjectMemberValue
            {
                id = h,
                classId = holderClass.id,
                genericBindings = new() { [holderClass.genericParams![0].id] = lookup },
                value = new() { ["Values"] = hl }
            });
            plan.Set(NeoValueOwnership.Save, new ArrayMemberValue { id = hl, value = Array.Empty<string>(), genericBindings = new() { [holderClass.genericParams![0].id] = lookup } });
            plan.Set(NeoValueOwnership.Save, new ObjectMemberValue { id = o, classId = ownerClass.id, value = new() { ["Values"] = ol } });
            plan.Set(NeoValueOwnership.Save, new ArrayMemberValue { id = ol, value = Array.Empty<string>() });
            plan.Bind(NeoValueOwnership.Save, PersistHolder, true, h);
            plan.Bind(NeoValueOwnership.Save, PersistOwner, true, o);
            plan.Commit();
            return (h, o);
        }
        [TestCase(false)]
        [TestCase(true)]
        public void EarlyCollectionSelectionSaveReloadUsesOnlyPersistedRows(bool temporary)
        {
            var setup = PersistenceSchema(temporary);
            string saved, entry, target, owner;
            WeakReference oldClient;
            using (var client = NeoTestSaveStack.ClientFromSchema(setup.schema, assumeCurrentSchema: false))
            {
                var roots = SeedPersistenceRoots(client, setup.lookup);
                owner = roots.owner;
                int calls = 0;
                client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
                {
                    ["save-selection-native"] = (c, _, _, _) => { calls++; c.CommitScriptRows(); return 1; }
                });
                Assert.AreEqual(temporary ? 1 : 22, NeoMemberNSFunction.GetOrCreateStatic(client, setup.function).InvokeStatic());
                Assert.AreEqual(1, calls);
                var holder = (ObjectMemberValue)client.saveValues[roots.holder];
                var entries = (ArrayMemberValue)client.saveValues[holder.value!["Values"]];
                entry = entries.value!.Single();
                target = ((ArrayMemberValue)client.saveValues[entry]).value!.Single();
                if (!temporary)
                {
                    Assert.AreEqual(((ObjectMemberValue)client.saveValues[owner]).value!["Values"], target);
                    var host = new NeoReference<NeoList<int>>(client, target, (LookupMember)client.members[setup.lookup], entry);
                    Assert.AreEqual(2, host.Value.Count);
                    CollectionAssert.AreEqual(new[] { 7, 9 }, host.Value);
                }
                else
                    Assert.IsFalse(client.sessionValues.ContainsKey(target), "Unreturned reference-only publication target must not become permanent.");
                saved = client.SerializeSaveData();
                oldClient = new WeakReference(client);
            }
            // Independently parse the original export and rebuild the same static test declarations.
            var freshSchema = PersistenceSchema(temporary).schema;
            using (var reloaded = NeoTestSaveStack.ClientFromSchema(freshSchema, assumeCurrentSchema: false, loadedSaveContent: saved))
            {
                Assert.AreNotSame(oldClient.Target, reloaded);
                Assert.IsNull(reloaded.PendingScriptWrites);
                Assert.AreEqual(target, ((ArrayMemberValue)reloaded.saveValues[entry]).value!.Single());
                var host = new NeoReference<NeoList<int>>(reloaded, target, (LookupMember)reloaded.members[setup.lookup], entry);
                if (temporary)
                {
                    Assert.IsFalse(reloaded.sessionValues.ContainsKey(target));
                    Assert.Catch(() => { _ = host.Value.Count; });
                    return;
                }
                Assert.AreEqual(2, host.Value.Count);
                var ownerNode = (NeoMemberClassWritable)NeoMember.CreateWritable(reloaded, reloaded.members[PersistOwner], owner, NeoValueOwnership.Save);
                var list = ownerNode.Get<NeoMemberListWritable>("Values");
                var payload = NeoGeneratedTypesSupport.MaterializeCollectionAssignment(
                    reloaded, list.member, new object?[] { 1, 2, 3 }, NeoValueOwnership.Save,
                    new NSGetterEvaluator.Context(reloaded, null, null));
                ownerNode.SetSerializedValue("Values", NeoValueWritePayload.FromValue(payload));
                Assert.AreEqual(target, host.TargetId);
                Assert.AreEqual(target, ((ArrayMemberValue)reloaded.saveValues[entry]).value!.Single());
                Assert.AreEqual(3, host.Value.Count);
                CollectionAssert.AreEqual(new[] { 1, 2, 3 }, host.Value);
            }
        }
    }
}
