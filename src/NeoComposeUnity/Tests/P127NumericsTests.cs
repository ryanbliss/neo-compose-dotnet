// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Linq;
using NeoCompose.Runtime.NeoScript;
using NeoCompose.Runtime.NeoScript.Numerics;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>P127 §3.1 and §3.3 behavior the shared fixture does not reach.</summary>
    public class P127NumericsTests
    {
        [Test]
        public void AggregatesAnySequence()
        {
            Assert.That(NeoInt32.Sum(Enumerable.Repeat(3, 4)), Is.EqualTo(12));
            Assert.That(NeoInt32.Average(Enumerable.Repeat(3, 4)), Is.EqualTo(3f));
            Assert.That(NeoInt32.Average(Enumerable.Empty<int>()), Is.Null);
        }

        [Test]
        public void ThrowsDivisionByZeroWithItsMintedClass()
        {
            NeoRuntimeError error = Assert.Throws<NeoRuntimeError>(() => NeoInt32.Divide(1, 0))!;
            Assert.That(error.ErrorClass, Is.EqualTo(NeoErrorClass.NeoDivideByZeroError));
            Assert.That(error.Payload.ClassId, Is.EqualTo(NeoErrorContract.NeoDivideByZeroError));
        }
    }
}
