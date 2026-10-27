// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>Actual compiler export: generic stored references retain their ownership distinction.</summary>
    public class NeoGenericReferenceConstructionTests
    {
        [TestCase("RejectAtomic", 10)]
        [TestCase("FreshSets", 2)]
        [TestCase("ReadInsertedValues", 12)]
        [TestCase("RejectedInsertAtomic", 11)]
        [TestCase("ListReferences", 7)]
        [TestCase("DictionaryReferences", 7)]
        [TestCase("TupleReferences", 8)]
        [TestCase("ExplicitListReferences", 7)]
        [TestCase("FailedEncodingCleanup", 2)]
        public void SourceReturns(string probe, int expected)
        {
            using NeoClient client = Client();
            Assert.AreEqual(expected, InvokeProbe(client, probe));
        }

        [TestCase("OwnLocalSet")]
        [TestCase("OwnStoredSet")]
        [TestCase("OwnList")]
        [TestCase("InsertOwnedSet")]
        [TestCase("DuplicateSet")]
        [TestCase("OwnDictionaryEntry")]
        [TestCase("OwnTupleSet")]
        [TestCase("NestedOwned")]
        [TestCase("NonGenericNestedOwned")]
        [TestCase("RefuseOwnedClass")]
        public void SourceRefusesSecondOwnership(string probe)
        {
            using NeoClient client = Client();
            StringAssert.Contains("already owned", Assert.Catch(() => InvokeProbe(client, probe)).ToString());
        }

        [Test]
        public void ScalarIdsRemainLiteralAcrossCalls()
        {
            using NeoClient client = Client();
            Assert.AreEqual(1, InvokeProbe(client, "SaveScalarIds"));
            Assert.AreEqual(2, InvokeProbe(client, "ReadScalarIds"));
        }

        // A construction's reference type arguments are bindings (P127
        // §12.1), so retargeting the export's leftover Lookup members
        // changes nothing it builds.
        [TestCase("ListReferences", 7)]
        [TestCase("DictionaryReferences", 7)]
        [TestCase("TupleReferences", 8)]
        public void ConstructionsIgnoreLookupDescriptorMembers(string probe, int expected)
        {
            ProjectData data = Data();
            string differentTarget = data.classes.Values.Single(type => type.name == "Deep").id;
            foreach (LookupMember lookup in data.members.Values.OfType<LookupMember>())
            {
                if (lookup.declaredTypeInfo is ClassTypeInfo target)
                    target.classId = differentTarget;
            }
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false);
            Assert.AreEqual(expected, InvokeProbe(client, probe));
        }

        [Test]
        public void ReadProjectionStillElidesHandleReferencesWhileStoredProjectionDoesNot()
        {
            using NeoClient client = Client();
            Member descriptor = client.members.Values.OfType<ListMember>().Single(list =>
                client.members[list.entryMemberId] is LookupMember lookup && lookup.declaredTypeInfo is ClassTypeInfo);
            var read = (CollectionTypeInfo)NeoNSFunctionRuntime.TypeInfoFromBindingMember(
                client, descriptor, NeoGenericResolution.EmptyEnv, new HashSet<string>());
            var stored = (CollectionTypeInfo)NeoNSFunctionRuntime.TypeInfoFromBindingMember(
                client, descriptor, NeoGenericResolution.EmptyEnv, new HashSet<string>(),
                NeoNSFunctionRuntime.BindingTypeProjection.Stored);
            Assert.AreEqual(MemberKind.Class, read.entryTypeInfo.type);
            Assert.AreEqual(MemberKind.Reference, stored.entryTypeInfo.type);
            Assert.IsFalse(NeoClient.TypeInfoMatches(read, stored));
            Assert.IsTrue(NeoClient.TypeInfoMatches(read.entryTypeInfo, ((ReferenceTypeInfo)stored.entryTypeInfo).targetTypeInfo));
        }

        private static ProjectData Data()
        {
            return NeoTestExport.Read(File.ReadAllText(
                "Packages/com.ryanbliss.neocompose/Tests/generic-reference-construction-export.json"));
        }

        private static NeoClient Client()
        {
            return NeoTestSaveStack.ClientFromSchema(Data(), assumeCurrentSchema: false);
        }

        private static object? InvokeProbe(NeoClient client, string name)
        {
            Member function = client.members.Values.Single(member => member is NSFunctionMember && member.name == name);
            return NeoMemberNSFunction.GetOrCreateStatic(client, function.id).InvokeStatic(Array.Empty<object?>());
        }
    }
}
