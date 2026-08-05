// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class NeoScriptAllocationTrackerTests
    {
        [Test]
        public void ConstructorRowSafetyCeilingMatchesLargeAuthoredGraphs()
        {
            Assert.AreEqual(
                4_096,
                NeoScriptExecutionBudgetLimits.DefaultConstructedSessionRows);
        }

        [Test]
        public void RepublishingNestedRowsDoesNotChargeThemTwice()
        {
            var tracker = new NeoScriptAllocationTracker(
                new NeoScriptExecutionBudgetLimits(
                    producedCollectionEntries: 3,
                    constructedSessionRows: 2));
            tracker.EnterExecution();
            var rows = new MemberValue[]
            {
                new ObjectMemberValue
                {
                    id = "object-row",
                    value = new Dictionary<string, string>
                    {
                        ["A"] = "a",
                        ["B"] = "b",
                    },
                },
                new ArrayMemberValue
                {
                    id = "array-row",
                    value = new[] { "entry" },
                },
            };

            Assert.DoesNotThrow(() => tracker.ConsumeCreatedSessionRows(rows));
            Assert.DoesNotThrow(() => tracker.ConsumeCreatedSessionRows(rows));
        }
    }
}
