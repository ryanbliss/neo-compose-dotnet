// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Tests.SequenceGenerated;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using NeoCompose.Runtime.NeoScript;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Tests
{
    public class NeoSequenceBridgeTests
    {
        private TestProjectNeo project = null!;
        [SetUp]
        public void Setup() => project = new TestProjectNeo(NeoTestSaveStack.LoadClient(
            File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/sequence-generated-export.json")));
        [TearDown]
        public void Teardown() => project.Dispose();

        [Test]
        public void GeneratedMethodsRoundTripLazyRuntimeAndForeignSequences()
        {
            IEnumerable<int> source = SequenceApi.Range(4);
            Assert.That(source.ToArray(), Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(SequenceApi.Sum(source), Is.EqualTo(6));
            using IEnumerator<int> cursor = SequenceApi.Cursor(source);
            Assert.That(cursor.MoveNext(), Is.True);
            Assert.That(cursor.Current, Is.Zero);
            Assert.That(SequenceApi.Remaining(cursor), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(cursor.MoveNext(), Is.False);
            Assert.That(() => ((IEnumerator)cursor).Reset(), Throws.TypeOf<NotSupportedException>());
            Assert.That(SequenceApi.One(42).Single(), Is.EqualTo(42));
        }

        [Test]
        public void SequenceConstrainedGenericResultsPreserveClrValuesAndNullability()
        {
            IEnumerable<int> foreign = new[] { 2, 3 };
            Assert.That(SequenceApi.EchoConstrained(foreign), Is.SameAs(foreign));
            Assert.That(SequenceApi.EchoNullable(foreign), Is.SameAs(foreign));
            Assert.That(SequenceApi.EchoNullable<IEnumerable<int>>(null), Is.Null);
            IEnumerable<int> generated = SequenceApi.Range(3);
            Assert.That(SequenceApi.EchoConstrained(generated).ToArray(), Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SequencePropertyAdaptersPreserveWritesAndReadOnlyGuards(bool inherited)
        {
            NeoGeneratedClassValue sequence = inherited ? new InheritedMutableSequence(new SequenceItem()) : new MutableSequence();
            var writable = (IMutableSequence<IReadOnlySequenceItem>)sequence;
            var item = new SequenceItem { Number = 42 };
            writable.Value = item;
            Assert.That(writable.Value.Number, Is.EqualTo(42));
            Assert.That(writable.Single().Number, Is.EqualTo(42));
            var node = new NeoMemberClass(project.Client, sequence.BackingMember, sequence.valueId, sequence.ValueOwnership);
            using NeoGeneratedClassValue view = inherited
                ? new InheritedMutableSequence(project.Client, node, true)
                : new MutableSequence(project.Client, node, true);
            Assert.That(view.IsReadOnly, Is.True);
            var readOnly = (IMutableSequence<IReadOnlySequenceItem>)view;
            Assert.That(() => readOnly.Value = new SequenceItem(), Throws.InvalidOperationException);
            Assert.That(writable.Value.Number, Is.EqualTo(42));
        }

        [Test]
        public void GenericAuthoredClassImplementsTheClosedClrProtocol()
        {
            IReadOnlyBox<int> box = SequenceApi.MakeBox();
            IEnumerable<int> values = box;
            Assert.That(values.Single(), Is.EqualTo(42));
            Assert.That(SequenceApi.Sum(values), Is.EqualTo(42));
            Assert.That(SequenceApi.SumConcrete(), Is.EqualTo(6));
            Assert.That(SequenceApi.GenericListCheck(), Is.True);
            Assert.That(SequenceApi.ConstrainedCheck(), Is.True);
            Assert.That(SequenceApi.ClassGenericCheck(), Is.True);
            Assert.That(SequenceApi.SumInherited(), Is.EqualTo(42));
            Assert.That(SequenceApi.YieldInherited().Single(), Is.EqualTo(42));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WholeStoredCollectionWritesRejectRuntimeSequenceEntries(bool dictionary)
        {
            var holder = new SequenceStorage();
            IEnumerable<int> values = SequenceApi.Range(2);
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Exception,
                new System.Text.RegularExpressions.Regex("Stored interface field.*requires an authored class"));
            var error = Assert.Catch(() =>
            {
                if (dictionary)
                    holder.StoreDictionary(values);
                else
                    holder.StoreList(values);
            });
            Assert.That(error!.ToString(), Does.Contain("authored class"));
        }

        [Test]
        public void ReadOnlySdkListWorksAsAnEnumerableWithoutMaterialization()
        {
            var data = JObject.Parse(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/sequence-generated-export.json"));
            const string entryId = "8381d5c8-c9c6-588f-91ea-7c89518425dc";
            data["members"]!["numbers"] = new JObject { ["id"] = "numbers", ["name"] = "Numbers", ["projectId"] = "project-id", ["kind"] = 6, ["entryMemberId"] = entryId };
            data["values"]!["number-a"] = new JObject { ["id"] = "number-a", ["projectId"] = "project-id", ["memberId"] = entryId, ["value"] = 2 };
            data["values"]!["number-b"] = new JObject { ["id"] = "number-b", ["projectId"] = "project-id", ["memberId"] = entryId, ["value"] = 3 };
            data["values"]!["numbers-row"] = new JObject { ["id"] = "numbers-row", ["projectId"] = "project-id", ["memberId"] = "numbers", ["value"] = new JArray("number-a", "number-b") };
            project.Dispose();
            using var local = new TestProjectNeo(NeoTestSaveStack.LoadClient(data.ToString()));
            var values = new NeoReadOnlyList<int>(local.Client, new NeoMemberList(local.Client, "numbers", "numbers-row"), (_, item) => NeoGeneratedTypesSupport.ReadInt((NeoMemberInt)item) ?? throw new InvalidOperationException("Missing list entry."));
            Assert.That(SequenceApi.Sum(values), Is.EqualTo(5));
        }

        [Test]
        public void NestedSequencesPreserveNullableElements()
        {
            string?[] values = { "first", null, "last" };
            IEnumerable<IEnumerable<string?>> nested = SequenceApi.Nested(values);
            Assert.That(nested.Single().ToArray(), Is.EqualTo(values));
            Assert.That(SequenceApi.One<int?>(null).Single(), Is.Null);
            Assert.That(SequenceApi.SumNested(new[] { ((IEnumerable<int>)new[] { 2, 4 }, 0) }), Is.EqualTo(6));
        }

        [Test]
        public void UnconstrainedGenericSequenceElementsRemainLazy()
        {
            IEnumerable<int> foreign = new[] { 2, 3 };
            Assert.That(SequenceApi.One(foreign).Single().ToArray(), Is.EqualTo(new[] { 2, 3 }));
            IEnumerable<int> script = SequenceApi.Range(3);
            Assert.That(SequenceApi.One(script).Single().ToArray(), Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LoadingStoredCursorShapesIsRejected(bool nested)
        {
            var data = JObject.Parse(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/sequence-generated-export.json"));
            var members = (JObject)data["members"]!;
            members["cursor-field"] = new JObject
            {
                ["id"] = "cursor-field",
                ["projectId"] = "project-id",
                ["name"] = "Cursor",
                ["kind"] = 7,
                ["interfaceId"] = NeoSequenceContract.Enumerator,
                ["requirement"] = 0,
                ["typeArguments"] = new JObject { [NeoSequenceContract.EnumeratorElement] = new JObject { ["kind"] = 1, ["memberId"] = "8381d5c8-c9c6-588f-91ea-7c89518425dc" } },
            };
            string field = "cursor-field";
            if (nested)
            {
                members["list-field"] = new JObject { ["id"] = "list-field", ["projectId"] = "project-id", ["name"] = "Cursors", ["kind"] = 6, ["entryMemberId"] = field, ["requirement"] = 0 };
                field = "list-field";
            }
            data["classes"]!["12500000-0000-4000-8000-000000000006"]!["schema"]!["Cursors"] = field;
            Assert.That(() => NeoTestSaveStack.LoadClient(data.ToString()), Throws.Exception.With.Message.Contains("IEnumerator"));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void ForeignElementsCannotResolveThroughAnotherClientsIdenticalRowId(bool nested, bool payload)
        {
            var data = JObject.Parse(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/sequence-generated-export.json"));
            const string classId = "12500000-0000-4000-8000-000000000006";
            data["members"]!["shared-member"] = new JObject { ["id"] = "shared-member", ["name"] = "Shared", ["projectId"] = "project-id", ["kind"] = 7, ["classId"] = classId };
            data["values"]!["shared-row"] = new JObject { ["id"] = "shared-row", ["projectId"] = "project-id", ["classId"] = classId, ["value"] = new JObject() };
            using var first = NeoTestSaveStack.LoadClient(data.ToString());
            using var second = NeoTestSaveStack.LoadClient(data.ToString());
            var foreign = NumberStream.Create(second, new NeoMemberClass(second, "shared-member", "shared-row", NeoValueOwnership.Asset));
            TypeInfo element = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = classId };
            object value = payload ? NeoValueWritePayload.FromValueReference("shared-row", foreign) : foreign;
            if (nested)
            {
                element = new TupleTypeInfo { type = MemberKind.Tuple, required = true, elements = new[] { new TupleElementTypeInfo { typeInfo = element }, new TupleElementTypeInfo { typeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true } } } };
                value = (value, 1);
            }
            var sequence = (NSGetterEvaluator.ScriptSequence)NeoSequences.WriteEnumerable(first, new[] { value }, element, item => item)!;
            var context = first.CreateGetterContext(NeoValueOwnership.Asset);
            var cursor = sequence.GetEnumerator(context);
            Assert.That(cursor.MoveNext(context), Is.True);
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Exception, new System.Text.RegularExpressions.Regex("InvalidOperationException: An SDK value belongs to a different project client\\."));
            Assert.Catch<NSGetterRuntimeError>(() => cursor.Current(context));
            cursor.Dispose(context);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void ForeignSdkCollectionElementsAreRejectedBeforeReading(bool nested, bool disposed)
        {
            using var other = NeoTestSaveStack.LoadClient(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/sequence-generated-export.json"));
            // A missing backing node proves the ownership check happens before reading the view.
            var values = new NeoReadOnlyList<int>(other, null!, (_, _) => 0);
            if (disposed)
                other.Dispose();
            var owner = project.Client;
            TypeInfo element = new CollectionTypeInfo { type = MemberKind.List, required = true, entryTypeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true } };
            object value = values;
            if (nested)
            {
                element = new TupleTypeInfo { type = MemberKind.Tuple, required = true, elements = new[] { new TupleElementTypeInfo { typeInfo = element }, new TupleElementTypeInfo { typeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true } } } };
                value = (values, 1);
            }
            var sequence = (NSGetterEvaluator.ScriptSequence)NeoSequences.WriteEnumerable(owner, new[] { value }, element, item => item)!;
            var ctx = owner.CreateGetterContext(NeoValueOwnership.Asset);
            var cursor = sequence.GetEnumerator(ctx);
            Assert.That(cursor.MoveNext(ctx), Is.True);
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Exception, new System.Text.RegularExpressions.Regex("InvalidOperationException: An SDK value belongs to a different project client\\."));
            Assert.Catch<NSGetterRuntimeError>(() => cursor.Current(ctx));
            cursor.Dispose(ctx);
        }

        [Test]
        public void ForeignValuesAreAcquiredAndReadOnlyDuringConsumption()
        {
            var foreign = new TrackedEnumerable();
            using IEnumerator<int> cursor = SequenceApi.Cursor(foreign);
            Assert.That(foreign.Acquisitions, Is.EqualTo(1));
            Assert.That(foreign.Advances, Is.Zero);
            Assert.That(foreign.Reads, Is.Zero);
            Assert.That(cursor.MoveNext(), Is.True);
            Assert.That(foreign.Reads, Is.Zero);
            Assert.That(cursor.Current, Is.EqualTo(1));
            Assert.That(foreign.Reads, Is.EqualTo(1));
            cursor.Dispose();
            cursor.Dispose();
            Assert.That(foreign.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void AuthoredImplementationsSupplyStandardNonGenericBridges()
        {
            var authored = new NumberStream();
            Assert.That(authored.Values.ToArray(), Is.EqualTo(new[] { 7 }));
            Assert.That(SequenceApi.SumNested(new[] { ((IEnumerable<int>)authored, 0) }), Is.EqualTo(6));
            IEnumerable<int> stream = authored;
            Assert.That(stream.ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(SequenceApi.Sum(stream), Is.EqualTo(6));
            IEnumerator cursor = ((IEnumerable)stream).GetEnumerator();
            Assert.That(cursor.MoveNext(), Is.True);
            Assert.That(cursor.Current, Is.EqualTo(1));
            Assert.That(() => cursor.Reset(), Throws.TypeOf<NotSupportedException>());
            ((IDisposable)cursor).Dispose();
        }

        private sealed class TrackedEnumerable : IEnumerable<int>, IEnumerator<int>
        {
            internal int Acquisitions, Advances, Reads, Disposals;
            public IEnumerator<int> GetEnumerator()
            {
                Acquisitions++;
                return this;
            }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            public bool MoveNext()
            {
                Advances++;
                return Advances < 3;
            }
            public int Current
            {
                get
                {
                    Reads++;
                    return Advances;
                }
            }
            object IEnumerator.Current => Current;
            public void Dispose() => Disposals++;
            public void Reset() => throw new NotSupportedException();
        }
    }
}
