// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using NeoCompose.Runtime.NeoScript;
using NeoCompose.Runtime.NeoScript.NumberMembers;
using NeoCompose.Runtime.NeoScript.Numerics;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>P127 §8.1: the number members' behavior the shared fixture can't show.</summary>
    public class P127NumberMembersTests
    {
        [Test]
        public void ReadsEveryNaNPatternAsTheOneNaN()
        {
            Assert.That(NeoSingleMembers.ToBits(float.NaN), Is.EqualTo(0xFFC00000u));
            Assert.That(NeoSingleMembers.ToBits(NeoSingleMembers.FromBits(0x7FC00001u)), Is.EqualTo(0xFFC00000u));
            Assert.That(NeoSingleMembers.ToBits(-0f), Is.EqualTo(0x80000000u));
        }

        [Test]
        public void ThrowsTheP122ArithmeticErrorForSignOfNaN()
        {
            NeoRuntimeError error = Assert.Throws<NeoRuntimeError>(() => NeoSingleMembers.Sign(float.NaN))!;
            Assert.That(error.Payload.ClassId, Is.EqualTo(NeoErrorContract.NeoArithmeticError));
            Assert.That(error.Message, Is.EqualTo("Function does not accept floating point Not-a-Number values."));
        }

        [Test]
        public void RoundsTheFusedMultiplyAddOnce()
        {
            Assert.That(NeoSingleMembers.MultiplyAddEstimate(0.1f, 10f, -1f), Is.EqualTo(1.4901161E-08f));
        }

        [Test]
        public void KeepsTheSignOfDecimalZero()
        {
            Assert.That(NeoDecimalMembers.Negate("0"), Is.EqualTo("-0"));
            Assert.That(NeoDecimalMembers.Negate("-0.00"), Is.EqualTo("0.00"));
            Assert.That(NeoDecimalMembers.Is(NeoNumberPredicate.IsNegative, "-0"), Is.True);
            Assert.That(NeoDecimalMembers.Is(NeoNumberPredicate.IsZero, "-0"), Is.True);
        }

        [Test]
        public void CreatesBetweenPrimitivesThroughTheHooks()
        {
            foreach (NeoCreateMode mode in (NeoCreateMode[])Enum.GetValues(typeof(NeoCreateMode)))
            {
                Assert.That(NeoNumberConversionMembers.CreateFrom(mode, NeoNumericKind.Int, NeoNumericKind.Float, 1.5f), Is.EqualTo(1));
                Assert.That(NeoNumberConversionMembers.CreateFrom(mode, NeoNumericKind.Decimal, NeoNumericKind.Int, 7), Is.EqualTo("7"));
                Assert.That(NeoNumberConversionMembers.CreateFrom(mode, NeoNumericKind.Float, NeoNumericKind.Decimal, "2.5"), Is.EqualTo(2.5f));
                Assert.That(
                    NeoNumberConversionMembers.CreateFrom(mode, NeoNumericKind.Int, NeoNumericKind.Decimal, "-2147483648.9"),
                    Is.EqualTo(NeoNumericConversions.Int32FromDecimal("-2147483648.9", mode)));
            }
            Assert.That(NeoNumberConversionMembers.CreateFrom(NeoCreateMode.Checked, NeoNumericKind.Decimal, NeoNumericKind.Decimal, "1.10"), Is.EqualTo("1.10"));
        }

        [Test]
        public void RefusesAuthoredNumberTypes()
        {
            Assert.That(NeoNumberConversionMembers.TryConvertFrom(NeoCreateMode.Checked, NeoNumericKind.Int, null, 1, out _), Is.False);
            Assert.That(NeoNumberConversionMembers.TryConvertTo(NeoCreateMode.Checked, NeoNumericKind.Decimal, null, "1", out _), Is.False);
            Assert.That(NeoNumberConversionMembers.CreateFromAuthored(true, 3), Is.EqualTo(3));
            NeoRuntimeError error = Assert.Throws<NeoRuntimeError>(() => NeoNumberConversionMembers.CreateFromAuthored(false, null))!;
            Assert.That(error.ErrorClass, Is.EqualTo(NeoErrorClass.NeoNotSupportedError));
            Assert.That(error.Message, Is.EqualTo("Specified method is not supported."));
        }
    }
}
