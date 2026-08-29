// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;
using UnityEngine;

namespace NeoCompose.Tests
{
    public sealed class NeoTileGridAuthoringBindingTests
    {
        private GameObject? root;
        private NeoClient? client;

        [TearDown]
        public void TearDown()
        {
            if (root != null)
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
            client?.Dispose();
        }

        [Test]
        public void PreviewResultExposesTheRenderedBorrowedState()
        {
            root = new GameObject("Authoring Binding Test");
            var renderer = root.AddComponent<NeoTileGridRenderer>();
            var content = CreateContent(out client);

            var result = new NeoTileGridPreviewResult("grid-value", renderer, content);

            Assert.AreEqual("grid-value", result.ValueId);
            Assert.AreSame(renderer, result.Renderer);
            Assert.AreSame(content, result.Content);
        }

        [Test]
        public void CallerCancellationPreventsRefreshFromCreatingRenderer()
        {
            root = new GameObject("Authoring Binding Test");
            root.SetActive(false);
            var binding = root.AddComponent<NeoTileGridAuthoringBinding>();
            binding.refreshOnEnable = false;
            binding.valueId = "grid-value";
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                binding.RefreshPreviewAsync(cancellation.Token).GetAwaiter().GetResult());
            Assert.IsNull(root.GetComponent<NeoTileGridRenderer>());
        }

        [Test]
        public void ClearPreviewStopsLiveSyncBeforeDisposalAndIsIdempotent()
        {
            root = new GameObject("Authoring Binding Test");
            root.SetActive(false);
            var renderer = root.AddComponent<NeoTileGridRenderer>();
            var binding = root.AddComponent<NeoTileGridAuthoringBinding>();
            binding.refreshOnEnable = false;
            binding.renderer = renderer;
            var content = CreateContent(out client);
            renderer.Render(content);
            Assert.IsTrue(renderer.IsLiveSynced);

            var disposal = new RecordingDisposable(() =>
                Assert.IsFalse(
                    renderer.IsLiveSynced,
                    "Renderer live sync must stop before the preview project is disposed."));
            SetPrivateField(binding, "previewProject", disposal);
            SetPrivateField(
                binding,
                "currentPreview",
                new NeoTileGridPreviewResult("grid-value", renderer, content));

            binding.ClearPreview();
            binding.ClearPreview();

            Assert.AreEqual(1, disposal.DisposeCalls);
            Assert.IsFalse(renderer.IsLiveSynced);
            Assert.IsNull(renderer.CurrentContent);
            Assert.IsNull(GetPrivateField(binding, "currentPreview"));
        }

        private static TestTileGridContent CreateContent(out NeoClient client)
        {
            const string rootClassId = "root-class";
            var data = new ProjectData
            {
                project = new Project
                {
                    id = "project-a",
                    _id = "project-a",
                    name = "Authoring Binding Test",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = new Dictionary<string, Member>
                {
                    ["root-assets"] = RootMember(
                        "root-assets",
                        "root-assets-value",
                        rootClassId),
                    ["root-save"] = RootMember(
                        "root-save",
                        "root-save-value",
                        rootClassId),
                    ["root-session"] = RootMember(
                        "root-session",
                        "root-session-value",
                        rootClassId),
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["root-assets-value"] = RootValue("root-assets-value", rootClassId),
                    ["root-save-value"] = RootValue("root-save-value", rootClassId),
                    ["root-session-value"] = RootValue("root-session-value", rootClassId),
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [rootClassId] = new NeoSchemaClass
                    {
                        id = rootClassId,
                        projectId = "project-a",
                        name = "Root",
                        schema = new Dictionary<string, string>(),
                    },
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
            client = NeoTestSaveStack.ClientFromSchema(data);
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(client, "grid-value");
            return new TestTileGridContent(primitive);
        }

        private static ClassMember RootMember(
            string id,
            string valueId,
            string classId)
        {
            return new ClassMember
            {
                id = id,
                projectId = "project-a",
                name = id,
                kind = MemberKind.Class,
                requirement = NeoMemberRequirementKind.Required,
                valueId = valueId,
                classId = classId,
            };
        }

        private static ObjectMemberValue RootValue(string id, string classId)
        {
            return new ObjectMemberValue
            {
                id = id,
                classId = classId,
                value = new Dictionary<string, string>(),
            };
        }

        private static void SetPrivateField(
            NeoTileGridAuthoringBinding binding,
            string name,
            object value)
        {
            typeof(NeoTileGridAuthoringBinding)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(binding, value);
        }

        private static object? GetPrivateField(
            NeoTileGridAuthoringBinding binding,
            string name)
        {
            return typeof(NeoTileGridAuthoringBinding)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(binding);
        }

        private sealed class RecordingDisposable : IDisposable
        {
            private readonly Action onDispose;

            public RecordingDisposable(Action onDispose)
            {
                this.onDispose = onDispose;
            }

            public int DisposeCalls { get; private set; }

            public void Dispose()
            {
                if (DisposeCalls > 0) return;
                DisposeCalls += 1;
                onDispose();
            }
        }

        private sealed class TestTileGridContent : INeoTileGridContent
        {
            public TestTileGridContent(NeoReadOnlyTileGridPrimitive primitive)
            {
                Primitive = primitive;
            }

            public NeoReadOnlyTileGridPrimitive Primitive { get; }
            public IReadOnlyList<IReadOnlyNeoTileLayerRuntime> TileLayersInOrder =>
                Array.Empty<IReadOnlyNeoTileLayerRuntime>();
            public IReadOnlyList<IReadOnlyNeoObjectLayerRuntime> ObjectLayersInOrder =>
                Array.Empty<IReadOnlyNeoObjectLayerRuntime>();
            public NeoTileGridRenderer? Renderer => Primitive.Renderer;
            public IDisposable OnChanged(Action<NeoTileGridChangedArgs> handler) =>
                Primitive.OnChanged(handler);
        }
    }
}
