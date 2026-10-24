// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>Verifies structured field validation across runtimes.</summary>
    public class NeoStructuredFieldWriteTests
    {
        private const string PackageRoot = "Packages/com.ryanbliss.neocompose/Tests";

        [TestCase("Endpoints")]
        [TestCase("FiniteVectors")]
        [TestCase("DictionaryFields")]
        [TestCase("AtomicColor")]
        [TestCase("AtomicVector")]
        [TestCase("CompoundEvaluatesOnce")]
        [TestCase("IntegerVectors")]
        [TestCase("ScalarNaNAllowed")]
        [TestCase("ParameterAtomic")]
        public void Value(string probe)
        {
            using NeoClient client = Client();
            Assert.AreEqual(true, InvokeProbe(client, probe));
        }

        [TestCase("StoredColorNaN", "Colour channel 'r' must be a finite number.")]
        [TestCase("LocalColorHigh", "Colour channel 'g' must be within [0, 1]")]
        [TestCase("StoredColorLow", "Colour channel 'b' must be within [0, 1]")]
        [TestCase("ColorInfinity", "Colour channel 'a' must be a finite number.")]
        [TestCase("StoredVectorNaN", "Vector field 'y' must be a finite number.")]
        [TestCase("LocalVectorInfinity", "Vector field 'x' must be a finite number.")]
        [TestCase("CompoundColor", "Colour channel 'r' must be within [0, 1]")]
        public void Refusal(string probe, string message)
        {
            using NeoClient client = Client();
            NSGetterRuntimeError error = Assert.Catch<NSGetterRuntimeError>(() => InvokeProbe(client, probe))!;
            StringAssert.Contains(message, error.Message);
            Assert.AreEqual(NeoErrorContract.NeoInvalidOperationError, error.Payload.ClassId);
        }

        private static NeoClient Client()
        {
            ProjectData data = NeoTestExport.Read(File.ReadAllText(PackageRoot + "/structured-field-write-export.json"));
            return NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false);
        }

        private static object? InvokeProbe(NeoClient client, string name)
        {
            Member function = client.members.Values.Single(member =>
                member is NSFunctionMember && member.name == name);
            return NeoMemberNSFunction.GetOrCreateStatic(client, function.id)
                .InvokeStatic(Array.Empty<object?>());
        }
    }
}
