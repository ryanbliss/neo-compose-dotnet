// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class NeoInterfaceResolutionTests
    {
        [Test]
        public void ResolveClosure_DeduplicatesDiamondInDeclaredOrder()
        {
            ProjectData data = Data(
                Interface("root"),
                Interface("left", "root"),
                Interface("right", "root"),
                Interface("leaf", "left", "right"));

            IReadOnlyList<Interface> closure =
                NeoInterfaceResolution.ResolveClosure("leaf", data);

            CollectionAssert.AreEqual(
                new[] { "leaf", "left", "root", "right" },
                Ids(closure));
        }

        [Test]
        public void ClassImplements_UsesClassAndInterfaceInheritance()
        {
            ProjectData data = Data(
                Interface("entity"),
                Interface("damageable", "entity"));
            data.classes["base"] = Class("base", "damageable");
            data.classes["child"] = Class("child", extendsClassId: "base");

            Assert.IsTrue(
                NeoInterfaceResolution.ClassImplements("child", "damageable", data));
            Assert.IsTrue(
                NeoInterfaceResolution.ClassImplements("child", "entity", data));
            Assert.IsFalse(
                NeoInterfaceResolution.ClassImplements("child", "missing", data));
        }

        [Test]
        public void ResolveClosure_CycleThrowsAndClassCheckFailsClosed()
        {
            ProjectData data = Data(
                Interface("left", "right"),
                Interface("right", "left"));
            data.classes["class"] = Class("class", "left");

            Assert.Throws<System.InvalidOperationException>(() =>
                NeoInterfaceResolution.ResolveClosure("left", data));
            Assert.IsFalse(
                NeoInterfaceResolution.ClassImplements("class", "left", data));
        }

        private static ProjectData Data(params Interface[] interfaces)
        {
            var data = new ProjectData
            {
                interfaces = new Dictionary<string, Interface>(),
                classes = new Dictionary<string, NeoSchemaClass>(),
            };
            foreach (Interface declaration in interfaces)
            {
                data.interfaces[declaration.id] = declaration;
            }
            return data;
        }

        private static Interface Interface(string id, params string[] parents)
        {
            return new Interface
            {
                id = id,
                projectId = "project",
                name = id,
                members = new Dictionary<string, InterfaceMember>(),
                extendsInterfaceIds = new List<string>(parents),
            };
        }

        private static NeoSchemaClass Class(
            string id,
            string? implementsInterfaceId = null,
            string? extendsClassId = null)
        {
            return new NeoSchemaClass
            {
                id = id,
                projectId = "project",
                name = id,
                schema = new Dictionary<string, string>(),
                extendsClassId = extendsClassId,
                implementsInterfaceIds = implementsInterfaceId is null
                    ? null
                    : new List<string> { implementsInterfaceId },
            };
        }

        private static string[] Ids(IReadOnlyList<Interface> declarations)
        {
            var ids = new string[declarations.Count];
            for (int index = 0; index < declarations.Count; index += 1)
            {
                ids[index] = declarations[index].id;
            }
            return ids;
        }
    }
}
