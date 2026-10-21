// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class FieldListAliasTests
    {
        private const string FixtureFile = "field-list-alias-parity.json";

        [Test]
        public void HandleReadBeforeBindingKeepsItsStoredRow()
        {
            // P112 keeps an existing SDK field handle on its stable row.
            // TypeScript retains the original array instead; this is an SDK control.
            NeoSourceParityFixture.AssertTrueCase(FixtureFile, "RowPreHandleFollow9");
        }

        [Test]
        public void NullAssignmentKeepsAnUnboundStaticUnbound()
        {
            NeoStaticBinding? binding = null;
            NeoSourceParityFixture.AssertTrueCase(FixtureFile, "StaticNullAssign",
                before: client =>
                {
                    string id = client.members.Values.Single(member => member.name == "OptionalItems").id;
                    binding = new NeoStaticBinding(client, id);
                    binding.Clear();
                    Assert.IsNull(binding.ValueId);
                },
                after: _ => Assert.IsNull(binding!.ValueId));
        }

        public static IEnumerable<string> Cases() => NeoSourceParityFixture.Cases(FixtureFile);

        [TestCaseSource(nameof(Cases))]
        public void CompiledFieldListAliasesMatchCli(string name) => NeoSourceParityFixture.AssertTrueCase(FixtureFile, name);
    }
}
