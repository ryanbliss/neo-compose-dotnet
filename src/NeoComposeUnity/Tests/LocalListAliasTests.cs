// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System.Collections.Generic;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class LocalListAliasTests
    {
        private const string FixtureFile = "local-list-alias-parity.json";

        public static IEnumerable<string> Cases() => NeoSourceParityFixture.Cases(FixtureFile);

        [TestCaseSource(nameof(Cases))]
        public void CompiledListAliasesPreserveIdentity(string name) => NeoSourceParityFixture.AssertTrueCase(FixtureFile, name);
    }
}
