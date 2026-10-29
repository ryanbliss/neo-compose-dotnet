// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NeoCompose.Runtime;
using NeoCompose.Runtime.NeoScript;
using NeoCompose.Runtime.NeoScript.Globalization;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P127 §11 and §14 "Culture types": runs the .NET 10 globalization
    /// fixture the compose runtime shares, and pins the three Unity-only
    /// differences Unity's Mono culture objects keep.
    /// </summary>
    public class P127GlobalizationParityTests
    {
        private const string FixturePath = "Packages/com.ryanbliss.neocompose/Tests/globalization-parity.json";
        private const string TablesPath = "Packages/com.ryanbliss.neocompose/Tests/globalization-unicode-tables.json";

        private static JObject? fixture;
        private readonly Dictionary<string, NumberFormatInfo> infos = new();

        private static JObject Fixture => fixture ??= JObject.Parse(KeepSurrogateEscapes(File.ReadAllText(FixturePath)));

        [Test]
        public void FormatsEveryValueFormatStringAndNumberFormatInfo()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("format"))
            {
                // Mono's setter refuses CurrencyNegativePattern 16 (§14), so Unity can't hold that info.
                if ((string?)c["i"] == "cneg16")
                    continue;
                string type = (string)c["t"]!;
                Check(failures, c, Run(() => FormatValue(type, c["v"]!, (string?)c["f"], Info((string?)c["i"]))), Expected((string?)c["r"], c, "e", "m"));
            }
            Report(failures);
        }

        [Test]
        public void ParsesEveryInputStyleAndNumberFormatInfo()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("parse"))
            {
                string type = (string)c["t"]!;
                string? s = (string?)c["s"];
                int? style = (int?)c["st"];
                NumberFormatInfo? info = Info((string?)c["i"]);
                Check(failures, c, Run(() => ParseValue(type, s, style, info)), Expected(Normalize(type, (string?)c["p"]), c, "pe", "pm"), "Parse");
                Check(failures, c, Run(() => TryParseValue(type, s, style, info)), Expected(Normalize(type, (string?)c["tp"]), c, "tpe", "tpm"), "TryParse");
            }
            Report(failures);
        }

        [Test]
        public void ParsesAndPrintsBool()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("bool"))
            {
                if (c["toString"] is not null)
                {
                    Assert.That(NeoStrings.FormatBoolean(true), Is.EqualTo((string)c["toString"]!));
                    Assert.That(NeoStrings.FormatBoolean(false), Is.EqualTo((string)c["toStringFalse"]!));
                    continue;
                }
                string? s = (string?)c["s"];
                Check(failures, c, Run(() => NeoStrings.ParseBoolean(s) ? "True" : "False"), Expected((string?)c["p"], c, "pe", "pm"));
                string? tried = NeoStrings.TryParseBoolean(s, out bool result) ? (result ? "True" : "False") : null;
                Check(failures, c, Value(tried), Value((string?)c["tp"]), "TryParse");
            }
            Report(failures);
        }

        [Test]
        public void ParsesStringThroughIParsable()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("string"))
            {
                string? s = (string?)c["s"];
                Check(failures, c, Run(() => NeoStrings.ParseString(s)), Expected((string?)c["p"], c, "pe", "pm"));
                bool ok = NeoStrings.TryParseString(s, out string? result);
                Check(failures, c, Value($"{result ?? "<null>"}|{ok}"), Value($"{(string?)c["tp"] ?? "<null>"}|{(bool)c["tpok"]!}"), "TryParse");
            }
            Report(failures);
        }

        [Test]
        public void ResolvesAnAuthoredProviderExactlyWhenDotNetDoes()
        {
            var failures = new List<string>();
            NumberFormatInfo custom = Info("custom")!;
            foreach (JToken c in Cases("providerCalls"))
            {
                string ret = (string)c["ret"]!;
                var provider = new CountingProvider(ret == "custom" ? custom : ret == "null" ? null : "not a number format");
                string op = (string)c["op"]!;
                string type = (string)c["t"]!;
                string outcome = Run(() => op switch
                {
                    "format" => FormatValue(type, c["v"] ?? "", (string?)c["f"], provider),
                    "parse" => ParseValue(type, (string?)c["s"], (int?)c["st"], provider),
                    _ => TryParseValue(type, (string?)c["s"], (int?)c["st"], provider),
                });
                string? result = op == "format" ? (string?)c["r"] : Normalize(type, (string?)c["r"]);
                Check(failures, c, outcome + $" calls={provider.Calls}", Expected(result, c, "e", "m") + $" calls={(int)c["calls"]!}");
            }
            Report(failures);
        }

        [Test]
        public void ChecksEverySetterInDotNetOrder()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("setters"))
            {
                string target = (string)c["target"]!;
                string prop = (string)c["prop"]!;
                if (target == "writable" && prop == nameof(NumberFormatInfo.CurrencyNegativePattern) && (int)c["value"]! == 16)
                    continue; // A Mono pin, asserted in CurrencyNegativePatternSixteenThrowsMonosError.
                string outcome = Run(() =>
                {
                    if (target == "static")
                        return NeoNumberFormatInfos.ReadOnly(null).ToString();
                    NumberFormatInfo info = target == "writable" ? new NumberFormatInfo() : NeoNumberFormatInfos.InvariantInfo;
                    NeoNumberFormatInfos.Set(info, prop, ToValue(c["value"]!));
                    return "True";
                });
                Check(failures, c, outcome, Expected("True", c, "e", "m"));
            }
            Report(failures);
        }

        [Test]
        public void MatchesCultureInfoIdentityCreationAndAliasing()
        {
            var failures = new List<string>();
            CultureInfo invariant = NeoCultures.InvariantCulture;
            CultureInfo created = NeoCultures.New("");
            CultureInfo cached = NeoCultures.GetCultureInfo("");
            var instances = new Dictionary<string, CultureInfo>
            {
                ["InvariantCulture"] = invariant,
                ["new"] = created,
                ["GetCultureInfo"] = cached,
                ["Clone"] = NeoCultures.Clone(invariant),
                ["ReadOnly"] = NeoCultures.ReadOnly(created),
                ["CurrentCulture"] = NeoCultures.CurrentCulture,
                ["CurrentUICulture"] = NeoCultures.CurrentUICulture,
            };
            foreach (JToken token in Cases("cultures"))
            {
                var c = (JObject)token;
                string kind = (string)c["culture"]!;
                if (instances.TryGetValue(kind, out CultureInfo? culture))
                {
                    var actual = new JObject
                    {
                        ["culture"] = kind,
                        ["Name"] = culture.Name,
                        ["DisplayName"] = culture.DisplayName,
                        ["EnglishName"] = culture.EnglishName,
                        ["NativeName"] = culture.NativeName,
                        ["TwoLetterISOLanguageName"] = culture.TwoLetterISOLanguageName,
                        ["ThreeLetterISOLanguageName"] = culture.ThreeLetterISOLanguageName,
                        ["ThreeLetterWindowsLanguageName"] = culture.ThreeLetterWindowsLanguageName,
                        ["LCID"] = culture.LCID,
                        ["IsNeutralCulture"] = culture.IsNeutralCulture,
                        ["IsReadOnly"] = culture.IsReadOnly,
                        ["NumberFormatIsReadOnly"] = culture.NumberFormat.IsReadOnly,
                        ["ToString"] = culture.ToString(),
                        ["ParentIsInvariant"] = ReferenceEquals(NeoCultures.Parent(culture), invariant),
                        ["EqualsInvariant"] = culture.Equals(invariant),
                        ["IsInvariantInstance"] = ReferenceEquals(culture, invariant),
                        ["NumberFormatIsInvariantInfo"] = ReferenceEquals(culture.NumberFormat, NeoNumberFormatInfos.InvariantInfo),
                    };
                    var wanted = (JObject)c.DeepClone();
                    // Unity's Mono reads "IVL" on every invariant culture (§14), asserted in MonoPinsEachUnityOnlyDifference.
                    wanted["ThreeLetterISOLanguageName"] = MonoThreeLetterIsoLanguageName;
                    Check(failures, kind, actual.ToString(), wanted.ToString());
                    continue;
                }
                switch (kind)
                {
                    case "identity":
                        {
                            var actual = new JObject
                            {
                                ["culture"] = kind,
                                ["GetCultureInfoCached"] = ReferenceEquals(NeoCultures.GetCultureInfo(""), cached),
                                ["CurrentInfoIsInvariantInfo"] = ReferenceEquals(NeoNumberFormatInfos.CurrentInfo, NeoNumberFormatInfos.InvariantInfo),
                                ["CurrentCultureIsInvariant"] = ReferenceEquals(NeoCultures.CurrentCulture, invariant),
                                ["NewEqualsNew"] = NeoCultures.New("").Equals(NeoCultures.New("")),
                                ["GetFormatNfi"] = ReferenceEquals(invariant.GetFormat(typeof(NumberFormatInfo)), invariant.NumberFormat),
                                ["GetFormatOther"] = invariant.GetFormat(typeof(string)) is null,
                                ["GetFormatNull"] = invariant.GetFormat(null!) is null,
                                ["NfiGetFormatSelf"] = ReferenceEquals(created.NumberFormat.GetFormat(typeof(NumberFormatInfo)), created.NumberFormat),
                                ["NfiGetFormatOther"] = created.NumberFormat.GetFormat(typeof(string)) is null,
                                ["NewNumberFormatIsWritable"] = !created.NumberFormat.IsReadOnly,
                                ["CloneNumberFormatIsWritable"] = !NeoCultures.Clone(invariant).NumberFormat.IsReadOnly,
                                ["ReadOnlyOfReadOnlyIsSame"] = ReferenceEquals(NeoCultures.ReadOnly(invariant), invariant),
                            };
                            Check(failures, kind, actual.ToString(), c.ToString());
                            break;
                        }
                    case "create":
                        {
                            string? name = (string?)c["name"];
                            string method = (string)c["method"]!;
                            string outcome = Run(() =>
                            {
                                CultureInfo result = method switch
                                {
                                    "new" => NeoCultures.New(name),
                                    "ReadOnly" => NeoCultures.ReadOnly(null),
                                    _ => NeoCultures.GetCultureInfo(name),
                                };
                                return result is null ? "null" : "True";
                            });
                            Check(failures, c, outcome, Expected("True", c, "e", "m"));
                            break;
                        }
                    case "setNumberFormat":
                        {
                            string target = (string)c["target"]!;
                            CultureInfo targetCulture = target == "InvariantCulture"
                                ? NeoCultures.InvariantCulture
                                : target == "new" ? NeoCultures.New("") : NeoCultures.GetCultureInfo("");
                            string outcome = Run(() =>
                            {
                                NeoCultures.SetNumberFormat(targetCulture, (string)c["value"]! == "null" ? null : new NumberFormatInfo());
                                return "True";
                            });
                            Check(failures, c, outcome, Expected("True", c, "e", "m"));
                            break;
                        }
                    case "aliasing":
                        {
                            var info = new NumberFormatInfo();
                            var digits = new object?[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
                            NeoNumberFormatInfos.Set(info, nameof(NumberFormatInfo.NativeDigits), digits);
                            NumberFormatInfo clone = NeoNumberFormatInfos.Clone(info);
                            digits[0] = "x";
                            NeoNumberFormatInfos.GetNativeDigits(info)[1] = "y";
                            var sizes = new object?[] { 3 };
                            NeoNumberFormatInfos.Set(info, nameof(NumberFormatInfo.NumberGroupSizes), sizes);
                            sizes[0] = 5;
                            var minus = new NumberFormatInfo();
                            NeoNumberFormatInfos.Set(minus, nameof(NumberFormatInfo.NegativeSign), "−");
                            var actual = new JObject
                            {
                                ["culture"] = kind,
                                ["nfiDigit0"] = NeoNumberFormatInfos.GetNativeDigits(info)[0],
                                ["cloneDigit0"] = NeoNumberFormatInfos.GetNativeDigits(clone)[0],
                                ["nfiDigit1"] = NeoNumberFormatInfos.GetNativeDigits(info)[1],
                                ["groupSize0"] = info.NumberGroupSizes[0],
                                ["cloneIsWritable"] = !NeoNumberFormatInfos.Clone(NeoNumberFormatInfos.InvariantInfo).IsReadOnly,
                                ["readOnlyCopyIsReadOnly"] = NeoNumberFormatInfos.ReadOnly(info).IsReadOnly,
                                ["readOnlyCopyIsSame"] = ReferenceEquals(NeoNumberFormatInfos.ReadOnly(info), info),
                                ["readOnlyOfReadOnlyIsSame"] = ReferenceEquals(NeoNumberFormatInfos.ReadOnly(NeoNumberFormatInfos.InvariantInfo), NeoNumberFormatInfos.InvariantInfo),
                                ["getInstanceNull"] = ReferenceEquals(NeoNumberFormatInfos.GetInstance(null), NeoNumberFormatInfos.CurrentInfo),
                                ["getInstanceCulture"] = ReferenceEquals(NeoNumberFormatInfos.GetInstance(NeoCultures.InvariantCulture), NeoNumberFormatInfos.InvariantInfo),
                                ["getInstanceNfi"] = ReferenceEquals(NeoNumberFormatInfos.GetInstance(info), info),
                                ["minusParsesHyphen"] = NeoNumberParsing.ParseInt32("-5", null, minus),
                            };
                            Check(failures, kind, actual.ToString(), c.ToString());
                            break;
                        }
                    default:
                        failures.Add($"Unknown culture record {kind}");
                        break;
                }
            }
            Report(failures);
        }

        [Test]
        public void ComparesStringsOrdinallyWithDotNetsExactIntegers()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("compare"))
            {
                string? a = (string?)c["a"];
                string? b = (string?)c["b"];
                string actual = $"{NeoStrings.Compare(a, b)}|{NeoStrings.CompareOrdinal(a, b)}|{string.Equals(a, b, StringComparison.Ordinal)}|{(a is null ? "-" : NeoStrings.Compare(a, b).ToString(CultureInfo.InvariantCulture))}";
                string wanted = $"{(int)c["compare"]!}|{(int)c["compareOrdinal"]!}|{(bool)c["equals"]!}|{(c["compareTo"] is null ? "-" : ((int)c["compareTo"]!).ToString(CultureInfo.InvariantCulture))}";
                Check(failures, c, actual, wanted);
            }
            Report(failures);
        }

        [Test]
        public void CasesStringsWithDotNetsSimpleCaseMapping()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("casing"))
            {
                string s = (string)c["s"]!;
                Check(failures, c, NeoUnicode.ToUpperInvariant(s), (string)c["upper"]!, "upper");
                Check(failures, c, NeoUnicode.ToLowerInvariant(s), (string)c["lower"]!, "lower");
                Check(failures, c, NeoUnicode.ToUpperInvariant(s), (string)c["upperCulture"]!, "upperCulture");
                Check(failures, c, NeoUnicode.ToLowerInvariant(s), (string)c["lowerCulture"]!, "lowerCulture");
            }
            Report(failures);
        }

        [Test]
        public void PadsInterpolationHolesByAlignment()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("alignment"))
            {
                string v = (string)c["v"]!;
                string text = (string)c["t"]! == "float" ? NeoNumberFormatting.FormatSingle(FloatFromBits(v), (string?)c["f"], null) : v;
                Check(failures, c, NeoStrings.Align(text, (int)c["align"]!), (string)c["r"]!);
            }
            Report(failures);
        }

        [Test]
        public void UnicodeTablesMatchTheSharedDotNetTables()
        {
            JObject tables = JObject.Parse(File.ReadAllText(TablesPath));
            Assert.That(NeoUnicodeTables.UpperCaseRuns, Is.EqualTo(Flatten(tables["upper"]!)));
            Assert.That(NeoUnicodeTables.LowerCaseRuns, Is.EqualTo(Flatten(tables["lower"]!)));
            Assert.That(NeoUnicodeTables.DecimalDigitRuns, Is.EqualTo(Flatten(tables["decimalDigits"]!)));
            Assert.That(NeoUnicodeTables.PrivateUseRanges, Is.EqualTo(Flatten(tables["privateUse"]!)));
            Assert.That((string?)tables["dotnet"], Is.EqualTo((string?)Fixture["dotnet"]));
        }

        private const string MonoThreeLetterIsoLanguageName = "IVL";
        private const int MonoInvariantHashCode = 127;
        private const string MonoCurrencyNegativePatternMessage = "Valid values are between 0 and 15, inclusive.\nParameter name: CurrencyNegativePattern";

        /// <summary>The four invariant culture instances §14 names.</summary>
        private static IEnumerable<(string, CultureInfo)> InvariantInstances()
        {
            yield return ("InvariantCulture", NeoCultures.InvariantCulture);
            yield return ("new", NeoCultures.New(""));
            yield return ("GetCultureInfo", NeoCultures.GetCultureInfo(""));
            yield return ("Clone of InvariantCulture", NeoCultures.Clone(NeoCultures.InvariantCulture));
            yield return ("Clone of new", NeoCultures.Clone(NeoCultures.New("")));
            yield return ("Clone of GetCultureInfo", NeoCultures.Clone(NeoCultures.GetCultureInfo("")));
        }

        [Test]
        public void MonoPinsEachUnityOnlyDifference()
        {
            foreach ((string label, CultureInfo culture) in InvariantInstances())
            {
                Assert.That(culture.ThreeLetterISOLanguageName, Is.EqualTo(MonoThreeLetterIsoLanguageName), label);
                Assert.That(culture.GetHashCode(), Is.EqualTo(MonoInvariantHashCode), label);
            }
        }

        /// <summary>
        /// §14 "One object": the <c>Parent</c> of every invariant culture is
        /// the <c>InvariantCulture</c> singleton, as on .NET 10, though Mono
        /// gives <c>new CultureInfo("")</c>, <c>GetCultureInfo("")</c> and
        /// <c>ReadOnly</c> copies a distinct invariant parent.
        /// </summary>
        [Test]
        public void ParentOfEveryInvariantCultureIsTheInvariantSingleton()
        {
            CultureInfo invariant = NeoCultures.InvariantCulture;
            foreach ((string label, CultureInfo culture) in InvariantInstances())
                Assert.That(NeoCultures.Parent(culture), Is.SameAs(invariant), label);
            Assert.That(NeoCultures.Parent(NeoCultures.ReadOnly(NeoCultures.New(""))), Is.SameAs(invariant));
            Assert.That(NeoCultures.Parent(NeoCultures.Clone(NeoCultures.GetCultureInfo(""))), Is.SameAs(invariant));
        }

        [Test]
        public void CurrencyNegativePatternSixteenThrowsMonosError()
        {
            foreach ((string label, CultureInfo culture) in InvariantInstances())
            {
                if (culture.NumberFormat.IsReadOnly)
                    continue;
                var error = Assert.Throws<NeoRuntimeError>(() =>
                    NeoNumberFormatInfos.Set(culture.NumberFormat, nameof(NumberFormatInfo.CurrencyNegativePattern), 16));
                Assert.That(error!.ErrorClass, Is.EqualTo(NeoErrorClass.NeoArgumentOutOfRangeError), label);
                Assert.That(error.Message, Is.EqualTo(MonoCurrencyNegativePatternMessage), label);
                Assert.That(culture.NumberFormat.CurrencyNegativePattern, Is.EqualTo(0), label);
            }
            var plain = Assert.Throws<NeoRuntimeError>(() =>
                NeoNumberFormatInfos.Set(new NumberFormatInfo(), nameof(NumberFormatInfo.CurrencyNegativePattern), 16));
            Assert.That(plain!.Message, Is.EqualTo(MonoCurrencyNegativePatternMessage));
            // NeoScript's own range check still runs first, with .NET 10's message.
            var range = Assert.Throws<NeoRuntimeError>(() =>
                NeoNumberFormatInfos.Set(new NumberFormatInfo(), nameof(NumberFormatInfo.CurrencyNegativePattern), 17));
            Assert.That(range!.Message, Is.EqualTo("Valid values are between 0 and 16, inclusive. (Parameter 'value')\nActual value was 17."));
        }

        [Test]
        public void EveryOtherCulturePropertyMatchesDotNet10()
        {
            foreach ((string label, CultureInfo culture) in InvariantInstances())
            {
                Assert.That(culture.Name, Is.EqualTo(""), label);
                Assert.That(culture.DisplayName, Is.EqualTo("Invariant Language (Invariant Country)"), label);
                Assert.That(culture.EnglishName, Is.EqualTo("Invariant Language (Invariant Country)"), label);
                Assert.That(culture.NativeName, Is.EqualTo("Invariant Language (Invariant Country)"), label);
                Assert.That(culture.TwoLetterISOLanguageName, Is.EqualTo("iv"), label);
                Assert.That(culture.ThreeLetterWindowsLanguageName, Is.EqualTo("IVL"), label);
                Assert.That(culture.LCID, Is.EqualTo(127), label);
                Assert.That(culture.IsNeutralCulture, Is.False, label);
                Assert.That(culture.ToString(), Is.EqualTo(""), label);
                Assert.That(NeoCultures.Parent(culture), Is.SameAs(NeoCultures.InvariantCulture), label);
                Assert.That(culture.Equals(NeoCultures.InvariantCulture), Is.True, label);
                AssertDotNet10Defaults(culture.NumberFormat, label);
            }
            AssertDotNet10Defaults(NeoNumberFormatInfos.InvariantInfo, "InvariantInfo");
            AssertDotNet10Defaults(new NumberFormatInfo(), "new NumberFormatInfo()");
            Assert.That(NeoNumberFormatInfos.InvariantInfo.IsReadOnly, Is.True);
        }

        [Test]
        public void NeoScriptChecksRunBeforeMonos()
        {
            var nullFormat = Assert.Throws<NeoRuntimeError>(() => NeoCultures.SetNumberFormat(NeoCultures.InvariantCulture, null));
            Assert.That(nullFormat!.ErrorClass, Is.EqualTo(NeoErrorClass.NeoArgumentNullError));
            Assert.That(nullFormat.Message, Is.EqualTo("Value cannot be null. (Parameter 'value')"));

            const string NotFound = "Only the invariant culture is supported in globalization-invariant mode. See https://aka.ms/GlobalizationInvariantMode for more information. (Parameter 'name')\n";
            var created = Assert.Throws<NeoRuntimeError>(() => NeoCultures.New("fr-FR"));
            Assert.That(created!.ErrorClass, Is.EqualTo(NeoErrorClass.NeoCultureNotFoundError));
            Assert.That(created.Message, Is.EqualTo(NotFound + "fr-FR is an invalid culture identifier."));
            var fetched = Assert.Throws<NeoRuntimeError>(() => NeoCultures.GetCultureInfo("fr-FR"));
            Assert.That(fetched!.ErrorClass, Is.EqualTo(NeoErrorClass.NeoCultureNotFoundError));
            Assert.That(fetched.Message, Is.EqualTo(NotFound + "fr-fr is an invalid culture identifier."));
        }

        [Test]
        public void IdentityRelationsMatchDotNet10()
        {
            Assert.That(NeoNumberFormatInfos.CurrentInfo, Is.SameAs(NeoNumberFormatInfos.InvariantInfo));
            Assert.That(NeoCultures.InvariantCulture.NumberFormat, Is.SameAs(NeoNumberFormatInfos.InvariantInfo));
            Assert.That(NeoCultures.CurrentCulture, Is.SameAs(NeoCultures.InvariantCulture));
            Assert.That(NeoCultures.CurrentUICulture, Is.SameAs(NeoCultures.InvariantCulture));
            Assert.That(NeoCultures.GetCultureInfo("").NumberFormat, Is.Not.SameAs(NeoNumberFormatInfos.InvariantInfo));
            Assert.That(NeoNumberFormatInfos.GetInstance(NeoCultures.GetCultureInfo("")), Is.Not.SameAs(NeoNumberFormatInfos.InvariantInfo));
        }

        private static void AssertDotNet10Defaults(NumberFormatInfo info, string label)
        {
            int[] three = { 3 };
            Assert.That(info.CurrencyDecimalDigits, Is.EqualTo(2), label);
            Assert.That(info.CurrencyDecimalSeparator, Is.EqualTo("."), label);
            Assert.That(info.CurrencyGroupSeparator, Is.EqualTo(","), label);
            Assert.That(info.CurrencyGroupSizes, Is.EqualTo(three), label);
            Assert.That(info.CurrencyNegativePattern, Is.EqualTo(0), label);
            Assert.That(info.CurrencyPositivePattern, Is.EqualTo(0), label);
            Assert.That(info.CurrencySymbol, Is.EqualTo("¤"), label);
            Assert.That(info.DigitSubstitution, Is.EqualTo(DigitShapes.None), label);
            Assert.That(info.NaNSymbol, Is.EqualTo("NaN"), label);
            Assert.That(info.NativeDigits, Is.EqualTo(new[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" }), label);
            Assert.That(info.NegativeInfinitySymbol, Is.EqualTo("-Infinity"), label);
            Assert.That(info.NegativeSign, Is.EqualTo("-"), label);
            Assert.That(info.NumberDecimalDigits, Is.EqualTo(2), label);
            Assert.That(info.NumberDecimalSeparator, Is.EqualTo("."), label);
            Assert.That(info.NumberGroupSeparator, Is.EqualTo(","), label);
            Assert.That(info.NumberGroupSizes, Is.EqualTo(three), label);
            Assert.That(info.NumberNegativePattern, Is.EqualTo(1), label);
            Assert.That(info.PercentDecimalDigits, Is.EqualTo(2), label);
            Assert.That(info.PercentDecimalSeparator, Is.EqualTo("."), label);
            Assert.That(info.PercentGroupSeparator, Is.EqualTo(","), label);
            Assert.That(info.PercentGroupSizes, Is.EqualTo(three), label);
            Assert.That(info.PercentNegativePattern, Is.EqualTo(0), label);
            Assert.That(info.PercentPositivePattern, Is.EqualTo(0), label);
            Assert.That(info.PercentSymbol, Is.EqualTo("%"), label);
            Assert.That(info.PerMilleSymbol, Is.EqualTo("‰"), label);
            Assert.That(info.PositiveInfinitySymbol, Is.EqualTo("Infinity"), label);
            Assert.That(info.PositiveSign, Is.EqualTo("+"), label);
        }

        /// <summary>
        /// Writes each <c>\uD800</c>-<c>\uDFFF</c> escape as its raw UTF-16
        /// unit, because Newtonsoft replaces a lone surrogate escape with
        /// U+FFFD and the fixture holds lone surrogates on purpose.
        /// </summary>
        private static string KeepSurrogateEscapes(string json) =>
            Regex.Replace(json, @"(?<!\\)\\u([dD][89a-fA-F][0-9a-fA-F]{2})", match =>
                ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString());

        private static JArray Cases(string section) => (JArray)Fixture[section]!;

        private NumberFormatInfo? Info(string? name)
        {
            if (name is null)
                return null;
            if (infos.TryGetValue(name, out NumberFormatInfo? cached))
                return cached;
            var info = new NumberFormatInfo();
            foreach (JProperty property in ((JObject)Fixture["infos"]![name]!).Properties())
                NeoNumberFormatInfos.Set(info, property.Name, ToValue(property.Value));
            infos[name] = info;
            return info;
        }

        /// <summary>A fixture value as the evaluator passes it: lists are <c>object?[]</c>.</summary>
        private static object? ToValue(JToken token) => token.Type switch
        {
            JTokenType.Null => null,
            JTokenType.Integer => (int)token,
            JTokenType.String => (string)token!,
            JTokenType.Array => token.Select(ToValue).ToArray(),
            _ => throw new ArgumentException($"Unexpected fixture value {token}."),
        };

        private static float FloatFromBits(string text) =>
            BitConverter.Int32BitsToSingle(unchecked((int)Convert.ToUInt32(text.Substring(2), 16)));

        // NeoScript has one NaN (§8.1), so every NaN compares as NaN.
        private static string FloatText(float value) =>
            float.IsNaN(value) ? "NaN" : "0x" + BitConverter.SingleToInt32Bits(value).ToString("X8", CultureInfo.InvariantCulture);

        private static string? Normalize(string type, string? text) =>
            type == "float" && text is not null && text.StartsWith("0x", StringComparison.Ordinal) && float.IsNaN(FloatFromBits(text)) ? "NaN" : text;

        private static string FormatValue(string type, JToken value, string? format, object? provider) => type switch
        {
            "int" => NeoNumberFormatting.FormatInt32((int)value, format, provider),
            "float" => NeoNumberFormatting.FormatSingle(FloatFromBits((string)value!), format, provider),
            _ => NeoNumberFormatting.FormatDecimal(NeoDecimalValues.Parse((string)value!), format, provider),
        };

        private static string ParseValue(string type, string? s, int? style, object? provider) => type switch
        {
            "int" => NeoNumberFormatting.Int32Text(NeoNumberParsing.ParseInt32(s, style, provider)),
            "float" => FloatText(NeoNumberParsing.ParseSingle(s, style, provider)),
            _ => NeoDecimalValues.Format(NeoNumberParsing.ParseDecimal(s, style, provider)),
        };

        private static string? TryParseValue(string type, string? s, int? style, object? provider)
        {
            switch (type)
            {
                case "int":
                    return NeoNumberParsing.TryParseInt32(s, style, provider, out int i) ? NeoNumberFormatting.Int32Text(i) : null;
                case "float":
                    return NeoNumberParsing.TryParseSingle(s, style, provider, out float f) ? FloatText(f) : null;
                default:
                    return NeoNumberParsing.TryParseDecimal(s, style, provider, out decimal d) ? NeoDecimalValues.Format(d) : null;
            }
        }

        private static string Value(string? value) => "value:" + (value ?? "<null>");

        private static string Run(Func<string?> action)
        {
            try
            {
                return Value(action());
            }
            catch (NeoRuntimeError error)
            {
                return $"error:{error.ErrorClass}:{error.Message}";
            }
        }

        private static string Expected(string? value, JToken c, string errorKey, string messageKey)
        {
            string? errorClass = (string?)c[errorKey];
            return errorClass is null ? Value(value) : $"error:{errorClass}:{(string?)c[messageKey] ?? ""}";
        }

        /// <summary>Collects mismatches so one failing section reports every broken case.</summary>
        private static void Check(List<string> failures, object label, string actual, string wanted, string? op = null)
        {
            if (actual == wanted)
                return;
            string name = label is JToken token ? token.ToString(Newtonsoft.Json.Formatting.None) : label.ToString()!;
            failures.Add($"{(op is null ? "" : op + " ")}{name}\n  got  {actual}\n  want {wanted}");
        }

        private static void Report(List<string> failures)
        {
            Assert.That(failures, Is.Empty, $"{failures.Count} mismatches:\n" + string.Join("\n", failures.Take(40)));
        }

        private static int[] Flatten(JToken runs) => runs.SelectMany(run => run.Select(value => (int)value)).ToArray();

        /// <summary>An authored provider that counts its <c>GetFormat(typeof(NumberFormatInfo))</c> calls.</summary>
        private sealed class CountingProvider : INeoAuthoredFormatProvider
        {
            private readonly object? answer;

            internal CountingProvider(object? answer)
            {
                this.answer = answer;
            }

            internal int Calls
            {
                get;
                private set;
            }

            public object? GetNumberFormat()
            {
                Calls++;
                return answer;
            }
        }
    }
}
