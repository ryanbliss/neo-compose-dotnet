// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
// Typed cases transcribed from the shared fixture and Compose gen-emul specs.
// Each operation calls generated classes, never the aggregate Probe functions.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NeoCompose.Runtime;
using NUnit.Framework;
using G = NeoCompose.Tests.P127InterfacesParity;

namespace NeoCompose.Tests
{
    public partial class P127InterfacesParityTests
    {
        private const string MonoDispatchIssue = "https://github.com/ryanbliss/neo-compose/issues/2488";

        public static IEnumerable<TestCaseData> HistoricalGeneratedCases()
        {
            yield return new TestCaseData("R19MergeChain", 0, "9", null,
                (Func<NeoClient, object?>)(client => { G.IR19J<G.R19Tag, int> x = CreateParityValue(client, G.R19Q<int, int>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R19MergeChain0");
            yield return new TestCaseData("R19MergeChain", 1, "2", null,
                (Func<NeoClient, object?>)(client => { G.IR19J<G.R19Tag, string> x = CreateParityValue(client, G.R19M<string, int>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R19MergeChain1");
            yield return new TestCaseData("R19MergeChain", 2, "2", null,
                (Func<NeoClient, object?>)(client => { G.IR19J<G.R19Tag, int> x = CreateParityValue(client, G.R19Eq<int, int>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R19MergeChain2");
            yield return new TestCaseData("R19MergeChain", 3, "9", null,
                (Func<NeoClient, object?>)(client => { G.IR19J<G.R19Tag, int> x = CreateParityValue(client, G.R19EqBelow<int, int>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R19MergeChain3");
            yield return new TestCaseData("R19MergeChain", 4, "9", null,
                (Func<NeoClient, object?>)(client => { G.IR19J<G.R19Tag, int> x = CreateParityValue(client, G.R19Closed.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R19MergeChain4");
            yield return new TestCaseData("R19MergeChain", 5, "2", null,
                (Func<NeoClient, object?>)(client => { G.IR19J<G.R19Tag, int> x = CreateParityValue(client, G.R19ClosedTop.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R19MergeChain5");
            yield return new TestCaseData("R19MergeChain", 6, "5", null,
                (Func<NeoClient, object?>)(client => { G.IR19J<G.R19Tag, int> x = CreateParityValue(client, G.R19NQ<int, int>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R19MergeChain6");
            yield return new TestCaseData("R19MergeChain", 7, "5", null,
                (Func<NeoClient, object?>)(client => { G.IR19J<G.R19Tag, int> x = CreateParityValue(client, G.R19VR<int, int>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R19MergeChain7");
            yield return new TestCaseData("R19MergeChain", 8, "7", null,
                (Func<NeoClient, object?>)(client => { G.IR19J<G.R19Tag, int> x = CreateParityValue(client, G.R19VO<int, int>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R19MergeChain8");
            yield return new TestCaseData("R19MergeChain", 9, "9", null,
                (Func<NeoClient, object?>)(client => { G.IR19F<G.R19Tag, int> x = CreateParityValue(client, G.R19Q<int, int>.CreateWritable); return x.F(); }))
                .SetName("GeneratedBridge_R19MergeChain9");
            yield return new TestCaseData("R19MergeChain", 10, "9", null,
                (Func<NeoClient, object?>)(client => { G.IR19F<G.R19Tag, int> x = CreateParityValue(client, G.R19EqBelow<int, int>.CreateWritable); return x.F(); }))
                .SetName("GeneratedBridge_R19MergeChain10");
            yield return new TestCaseData("R19MergeChain", 11, "9", null,
                (Func<NeoClient, object?>)(client => { G.IR19F<G.R19Tag, int> x = CreateParityValue(client, G.R19Closed.CreateWritable); return x.F(); }))
                .SetName("GeneratedBridge_R19MergeChain11");
            yield return new TestCaseData("R19MergeChain", 12, "2", null,
                (Func<NeoClient, object?>)(client => { G.IR19F<G.R19Tag, int> x = CreateParityValue(client, G.R19ClosedTop.CreateWritable); return x.F(); }))
                .SetName("GeneratedBridge_R19MergeChain12");
            yield return new TestCaseData("R19MergeChain", 13, "2", null,
                (Func<NeoClient, object?>)(client => { G.IR19F<G.R19Tag, int> x = CreateParityValue(client, G.R19Eq<int, int>.CreateWritable); return x.F(); }))
                .SetName("GeneratedBridge_R19MergeChain13");
            yield return new TestCaseData("R20DefaultSlots", 0, "222", null,
                (Func<NeoClient, object?>)(client => { G.IR20I<int> i = CreateParityValue(client, G.R20M<int, int>.CreateWritable); return i.V.ToString() + i.F().ToString() + i.G<string>().ToString(); }))
                .SetName("GeneratedBridge_R20DefaultSlots0");
            yield return new TestCaseData("R20DefaultSlots", 1, "222", null,
                (Func<NeoClient, object?>)(client => { G.IR20I<string> i = CreateParityValue(client, G.R20M<string, int>.CreateWritable); return i.V.ToString() + i.F().ToString() + i.G<string>().ToString(); }))
                .SetName("GeneratedBridge_R20DefaultSlots1");
            yield return new TestCaseData("R20DefaultSlots", 2, "444", null,
                (Func<NeoClient, object?>)(client => { G.IR20I<int> i = CreateParityValue(client, G.R20M<string, int>.CreateWritable); return i.V.ToString() + i.F().ToString() + i.G<string>().ToString(); }))
                .SetName("GeneratedBridge_R20DefaultSlots2");
            yield return new TestCaseData("R20DefaultSlots", 3, "222", null,
                (Func<NeoClient, object?>)(client => { G.IR20I<int> i = CreateParityValue(client, G.R20N<int, int>.CreateWritable); return i.V.ToString() + i.F().ToString() + i.G<string>().ToString(); }))
                .SetName("GeneratedBridge_R20DefaultSlots3");
            yield return new TestCaseData("R20DefaultSlots", 4, "222", null,
                (Func<NeoClient, object?>)(client => { G.IR20I<int> i = CreateParityValue(client, G.R20Z<int, int, int>.CreateWritable); return i.V.ToString() + i.F().ToString() + i.G<string>().ToString(); }))
                .SetName("GeneratedBridge_R20DefaultSlots4");
            yield return new TestCaseData("R20DefaultSlots", 5, "444", null,
                (Func<NeoClient, object?>)(client => { G.IR20I<string> i = CreateParityValue(client, G.R20Z<int, int, string>.CreateWritable); return i.V.ToString() + i.F().ToString() + i.G<string>().ToString(); }))
                .SetName("GeneratedBridge_R20DefaultSlots5");
            yield return new TestCaseData("R20DefaultSlots", 6, "222", null,
                (Func<NeoClient, object?>)(client => { G.IR20I<string> i = CreateParityValue(client, G.R20Z<string, int, string>.CreateWritable); return i.V.ToString() + i.F().ToString() + i.G<string>().ToString(); }))
                .SetName("GeneratedBridge_R20DefaultSlots6");
            yield return new TestCaseData("R20DefaultSlots", 7, "222", null,
                (Func<NeoClient, object?>)(client => { G.IR20I<int> i = CreateParityValue(client, G.R20MK<int, int>.CreateWritable); return i.V.ToString() + i.F().ToString() + i.G<string>().ToString(); }))
                .SetName("GeneratedBridge_R20DefaultSlots7");
            yield return new TestCaseData("R20DefaultSlots", 8, "664", null,
                (Func<NeoClient, object?>)(client => { G.IR20I<int> i = CreateParityValue(client, G.R20MK<string, int>.CreateWritable); return i.V.ToString() + i.F().ToString() + i.G<string>().ToString(); }))
                .SetName("GeneratedBridge_R20DefaultSlots8");
            yield return new TestCaseData("R20DefaultSlots", 9, "2/2", null,
                (Func<NeoClient, object?>)(client => { G.IR20P<int> p = CreateParityValue(client, G.R20M<int, int>.CreateWritable); G.R20Rec.S = 0; int g = p.P; p.P = 1; return g.ToString() + "/" + G.R20Rec.S.ToString(); }))
                .SetName("GeneratedBridge_R20DefaultSlots9");
            yield return new TestCaseData("R20DefaultSlots", 10, "9/2", null,
                (Func<NeoClient, object?>)(client => { G.IR20P<int> p = CreateParityValue(client, G.R20MS<int, int>.CreateWritable); G.R20Rec.S = 0; int g = p.P; p.P = 1; return g.ToString() + "/" + G.R20Rec.S.ToString(); }))
                .SetName("GeneratedBridge_R20DefaultSlots10");
            yield return new TestCaseData("R20DefaultSlots", 11, "9/4", null,
                (Func<NeoClient, object?>)(client => { G.IR20P<int> p = CreateParityValue(client, G.R20MS<string, int>.CreateWritable); G.R20Rec.S = 0; int g = p.P; p.P = 1; return g.ToString() + "/" + G.R20Rec.S.ToString(); }))
                .SetName("GeneratedBridge_R20DefaultSlots11");
            yield return new TestCaseData("R20DefaultSlots", 12, "999", null,
                (Func<NeoClient, object?>)(client => { G.IR20I<int> i = CreateParityValue(client, G.R20Z9<int, int, int>.CreateWritable); return i.V.ToString() + i.F().ToString() + i.G<string>().ToString(); }))
                .SetName("GeneratedBridge_R20DefaultSlots12");
            yield return new TestCaseData("R20DefaultSlots", 13, "664", null,
                (Func<NeoClient, object?>)(client => { G.IR20I<int> i = CreateParityValue(client, G.R20MK0<int, int>.CreateWritable); return i.V.ToString() + i.F().ToString() + i.G<string>().ToString(); }))
                .SetName("GeneratedBridge_R20DefaultSlots13");
            yield return new TestCaseData("R20DefaultSlots", 14, "664", null,
                (Func<NeoClient, object?>)(client => { G.IR20I<int> i = CreateParityValue(client, G.R20MK0<string, int>.CreateWritable); return i.V.ToString() + i.F().ToString() + i.G<string>().ToString(); }))
                .SetName("GeneratedBridge_R20DefaultSlots14");
            yield return new TestCaseData("R20DefaultSlots", 15, "444", null,
                (Func<NeoClient, object?>)(client => { G.IR20I<string> i = CreateParityValue(client, G.R20MK0<string, int>.CreateWritable); return i.V.ToString() + i.F().ToString() + i.G<string>().ToString(); }))
                .SetName("GeneratedBridge_R20DefaultSlots15");
            yield return new TestCaseData("R21VarianceSlots", 0, "4", null,
                (Func<NeoClient, object?>)(client => { G.IR21V<G.R21Ba> x = CreateParityValue(client, G.R21CV1.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R21VarianceSlots0");
            yield return new TestCaseData("R21VarianceSlots", 1, "6", null,
                (Func<NeoClient, object?>)(client => { G.IR21V<G.R21Ba> x = CreateParityValue(client, G.R21CV2.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R21VarianceSlots1");
            yield return new TestCaseData("R21VarianceSlots", 2, "6", "4",
                (Func<NeoClient, object?>)(client => { G.IR21V<G.R21Ba> x = CreateParityValue(client, G.R21MV0<G.R21D1, G.R21D2>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R21VarianceSlots2_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R21VarianceSlots", 3, "6", "4",
                (Func<NeoClient, object?>)(client => { G.IR21V<G.R21Ba> x = CreateParityValue(client, G.R21MV0<G.R21D2, G.R21D1>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R21VarianceSlots3_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R21VarianceSlots", 4, "4", "6",
                (Func<NeoClient, object?>)(client => { G.IR21V<G.R21Ba> x = CreateParityValue(client, G.R21MV1<G.R21D1, G.R21D2>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R21VarianceSlots4_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R21VarianceSlots", 5, "2", null,
                (Func<NeoClient, object?>)(client => { G.IR21V<G.R21Ba> x = CreateParityValue(client, G.R21MX<G.R21D1, G.R21D2>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R21VarianceSlots5");
            yield return new TestCaseData("R21VarianceSlots", 6, "2", null,
                (Func<NeoClient, object?>)(client => { G.IR21V<G.R21Ba> x = CreateParityValue(client, G.R21MXK<G.R21D1, G.R21D2>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R21VarianceSlots6");
            yield return new TestCaseData("R21VarianceSlots", 7, "6", null,
                (Func<NeoClient, object?>)(client => { G.IR21V<G.R21Ba> x = CreateParityValue(client, G.R21CKL.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R21VarianceSlots7");
            yield return new TestCaseData("R21VarianceSlots", 8, "7", "6",
                (Func<NeoClient, object?>)(client => { G.IR21V<G.R21Ba> x = CreateParityValue(client, G.R21MKL<G.R21D1, G.R21D2>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R21VarianceSlots8_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R21VarianceSlots", 9, "6", "4",
                (Func<NeoClient, object?>)(client => { G.IR21V<G.R21Ba> x = CreateParityValue(client, G.R21NV0<G.R21D1, G.R21D2>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R21VarianceSlots9_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R21VarianceSlots", 10, "6", null,
                (Func<NeoClient, object?>)(client => { G.IR21V<G.R21Ba> x = CreateParityValue(client, G.R21ZV<G.R21D1, G.R21D2, G.R21D2>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R21VarianceSlots10");
            yield return new TestCaseData("R21VarianceSlots", 11, "6", null,
                (Func<NeoClient, object?>)(client => { G.IR21V<G.R21Ba> x = CreateParityValue(client, G.R21ZV<G.R21D1, G.R21D1, G.R21D2>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R21VarianceSlots11");
            yield return new TestCaseData("R21VarianceSlots", 12, "7", "ambiguous",
                (Func<NeoClient, object?>)(client => { G.IR21V<G.R21Ba> x = CreateParityValue(client, G.R21MKL<G.R21D1, G.R21D1>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R21VarianceSlots12_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R21VarianceSlots", 13, "1", null,
                (Func<NeoClient, object?>)(client => { G.IR21V<G.R21Ba> x = CreateParityValue(client, G.R21CX.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R21VarianceSlots13");
            yield return new TestCaseData("R21VarianceSlots", 14, "2", null,
                (Func<NeoClient, object?>)(client => { G.IR21V<G.R21Ba> x = CreateParityValue(client, G.R21CX2.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R21VarianceSlots14");
            yield return new TestCaseData("R21VarianceSlots", 15, "2", "1",
                (Func<NeoClient, object?>)(client => { G.IR21V<G.R21Ba> x = CreateParityValue(client, G.R21MY.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R21VarianceSlots15_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R21VarianceSlots", 16, "1", null,
                (Func<NeoClient, object?>)(client => { G.IR21V<G.R21Ba> x = CreateParityValue(client, G.R21MYK.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R21VarianceSlots16");
            yield return new TestCaseData("R21VarianceSlots", 17, "2", "6",
                (Func<NeoClient, object?>)(client => { G.IR21V<G.R21Ba> x = CreateParityValue(client, G.R21MZ.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R21VarianceSlots17_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R21VarianceSlots", 18, "4", "6",
                (Func<NeoClient, object?>)(client => { G.IR21V<G.R21Ba> x = CreateParityValue(client, G.R21MZ0.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R21VarianceSlots18_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R21SpecificSlots", 0, "66", null,
                (Func<NeoClient, object?>)(client => { G.IR21I<string> x = CreateParityValue(client, G.R21M<int>.CreateWritable); return x.V.ToString() + x.F().ToString(); }))
                .SetName("GeneratedBridge_R21SpecificSlots0");
            yield return new TestCaseData("R21SpecificSlots", 1, "77", null,
                (Func<NeoClient, object?>)(client => { G.IR21I<int> x = CreateParityValue(client, G.R21M<string>.CreateWritable); return x.V.ToString() + x.F().ToString(); }))
                .SetName("GeneratedBridge_R21SpecificSlots1");
            yield return new TestCaseData("R21SpecificSlots", 2, "66", null,
                (Func<NeoClient, object?>)(client => { G.IR21I<string> x = CreateParityValue(client, G.R21M<string>.CreateWritable); return x.V.ToString() + x.F().ToString(); }))
                .SetName("GeneratedBridge_R21SpecificSlots2");
            yield return new TestCaseData("R21SpecificSlots", 3, "77", null,
                (Func<NeoClient, object?>)(client => { G.IR21I<string> x = CreateParityValue(client, G.R21M2<string>.CreateWritable); return x.V.ToString() + x.F().ToString(); }))
                .SetName("GeneratedBridge_R21SpecificSlots3");
            yield return new TestCaseData("R21SpecificSlots", 4, "77", null,
                (Func<NeoClient, object?>)(client => { G.IR21I<string> x = CreateParityValue(client, G.R21M2<int>.CreateWritable); return x.V.ToString() + x.F().ToString(); }))
                .SetName("GeneratedBridge_R21SpecificSlots4");
            yield return new TestCaseData("R21SpecificSlots", 5, "66", null,
                (Func<NeoClient, object?>)(client => { G.IR21I<int> x = CreateParityValue(client, G.R21M2<int>.CreateWritable); return x.V.ToString() + x.F().ToString(); }))
                .SetName("GeneratedBridge_R21SpecificSlots5");
            yield return new TestCaseData("R22RelistSlots", 0, "2", null,
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22RB1.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22RelistSlots0");
            yield return new TestCaseData("R22RelistSlots", 1, "1", null,
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22RB3.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22RelistSlots1");
            yield return new TestCaseData("R22RelistSlots", 2, "2", null,
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22Q1v.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22RelistSlots2");
            yield return new TestCaseData("R22RelistSlots", 3, "2", null,
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22Q1n.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22RelistSlots3");
            yield return new TestCaseData("R22RelistSlots", 4, "1", null,
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22PE1.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22RelistSlots4");
            yield return new TestCaseData("R22RelistSlots", 5, "1", null,
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22PV1.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22RelistSlots5");
            yield return new TestCaseData("R22RelistSlots", 6, "2", "1",
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22RV1.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22RelistSlots6_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R22RelistSlots", 7, "2", "1",
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22RV2.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22RelistSlots7_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R22RelistSlots", 8, "2", "1",
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22QV1.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22RelistSlots8_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R22RelistSlots", 9, "2", "1",
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22QN1.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22RelistSlots9_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R22RelistSlots", 10, "2", null,
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22RO1.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22RelistSlots10");
            yield return new TestCaseData("R22RelistSlots", 11, "1", null,
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22RO2.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22RelistSlots11");
            yield return new TestCaseData("R22ClassSlots", 0, "1", null,
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22H2.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22ClassSlots0");
            yield return new TestCaseData("R22ClassSlots", 1, "1", null,
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22H4.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22ClassSlots1");
            yield return new TestCaseData("R22ClassSlots", 2, "1", null,
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22H5.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22ClassSlots2");
            yield return new TestCaseData("R22ClassSlots", 3, "1", null,
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22H6.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22ClassSlots3");
            yield return new TestCaseData("R22ClassSlots", 4, "3", null,
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22H8.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22ClassSlots4");
            yield return new TestCaseData("R22ClassSlots", 5, "1", null,
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22EM2.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22ClassSlots5");
            yield return new TestCaseData("R22ClassSlots", 6, "2", null,
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22X1.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22ClassSlots6");
            yield return new TestCaseData("R22ClassSlots", 7, "2", null,
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22X2.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22ClassSlots7");
            yield return new TestCaseData("R22ClassSlots", 8, "1", null,
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22PI1.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22ClassSlots8");
            yield return new TestCaseData("R22ClassSlots", 9, "1", "6",
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22CR3.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22ClassSlots9_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R22ClassSlots", 10, "2", null,
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22ZO.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22ClassSlots10");
            yield return new TestCaseData("R22ClassSlots", 11, "1", null,
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22ZO2.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22ClassSlots11");
            yield return new TestCaseData("R22DefaultSlots", 0, "4", null,
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22CE.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22DefaultSlots0");
            yield return new TestCaseData("R22DefaultSlots", 1, "4", null,
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22BE1.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22DefaultSlots1");
            yield return new TestCaseData("R22DefaultSlots", 2, "6", "4",
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22C1.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22DefaultSlots2_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R22DefaultSlots", 3, "6", null,
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22C2.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22DefaultSlots3");
            yield return new TestCaseData("R22DefaultSlots", 4, "7", "6",
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22NC.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22DefaultSlots4_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R22DefaultSlots", 5, "7", null,
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22NC2.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22DefaultSlots5");
            yield return new TestCaseData("R22DefaultSlots", 6, "7", "6",
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22NM.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22DefaultSlots6_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R22DefaultSlots", 7, "8", null,
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22NQ.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22DefaultSlots7");
            yield return new TestCaseData("R22DefaultSlots", 8, "7", "6",
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22VD.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22DefaultSlots8_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R22DefaultSlots", 9, "6", "7",
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22VD2.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22DefaultSlots9_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R22DefaultSlots", 10, "7", "6",
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22TK.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22DefaultSlots10_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R22DefaultSlots", 11, "6", null,
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22TK2.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22DefaultSlots11");
            yield return new TestCaseData("R22DefaultSlots", 12, "6", null,
                (Func<NeoClient, object?>)(client => { G.IR22V<G.R22Ba> x = CreateParityValue(client, G.R22TN.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22DefaultSlots12");
            yield return new TestCaseData("R22DefaultSlots", 13, "6", "4",
                (Func<NeoClient, object?>)(client => { G.IR22C<G.R22D11> x = CreateParityValue(client, G.R22C3.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22DefaultSlots13_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R22DefaultSlots", 14, "7", "6",
                (Func<NeoClient, object?>)(client => { G.IR22C<G.R22D11> x = CreateParityValue(client, G.R22NCC.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22DefaultSlots14_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R22NestedSlots", 0, "7", "6",
                (Func<NeoClient, object?>)(client => { G.IR22NI<G.R22Ba> x = CreateParityValue(client, G.R22NestC.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22NestedSlots0_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R22NestedSlots", 1, "7", "6",
                (Func<NeoClient, object?>)(client => { G.IR22NI<G.R22Ba> x = CreateParityValue(client, G.R22NestM.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22NestedSlots1_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R22NestedSlots", 2, "8", null,
                (Func<NeoClient, object?>)(client => { G.IR22NI<G.R22Ba> x = CreateParityValue(client, G.R22NestC2.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22NestedSlots2");
            yield return new TestCaseData("R22NestedSlots", 3, "7", "6",
                (Func<NeoClient, object?>)(client => { G.IR22NI<G.R22Ba> x = CreateParityValue(client, G.R22NestC.CreateWritable); return x.F<int>(); }))
                .SetName("GeneratedBridge_R22NestedSlots3_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R22NestedSlots", 4, "7", "6",
                (Func<NeoClient, object?>)(client => { G.IR22NI<G.R22Ba> x = CreateParityValue(client, G.R22NestM.CreateWritable); return x.F<int>(); }))
                .SetName("GeneratedBridge_R22NestedSlots4_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R22NestedSlots", 5, "7", "6",
                (Func<NeoClient, object?>)(client => { G.IR22NI<G.R22Ba> x = CreateParityValue(client, G.R22NestC.CreateWritable); x.P = 1; return G.R22Rec.S; }))
                .SetName("GeneratedBridge_R22NestedSlots5_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R22NestedSlots", 6, "7", "6",
                (Func<NeoClient, object?>)(client => { G.IR22NI<G.R22Ba> x = CreateParityValue(client, G.R22NestM.CreateWritable); x.P = 1; return G.R22Rec.S; }))
                .SetName("GeneratedBridge_R22NestedSlots6_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R22NestedSlots", 9, "7", "6",
                (Func<NeoClient, object?>)(client => { G.IR22NN<G.R22D11> x = CreateParityValue(client, G.R22NestNC.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22NestedSlots9_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R22NestedSlots", 10, "3", null,
                (Func<NeoClient, object?>)(client => { G.IR22NI<G.R22Ba> x = CreateParityValue(client, G.R22VariantDerived.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22NestedSlots10");
            yield return new TestCaseData("R22NestedSlots", 11, "3", null,
                (Func<NeoClient, object?>)(client => { G.IR22NI<G.R22Ba> x = CreateParityValue(client, G.R22ExactDerived.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22NestedSlots11");
            yield return new TestCaseData("R22NestedSlots", 12, "6", null,
                (Func<NeoClient, object?>)(client => { G.IR22NI<G.R22D1> x = CreateParityValue(client, G.R22NestC.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22NestedSlots12");
            yield return new TestCaseData("R22NestedSlots", 13, "6", null,
                (Func<NeoClient, object?>)(client => { G.IR22NI<G.R22D1> x = CreateParityValue(client, G.R22NestM.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R22NestedSlots13");
            yield return new TestCaseData("R24BaseSlots", 0, "5", null,
                (Func<NeoClient, object?>)(client => { G.R24BIV<G.R24BBa> x = CreateParityValue(client, G.R24BM1.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R24M1");
            yield return new TestCaseData("R24BaseSlots", 1, "9", null,
                (Func<NeoClient, object?>)(client => { G.R24BIV<G.R24BBa> x = CreateParityValue(client, G.R24BM2.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R24M2");
            yield return new TestCaseData("R24BaseSlots", 2, "9", null,
                (Func<NeoClient, object?>)(client => { G.R24BIV<G.R24BBa> x = CreateParityValue(client, G.R24BM5.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R24M5");
            yield return new TestCaseData("R24BaseSlots", 3, "5", null,
                (Func<NeoClient, object?>)(client => { G.R24BIV<G.R24BBa> x = CreateParityValue(client, G.R24BG1.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R24G1");
            yield return new TestCaseData("R24BaseSlots", 4, "9", null,
                (Func<NeoClient, object?>)(client => { G.R24BIV<G.R24BBa> x = CreateParityValue(client, G.R24BM5c.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R24M5c");
            yield return new TestCaseData("R24BaseSlots", 5, "9", null,
                (Func<NeoClient, object?>)(client => { G.R24BIV<G.R24BBa> x = CreateParityValue(client, G.R24BM2c.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R24M2c");
            yield return new TestCaseData("R24BaseSlots", 6, "1", null,
                (Func<NeoClient, object?>)(client => { G.R24BIV<G.R24BBa> x = CreateParityValue(client, G.R24BN1.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R24N1");
            yield return new TestCaseData("R24BaseSlots", 7, "5", null,
                (Func<NeoClient, object?>)(client => { G.R24BIW2<G.R24BBa> x = CreateParityValue(client, G.R24BM4.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R24M4");
            yield return new TestCaseData("R24BaseSlots", 8, "9", null,
                (Func<NeoClient, object?>)(client => { G.R24BIW2<G.R24BBa> x = CreateParityValue(client, G.R24BM6.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R24M6");
            yield return new TestCaseData("R24BaseSlots", 9, "9", null,
                (Func<NeoClient, object?>)(client => { G.R24BIW2<G.R24BBa> x = CreateParityValue(client, G.R24BM7.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R24M7");
            yield return new TestCaseData("R24BaseSlots", 10, "9", null,
                (Func<NeoClient, object?>)(client => { G.R24BIV<int> x = CreateParityValue(client, G.R24BM8.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R24M8");
            yield return new TestCaseData("R24BaseSlots", 11, "9", null,
                (Func<NeoClient, object?>)(client => { G.R24BIV<G.R24BIAn> x = CreateParityValue(client, G.R24BM5.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R24M5a");
            yield return new TestCaseData("R24BaseSlots", 12, "1", null,
                (Func<NeoClient, object?>)(client => { G.R24BIV<G.R24BIAn> x = CreateParityValue(client, G.R24BN1.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R24N1a");
            yield return new TestCaseData("R24BaseSlots", 13, "1", null,
                (Func<NeoClient, object?>)(client => { G.R24BIC<G.R24BD1> x = CreateParityValue(client, G.R24BCN.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R24CN");
            yield return new TestCaseData("R24BaseSlots", 14, "1", null,
                (Func<NeoClient, object?>)(client => { G.R24BIC<G.R24BBa> x = CreateParityValue(client, G.R24BCN.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R24CNb");
            yield return new TestCaseData("R24BaseSlots", 15, "9", null,
                (Func<NeoClient, object?>)(client => { G.R24BIC<G.R24BD1> x = CreateParityValue(client, G.R24BCM.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R24CM");
            yield return new TestCaseData("R24BaseSlots", 16, "9", null,
                (Func<NeoClient, object?>)(client => { G.R24BIC<G.R24BBa> x = CreateParityValue(client, G.R24BCM.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R24CMb");
            yield return new TestCaseData("R24BaseSlots", 17, "9", null,
                (Func<NeoClient, object?>)(client => { G.R24BIV<G.R24BBox<G.R24BBa>> x = CreateParityValue(client, G.R24BNB1.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R24NB1");
            yield return new TestCaseData("R24BaseSlots", 18, "9", null,
                (Func<NeoClient, object?>)(client => { G.R24BIV<G.R24BBox<G.R24BBa>> x = CreateParityValue(client, G.R24BGB.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R24GB");
            yield return new TestCaseData("R24BaseSlots", 19, "9", null,
                (Func<NeoClient, object?>)(client => { G.R24BIW3<G.R24BBa, int> x = CreateParityValue(client, G.R24BPQ.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R24PQ");
            yield return new TestCaseData("R24BaseSlots", 20, "9", null,
                (Func<NeoClient, object?>)(client => { G.R24BIW3<G.R24BBa, string> x = CreateParityValue(client, G.R24BPN<string>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R24PN");
            yield return new TestCaseData("R24BaseSlots", 21, "9", null,
                (Func<NeoClient, object?>)(client => { G.R24BIV<G.R24BBa> x = CreateParityValue(client, G.R24BUQ<G.R24BBa>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R24UQb");
            yield return new TestCaseData("R24BaseSlots", 22, "9", null,
                (Func<NeoClient, object?>)(client => { G.R24BIV<G.R24BBa> x = CreateParityValue(client, G.R24BUQ<G.R24BD1>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R24UQd");
            yield return new TestCaseData("R24BaseSlots", 23, "1", null,
                (Func<NeoClient, object?>)(client => { G.R24BIV<G.R24BBa> x = CreateParityValue(client, G.R24BUQ<int>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R24UQi");
            yield return new TestCaseData("R24BaseSlots", 24, "2", null,
                (Func<NeoClient, object?>)(client => { G.R24BIPV<G.R24BBa> x = CreateParityValue(client, G.R24BQX.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_R24QXGet");
            yield return new TestCaseData("R24BaseSlots", 25, "3", null,
                (Func<NeoClient, object?>)(client => { G.R24BIPV<G.R24BBa> x = CreateParityValue(client, G.R24BQX.CreateWritable); x.P = 0; return G.R24BRec.S; }))
                .SetName("GeneratedBridge_R24QXSet");
            yield return new TestCaseData("R24OpenSlots", 0, "2", null,
                (Func<NeoClient, object?>)(client => { G.R24VIPV<G.R24VIQ<G.R24VBa>> x = CreateParityValue(client, G.R24VX<G.R24VBa>.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_R24XBaGet");
            yield return new TestCaseData("R24OpenSlots", 1, "3", null,
                (Func<NeoClient, object?>)(client => { G.R24VIPV<G.R24VIQ<G.R24VBa>> x = CreateParityValue(client, G.R24VX<G.R24VBa>.CreateWritable); x.P = 0; return G.R24VRec.S; }))
                .SetName("GeneratedBridge_R24XBaSet");
            yield return new TestCaseData("R24OpenSlots", 2, "5", null,
                (Func<NeoClient, object?>)(client => { G.R24VIPV<G.R24VIQ<G.R24VBa>> x = CreateParityValue(client, G.R24VX<string>.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_R24XstringGet");
            yield return new TestCaseData("R24OpenSlots", 3, "3", null,
                (Func<NeoClient, object?>)(client => { G.R24VIPV<G.R24VIQ<G.R24VBa>> x = CreateParityValue(client, G.R24VX<string>.CreateWritable); x.P = 0; return G.R24VRec.S; }))
                .SetName("GeneratedBridge_R24XstringSet");
            yield return new TestCaseData("R24OpenSlots", 4, "3", null,
                (Func<NeoClient, object?>)(client => { G.R24VIPV<G.R24VIQ<G.R24VBa>> x = CreateParityValue(client, G.R24VY<G.R24VBa>.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_R24YBaGet");
            yield return new TestCaseData("R24OpenSlots", 5, "2", null,
                (Func<NeoClient, object?>)(client => { G.R24VIPV<G.R24VIQ<G.R24VBa>> x = CreateParityValue(client, G.R24VY<G.R24VBa>.CreateWritable); x.P = 0; return G.R24VRec.S; }))
                .SetName("GeneratedBridge_R24YBaSet");
            yield return new TestCaseData("R24OpenSlots", 6, "3", null,
                (Func<NeoClient, object?>)(client => { G.R24VIPV<G.R24VIQ<G.R24VBa>> x = CreateParityValue(client, G.R24VY<string>.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_R24YstringGet");
            yield return new TestCaseData("R24OpenSlots", 7, "6", null,
                (Func<NeoClient, object?>)(client => { G.R24VIPV<G.R24VIQ<G.R24VBa>> x = CreateParityValue(client, G.R24VY<string>.CreateWritable); x.P = 0; return G.R24VRec.S; }))
                .SetName("GeneratedBridge_R24YstringSet");
            yield return new TestCaseData("R24OpenSlots", 8, "2", null,
                (Func<NeoClient, object?>)(client => { G.R24VIPV<G.R24VIQ<G.R24VBa>> x = CreateParityValue(client, G.R24VW<G.R24VBa>.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_R24WBaGet");
            yield return new TestCaseData("R24OpenSlots", 9, "3", null,
                (Func<NeoClient, object?>)(client => { G.R24VIPV<G.R24VIQ<G.R24VBa>> x = CreateParityValue(client, G.R24VW<G.R24VBa>.CreateWritable); x.P = 0; return G.R24VRec.S; }))
                .SetName("GeneratedBridge_R24WBaSet");
            yield return new TestCaseData("R24OpenSlots", 10, "5", null,
                (Func<NeoClient, object?>)(client => { G.R24VIPV<G.R24VIQ<G.R24VBa>> x = CreateParityValue(client, G.R24VW<string>.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_R24WstringGet");
            yield return new TestCaseData("R24OpenSlots", 11, "3", null,
                (Func<NeoClient, object?>)(client => { G.R24VIPV<G.R24VIQ<G.R24VBa>> x = CreateParityValue(client, G.R24VW<string>.CreateWritable); x.P = 0; return G.R24VRec.S; }))
                .SetName("GeneratedBridge_R24WstringSet");
            yield return new TestCaseData("R24OpenSlots", 12, "8", null,
                (Func<NeoClient, object?>)(client => { G.R24VIPV<G.R24VIQ<G.R24VBa>> x = CreateParityValue(client, G.R24VV2<G.R24VBa, G.R24VBa>.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_R24V2BaBaGet");
            yield return new TestCaseData("R24OpenSlots", 13, "3", null,
                (Func<NeoClient, object?>)(client => { G.R24VIPV<G.R24VIQ<G.R24VBa>> x = CreateParityValue(client, G.R24VV2<G.R24VBa, G.R24VBa>.CreateWritable); x.P = 0; return G.R24VRec.S; }))
                .SetName("GeneratedBridge_R24V2BaBaSet");
            yield return new TestCaseData("R24OpenSlots", 14, "2", null,
                (Func<NeoClient, object?>)(client => { G.R24VIPV<G.R24VIQ<G.R24VBa>> x = CreateParityValue(client, G.R24VV2<G.R24VBa, string>.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_R24V2BastringGet");
            yield return new TestCaseData("R24OpenSlots", 15, "3", null,
                (Func<NeoClient, object?>)(client => { G.R24VIPV<G.R24VIQ<G.R24VBa>> x = CreateParityValue(client, G.R24VV2<G.R24VBa, string>.CreateWritable); x.P = 0; return G.R24VRec.S; }))
                .SetName("GeneratedBridge_R24V2BastringSet");
            yield return new TestCaseData("R24OpenSlots", 16, "8", null,
                (Func<NeoClient, object?>)(client => { G.R24VIPV<G.R24VIQ<G.R24VBa>> x = CreateParityValue(client, G.R24VV2<string, G.R24VBa>.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_R24V2stringBaGet");
            yield return new TestCaseData("R24OpenSlots", 17, "3", null,
                (Func<NeoClient, object?>)(client => { G.R24VIPV<G.R24VIQ<G.R24VBa>> x = CreateParityValue(client, G.R24VV2<string, G.R24VBa>.CreateWritable); x.P = 0; return G.R24VRec.S; }))
                .SetName("GeneratedBridge_R24V2stringBaSet");
            yield return new TestCaseData("R24OpenSlots", 18, "5", null,
                (Func<NeoClient, object?>)(client => { G.R24VIPV<G.R24VIQ<G.R24VBa>> x = CreateParityValue(client, G.R24VV2<string, string>.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_R24V2stringstringGet");
            yield return new TestCaseData("R24OpenSlots", 19, "3", null,
                (Func<NeoClient, object?>)(client => { G.R24VIPV<G.R24VIQ<G.R24VBa>> x = CreateParityValue(client, G.R24VV2<string, string>.CreateWritable); x.P = 0; return G.R24VRec.S; }))
                .SetName("GeneratedBridge_R24V2stringstringSet");
            yield return new TestCaseData("R24OverrideSlots", 0, "2", null,
                (Func<NeoClient, object?>)(client => { G.R24OI<int> x = CreateParityValue(client, G.R24OC2<int>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R24C2");
            yield return new TestCaseData("R24OverrideSlots", 1, "7", null,
                (Func<NeoClient, object?>)(client => { G.R24OI<int> x = CreateParityValue(client, G.R24OC3<int>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R24C3");
            yield return new TestCaseData("R24OverrideSlots", 2, "3", null,
                (Func<NeoClient, object?>)(client => { G.R24OIR x = CreateParityValue(client, G.R24OR.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R24RGet");
            yield return new TestCaseData("R24OverrideSlots", 3, "2", null,
                (Func<NeoClient, object?>)(client => { G.R24OIR x = CreateParityValue(client, G.R24OR.CreateWritable); x.V = 0; return G.R24ORec.S; }))
                .SetName("GeneratedBridge_R24RSet");
            yield return new TestCaseData("R24SplitSlots", 0, "3", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY000.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_R24N000Get");
            yield return new TestCaseData("R24SplitSlots", 1, "3", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY000.CreateWritable); x.P = 0; return G.R24NRec.S; }))
                .SetName("GeneratedBridge_R24N000Set");
            yield return new TestCaseData("R24SplitSlots", 2, "9", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY011.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_R24N011Get");
            yield return new TestCaseData("R24SplitSlots", 3, "3", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY011.CreateWritable); x.P = 0; return G.R24NRec.S; }))
                .SetName("GeneratedBridge_R24N011Set");
            yield return new TestCaseData("R24SplitSlots", 4, "3", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY022.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_R24N022Get");
            yield return new TestCaseData("R24SplitSlots", 5, "9", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY022.CreateWritable); x.P = 0; return G.R24NRec.S; }))
                .SetName("GeneratedBridge_R24N022Set");
            yield return new TestCaseData("R24SplitSlots", 6, "9", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY033.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_R24N033Get");
            yield return new TestCaseData("R24SplitSlots", 7, "9", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY033.CreateWritable); x.P = 0; return G.R24NRec.S; }))
                .SetName("GeneratedBridge_R24N033Set");
            yield return new TestCaseData("R24SplitSlots", 8, "2", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY101.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_R24N101Get");
            yield return new TestCaseData("R24SplitSlots", 9, "3", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY101.CreateWritable); x.P = 0; return G.R24NRec.S; }))
                .SetName("GeneratedBridge_R24N101Set");
            yield return new TestCaseData("R24SplitSlots", 10, "2", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY112.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_R24N112Get");
            yield return new TestCaseData("R24SplitSlots", 11, "9", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY112.CreateWritable); x.P = 0; return G.R24NRec.S; }))
                .SetName("GeneratedBridge_R24N112Set");
            yield return new TestCaseData("R24SplitSlots", 12, "2", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY123.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_R24N123Get");
            yield return new TestCaseData("R24SplitSlots", 13, "9", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY123.CreateWritable); x.P = 0; return G.R24NRec.S; }))
                .SetName("GeneratedBridge_R24N123Set");
            yield return new TestCaseData("R24SplitSlots", 14, "2", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY130.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_R24N130Get");
            yield return new TestCaseData("R24SplitSlots", 15, "7", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY130.CreateWritable); x.P = 0; return G.R24NRec.S; }))
                .SetName("GeneratedBridge_R24N130Set");
            yield return new TestCaseData("R24SplitSlots", 16, "3", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY202.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_R24N202Get");
            yield return new TestCaseData("R24SplitSlots", 17, "2", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY202.CreateWritable); x.P = 0; return G.R24NRec.S; }))
                .SetName("GeneratedBridge_R24N202Set");
            yield return new TestCaseData("R24SplitSlots", 18, "9", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY213.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_R24N213Get");
            yield return new TestCaseData("R24SplitSlots", 19, "2", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY213.CreateWritable); x.P = 0; return G.R24NRec.S; }))
                .SetName("GeneratedBridge_R24N213Set");
            yield return new TestCaseData("R24SplitSlots", 20, "3", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY220.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_R24N220Get");
            yield return new TestCaseData("R24SplitSlots", 21, "2", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY220.CreateWritable); x.P = 0; return G.R24NRec.S; }))
                .SetName("GeneratedBridge_R24N220Set");
            yield return new TestCaseData("R24SplitSlots", 22, "9", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY231.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_R24N231Get");
            yield return new TestCaseData("R24SplitSlots", 23, "2", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY231.CreateWritable); x.P = 0; return G.R24NRec.S; }))
                .SetName("GeneratedBridge_R24N231Set");
            yield return new TestCaseData("R24SplitSlots", 24, "2", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY303.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_R24N303Get");
            yield return new TestCaseData("R24SplitSlots", 25, "2", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY303.CreateWritable); x.P = 0; return G.R24NRec.S; }))
                .SetName("GeneratedBridge_R24N303Set");
            yield return new TestCaseData("R24SplitSlots", 26, "2", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY310.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_R24N310Get");
            yield return new TestCaseData("R24SplitSlots", 27, "2", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY310.CreateWritable); x.P = 0; return G.R24NRec.S; }))
                .SetName("GeneratedBridge_R24N310Set");
            yield return new TestCaseData("R24SplitSlots", 28, "2", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY321.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_R24N321Get");
            yield return new TestCaseData("R24SplitSlots", 29, "2", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY321.CreateWritable); x.P = 0; return G.R24NRec.S; }))
                .SetName("GeneratedBridge_R24N321Set");
            yield return new TestCaseData("R24SplitSlots", 30, "2", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY332.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_R24N332Get");
            yield return new TestCaseData("R24SplitSlots", 31, "2", null,
                (Func<NeoClient, object?>)(client => { G.R24NIPV<G.R24NBa> x = CreateParityValue(client, G.R24NY332.CreateWritable); x.P = 0; return G.R24NRec.S; }))
                .SetName("GeneratedBridge_R24N332Set");
            yield return new TestCaseData("R23RelistSlots", 0, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23WIW x = CreateParityValue(client, G.R23WW1.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_W1Get");
            yield return new TestCaseData("R23RelistSlots", 1, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23WIW x = CreateParityValue(client, G.R23WW1.CreateWritable); return x.F(); }))
                .SetName("GeneratedBridge_W1Method");
            yield return new TestCaseData("R23RelistSlots", 2, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23WIW x = CreateParityValue(client, G.R23WW1.CreateWritable); return x.G<int>(); }))
                .SetName("GeneratedBridge_W1Generic");
            yield return new TestCaseData("R23RelistSlots", 3, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23WIW x = CreateParityValue(client, G.R23WW1.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_W1Property");
            yield return new TestCaseData("R23RelistSlots", 4, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23WIW x = CreateParityValue(client, G.R23WW1.CreateWritable); x.P = 0; return G.R23WRec.S; }))
                .SetName("GeneratedBridge_W1Set");
            yield return new TestCaseData("R23RelistSlots", 5, "1", null,
                (Func<NeoClient, object?>)(client => { G.R23WIW x = CreateParityValue(client, G.R23WW2.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_W2Get");
            yield return new TestCaseData("R23RelistSlots", 6, "1", null,
                (Func<NeoClient, object?>)(client => { G.R23WIW x = CreateParityValue(client, G.R23WW2.CreateWritable); return x.F(); }))
                .SetName("GeneratedBridge_W2Method");
            yield return new TestCaseData("R23RelistSlots", 7, "1", null,
                (Func<NeoClient, object?>)(client => { G.R23WIW x = CreateParityValue(client, G.R23WW2.CreateWritable); return x.G<int>(); }))
                .SetName("GeneratedBridge_W2Generic");
            yield return new TestCaseData("R23RelistSlots", 8, "1", null,
                (Func<NeoClient, object?>)(client => { G.R23WIW x = CreateParityValue(client, G.R23WW2.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_W2Property");
            yield return new TestCaseData("R23RelistSlots", 9, "1", null,
                (Func<NeoClient, object?>)(client => { G.R23WIW x = CreateParityValue(client, G.R23WW2.CreateWritable); x.P = 0; return G.R23WRec.S; }))
                .SetName("GeneratedBridge_W2Set");
            yield return new TestCaseData("R23RelistSlots", 10, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23WIW x = CreateParityValue(client, G.R23WW3.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_W3Get");
            yield return new TestCaseData("R23RelistSlots", 11, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23WIW x = CreateParityValue(client, G.R23WW3.CreateWritable); return x.F(); }))
                .SetName("GeneratedBridge_W3Method");
            yield return new TestCaseData("R23RelistSlots", 12, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23WIW x = CreateParityValue(client, G.R23WW3.CreateWritable); return x.G<int>(); }))
                .SetName("GeneratedBridge_W3Generic");
            yield return new TestCaseData("R23RelistSlots", 13, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23WIW x = CreateParityValue(client, G.R23WW3.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_W3Property");
            yield return new TestCaseData("R23RelistSlots", 14, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23WIW x = CreateParityValue(client, G.R23WW3.CreateWritable); x.P = 0; return G.R23WRec.S; }))
                .SetName("GeneratedBridge_W3Set");
            yield return new TestCaseData("R23RelistSlots", 15, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23WIW x = CreateParityValue(client, G.R23WW4.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_W4Get");
            yield return new TestCaseData("R23RelistSlots", 16, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23WIW x = CreateParityValue(client, G.R23WW4.CreateWritable); return x.F(); }))
                .SetName("GeneratedBridge_W4Method");
            yield return new TestCaseData("R23RelistSlots", 17, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23WIW x = CreateParityValue(client, G.R23WW4.CreateWritable); return x.G<int>(); }))
                .SetName("GeneratedBridge_W4Generic");
            yield return new TestCaseData("R23RelistSlots", 18, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23WIW x = CreateParityValue(client, G.R23WW4.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_W4Property");
            yield return new TestCaseData("R23RelistSlots", 19, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23WIW x = CreateParityValue(client, G.R23WW4.CreateWritable); x.P = 0; return G.R23WRec.S; }))
                .SetName("GeneratedBridge_W4Set");
            yield return new TestCaseData("R23RelistSlots", 20, "7", null,
                (Func<NeoClient, object?>)(client => { G.R23WIW x = CreateParityValue(client, G.R23WW5.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_W5");
            yield return new TestCaseData("R23RelistSlots", 21, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23WIW x = CreateParityValue(client, G.R23WW6.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_W6");
            yield return new TestCaseData("R23RelistSlots", 22, "7", null,
                (Func<NeoClient, object?>)(client => { G.R23WIW x = CreateParityValue(client, G.R23WW7.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_W7");
            yield return new TestCaseData("R23RelistSlots", 23, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23WIW x = CreateParityValue(client, G.R23WWA.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_WA");
            yield return new TestCaseData("R23RelistSlots", 24, "9", null,
                (Func<NeoClient, object?>)(client => { G.R23WIW x = CreateParityValue(client, G.R23WWAo.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_WAo");
            yield return new TestCaseData("R23RelistSlots", 25, "8", null,
                (Func<NeoClient, object?>)(client => { G.R23WIW x = CreateParityValue(client, G.R23WWS.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_WS");
            yield return new TestCaseData("R23RelistSlots", 26, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23PIPW x = CreateParityValue(client, G.R23PPC.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_PCGet");
            yield return new TestCaseData("R23RelistSlots", 27, "1", null,
                (Func<NeoClient, object?>)(client => { G.R23PIPW x = CreateParityValue(client, G.R23PPC.CreateWritable); x.P = 0; return G.R23PRec.S; }))
                .SetName("GeneratedBridge_PCSet");
            yield return new TestCaseData("R23RelistSlots", 28, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23PIPW x = CreateParityValue(client, G.R23PPC2.CreateWritable); x.P = 0; return G.R23PRec.S; }))
                .SetName("GeneratedBridge_PC2Set");
            yield return new TestCaseData("R23RelistSlots", 29, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23VIV<G.R23VBa> x = CreateParityValue(client, G.R23VA1.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_A1");
            yield return new TestCaseData("R23RelistSlots", 30, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23VIV<G.R23VBa> x = CreateParityValue(client, G.R23VA2.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_A2");
            yield return new TestCaseData("R23RelistSlots", 31, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23VIV<G.R23VBa> x = CreateParityValue(client, G.R23VA3.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_A3");
            yield return new TestCaseData("R23RelistSlots", 32, "9", null,
                (Func<NeoClient, object?>)(client => { G.R23VIV<G.R23VBa> x = CreateParityValue(client, G.R23VA3o.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_A3o");
            yield return new TestCaseData("R23RelistSlots", 33, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23VIV<G.R23VD1> x = CreateParityValue(client, G.R23VA4.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_A4");
            yield return new TestCaseData("R23RelistSlots", 34, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23VIV<G.R23VBa> x = CreateParityValue(client, G.R23VA4.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_A4b");
            yield return new TestCaseData("R23RelistSlots", 35, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23VIV<G.R23VBa> x = CreateParityValue(client, G.R23VA5.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_A5");
            yield return new TestCaseData("R23RelistSlots", 36, "5", "1",
                (Func<NeoClient, object?>)(client => { G.R23VIV<G.R23VBa> x = CreateParityValue(client, G.R23VA6.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_A6_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R23RelistSlots", 37, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23VIV<G.R23VBa> x = CreateParityValue(client, G.R23VA7.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_A7");
            yield return new TestCaseData("R23RelistSlots", 38, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23VIV<G.R23VBa> x = CreateParityValue(client, G.R23VA8.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_A8");
            yield return new TestCaseData("R23RelistSlots", 39, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23VIV<G.R23VBa> x = CreateParityValue(client, G.R23VA9.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_A9");
            yield return new TestCaseData("R23RelistSlots", 40, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23VIV<G.R23VBa> x = CreateParityValue(client, G.R23VAK.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_AK");
            yield return new TestCaseData("R23RelistSlots", 41, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23GIG<int> x = CreateParityValue(client, G.R23GG1.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_G1i");
            yield return new TestCaseData("R23RelistSlots", 42, "g0", null,
                (Func<NeoClient, object?>)(client => { G.R23GIG<string> x = CreateParityValue(client, G.R23GG1.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_G1s");
            yield return new TestCaseData("R23RelistSlots", 43, "0", null,
                (Func<NeoClient, object?>)(client => { G.R23GIG<int> x = CreateParityValue(client, G.R23GG2.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_G2i");
            yield return new TestCaseData("R23RelistSlots", 44, "g0", null,
                (Func<NeoClient, object?>)(client => { G.R23GIG<string> x = CreateParityValue(client, G.R23GG2.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_G2s");
            yield return new TestCaseData("R23RelistSlots", 45, "1", null,
                (Func<NeoClient, object?>)(client => { G.R23GIG<int> x = CreateParityValue(client, G.R23GG3.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_G3i");
            yield return new TestCaseData("R23RelistSlots", 46, "1", null,
                (Func<NeoClient, object?>)(client => { G.R23GIG<int> x = CreateParityValue(client, G.R23GG4<int>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_G4ii");
            yield return new TestCaseData("R23RelistSlots", 47, "g0", null,
                (Func<NeoClient, object?>)(client => { G.R23GIG<string> x = CreateParityValue(client, G.R23GG4<int>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_G4is");
            yield return new TestCaseData("R23RelistSlots", 48, "1", null,
                (Func<NeoClient, object?>)(client => { G.R23GIG<int> x = CreateParityValue(client, G.R23GG4<string>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_G4si");
            yield return new TestCaseData("R23RelistSlots", 49, "g0", null,
                (Func<NeoClient, object?>)(client => { G.R23GIG<string> x = CreateParityValue(client, G.R23GG4<string>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_G4ss");
            yield return new TestCaseData("R23RuntimeSlots", 0, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23LJ<G.R23LTag, int> x = CreateParityValue(client, G.R23LLX<int, int>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_LX");
            yield return new TestCaseData("R23RuntimeSlots", 1, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23LJ<G.R23LTag, int> x = CreateParityValue(client, G.R23LLX<int, string>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_LXu");
            yield return new TestCaseData("R23RuntimeSlots", 2, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23LJ<G.R23LTag, int> x = CreateParityValue(client, G.R23LLXv<int, int>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_LXv");
            yield return new TestCaseData("R23RuntimeSlots", 3, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23LJ<G.R23LTag, int> x = CreateParityValue(client, G.R23LOX<int, int>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_OX");
            yield return new TestCaseData("R23RuntimeSlots", 4, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23LJ<G.R23LTag, int> x = CreateParityValue(client, G.R23LHX<int, int>.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_HX");
            yield return new TestCaseData("R23RuntimeSlots", 5, "2", null,
                (Func<NeoClient, object?>)(client => { G.R23RIV<G.R23RBa> x = CreateParityValue(client, G.R23RR3.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_R3");
            yield return new TestCaseData("R23RuntimeSlots", 6, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23RIV<G.R23RBa> x = CreateParityValue(client, G.R23RQ4.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_Q4");
            yield return new TestCaseData("R23RuntimeSlots", 7, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23RIV<G.R23RBa> x = CreateParityValue(client, G.R23RQ4v.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_Q4v");
            yield return new TestCaseData("R23RuntimeSlots", 8, "2", null,
                (Func<NeoClient, object?>)(client => { G.R23RIV<G.R23RBa> x = CreateParityValue(client, G.R23RP5.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_P5");
            yield return new TestCaseData("R23RuntimeSlots", 9, "2", null,
                (Func<NeoClient, object?>)(client => { G.R23RIV<G.R23RBa> x = CreateParityValue(client, G.R23RK6.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_K6");
            yield return new TestCaseData("R23RuntimeSlots", 10, "2", null,
                (Func<NeoClient, object?>)(client => { G.R23RIV<G.R23RBa> x = CreateParityValue(client, G.R23RN6.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_N6");
            yield return new TestCaseData("R23RuntimeSlots", 11, "5", null,
                (Func<NeoClient, object?>)(client => { G.R23RIW x = CreateParityValue(client, G.R23RT8.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_T8");
            yield return new TestCaseData("R23RuntimeSlots", 12, "2", null,
                (Func<NeoClient, object?>)(client => { G.R23CIPV<G.R23CBa> x = CreateParityValue(client, G.R23CX.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_XGet");
            yield return new TestCaseData("R23RuntimeSlots", 13, "3", null,
                (Func<NeoClient, object?>)(client => { G.R23CIPV<G.R23CBa> x = CreateParityValue(client, G.R23CX.CreateWritable); x.P = 0; return G.R23CRec.S; }))
                .SetName("GeneratedBridge_XSet");
            yield return new TestCaseData("R23RuntimeSlots", 14, "3", null,
                (Func<NeoClient, object?>)(client => { G.R23CIPV<G.R23CBa> x = CreateParityValue(client, G.R23CY.CreateWritable); return x.P; }))
                .SetName("GeneratedBridge_YGet");
            yield return new TestCaseData("R23RuntimeSlots", 15, "2", null,
                (Func<NeoClient, object?>)(client => { G.R23CIPV<G.R23CBa> x = CreateParityValue(client, G.R23CY.CreateWritable); x.P = 0; return G.R23CRec.S; }))
                .SetName("GeneratedBridge_YSet");
            yield return new TestCaseData("R23OrderSlots", 0, "8", null,
                (Func<NeoClient, object?>)(client => { G.R23MI<G.R23MBa> x = CreateParityValue(client, G.R23MC8.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_C8Get");
            yield return new TestCaseData("R23OrderSlots", 1, "8", null,
                (Func<NeoClient, object?>)(client => { G.R23MI<G.R23MBa> x = CreateParityValue(client, G.R23MC8.CreateWritable); return x.F<G.R23MD1>(); }))
                .SetName("GeneratedBridge_C8Generic");
            yield return new TestCaseData("R23OrderSlots", 2, "8", null,
                (Func<NeoClient, object?>)(client => { G.R23MI<G.R23MBa> x = CreateParityValue(client, G.R23MC8.CreateWritable); x.P = 0; return G.R23MRec.S; }))
                .SetName("GeneratedBridge_C8Set");
            yield return new TestCaseData("R23OrderSlots", 3, "6", null,
                (Func<NeoClient, object?>)(client => { G.R23MI<G.R23MBa> x = CreateParityValue(client, G.R23MC8r.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_C8rGet");
            yield return new TestCaseData("R23OrderSlots", 4, "6", null,
                (Func<NeoClient, object?>)(client => { G.R23MI<G.R23MBa> x = CreateParityValue(client, G.R23MC8r.CreateWritable); return x.F<G.R23MD1>(); }))
                .SetName("GeneratedBridge_C8rGeneric");
            yield return new TestCaseData("R23OrderSlots", 5, "6", null,
                (Func<NeoClient, object?>)(client => { G.R23MI<G.R23MBa> x = CreateParityValue(client, G.R23MC8r.CreateWritable); x.P = 0; return G.R23MRec.S; }))
                .SetName("GeneratedBridge_C8rSet");
            yield return new TestCaseData("R23OrderSlots", 6, "8", null,
                (Func<NeoClient, object?>)(client => { G.R23MI<G.R23MBa> x = CreateParityValue(client, G.R23MC8d.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_C8dGet");
            yield return new TestCaseData("R23OrderSlots", 7, "8", null,
                (Func<NeoClient, object?>)(client => { G.R23MI<G.R23MBa> x = CreateParityValue(client, G.R23MC8d.CreateWritable); return x.F<G.R23MD1>(); }))
                .SetName("GeneratedBridge_C8dGeneric");
            yield return new TestCaseData("R23OrderSlots", 8, "8", null,
                (Func<NeoClient, object?>)(client => { G.R23MI<G.R23MBa> x = CreateParityValue(client, G.R23MC8d.CreateWritable); x.P = 0; return G.R23MRec.S; }))
                .SetName("GeneratedBridge_C8dSet");
            yield return new TestCaseData("R23OrderSlots", 9, "6", null,
                (Func<NeoClient, object?>)(client => { G.R23MI<G.R23MBa> x = CreateParityValue(client, G.R23MC1.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_C1Get");
            yield return new TestCaseData("R23OrderSlots", 10, "6", null,
                (Func<NeoClient, object?>)(client => { G.R23MI<G.R23MBa> x = CreateParityValue(client, G.R23MC1.CreateWritable); return x.F<G.R23MD1>(); }))
                .SetName("GeneratedBridge_C1Generic");
            yield return new TestCaseData("R23OrderSlots", 11, "6", null,
                (Func<NeoClient, object?>)(client => { G.R23MI<G.R23MBa> x = CreateParityValue(client, G.R23MC1.CreateWritable); x.P = 0; return G.R23MRec.S; }))
                .SetName("GeneratedBridge_C1Set");
            yield return new TestCaseData("R23OrderSlots", 12, "6", null,
                (Func<NeoClient, object?>)(client => { G.R23MI<G.R23MBa> x = CreateParityValue(client, G.R23MC2.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_C2Get");
            yield return new TestCaseData("R23OrderSlots", 13, "6", null,
                (Func<NeoClient, object?>)(client => { G.R23MI<G.R23MBa> x = CreateParityValue(client, G.R23MC2.CreateWritable); return x.F<G.R23MD1>(); }))
                .SetName("GeneratedBridge_C2Generic");
            yield return new TestCaseData("R23OrderSlots", 14, "6", null,
                (Func<NeoClient, object?>)(client => { G.R23MI<G.R23MBa> x = CreateParityValue(client, G.R23MC2.CreateWritable); x.P = 0; return G.R23MRec.S; }))
                .SetName("GeneratedBridge_C2Set");
            yield return new TestCaseData("R23OrderSlots", 15, "6", null,
                (Func<NeoClient, object?>)(client => { G.R23MI<G.R23MBa> x = CreateParityValue(client, G.R23MC6.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_C6Get");
            yield return new TestCaseData("R23OrderSlots", 16, "6", null,
                (Func<NeoClient, object?>)(client => { G.R23MI<G.R23MBa> x = CreateParityValue(client, G.R23MC6.CreateWritable); return x.F<G.R23MD1>(); }))
                .SetName("GeneratedBridge_C6Generic");
            yield return new TestCaseData("R23OrderSlots", 17, "6", null,
                (Func<NeoClient, object?>)(client => { G.R23MI<G.R23MBa> x = CreateParityValue(client, G.R23MC6.CreateWritable); x.P = 0; return G.R23MRec.S; }))
                .SetName("GeneratedBridge_C6Set");
            yield return new TestCaseData("R23OrderSlots", 18, "8", "6",
                (Func<NeoClient, object?>)(client => { G.R23MI<G.R23MBa> x = CreateParityValue(client, G.R23MP1.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_P1Get_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R23OrderSlots", 19, "8", "6",
                (Func<NeoClient, object?>)(client => { G.R23MI<G.R23MBa> x = CreateParityValue(client, G.R23MP1.CreateWritable); return x.F<G.R23MD1>(); }))
                .SetName("GeneratedBridge_P1Generic_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R23OrderSlots", 20, "8", "6",
                (Func<NeoClient, object?>)(client => { G.R23MI<G.R23MBa> x = CreateParityValue(client, G.R23MP1.CreateWritable); x.P = 0; return G.R23MRec.S; }))
                .SetName("GeneratedBridge_P1Set_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R23OrderSlots", 21, "8", "6",
                (Func<NeoClient, object?>)(client => { G.R23MI<G.R23MBa> x = CreateParityValue(client, G.R23MP2.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_P2Get_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R23OrderSlots", 22, "8", "6",
                (Func<NeoClient, object?>)(client => { G.R23MI<G.R23MBa> x = CreateParityValue(client, G.R23MP2.CreateWritable); return x.F<G.R23MD1>(); }))
                .SetName("GeneratedBridge_P2Generic_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R23OrderSlots", 23, "8", "6",
                (Func<NeoClient, object?>)(client => { G.R23MI<G.R23MBa> x = CreateParityValue(client, G.R23MP2.CreateWritable); x.P = 0; return G.R23MRec.S; }))
                .SetName("GeneratedBridge_P2Set_Mono_2488")
                .SetDescription("Mono variant selection; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R23OrderSlots", 24, "1", null,
                (Func<NeoClient, object?>)(client => { G.R23EJ<G.R23EBa> x = CreateParityValue(client, G.R23EE.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_E");
            yield return new TestCaseData("R23OrderSlots", 25, "2", null,
                (Func<NeoClient, object?>)(client => { G.R23EJ<G.R23EBa> x = CreateParityValue(client, G.R23EE2.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_E2");
            yield return new TestCaseData("R23OrderSlots", 26, "1", null,
                (Func<NeoClient, object?>)(client => { G.R23EJ<G.R23EBa> x = CreateParityValue(client, G.R23EE3.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_E3");
            yield return new TestCaseData("R23OrderSlots", 27, "1", null,
                (Func<NeoClient, object?>)(client => { G.R23EJ<G.R23EBa> x = CreateParityValue(client, G.R23EE9.CreateWritable); return x.V; }))
                .SetName("GeneratedBridge_E9");
            yield return new TestCaseData("R22ExactSlots", 0, "AR22D1", null,
                (Func<NeoClient, object?>)(client => { try { G.IR22YI<G.R22Ba> x = CreateParityValue(client, G.R22YC0<G.R22Ba, G.R22Ba>.CreateWritable); return x.V; } catch (System.Runtime.AmbiguousImplementationException) { return "ambiguous"; } }))
                .SetName("GeneratedBridge_R22YC0Get");
            yield return new TestCaseData("R22ExactSlots", 1, "AR22D1", null,
                (Func<NeoClient, object?>)(client => { try { G.IR22YI<G.R22Ba> x = CreateParityValue(client, G.R22YC0<G.R22Ba, G.R22Ba>.CreateWritable); return x.M(); } catch (System.Runtime.AmbiguousImplementationException) { return "ambiguous"; } }))
                .SetName("GeneratedBridge_R22YC0Method");
            yield return new TestCaseData("R22ExactSlots", 2, "AR22D1", null,
                (Func<NeoClient, object?>)(client => { try { G.IR22YI<G.R22Ba> x = CreateParityValue(client, G.R22YC0<G.R22Ba, G.R22Ba>.CreateWritable); return x.G<int>(); } catch (System.Runtime.AmbiguousImplementationException) { return "ambiguous"; } }))
                .SetName("GeneratedBridge_R22YC0Generic");
            yield return new TestCaseData("R22ExactSlots", 3, "AR22D1", null,
                (Func<NeoClient, object?>)(client => { try { G.IR22YI<G.R22Ba> x = CreateParityValue(client, G.R22YC0<G.R22Ba, G.R22Ba>.CreateWritable); x.P = ""; return G.R22YRec.S; } catch (System.Runtime.AmbiguousImplementationException) { return "ambiguous"; } }))
                .SetName("GeneratedBridge_R22YC0Set");
            yield return new TestCaseData("R22ExactSlots", 5, "AR22D1", null,
                (Func<NeoClient, object?>)(client => { try { G.IR22YI<G.R22Ba> x = CreateParityValue(client, G.R22YC1<G.R22Ba, G.R22Ba>.CreateWritable); return x.V; } catch (System.Runtime.AmbiguousImplementationException) { return "ambiguous"; } }))
                .SetName("GeneratedBridge_R22YC1Get");
            yield return new TestCaseData("R22ExactSlots", 6, "AR22D1", null,
                (Func<NeoClient, object?>)(client => { try { G.IR22YI<G.R22Ba> x = CreateParityValue(client, G.R22YC1<G.R22Ba, G.R22Ba>.CreateWritable); return x.M(); } catch (System.Runtime.AmbiguousImplementationException) { return "ambiguous"; } }))
                .SetName("GeneratedBridge_R22YC1Method");
            yield return new TestCaseData("R22ExactSlots", 7, "AR22D1", null,
                (Func<NeoClient, object?>)(client => { try { G.IR22YI<G.R22Ba> x = CreateParityValue(client, G.R22YC1<G.R22Ba, G.R22Ba>.CreateWritable); return x.G<int>(); } catch (System.Runtime.AmbiguousImplementationException) { return "ambiguous"; } }))
                .SetName("GeneratedBridge_R22YC1Generic");
            yield return new TestCaseData("R22ExactSlots", 8, "AR22D1", null,
                (Func<NeoClient, object?>)(client => { try { G.IR22YI<G.R22Ba> x = CreateParityValue(client, G.R22YC1<G.R22Ba, G.R22Ba>.CreateWritable); x.P = ""; return G.R22YRec.S; } catch (System.Runtime.AmbiguousImplementationException) { return "ambiguous"; } }))
                .SetName("GeneratedBridge_R22YC1Set");
            yield return new TestCaseData("R22ExactSlots", 10, "ambiguous", null,
                (Func<NeoClient, object?>)(client => { try { G.IR22YI<G.R22Ba> x = CreateParityValue(client, G.R22YC2<G.R22Ba, G.R22Ba>.CreateWritable); return x.V; } catch (System.Runtime.AmbiguousImplementationException) { return "ambiguous"; } }))
                .SetName("GeneratedBridge_R22YC2Get");
            yield return new TestCaseData("R22ExactSlots", 11, "ambiguous", null,
                (Func<NeoClient, object?>)(client => { try { G.IR22YI<G.R22Ba> x = CreateParityValue(client, G.R22YC2<G.R22Ba, G.R22Ba>.CreateWritable); return x.M(); } catch (System.Runtime.AmbiguousImplementationException) { return "ambiguous"; } }))
                .SetName("GeneratedBridge_R22YC2Method");
            yield return new TestCaseData("R22ExactSlots", 12, "ambiguous", null,
                (Func<NeoClient, object?>)(client => { try { G.IR22YI<G.R22Ba> x = CreateParityValue(client, G.R22YC2<G.R22Ba, G.R22Ba>.CreateWritable); return x.G<int>(); } catch (System.Runtime.AmbiguousImplementationException) { return "ambiguous"; } }))
                .SetName("GeneratedBridge_R22YC2Generic");
            yield return new TestCaseData("R22ExactSlots", 13, "ambiguous", null,
                (Func<NeoClient, object?>)(client => { try { G.IR22YI<G.R22Ba> x = CreateParityValue(client, G.R22YC2<G.R22Ba, G.R22Ba>.CreateWritable); x.P = ""; return G.R22YRec.S; } catch (System.Runtime.AmbiguousImplementationException) { return "ambiguous"; } }))
                .SetName("GeneratedBridge_R22YC2Set");
            yield return new TestCaseData("R22ExactSlots", 15, "J", null,
                (Func<NeoClient, object?>)(client => { try { G.IR22YJ<G.R22Ba> x = CreateParityValue(client, G.R22YE.CreateWritable); return x.V; } catch (System.Runtime.AmbiguousImplementationException) { return "ambiguous"; } }))
                .SetName("GeneratedBridge_R22YE");
            yield return new TestCaseData("R22ExactSlots", 16, "KR22Ba", null,
                (Func<NeoClient, object?>)(client => { try { G.IR22YJ<G.R22Ba> x = CreateParityValue(client, G.R22YO.CreateWritable); return x.V; } catch (System.Runtime.AmbiguousImplementationException) { return "ambiguous"; } }))
                .SetName("GeneratedBridge_R22YO");
            yield return new TestCaseData("R22ExactSlots", 17, "LR22D1", "ambiguous",
                (Func<NeoClient, object?>)(client => { try { G.IR22YJ<G.R22Ba> x = CreateParityValue(client, G.R22YX1<G.R22Ba, G.R22Ba>.CreateWritable); return x.V; } catch (System.Runtime.AmbiguousImplementationException) { return "ambiguous"; } }))
                .SetName("GeneratedBridge_R22YX1_Mono_2488")
                .SetDescription("Mono throws before entering a body; #2488: https://github.com/ryanbliss/neo-compose/issues/2488");
            yield return new TestCaseData("R22ExactSlots", 18, "LR22D1", null,
                (Func<NeoClient, object?>)(client => { try { G.IR22YJ<G.R22Ba> x = CreateParityValue(client, G.R22YX3<G.R22Ba, G.R22Ba>.CreateWritable); return x.V; } catch (System.Runtime.AmbiguousImplementationException) { return "ambiguous"; } }))
                .SetName("GeneratedBridge_R22YX3");
            yield return new TestCaseData("R22ExactSlots", 19, "ambiguous", null,
                (Func<NeoClient, object?>)(client => { try { G.IR22YJ<G.R22Ba> x = CreateParityValue(client, G.R22YX5<G.R22Ba, G.R22Ba>.CreateWritable); return x.V; } catch (System.Runtime.AmbiguousImplementationException) { return "ambiguous"; } }))
                .SetName("GeneratedBridge_R22YX5");
            yield return new TestCaseData("R22ExactSlots", 20, "LR22Ba", null,
                (Func<NeoClient, object?>)(client => { try { G.IR22YJ<G.R22Ba> x = CreateParityValue(client, G.R22YX6.CreateWritable); return x.V; } catch (System.Runtime.AmbiguousImplementationException) { return "ambiguous"; } }))
                .SetName("GeneratedBridge_R22YX6");
            yield return new TestCaseData("R22ExactSlots", 21, "KR22D11", null,
                (Func<NeoClient, object?>)(client => { try { G.IR22YJ<G.R22Ba> x = CreateParityValue(client, G.R22YT2<G.R22Ba, G.R22Ba, G.R22Ba>.CreateWritable); return x.V; } catch (System.Runtime.AmbiguousImplementationException) { return "ambiguous"; } }))
                .SetName("GeneratedBridge_R22YT2");
            yield return new TestCaseData("R22ExactSlots", 22, "KR22D11", null,
                (Func<NeoClient, object?>)(client => { try { G.IR22YJ<G.R22Ba> x = CreateParityValue(client, G.R22YT6<G.R22Ba, G.R22Ba, G.R22Ba>.CreateWritable); return x.V; } catch (System.Runtime.AmbiguousImplementationException) { return "ambiguous"; } }))
                .SetName("GeneratedBridge_R22YT6");
            yield return new TestCaseData("R22ExactSlots", 23, "B", null,
                (Func<NeoClient, object?>)(client => { try { G.IR22YJ<G.R22Ba> x = CreateParityValue(client, G.R22YR0.CreateWritable); return x.V; } catch (System.Runtime.AmbiguousImplementationException) { return "ambiguous"; } }))
                .SetName("GeneratedBridge_R22YR0");
            yield return new TestCaseData("R22ExactSlots", 24, "ambiguous", null,
                (Func<NeoClient, object?>)(client => { try { G.IR22YJ<G.R22Ba> x = CreateParityValue(client, G.R22YN1<G.R22Ba, G.R22Ba>.CreateWritable); return x.V; } catch (System.Runtime.AmbiguousImplementationException) { return "ambiguous"; } }))
                .SetName("GeneratedBridge_R22YN1");
            yield return new TestCaseData("R22ExactSlots", 25, "CR22Ba", null,
                (Func<NeoClient, object?>)(client => { try { G.IR22YJ<G.R22Ba> x = CreateParityValue(client, G.R22YV2<G.R22Ba, G.R22Ba>.CreateWritable); return x.V; } catch (System.Runtime.AmbiguousImplementationException) { return "ambiguous"; } }))
                .SetName("GeneratedBridge_R22YV2");
            yield return new TestCaseData("R22ExactSlots", 26, "KR22D1", null,
                (Func<NeoClient, object?>)(client => { try { G.IR22YJ<G.R22Ba> x = CreateParityValue(client, G.R22YDp<G.R22Ba, G.R22Ba>.CreateWritable); return x.V; } catch (System.Runtime.AmbiguousImplementationException) { return "ambiguous"; } }))
                .SetName("GeneratedBridge_R22YDp");
            yield return new TestCaseData("R22ExactSlots", 27, "KR22Ba", null,
                (Func<NeoClient, object?>)(client => { try { G.IR22YJ<G.R22Ba> x = CreateParityValue(client, G.R22YW1<G.R22Ba, G.R22D1>.CreateWritable); return x.V; } catch (System.Runtime.AmbiguousImplementationException) { return "ambiguous"; } }))
                .SetName("GeneratedBridge_R22YW1");
            yield return new TestCaseData("R23SXM", -1, "5", null,
                (Func<NeoClient, object?>)(client => { return G.R23SXM.S(); }))
                .SetName("GeneratedBridge_R23SXMClassStatic");
            yield return new TestCaseData("R24BSM", -1, "9", null,
                (Func<NeoClient, object?>)(client => { return G.R24BSM.S(); }))
                .SetName("GeneratedBridge_R24BSMClassStatic");
            yield return new TestCaseData("R24BSN", -1, "7", null,
                (Func<NeoClient, object?>)(client => { return G.R24BSN.S(); }))
                .SetName("GeneratedBridge_R24BSNClassStatic");
        }

        [TestCaseSource(nameof(HistoricalGeneratedCases))]
        public void GeneratedHistoricalBridgeMatchesFixture(string group, int slot, string neoExpected, string? monoExpected, Func<NeoClient, object?> invoke)
        {
            var fixture = JObject.Parse(File.ReadAllText(Root + "p127-interfaces-parity-fixture.json"));
            if (slot >= 0)
                Assert.That(fixture["returns"]![group]!.Value<string>()!.Split(',')[slot], Is.EqualTo(neoExpected), "Shared fixture pin changed");
            using var client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText(Export)), assumeCurrentSchema: false);
            using var project = new G.TestProjectNeo(client);
            if (slot < 0)
            {
                var owner = client.ProjectDataForRuntime.classes.Values.Single(type => type.name == group);
                object? evaluated = NeoMemberNSFunction.GetOrCreateStatic(client, owner.schema["S"]).InvokeStatic(Array.Empty<object?>());
                Assert.That(Convert.ToString(evaluated, System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(neoExpected), "Direct class static body");
            }
            object? value;
            try
            {
                value = invoke(client);
            }
            catch (System.Runtime.AmbiguousImplementationException) when ((monoExpected ?? neoExpected) == "ambiguous")
            {
                value = "ambiguous";
            }
            string? actual = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.That(actual, Is.EqualTo(monoExpected ?? neoExpected), monoExpected is null ? group : "Pinned Mono behavior: " + MonoDispatchIssue);
        }
    }
}
