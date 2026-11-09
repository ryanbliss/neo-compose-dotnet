// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using G = NeoCompose.Tests.P127InterfacesParity;

namespace NeoCompose.Tests
{
    /// <summary>The SDK consumer of the B3 source/export parity pair.</summary>
    public partial class P127InterfacesParityTests
    {
        private const string Root = "Packages/com.ryanbliss.neocompose/Tests/";
        private const string Export = Root + "p127-interfaces-parity-export.json";

        public static IEnumerable<TestCaseData> Cases() => JObject.Parse(File.ReadAllText(Root + "p127-interfaces-parity-fixture.json"))["returns"]!
            .Children<JProperty>().Select(test => new TestCaseData(test.Name, test.Value).SetName("B3_" + test.Name));

        [Test]
        public void GeneratedR24DefaultSlotsMatchSharedFixture()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText(Export)), assumeCurrentSchema: false);
            using var project = new G.TestProjectNeo(client);
            int[] actual =
            {
                ((G.R24EIV<G.R24EIQ<G.R24EK0>>)CreateParityValue(client, G.R24EC0<G.R24EK0>.CreateWritable)).V, // R24EC0K0IVIQK0
                ((G.R24EIV<G.R24EIQ<G.R24EK0>>)CreateParityValue(client, G.R24EC0<G.R24EK2>.CreateWritable)).V, // R24EC0K2IVIQK0
                ((G.R24EIV<G.R24EIQ<G.R24EK0>>)CreateParityValue(client, G.R24EC0<G.R24EK4>.CreateWritable)).V, // R24EC0K4IVIQK0
                ((G.R24EIV<G.R24EIQ<G.R24EK0>>)CreateParityValue(client, G.R24EC1<G.R24EK0>.CreateWritable)).V, // R24EC1K0IVIQK0
                ((G.R24EIV<G.R24EG<G.R24EK0>>)CreateParityValue(client, G.R24EC1<G.R24EK0>.CreateWritable)).V, // R24EC1K0IVGK0
                ((G.R24EIV<G.R24EIQ<G.R24EK0>>)CreateParityValue(client, G.R24EC1<G.R24EK2>.CreateWritable)).V, // R24EC1K2IVIQK0
                ((G.R24EIV<G.R24EIQ<G.R24EK2>>)CreateParityValue(client, G.R24EC1<G.R24EK2>.CreateWritable)).V, // R24EC1K2IVIQK2
                ((G.R24EIV<G.R24EG<G.R24EK2>>)CreateParityValue(client, G.R24EC1<G.R24EK2>.CreateWritable)).V, // R24EC1K2IVGK2
                ((G.R24EIV<G.R24EIQ<G.R24EK0>>)CreateParityValue(client, G.R24EC1<G.R24EK4>.CreateWritable)).V, // R24EC1K4IVIQK0
                ((G.R24EIV<G.R24EIQ<G.R24EK4>>)CreateParityValue(client, G.R24EC1<G.R24EK4>.CreateWritable)).V, // R24EC1K4IVIQK4
                ((G.R24EIV<G.R24EG<G.R24EK4>>)CreateParityValue(client, G.R24EC1<G.R24EK4>.CreateWritable)).V, // R24EC1K4IVGK4
                ((G.R24EIV<G.R24EIQ<G.R24EK0>>)CreateParityValue(client, G.R24EC2<G.R24EK0>.CreateWritable)).V, // R24EC2K0IVIQK0
                ((G.R24EIV<G.R24EIQ<G.R24EK2>>)CreateParityValue(client, G.R24EC2<G.R24EK0>.CreateWritable)).V, // R24EC2K0IVIQK2
                ((G.R24EIV<G.R24EG<G.R24EK0>>)CreateParityValue(client, G.R24EC2<G.R24EK0>.CreateWritable)).V, // R24EC2K0IVGK0
                ((G.R24EIV<G.R24EIQ<G.R24EK0>>)CreateParityValue(client, G.R24EC2<G.R24EK2>.CreateWritable)).V, // R24EC2K2IVIQK0
                ((G.R24EIV<G.R24EIQ<G.R24EK2>>)CreateParityValue(client, G.R24EC2<G.R24EK2>.CreateWritable)).V, // R24EC2K2IVIQK2
                ((G.R24EIV<G.R24EG<G.R24EK2>>)CreateParityValue(client, G.R24EC2<G.R24EK2>.CreateWritable)).V, // R24EC2K2IVGK2
                ((G.R24EIV<G.R24EIQ<G.R24EK0>>)CreateParityValue(client, G.R24EC2<G.R24EK4>.CreateWritable)).V, // R24EC2K4IVIQK0
                ((G.R24EIV<G.R24EIQ<G.R24EK2>>)CreateParityValue(client, G.R24EC2<G.R24EK4>.CreateWritable)).V, // R24EC2K4IVIQK2
                ((G.R24EIV<G.R24EIQ<G.R24EK4>>)CreateParityValue(client, G.R24EC2<G.R24EK4>.CreateWritable)).V, // R24EC2K4IVIQK4
                ((G.R24EIV<G.R24EG<G.R24EK4>>)CreateParityValue(client, G.R24EC2<G.R24EK4>.CreateWritable)).V, // R24EC2K4IVGK4
                ((G.R24EIV<G.R24EIQ<G.R24EK0>>)CreateParityValue(client, G.R24EC3<G.R24EK0>.CreateWritable)).V, // R24EC3K0IVIQK0
                ((G.R24EIV<G.R24EIQ<G.R24EK2>>)CreateParityValue(client, G.R24EC3<G.R24EK0>.CreateWritable)).V, // R24EC3K0IVIQK2
                ((G.R24EIV<G.R24EG<G.R24EK0>>)CreateParityValue(client, G.R24EC3<G.R24EK0>.CreateWritable)).V, // R24EC3K0IVGK0
                ((G.R24EIV<G.R24EIQ<G.R24EK0>>)CreateParityValue(client, G.R24EC3<G.R24EK2>.CreateWritable)).V, // R24EC3K2IVIQK0
                ((G.R24EIV<G.R24EIQ<G.R24EK2>>)CreateParityValue(client, G.R24EC3<G.R24EK2>.CreateWritable)).V, // R24EC3K2IVIQK2
                ((G.R24EIV<G.R24EG<G.R24EK2>>)CreateParityValue(client, G.R24EC3<G.R24EK2>.CreateWritable)).V, // R24EC3K2IVGK2
                ((G.R24EIV<G.R24EIQ<G.R24EK0>>)CreateParityValue(client, G.R24EC3<G.R24EK4>.CreateWritable)).V, // R24EC3K4IVIQK0
                ((G.R24EIV<G.R24EIQ<G.R24EK2>>)CreateParityValue(client, G.R24EC3<G.R24EK4>.CreateWritable)).V, // R24EC3K4IVIQK2
                ((G.R24EIV<G.R24EIQ<G.R24EK4>>)CreateParityValue(client, G.R24EC3<G.R24EK4>.CreateWritable)).V, // R24EC3K4IVIQK4
                ((G.R24EIV<G.R24EG<G.R24EK4>>)CreateParityValue(client, G.R24EC3<G.R24EK4>.CreateWritable)).V, // R24EC3K4IVGK4
                ((G.R24EIV<G.R24EIQ<G.R24EK0>>)CreateParityValue(client, G.R24EC4<G.R24EK0>.CreateWritable)).V, // R24EC4K0IVIQK0
                ((G.R24EIV<G.R24EIQ<G.R24EK2>>)CreateParityValue(client, G.R24EC4<G.R24EK0>.CreateWritable)).V, // R24EC4K0IVIQK2
                ((G.R24EIV<G.R24EIQ<G.R24EK4>>)CreateParityValue(client, G.R24EC4<G.R24EK0>.CreateWritable)).V, // R24EC4K0IVIQK4
                ((G.R24EIV<G.R24EG<G.R24EK0>>)CreateParityValue(client, G.R24EC4<G.R24EK0>.CreateWritable)).V, // R24EC4K0IVGK0
                ((G.R24EIV<G.R24EIQ<G.R24EK0>>)CreateParityValue(client, G.R24EC4<G.R24EK2>.CreateWritable)).V, // R24EC4K2IVIQK0
                ((G.R24EIV<G.R24EIQ<G.R24EK2>>)CreateParityValue(client, G.R24EC4<G.R24EK2>.CreateWritable)).V, // R24EC4K2IVIQK2
                ((G.R24EIV<G.R24EIQ<G.R24EK4>>)CreateParityValue(client, G.R24EC4<G.R24EK2>.CreateWritable)).V, // R24EC4K2IVIQK4
                ((G.R24EIV<G.R24EG<G.R24EK2>>)CreateParityValue(client, G.R24EC4<G.R24EK2>.CreateWritable)).V, // R24EC4K2IVGK2
                ((G.R24EIV<G.R24EIQ<G.R24EK0>>)CreateParityValue(client, G.R24EC4<G.R24EK4>.CreateWritable)).V, // R24EC4K4IVIQK0
                ((G.R24EIV<G.R24EIQ<G.R24EK2>>)CreateParityValue(client, G.R24EC4<G.R24EK4>.CreateWritable)).V, // R24EC4K4IVIQK2
                ((G.R24EIV<G.R24EIQ<G.R24EK4>>)CreateParityValue(client, G.R24EC4<G.R24EK4>.CreateWritable)).V, // R24EC4K4IVIQK4
                ((G.R24EIV<G.R24EG<G.R24EK4>>)CreateParityValue(client, G.R24EC4<G.R24EK4>.CreateWritable)).V, // R24EC4K4IVGK4
            };
            var expected = JObject.Parse(File.ReadAllText(Root + "p127-interfaces-parity-fixture.json"))["returns"]!["R24DefaultSlots"]!.Value<string>();
            TestContext.WriteLine(string.Join(",", actual));
            var values = expected!.Split(',').Select(int.Parse).ToArray();
            Assert.That(actual.Length, Is.EqualTo(values.Length));
            Assert.That(actual, Is.EqualTo(values));
        }

        [Test]
        public void GeneratedR24HidingSlotsMatchSharedFixture()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText(Export)), assumeCurrentSchema: false);
            using var project = new G.TestProjectNeo(client);
            int[] actual =
            {
                ((G.R24HIF<int>)CreateParityValue(client, G.R24HC1<int>.CreateWritable)).G(0), // R24HC1IFG
                ((G.R24HC1<int>)CreateParityValue(client, G.R24HC1<int>.CreateWritable)).G(0), // R24HC1G
                ((G.R24HIF<int>)CreateParityValue(client, G.R24HC2<int>.CreateWritable)).G(0), // R24HC2IFG
                ((G.R24HIE<int>)CreateParityValue(client, G.R24HC2<int>.CreateWritable)).E(0), // R24HC2IEE
                ((G.R24HC2<int>)CreateParityValue(client, G.R24HC2<int>.CreateWritable)).E(0), // R24HC2E
                ((G.R24HC2<int>)CreateParityValue(client, G.R24HC2<int>.CreateWritable)).G(0), // R24HC2G
            };
            var expected = JObject.Parse(File.ReadAllText(Root + "p127-interfaces-parity-fixture.json"))["returns"]!["R24HidingSlots"]!.Value<string>();
            TestContext.WriteLine(string.Join(",", actual));
            var values = expected!.Split(',').Select(int.Parse).ToArray();
            Assert.That(actual.Length, Is.EqualTo(values.Length));
            Assert.That(actual, Is.EqualTo(values));
        }

        [Test]
        public void GeneratedR24InterfaceOverrideSlotsMatchSharedFixture()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText(Export)), assumeCurrentSchema: false);
            using var project = new G.TestProjectNeo(client);
            int[] actual =
            {
                ((G.R24FOIV<G.R24FOIQ<G.R24FOK2>>)CreateParityValue(client, G.R24FOC2<G.R24FOK2>.CreateWritable)).V, // R24FOC2K2IVIQK2
                ((G.R24FOIV<G.R24FOIQ<G.R24FOK2>>)CreateParityValue(client, G.R24FOC2<G.R24FOK0>.CreateWritable)).V, // R24FOC2K0IVIQK2
                ((G.R24FOIV<G.R24FOIQ<G.R24FOK2>>)CreateParityValue(client, G.R24FOC3<G.R24FOK2>.CreateWritable)).V, // R24FOC3K2IVIQK2
                ((G.R24FOIV<G.R24FOIQ<G.R24FOK2>>)CreateParityValue(client, G.R24FOC3<G.R24FOK0>.CreateWritable)).V, // R24FOC3K0IVIQK2
                ((G.R24FOIV<G.R24FOIQ<G.R24FOK0>>)CreateParityValue(client, G.R24FOC3<G.R24FOK0>.CreateWritable)).V, // R24FOC3K0IVIQK0
                ((G.R24FOIV<G.R24FOIQ<G.R24FOK0>>)CreateParityValue(client, G.R24FOC2<G.R24FOK2>.CreateWritable)).V, // R24FOC2K2IVIQK0
            };
            var expected = JObject.Parse(File.ReadAllText(Root + "p127-interfaces-parity-fixture.json"))["returns"]!["R24InterfaceOverrideSlots"]!.Value<string>();
            TestContext.WriteLine(string.Join(",", actual));
            var values = expected!.Split(',').Select(int.Parse).ToArray();
            Assert.That(actual.Length, Is.EqualTo(values.Length));
            Assert.That(actual, Is.EqualTo(values));
        }

        private static T CreateParityValue<T>(NeoClient client, Func<NeoClient, NeoMemberClassWritable, T> factory)
        {
            var context = client.RentDirectFunctionContext(NeoValueOwnership.Session);
            try
            {
                var constructed = NeoGeneratedTypesSupport.CreateRuntimeClassValue(
                    client, (ClassTypeInfo)client.TypeArguments.Of<T>(),
                    Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>(), _ => null, context);
                var node = new NeoMemberClassWritable(client, constructed.member, constructed.value.id, NeoValueOwnership.Session);
                return factory(client, node);
            }
            finally
            {
                client.ReturnDirectFunctionContext(context, null);
            }
        }

        [Test]
        public void GeneratedUnlistedInterfaceKeepsBaseExplicitSite()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText(Export)), assumeCurrentSchema: false);
            var owner = client.ProjectDataForRuntime.classes.Values.Single(type => type.name == "R15Unlisted");
            var node = NeoGeneratedTypesSupport.CreateWritableClassValue(client, owner.id);
            var value = P127InterfacesParity.R15Unlisted.CreateWritable(client, node);

            Assert.That(value.V, Is.EqualTo(3));
            Assert.That(((P127InterfacesParity.IR15V)value).V, Is.EqualTo(2));
        }

        [TestCase(true, 2, 3)]
        [TestCase(false, 3, 2)]
        public void GeneratedRelistingPropertyMapsEachAccessor(bool getterOverride, int expectedGet, int expectedSet)
        {
            using var client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText(Export)), assumeCurrentSchema: false);
            string name = getterOverride ? "R23CX" : "R23CY";
            var owner = client.ProjectDataForRuntime.classes.Values.Single(type => type.name == name);
            var node = NeoGeneratedTypesSupport.CreateWritableClassValue(client, owner.id);
            P127InterfacesParity.R23CIPV<P127InterfacesParity.R23CBa> value = getterOverride
                ? P127InterfacesParity.R23CX.CreateWritable(client, node)
                : P127InterfacesParity.R23CY.CreateWritable(client, node);
            Assert.That(value.P, Is.EqualTo(expectedGet));
            value.P = 0;
            var recorder = client.ProjectDataForRuntime.classes.Values.Single(type => type.name == "R23CRec");
            var binding = NeoGeneratedTypesSupport.StaticBinding(client, recorder.schema["S"], NeoValueOwnership.Session);
            Assert.That(NeoGeneratedTypesSupport.ReadInt(binding.GetRequiredNode<NeoMemberInt>()), Is.EqualTo(expectedSet));
        }

        [Test]
        public void GenericStaticCollectionsSaveRoundTrip()
        {
            var schema = NeoTestExport.Read(File.ReadAllText(Export));
            var owner = schema.classes.Values.Single(type => type.name == "StaticBox");
            foreach (string id in owner.schema.Values)
                if (schema.members[id] is not NSFunctionMember)
                    schema.members[id].Storage = NeoMemberStorage.Save;
            var probe = schema.classes.Values.Single(type => type.name == "Probe");
            var function = (NSFunctionMember)schema.members[probe.schema["GenericStaticCollections"]];
            // This Save variant uses the matching compiled write lifetime.
            foreach (var mutation in function.action!.instructions.OfType<CollectionCallInstruction>())
                mutation.target.writability = WritabilityKind.Save;
            object? Run(NeoClient client) => NeoMemberNSFunction.GetOrCreateStatic(client, function.id).InvokeStatic(Array.Empty<object?>());
            using var original = NeoTestSaveStack.ClientFromSchema(schema, assumeCurrentSchema: false);
            Assert.That(Run(original), Is.EqualTo(20));
            string saved = original.SerializeSaveData();
            using var restored = NeoTestSaveStack.ClientFromSchema(schema, loadedSaveContent: saved, assumeCurrentSchema: false);
            Assert.That(Run(restored), Is.EqualTo(40), "The touched list restores with its two entries before the next two are added.");
        }

        [TestCase("StaticStoredClosureB", "bool")]
        [TestCase("InterfaceStoredClosureB", "string")]
        public void ConstructionClosureStoredInSaveFieldSurvivesReload(string probeName, string expected)
        {
            var schema = NeoTestExport.Read(File.ReadAllText(Export));
            var holder = schema.classes.Values.Single(type => type.name == "HolderB");
            var field = (DelegateMember)schema.members[holder.schema["G"]];
            field.Storage = NeoMemberStorage.Save;
            var probe = schema.classes.Values.Single(type => type.name == "Probe");
            var function = (NSFunctionMember)schema.members[probe.schema[probeName]];
            var assignment = function.action!.instructions.OfType<AssignInstruction>().Single();
            assignment.target.writability = WritabilityKind.Save;
            var source = (CallFunctionPointer)assignment.pointer;
            var construction = NeoTypeBindings.FromTypeInfo(source.receiver!.staticConstruction!);
            string saved;
            string valueId;
            using (var original = NeoTestSaveStack.ClientFromSchema(schema, assumeCurrentSchema: false))
            {
                Assert.That(NeoMemberNSFunction.GetOrCreateStatic(original, function.id).InvokeStatic(Array.Empty<object?>()), Is.EqualTo(expected));
                Assert.That(original.TryResolveStaticBinding(field.id, out _, out var ownership, out var resolvedId), Is.True);
                Assert.That(ownership, Is.EqualTo(NeoValueOwnership.Save));
                valueId = resolvedId!;
                saved = original.SerializeSaveData();
            }
            var content = JsonConvert.DeserializeObject<ProjectSaveData>(saved)!;
            var closure = ((DelegateMemberValue)content.values[valueId]).value!;
            Assert.That(closure.site, Is.EqualTo(source.memberId + ":function:0"));
            Assert.That(closure.declaringConstruction, Is.EqualTo(construction));
            using var restored = NeoTestSaveStack.ClientFromSchema(schema, loadedSaveContent: saved, assumeCurrentSchema: false);
            using var stored = new NeoMemberDelegate(restored, field.id, valueId, NeoValueOwnership.Save);
            Assert.That(stored.Invoke(Array.Empty<object?>()), Is.EqualTo(expected), "Invoke the saved closure without rerunning its factory.");
        }

        [TestCaseSource(nameof(Cases))]
        public void ReturnsWhatTheSharedFixtureRequires(string name, JToken expected)
        {
            Assert.That(File.Exists(Export), Is.True, "Vendor the validated B3 export before running parity.");
            using var client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText(Export)), assumeCurrentSchema: false);
            var probe = client.ProjectDataForRuntime.classes.Values.Single(type => type.name == "Probe");
            var function = (NSFunctionMember)client.ProjectDataForRuntime.members[probe.schema[name]];
            object? value = NeoMemberNSFunction.GetOrCreateStatic(client, function.id).InvokeStatic(Array.Empty<object?>());
            JToken actual = value is null ? JValue.CreateNull() : JToken.FromObject(value);
            if (expected.Type is JTokenType.Integer or JTokenType.Float)
                Assert.That(actual.Value<double>(), Is.EqualTo(expected.Value<double>()), name);
            else
                Assert.That(JToken.DeepEquals(actual, expected), Is.True, $"{name}: {actual}");
        }

        [Test]
        public void DetachedConstructorListAllowsNullableEntryWrite()
        {
            var schema = NeoTestExport.Read(File.ReadAllText(Export));
            var owner = schema.classes.Values.Single(type => type.name == "CtorListK");
            var list = (ListMember)schema.members[owner.schema["L"]];
            schema.members[list.entryMemberId].Requirement = NeoMemberRequirementKind.Optional;
            using var client = NeoTestSaveStack.ClientFromSchema(schema, assumeCurrentSchema: false);
            var probe = schema.classes.Values.Single(type => type.name == "Probe");
            var function = (NSFunctionMember)schema.members[probe.schema["CtorCollectionNullK"]];
            Assert.That(NeoMemberNSFunction.GetOrCreateStatic(client, function.id).InvokeStatic(Array.Empty<object?>()), Is.EqualTo("no"));
        }

    }
}
