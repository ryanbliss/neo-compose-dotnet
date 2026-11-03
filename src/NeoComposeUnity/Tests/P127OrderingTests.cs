// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using NeoCompose.Runtime.NeoScript;
using NeoCompose.Runtime.NeoScript.Ordering;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>P127 §10.2: ordering behavior the shared fixture can't express.</summary>
    public class P127OrderingTests
    {
        private static readonly Func<Exception, bool> IsCatchable = NeoScriptErrorClassification.IsAuthoredCatchable;

        private static int Ascending(int x, int y) => x.CompareTo(y);

        [Test]
        public void WrapsACatchableComparerErrorWithTheOriginalAsItsInnerError()
        {
            var boom = new NSGetterRuntimeError("boom");
            var error = Assert.Throws<NeoRuntimeError>(() => NeoIntroSort.SortList(
                new[] { 3, 1, 2 },
                0,
                3,
                NeoSortHelper.Comparer,
                (x, y) => throw boom,
                null,
                () => "unused",
                IsCatchable));
            Assert.That(error!.Message, Is.EqualTo("Failed to compare two elements in the array."));
            Assert.That(error.InnerException, Is.SameAs(boom));
            Assert.That(error.Payload.ClassId, Is.EqualTo(NeoErrorContract.NeoInvalidOperationError));
            Assert.That(error.Payload.InnerError, Is.SameAs(boom.Payload));
            var orderError = Assert.Throws<NeoRuntimeError>(() => NeoEnumerableOrdering.OrderBy(
                new[] { 0, 1 },
                new[] { new NeoOrderLevel<int>(new[] { 2, 1 }, (x, y) => throw boom, false) },
                () => "unused",
                IsCatchable));
            Assert.That(orderError!.Payload.InnerError, Is.SameAs(boom.Payload));
        }

        [Test]
        public void LetsErrorsNeoScriptCannotCatchPassThroughUnwrapped()
        {
            var fault = new NeoScriptPreExecutionValidationError("deadline");
            Comparison<int> throwing = (x, y) => throw fault;
            var sortError = Assert.Throws<NeoScriptPreExecutionValidationError>(() => NeoIntroSort.SortList(
                new[] { 3, 1, 2 },
                0,
                3,
                NeoSortHelper.Comparable,
                throwing,
                null,
                () => "unused",
                IsCatchable));
            Assert.That(sortError, Is.SameAs(fault));
            var orderError = Assert.Throws<NeoScriptPreExecutionValidationError>(() => NeoEnumerableOrdering.OrderBy(
                new[] { 0, 1 },
                new[] { new NeoOrderLevel<int>(new[] { 2, 1 }, throwing, false) },
                () => "unused",
                IsCatchable));
            Assert.That(orderError, Is.SameAs(fault));
        }

        [Test]
        public void ReadsTheComparerTextOnlyWhenTheComparerProvesInconsistent()
        {
            int reads = 0;
            Func<string> comparerText = () =>
            {
                reads++;
                return "Bogus";
            };
            var items = new int[20];
            for (int i = 0; i < items.Length; i++)
                items[i] = i;
            NeoIntroSort.SortList(items, 0, 20, NeoSortHelper.Comparer, Ascending, null, comparerText, IsCatchable);
            Assert.That(reads, Is.EqualTo(0));
            var error = Assert.Throws<NeoRuntimeError>(() => NeoIntroSort.SortList(items, 0, 20, NeoSortHelper.Comparer, (x, y) => -1, null, comparerText, IsCatchable));
            Assert.That(error!.Message, Does.EndWith("IComparer: 'Bogus'."));
            Assert.That(reads, Is.EqualTo(1));
        }

        [Test]
        public void ReSortsEveryLevelFromSourceOrderSoAnEagerThenByMatchesOneDotNetPass()
        {
            var source = new[] { "d", "a", "c", "b", "e", "f" };
            var first = new NeoOrderLevel<int>(new[] { 1, 0, 1, 0, 1, 0 }, Ascending, false);
            var second = new NeoOrderLevel<int>(new[] { 5, 5, 4, 4, 5, 3 }, Ascending, true);
            Assert.That(
                NeoEnumerableOrdering.OrderBy(source, new[] { first }, () => "unused", IsCatchable),
                Is.EqualTo(new[] { "a", "b", "f", "d", "c", "e" }));
            Assert.That(
                NeoEnumerableOrdering.OrderBy(source, new[] { first, second }, () => "unused", IsCatchable),
                Is.EqualTo(new[] { "a", "b", "f", "d", "e", "c" }));
        }

        [Test]
        public void ThrowsForAnEmptySequenceOfANonNullableType()
        {
            var error = Assert.Throws<NeoRuntimeError>(() => NeoEnumerableOrdering.MinMax(Array.Empty<int>(), x => x, NeoExtreme.Min, Ascending, false));
            Assert.That(error!.Message, Is.EqualTo("Sequence contains no elements"));
            Assert.That(NeoEnumerableOrdering.MinMax<string?, string?>(Array.Empty<string?>(), x => x, NeoExtreme.Min, string.CompareOrdinal, true), Is.Null);
        }
    }
}
