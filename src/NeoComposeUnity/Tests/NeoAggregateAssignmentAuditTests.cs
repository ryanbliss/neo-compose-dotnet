// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NUnit.Framework;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Tests
{
    public class NeoAggregateAssignmentAuditTests
    {
        [TestCase("SameSlot")]
        [TestCase("NestedSameSlot")]
        [TestCase("SparseRemove")]
        [TestCase("SparseRemoveMiddle")]
        [TestCase("StaticStoredMap")]
        [TestCase("StaticStoredList")]
        [TestCase("DictAlias")]
        [TestCase("SameSlotList")]
        [TestCase("SameSlotMap")]
        [TestCase("SparseOverride")]
        [TestCase("SparseClear")]
        [TestCase("SparseInsert")]
        [TestCase("DictReverse")]
        [TestCase("DictReplacement")]
        [TestCase("DictNull")]
        [TestCase("DictClass")]
        [TestCase("SetAlias")]
        [TestCase("SetReverse")]
        [TestCase("SetReplacement")]
        [TestCase("SetAliasOfAlias")]
        [TestCase("SetNull")]
        [TestCase("StaticDictAlias")]
        [TestCase("StaticSetAlias")]
        [TestCase("SetFieldHandle")]
        [TestCase("DictFieldHandle")]
        [TestCase("NestedTupleSnapshot")]
        [TestCase("FreshRemove")]
        [TestCase("CopiedRemove")]
        [TestCase("FreshInsert")]
        [TestCase("EnumDictAlias")]
        public void AssignmentsPreserveValues(string name) =>
            NeoSourceParityFixture.AssertTrueCase("aggregate-assignment-audit.json", name);

        [Test]
        public void RemovedDefaultListEntryKeepsSurvivorsAfterSaveReload()
        {
            using var original = NeoSourceParityFixture.CreateClient("aggregate-assignment-audit.json");
            var remove = original.members.Values.Single(m => m.name == "RemoveSavedFirst");
            var read = original.members.Values.Single(m => m.name == "ReadSavedSurvivors");
            NeoMemberNSFunction.GetOrCreateStatic(original, remove.id).InvokeStatic();
            Assert.That(NeoMemberNSFunction.GetOrCreateStatic(original, read.id).InvokeStatic(), Is.True);
            using var reopened = NeoTestSaveStack.ClientFromSchema(original.ProjectDataForRuntime,
                loadedSaveContent: original.SerializeSaveData());
            Assert.That(NeoMemberNSFunction.GetOrCreateStatic(reopened, read.id).InvokeStatic(), Is.True);
        }

        [TestCase("RejectDuplicate")]
        [TestCase("RejectOtherSlot")]
        [TestCase("RejectOtherOwner")]
        public void InvalidTupleAssignmentLeavesDestinationUnchanged(string method)
        {
            using var client = NeoSourceParityFixture.CreateClient("aggregate-assignment-audit.json");
            object? Call(string name, params object?[] args) => NeoMemberNSFunction.GetOrCreateStatic(client,
                client.members.Values.Single(m => m.name == name).id).InvokeStatic(args);
            var holder = Call("CreateHolder");
            Assert.Throws<NeoScriptException>(() => Call(method, holder));
            Assert.That(Call("HolderUnchanged", holder), Is.True);
        }
    }
}
