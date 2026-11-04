// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using G = NeoCompose.Tests.P127CodegenShapesParity;

namespace NeoCompose.Tests
{
    public sealed class P127CodegenShapesParityTests
    {
        private const string Root = "Packages/com.ryanbliss.neocompose/Tests/";
        private const string Export = Root + "p127-codegen-shapes-parity-export.json";
        private const string Fixture = Root + "p127-codegen-shapes-parity-fixture.json";

        public static IEnumerable<TestCaseData> Cases()
        {
            foreach (var test in JObject.Parse(File.ReadAllText(Fixture))["returns"]!.Children<JProperty>())
                foreach (bool generated in new[] { false, true })
                    yield return new TestCaseData(test.Name, test.Value, generated)
                        .SetName("Shapes_" + test.Name + (generated ? "_Generated" : "_Evaluator"));
        }

        [Test]
        public void DeliveryContainsAllCasesAndItsRequiredExport()
        {
            Assert.That(File.Exists(Export), Is.True, "The shared export is required; missing delivery must fail.");
            var fixture = JObject.Parse(File.ReadAllText(Fixture));
            Assert.That(fixture["returns"]!.Children().Count(), Is.EqualTo(28));
            Assert.That(fixture["csharpBridges"]!.Children().Count(), Is.EqualTo(7));
        }

