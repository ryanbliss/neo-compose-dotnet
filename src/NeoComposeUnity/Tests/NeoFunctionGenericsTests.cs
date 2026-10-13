// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Assets.Scripts.Neo;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P110 function generics, run through the generated synth wrappers over
    /// bodies the web compiler emitted (scripts/dump-synth-export.ts).
    /// </summary>
    public class NeoFunctionGenericsTests
    {
        private const string PackageRoot = "Packages/com.ryanbliss.neocompose/Tests";

        private static TestProjectNeo LoadApp()
        {
            var stack = NeoTestSaveStack.Create(File.ReadAllText(Path.Combine(PackageRoot, "synth-example.json")));
            return new TestProjectNeo(NeoTestSaveStack.LoadSynchronously(stack.Synchronizer));
        }

        [Test]
        public void IsT_ChecksTheTypeArgument()
        {
            LoadApp();
            var probe = new GenericProbe();
            var dog = probe.MakeDog();
            var cat = probe.MakeCat();

            Assert.IsTrue(probe.IsA<IReadOnlyGenericDog>(dog));
            Assert.IsFalse(probe.IsA<IReadOnlyGenericDog>(cat));
            Assert.IsTrue(probe.IsA<IReadOnlyGenericAnimal>(cat));
            Assert.IsTrue(probe.IsA<GenericDog>(dog));
        }

        [Test]
        public void ForwardedTypeArguments_ResolveThroughTwoCalls()
        {
            LoadApp();
            var probe = new GenericProbe();

            Assert.IsTrue(probe.Outer<IReadOnlyGenericDog>(probe.MakeDog()));
            Assert.IsFalse(probe.Outer<IReadOnlyGenericDog>(probe.MakeCat()));
        }

        [Test]
        public void OmittedTypeArgument_BindsItsDefault()
        {
            LoadApp();
            var probe = new GenericProbe();

            Assert.IsTrue(probe.IsDefault(probe.MakeDog()));
            Assert.IsFalse(probe.IsDefault(probe.MakeCat()));
            Assert.IsTrue(probe.IsDefault<IReadOnlyGenericCat>(probe.MakeCat()));
        }

        [Test]
        public void Override_ThatRenamedItsParameter_BindsByPosition()
        {
            LoadApp();
            var probe = new GenericProbe();
            var checker = probe.MakeDogChecker();
            IGenericCheck contract = checker;

            Assert.IsTrue(checker.Check<IReadOnlyGenericDog>(probe.MakeDog()));
            Assert.IsFalse(checker.Check<IReadOnlyGenericCat>(probe.MakeDog()));
            Assert.IsTrue(contract.Check<IReadOnlyGenericDog>(probe.MakeDog()));
        }

        [Test]
        public void ListOfT_KeepsOnlyMatchesInOrder()
        {
            LoadApp();
            var probe = new GenericProbe();
            var first = probe.MakeDog();
            var last = probe.MakeDog();
            var animals = new[] { first, probe.MakeCat(), last };

            IReadOnlyList<IReadOnlyGenericDog> dogs = probe.OnlyOf<IReadOnlyGenericDog>(animals);

            Assert.AreEqual(2, dogs.Count);
            Assert.AreEqual(first.valueId, dogs[0].valueId);
            Assert.AreEqual(last.valueId, dogs[1].valueId);
            Assert.AreEqual(1, probe.OnlyOf<IReadOnlyGenericCat>(animals).Count);
        }

        [Test]
        public void Closure_KeepsTheTypeArgumentsOfTheFrameThatMadeIt()
        {
            LoadApp();
            var probe = new GenericProbe();

            Assert.IsTrue(probe.ClosureCheck<IReadOnlyGenericDog>(probe.MakeDog()));
            Assert.IsFalse(probe.ClosureCheck<IReadOnlyGenericDog>(probe.MakeCat()));
        }

        [Test]
        public void GenericNative_RoundTripsAClassTypeArgumentThroughItsHandler()
        {
            LoadApp();
            var probe = new GenericProbe();
            var handler = new PickHandler();
            probe.FunctionHandler = handler;
            var dog = probe.MakeDog();
            var animals = new[] { probe.MakeCat(), dog };

            IReadOnlyGenericDog? picked = probe.PickVia<IReadOnlyGenericDog>(animals);

            Assert.AreEqual(typeof(IReadOnlyGenericDog), handler.LastTypeArgument);
            Assert.AreEqual(dog.valueId, picked?.valueId);
        }

        [Test]
        public void TypeArgumentsOf_MapsGeneratedAndBuiltInTypes()
        {
            var app = LoadApp();
            var typeArguments = app.Client.TypeArguments;

            Assert.AreEqual("class-generic-dog", ((ClassTypeInfo)typeArguments.Of<IReadOnlyGenericDog>()).classId);
            Assert.AreEqual("class-generic-dog", ((ClassTypeInfo)typeArguments.Of<GenericDog>()).classId);
            Assert.AreEqual(MemberKind.Int, typeArguments.Of<int>().type);
            var list = (CollectionTypeInfo)typeArguments.Of<IReadOnlyList<IReadOnlyGenericCat>>();
            Assert.AreEqual(MemberKind.List, list.type);
            Assert.AreEqual("class-generic-cat", ((ClassTypeInfo)list.entryTypeInfo).classId);
            Assert.AreSame(typeArguments.Of<GenericDog>(), typeArguments.Of<GenericDog>());
        }

        [Test]
        public void TypeArgumentsOf_NamesAnUnmappedType()
        {
            var app = LoadApp();

            var error = Assert.Throws<NeoTypeArgumentException>(() => app.Client.TypeArguments.Of<Uri>());

            StringAssert.Contains("System.Uri", error.Message);
        }

        [Test]
        public void ReadTypeArgument_NamesTheRowThatFailedTheTypeArgument()
        {
            var app = LoadApp();
            var cat = new GenericProbe().MakeCat();

            var error = Assert.Throws<NeoTypeArgumentException>(() => NeoGeneratedTypesSupport.ReadTypeArgument<IReadOnlyGenericDog>(
                app.Client,
                cat,
                "T",
                TestProjectNeo.NeoReadOnlyValueFactories,
                TestProjectNeo.NeoWritableValueFactories));

            StringAssert.Contains($"Row '{cat.valueId}'", error.Message);
            StringAssert.Contains("'T'", error.Message);
        }

        [Test]
        public void CallTable_IsNullForANonGenericCallee_AndReusedWhenFullyWritten()
        {
            var written = new TypeInfo[] { new ClassTypeInfo { type = MemberKind.Class, classId = "class-generic-dog", required = true } };
            var parameters = new[] { new FunctionGenericParam { name = "T" } };

            Assert.IsNull(NeoFunctionTypeArguments.ForCall(null, false, null, "Plain", null));
            Assert.AreSame(written, NeoFunctionTypeArguments.ForCall(written, false, parameters, "IsA", null));
        }

        private sealed class PickHandler : IGenericProbeFunctionHandler
        {
            public Type? LastTypeArgument
            {
                get; private set;
            }

            public T? Pick<T>(IReadOnlyList<IReadOnlyGenericAnimal> animals) where T : class, IReadOnlyGenericAnimal
            {
                LastTypeArgument = typeof(T);
                return animals.OfType<T>().FirstOrDefault();
            }
        }
    }
}
