// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class NeoReferenceGetterContractTests
    {
        [TestCase("NestedGetterRead", 7)]
        [TestCase("NestedGetterIdentity", true)]
        [TestCase("HandleIdentity", true)]
        [TestCase("OptionalReferenceNull", true)]
        [TestCase("OptionalReferenceValue", 7)]
        [TestCase("Check", 1)]
        [TestCase("DerivedRead", 7)]
        [TestCase("BaseRead", 7)]
        [TestCase("ConstructorArguments", true)]
        [TestCase("BaseEquality", true)]
        [TestCase("NotEqual", true)]
        [TestCase("StringRead", "seven")]
        [TestCase("BoolRead", true)]
        [TestCase("ClosedSubclass", 7)]
        [TestCase("OptionalNull", true)]
        [TestCase("OptionalValue", 9)]
        [TestCase("OrdinaryGetter", 7)]
        [TestCase("ReferenceGetterIdentity", true)]
        [TestCase("ReferenceGetterRead", 7)]
        [TestCase("StoredReferenceIdentity", true)]
        [TestCase("StoredReferenceRead", 7)]
        public void ReadsDeclaredContract(string probe, object expected)
            => RunProbe(probe, expected, inheritReturnContract: false);

        [TestCase("SparseScalar", 7)]
        [TestCase("SparseReferenceRead", 7)]
        [TestCase("SparseReferenceIdentity", true)]
        public void ReadsInheritedContract(string probe, object expected)
            => RunProbe(probe, expected, inheritReturnContract: true);

        private static void RunProbe(string probe, object expected, bool inheritReturnContract)
        {
            ProjectData data = NeoTestExport.Read(File.ReadAllText(
                "Packages/com.ryanbliss.neocompose/Tests/reference-getter-contract-export.json"));
            if (inheritReturnContract)
            {
                // Exercise the supported sparse wire shape: the intermediate override
                // inherits its generic return contract from ValueBase<T>.
                var intermediate = data.classes.Values.Single(schema => schema.name == "SparseIntermediate");
                var property = (NSPropertyMember)data.members[intermediate.schema["Value"]];
                Assert.IsNotNull(property.extendsMemberId);
                property.returnTypeInfo = null;
            }
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false,
                localization: NeoLocalization.LoadMain(data.localization, new LocaleSource()));
            Member function = client.members.Values.Single(member =>
                member is NSFunctionMember && member.name == probe);
            object? actual = NeoMemberNSFunction.GetOrCreateStatic(client, function.id)
                .InvokeStatic(Array.Empty<object?>());
            Assert.AreEqual(expected, actual);
        }

        private sealed class LocaleSource : INeoLocalizationLocaleFileSource
        {
            public bool TryLoadResourcesLocale(ProjectLocalizationExport localization, string locale, out ProjectLocalizationLocaleFile? file)
            {
                file = JsonConvert.DeserializeObject<ProjectLocalizationLocaleFile>(File.ReadAllText(
                    "Packages/com.ryanbliss.neocompose/Tests/reference-getter-contract-export.locale.json"));
                return true;
            }

            public Task<ProjectLocalizationLocaleFile?> LoadStreamingAssetsLocaleAsync(
                ProjectLocalizationExport localization, string locale, string streamingAssetsRelativePath)
                => Task.FromResult<ProjectLocalizationLocaleFile?>(null);
        }
    }
}