        [TestCaseSource(nameof(Cases))]
        public void SharedCasesMatchInEvaluatorAndGeneratedCSharp(string name, JToken expected, bool generated)
        {
            Assert.That(File.Exists(Export), Is.True, "The shared export is required.");
            using var client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText(Export)), assumeCurrentSchema: false);
            using var project = new G.TestProjectNeo(client);
            object? actual;
            if (generated)
                actual = name switch
                {
                    "OutArgument" => G.Probe.OutArgument(),
                    "ArityPair" => G.Probe.ArityPair(),
                    "InterfaceArityPair" => G.Probe.InterfaceArityPair(),
                    "GenericStaticPerConstruction" => G.Probe.GenericStaticPerConstruction(),
                    "OverrideToString" => G.Probe.OverrideToString(),
                    "OverrideEquals" => G.Probe.OverrideEquals(),
                    "OverrideGetHashCode" => G.Probe.OverrideGetHashCode(),
                    "OperatorForward" => G.Probe.OperatorForward(),
                    "ComparerSlot" => G.Probe.ComparerSlot(),
                    "DefaultInterfaceMember" => G.Probe.DefaultInterfaceMember(),
                    "MaybeNullOutFails" => G.Probe.MaybeNullOutFails(),
                    "MaybeNullOutSucceeds" => G.Probe.MaybeNullOutSucceeds(),
                    "NotNullOutFails" => G.Probe.NotNullOutFails(),
                    "GenericMaybeNullOutFails" => G.Probe.GenericMaybeNullOutFails(),
                    "NonGenericComparerEquals" => G.Probe.NonGenericComparerEquals(),
                    "NonGenericComparerGetHashCode" => G.Probe.NonGenericComparerGetHashCode(),
                    "NonGenericComparerCompare" => G.Probe.NonGenericComparerCompare(),
                    "AbstractOverrideHash" => G.Probe.AbstractOverrideHash(),
                    "InheritedEquatable" => G.Probe.InheritedEquatable(),
                    "ExplicitEquatable" => G.Probe.ExplicitEquatable(),
                    "ExplicitComparable" => G.Probe.ExplicitComparable(),
                    "ProtectedAndPrivateGetters" => G.Probe.ProtectedAndPrivateGetters(),
                    "ReferenceIntWrite" => G.Probe.ReferenceIntWrite(),
                    "ReferenceIntRead" => G.Probe.ReferenceIntRead(),
                    "ReferenceClassWrite" => G.Probe.ReferenceClassWrite(),
                    "ReferenceClassSame" => G.Probe.ReferenceClassSame(),
                    "ReferenceStoredInt" => G.Probe.ReferenceStoredInt(),
                    "ReferenceStoredClass" => G.Probe.ReferenceStoredClass(),
                    _ => throw new InvalidOperationException("Unconsumed shared case: " + name),
                };
            else
            {
                var probe = client.ProjectDataForRuntime.classes.Values.Single(type => type.name == "Probe");
                actual = NeoMemberNSFunction.GetOrCreateStatic(client, probe.schema[name]).InvokeStatic(Array.Empty<object?>());
            }
            if (expected.Type is JTokenType.Integer or JTokenType.Float)
                Assert.That(Convert.ToDouble(actual), Is.EqualTo(expected.Value<double>()), name);
            else
                Assert.That(JToken.DeepEquals(actual is null ? JValue.CreateNull() : JToken.FromObject(actual), expected), Is.True,
                    $"{name}: expected {expected}, got {actual}");
        }

        public static IEnumerable<TestCaseData> Bridges()
        {
            foreach (var entry in JObject.Parse(File.ReadAllText(Fixture))["csharpBridges"]!)
                yield return new TestCaseData(entry["name"]!.Value<string>(),
                    entry["receiver"]!.Value<string>(), entry["interface"]!.Value<string>(),
                    entry["call"]!.Value<string>(), entry["returns"]!)
                    .SetName("CSharpBridge_" + entry["name"]!.Value<string>() + "_" + entry["interface"]!.Value<string>());
        }

        [TestCaseSource(nameof(Bridges))]
        public void EverySharedBridgeCallsTheGeneratedClrInterface(string name, string receiver, string contract,
            string call, JToken expected)
        {
            using var client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText(Export)), assumeCurrentSchema: false);
            using var project = new G.TestProjectNeo(client);
            object actual = (receiver, contract, call) switch
            {
                ("Probe.FoldComparer", "System.Collections.IEqualityComparer", "Equals(\"lower\", \"LOWER\")") =>
                    ((System.Collections.IEqualityComparer)G.Probe.FoldComparer).Equals("lower", "LOWER"),
                ("Probe.FoldComparer", "System.Collections.IEqualityComparer", "GetHashCode(\"abc\")") =>
                    ((System.Collections.IEqualityComparer)G.Probe.FoldComparer).GetHashCode("abc"),
                ("Probe.LengthComparer", "System.Collections.IComparer", "Compare(\"aa\", \"b\")") =>
                    ((System.Collections.IComparer)G.Probe.LengthComparer).Compare("aa", "b"),
                ("Probe.EqLeft", "System.IEquatable<ExplicitEq>", "Equals(Probe.EqRight)") =>
                    ((IEquatable<G.ExplicitEq>)G.Probe.EqLeft).Equals(G.Probe.EqRight),
                ("Probe.EqLeft", "System.IEquatable<IReadOnlyExplicitEq>", "Equals(Probe.EqRight)") =>
                    ((IEquatable<G.IReadOnlyExplicitEq>)G.Probe.EqLeft).Equals(G.Probe.EqRight),
                ("Probe.CmpLeft", "System.IComparable<ExplicitCmp>", "CompareTo(Probe.CmpRight)") =>
                    ((IComparable<G.ExplicitCmp>)G.Probe.CmpLeft).CompareTo(G.Probe.CmpRight),
                ("Probe.CmpLeft", "System.IComparable<IReadOnlyExplicitCmp>", "CompareTo(Probe.CmpRight)") =>
                    ((IComparable<G.IReadOnlyExplicitCmp>)G.Probe.CmpLeft).CompareTo(G.Probe.CmpRight),
                _ => throw new InvalidOperationException("Unconsumed C# bridge: " + name),
            };
            Assert.That(JToken.DeepEquals(JToken.FromObject(actual), expected), Is.True, name);
        }

        [Test]
        public void GameCSharpUsesTheGeneratedShapesDirectly()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText(Export)), assumeCurrentSchema: false);
            using var project = new G.TestProjectNeo(client);
            Assert.That(G.Probe.TryIt(out int value), Is.True);
            Assert.That(value, Is.EqualTo(1));
            Assert.That(G.Parsed.TryParse("bad", out var absent), Is.False);
            Assert.That(absent, Is.Null);
            Assert.That(G.Parsed.TryParse("ok", out var parsed), Is.True);
            Assert.That(parsed.V, Is.EqualTo(1));
            Assert.That(G.Parsed.TryFind("bad", out var notFound), Is.False);
            Assert.That(notFound, Is.Null);
            Assert.That(G.Parsed.TryFirst(Array.Empty<string>(), out var first), Is.False);
            Assert.That(first, Is.Null);
            Assert.That(G.Parsed.TryFirst(new[] { "found" }, out first), Is.True);
            Assert.That(first, Is.EqualTo("found"));
            Assert.That(typeof(G.Box).Name, Is.EqualTo("Box"));
            Assert.That(typeof(G.Box<string>).Name, Is.EqualTo("Box`1"));
            var square = new G.Sq();
            Assert.That(((G.IShape)square).Sides(), Is.EqualTo(4));
            Assert.That(((G.IShape<int>)square).Size(), Is.EqualTo(2));
            G.Box<int>.Count = 3;
            G.Box<string>.Count = 5;
            Assert.That(G.Box<int>.Count * 10 + G.Box<string>.Count, Is.EqualTo(35));
            Assert.That(G.Box<int>.Changed, Is.SameAs(G.Box<int>.Changed));
            Assert.That(G.Box<int>.Changed, Is.Not.SameAs(G.Box<string>.Changed));
            G.Box<int>.Changed = G.Box<int>.Changed;
            G.Box<int>.Changed.Invoke();
            G.Box<string>.Changed.Invoke();
            var a = new G.Coin(V: 9);
            Assert.That(a.ToString(), Is.EqualTo("coin"));
            Assert.That(a.Equals((object)new G.Coin(V: 9)), Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(9));
            Assert.That((new G.Pt(X: 1) + new G.Pt(X: 2)).X, Is.EqualTo(3));
            var holder = new G.Holder();
            var comparer = new G.Ci();
            holder.Cmp = comparer;
            Assert.That(holder.Cmp, Is.InstanceOf<G.Ci>());
            Assert.That(holder.Cmp, Is.SameAs(holder.Cmp));
            Assert.That(holder.Cmp!.Equals("a", "a"), Is.True);
            Assert.That(holder.Cmp.GetHashCode("x"), Is.Zero);
            Assert.That(((G.IV<string>)new G.Kv()).Name(), Is.EqualTo("v"));
        }
    }
}
