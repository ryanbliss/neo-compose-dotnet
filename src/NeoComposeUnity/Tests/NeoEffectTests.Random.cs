// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
namespace NeoCompose.Tests
{
    public partial class NeoEffectTests
    {
        [Test]
        public void AuthoredEffectsContinueTheClientRandomStream()
        {
            var ir = JObject.Parse(NeoScriptRandomIrFixture.Json)["rangeInt"]!.ToObject<FunctionWithReturnType>()!;
            // Keep the compiler's locals and Range pointer; store the result in the fixture's Runs field.
            var result = (ReturnInstruction)ir.instructions[^1];
            using Fixture fixture = Build(plants: 1, configure: data =>
            {
                var effect = ((NSFunctionMember)data.members["member-check"]).action!;
                var instructions = new Instruction[ir.instructions.Length];
                System.Array.Copy(ir.instructions, instructions, instructions.Length);
                instructions[^1] = Assign(KeyOf(This(), "Runs"), result.pointer);
                effect.instructions = instructions;
            });
            fixture.client.Random.InitState(17);
            var baseline = new NeoRandom(17);
            fixture.client.StartScriptRuntime();
            Assert.AreEqual(baseline.Range(int.MinValue, int.MaxValue), fixture.RunsOf("plant-0"));
            Assert.AreEqual(baseline.value, fixture.client.Random.value);
        }
    }
}
