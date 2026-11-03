// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class SortGuardAliasTests
    {
        private const string FixtureFile = "sort-guard-alias-parity.json";

        public static IEnumerable<string> Cases() => NeoSourceParityFixture.Cases(FixtureFile);

        [TestCaseSource(nameof(Cases))]
        public void CompiledSortGuardAliasesMatchContract(string name)
        {
            using var client = NeoSourceParityFixture.CreateClient(FixtureFile);
            string id = client.ProjectDataForRuntime.members.Values.Single(member => member.name == name).id;
            object? Invoke() => NeoMemberNSFunction.GetOrCreateStatic(client, id).InvokeStatic(Array.Empty<object?>());
            if (name is "CleanupAfterThrow" or "UnrelatedMutation" or "UnrelatedSort")
            {
                Assert.That(Invoke(), Is.True);
                return;
            }
            // The old compiled bodies attempt authored catches. Change 2
            // requires the host integrity error to escape every one of them.
            var error = Assert.Throws<NeoSortIntegrityError>(() => Invoke())!;
            Assert.That(error.DiagnosticMessage, Is.EqualTo("Cannot change a list while its Sort comparison is running."));
            Assert.That(NeoScriptErrorClassification.IsAuthoredCatchable(error), Is.False);
        }

        [Test]
        public void HostGuardUnwindsSortState([Values(false, true)] bool nested)
        {
            using var client = NeoSourceParityFixture.CreateClient(FixtureFile);
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var scope = new NeoScriptScope();
            var type = new PrimitiveTypeInfo { type = MemberKind.Int, required = true };
            CollectionCallInstruction Sort(string memberId)
            {
                client.ProjectDataForRuntime.members[memberId] = new FunctionMember
                {
                    id = memberId,
                    name = memberId,
                    kind = MemberKind.Function,
                    Modifier = NeoMemberModifierKind.Static,
                    returnTypeInfo = type,
                    argumentTypes = new[]
                    {
                        new FunctionArgumentTypeInfo { name = "x", type = MemberKind.Int, required = true },
                        new FunctionArgumentTypeInfo { name = "y", type = MemberKind.Int, required = true },
                    },
                };
                var comparer = new NSGetterEvaluator.NeoBuiltInComparer(NeoBuiltInComparerKind.Create, NeoComparerFamilyKind.Comparison,
                    NeoTypeBindings.FromTypeInfo(type), new[] { new NeoDelegateValue { memberId = memberId } });
                return new CollectionCallInstruction
                {
                    ordering = new OrderingSite { type = type, comparer = new ValuePointer { type = PointerKind.Value, primitiveResolved = true, primitive = comparer } },
                };
            }
            var sort = Sort("test-outer-compare");
            var nestedSort = Sort("test-inner-compare");
            object?[] outer = { 2d, 1d }, inner = { 4d, 3d };
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["test-outer-compare"] = (_, _, _, _) =>
                {
                    if (nested)
                        NSGetterEvaluator.SortCollection(inner, nestedSort, scope, ctx);
                    NSGetterEvaluator.AssertNotSorting(outer, ctx);
                    return 0d;
                },
                ["test-inner-compare"] = (_, _, _, _) =>
                {
                    NSGetterEvaluator.AssertNotSorting(outer, ctx);
                    return 0d;
                },
            });
            Assert.Throws<NeoSortIntegrityError>(() => NSGetterEvaluator.SortCollection(outer, sort, scope, ctx));
            Assert.That(NSGetterEvaluator.HasActiveSort(ctx), Is.False);
            var normal = new CollectionCallInstruction { ordering = new OrderingSite { type = type } };
            Assert.That(NSGetterEvaluator.SortCollection(outer, normal, scope, ctx), Is.EqualTo(new[] { 1d, 2d }));
            Assert.That(NSGetterEvaluator.SortCollection(inner, normal, scope, ctx), Is.EqualTo(new[] { 3d, 4d }));
        }
    }
}
