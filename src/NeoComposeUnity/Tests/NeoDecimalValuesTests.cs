// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using NeoCompose.Runtime;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// The <see cref="NeoDecimalValues"/> boundary funnel
    /// (specs/decimal-member.md §6.2): invariant-culture parse/format,
    /// scale preservation, negative-zero normalization, and the four distinct
    /// single-condition failures.
    /// </summary>
    public class NeoDecimalValuesTests
    {
        // ------------------------------------------------------------------
        // Parse / Format round-trip + scale preservation.
        // ------------------------------------------------------------------

        [Test]
        public void Parse_ReadsCanonicalStrings()
        {
            Assert.AreEqual(0m, NeoDecimalValues.Parse("0"));
            Assert.AreEqual(-1.5m, NeoDecimalValues.Parse("-1.5"));
            Assert.AreEqual(123.456m, NeoDecimalValues.Parse("123.456"));
        }

        [Test]
        public void Format_PreservesTrailingFractionalZeros()
        {
            Assert.AreEqual("1.10", NeoDecimalValues.Format(1.10m));
            Assert.AreEqual("1.10", NeoDecimalValues.Format(NeoDecimalValues.Parse("1.10")));
        }

        [Test]
        public void ParseFormat_FullScaleRoundTrip()
        {
            const string value = "1.0000000000000000001";
            Assert.AreEqual(value, NeoDecimalValues.Format(NeoDecimalValues.Parse(value)));
        }

        [Test]
        public void Format_NoExponentOrLeadingZeros()
        {
            Assert.AreEqual("0.5", NeoDecimalValues.Format(0.5m));
            Assert.AreEqual("-0.5", NeoDecimalValues.Format(-0.5m));
            Assert.AreEqual("123", NeoDecimalValues.Format(123m));
        }

        // ------------------------------------------------------------------
        // Negative-zero normalization (decision 1 — "-0" never produced).
        // ------------------------------------------------------------------

        [Test]
        public void Format_NegativeZeroNormalizesToPlainZero()
        {
            Assert.AreEqual("0", NeoDecimalValues.Format(decimal.Negate(0m)));
            Assert.AreEqual("0.00", NeoDecimalValues.Format(decimal.Negate(0.00m)));
        }

        [Test]
        public void Parse_AcceptsNegativeZeroInputButFormatsPositive()
        {
            Assert.AreEqual(0m, NeoDecimalValues.Parse("-0"));
            Assert.AreEqual("0", NeoDecimalValues.Format(NeoDecimalValues.Parse("-0")));
            Assert.AreEqual("0.00", NeoDecimalValues.Format(NeoDecimalValues.Parse("-0.00")));
        }

        // ------------------------------------------------------------------
        // OrNull helpers.
        // ------------------------------------------------------------------

        [Test]
        public void OrNull_PassNullThrough()
        {
            Assert.IsNull(NeoDecimalValues.ParseOrNull(null));
            Assert.IsNull(NeoDecimalValues.FormatOrNull(null));
            Assert.AreEqual(1.5m, NeoDecimalValues.ParseOrNull("1.5"));
            Assert.AreEqual("1.5", NeoDecimalValues.FormatOrNull(1.5m));
        }

        // ------------------------------------------------------------------
        // Distinct single-condition failures (decision 2).
        // ------------------------------------------------------------------

        [Test]
        public void Parse_NullThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() => NeoDecimalValues.Parse(null!));
        }

        [Test]
        public void Parse_WhitespaceIsNonCanonical()
        {
            var error = Assert.Throws<FormatException>(() => NeoDecimalValues.Parse(" 1"));
            StringAssert.Contains("canonical decimal string", error!.Message);
        }

        [Test]
        public void Parse_ExponentIsNonCanonical()
        {
            var error = Assert.Throws<FormatException>(() => NeoDecimalValues.Parse("1e5"));
            StringAssert.Contains("canonical decimal string", error!.Message);
        }

        [Test]
        public void Parse_LeadingZerosAreNonCanonical()
        {
            Assert.Throws<FormatException>(() => NeoDecimalValues.Parse("01"));
        }

        [Test]
        public void Parse_TooManySignificantDigitsThrowsWithDistinctMessage()
        {
            var error = Assert.Throws<OverflowException>(
                () => NeoDecimalValues.Parse(new string('9', 29)));
            StringAssert.Contains("significant digits", error!.Message);
        }

        [Test]
        public void Parse_ScaleTooLargeThrowsWithDistinctMessage()
        {
            // 1 significant digit but 29 fractional digits — isolates the scale
            // failure from the significant-digit failure.
            string scale29 = "0." + new string('0', 28) + "1";
            var error = Assert.Throws<OverflowException>(() => NeoDecimalValues.Parse(scale29));
            StringAssert.Contains("fractional digits", error!.Message);
        }

        [Test]
        public void Parse_AcceptsBoundaryValues()
        {
            Assert.DoesNotThrow(() => NeoDecimalValues.Parse(new string('9', 28)));
            string scale28 = "0." + new string('0', 27) + "1";
            Assert.DoesNotThrow(() => NeoDecimalValues.Parse(scale28));
        }
    }
}
