// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using NeoCompose.Runtime;
using NeoCompose.Unity.Editor;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace NeoCompose.Tests
{
    internal sealed class FixtureAreaProject
    {
        public List<FixtureAreaTileGrid> Grids { get; set; } = new();

        public FixtureAreaTileGrid RequireGrid(string valueId) =>
            Grids.Single(grid => grid.ValueId == valueId);
    }

    /// <summary>
    /// Test-owned approximation of the generated TileGrid that will eventually
    /// request a navigation-like artifact in a consumer project.
    /// </summary>
    internal sealed partial class FixtureAreaTileGrid
    {
        public string ValueId { get; set; } = "";
        public List<FixtureTileOccupancy> FixtureGroundTileLayer { get; set; } = new();
        public List<FixtureTileOccupancy> FixtureIrregularObstacleTileLayer { get; set; } = new();
        public List<FixtureObstacleObject> FixtureStaticObstacleObjectLayer { get; set; } = new();
        public List<FixtureObstacleObject> FixtureDynamicObstacleObjectLayer { get; set; } = new();

    }

    /// <summary>
    /// Test-owned generated-partial extension: the generated wrapper remains the
    /// discoverable integration surface while the durable handler stays editor-owned.
    /// </summary>
    internal sealed partial class FixtureAreaTileGrid
    {
        public void OnDidSynchronize()
        {
            NeoPostSynchronizeTasks.Request(
                "tests.navigation-artifact",
                ValueId,
                $"Generate navigation-like artifact for {ValueId}");
        }
    }

    internal sealed class FixtureTileOccupancy
    {
        public int X { get; set; }
        public int Y { get; set; }
        public string TileIdentity { get; set; } = "";
        public string CollisionIdentity { get; set; } = "full-cell";
        public string VisualTint { get; set; } = "white";
    }

    internal sealed class FixtureObstacleObject
    {
        public string ObjectIdentity { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public float Rotation { get; set; }
        public float ScaleX { get; set; } = 1f;
        public float ScaleY { get; set; } = 1f;
        public string VisualVariant { get; set; } = "default";
    }

    internal static class FixtureNavigationArtifactHandler
    {
        internal const string Kind = "tests.navigation-artifact";
        internal const string AssetDirectory = "Assets/NeoComposeTests/GeneratedArtifacts";

        internal static bool FailBeforeCommit { get; set; }
        internal static int CommitCount { get; private set; }

        internal static string AssetPathFor(string ownerValueId)
        {
            var safe = new StringBuilder(ownerValueId.Length);
            foreach (char character in ownerValueId)
            {
                safe.Append(char.IsLetterOrDigit(character) || character is '-' or '_'
                    ? character
                    : '_');
            }
            return $"{AssetDirectory}/{safe}.asset";
        }

        internal static Awaitable HandleAsync(
            NeoPostSynchronizeTaskContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fixture = JsonConvert.DeserializeObject<FixtureAreaProject>(
                    File.ReadAllText(context.ProjectJsonPath))
                ?? throw new InvalidOperationException(
                    "Fixture area project JSON could not be deserialized.");
            var grid = fixture.RequireGrid(context.OwnerValueId);
            string contentHash = ComputeContentHash(grid);
            string assetPath = AssetPathFor(context.OwnerValueId);
            var existing = AssetDatabase.LoadAssetAtPath<FixtureNavigationArtifact>(
                assetPath);
            if (existing != null && existing.ContentHash == contentHash)
            {
                return NeoAwaitable.Completed();
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (FailBeforeCommit)
            {
                throw new InvalidOperationException(
                    "Fixture handler failed before committing its generated artifact.");
            }

            EnsureAssetDirectory();
            if (existing == null)
            {
                existing = ScriptableObject.CreateInstance<FixtureNavigationArtifact>();
                existing.OwnerValueId = context.OwnerValueId;
                existing.ContentHash = contentHash;
                existing.Revision = 1;
                AssetDatabase.CreateAsset(existing, assetPath);
            }
            else
            {
                existing.OwnerValueId = context.OwnerValueId;
                existing.ContentHash = contentHash;
                existing.Revision += 1;
                EditorUtility.SetDirty(existing);
            }

            CommitCount += 1;
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(
                assetPath,
                ImportAssetOptions.ForceSynchronousImport);
            return NeoAwaitable.Completed();
        }

        internal static string ComputeContentHash(FixtureAreaTileGrid grid)
        {
            var semantic = new StringBuilder();
            semantic.Append("grid:").Append(grid.ValueId).Append('\n');
            AppendTiles(semantic, "ground", grid.FixtureGroundTileLayer);
            AppendTiles(
                semantic,
                "irregular",
                grid.FixtureIrregularObstacleTileLayer);
            foreach (var obstacle in grid.FixtureStaticObstacleObjectLayer
                         .OrderBy(value => value.ObjectIdentity, StringComparer.Ordinal)
                         .ThenBy(value => value.X)
                         .ThenBy(value => value.Y)
                         .ThenBy(value => value.Rotation)
                         .ThenBy(value => value.ScaleX)
                         .ThenBy(value => value.ScaleY))
            {
                semantic
                    .Append("static:")
                    .Append(obstacle.ObjectIdentity).Append(':')
                    .Append(Float(obstacle.X)).Append(',')
                    .Append(Float(obstacle.Y)).Append(':')
                    .Append(Float(obstacle.Rotation)).Append(':')
                    .Append(Float(obstacle.ScaleX)).Append(',')
                    .Append(Float(obstacle.ScaleY)).Append('\n');
            }

            using var sha = SHA256.Create();
            return BitConverter.ToString(
                    sha.ComputeHash(Encoding.UTF8.GetBytes(semantic.ToString())))
                .Replace("-", "")
                .ToLowerInvariant();
        }

        internal static void Reset()
        {
            FailBeforeCommit = false;
            CommitCount = 0;
        }

        private static void AppendTiles(
            StringBuilder semantic,
            string layer,
            IEnumerable<FixtureTileOccupancy> tiles)
        {
            foreach (var tile in tiles
                         .OrderBy(value => value.X)
                         .ThenBy(value => value.Y)
                         .ThenBy(value => value.TileIdentity, StringComparer.Ordinal)
                         .ThenBy(value => value.CollisionIdentity, StringComparer.Ordinal))
            {
                semantic
                    .Append(layer).Append(':')
                    .Append(tile.X).Append(',').Append(tile.Y).Append(':')
                    .Append(tile.TileIdentity).Append(':')
                    .Append(tile.CollisionIdentity).Append('\n');
            }
        }

        private static string Float(float value) =>
            value.ToString("R", CultureInfo.InvariantCulture);

        private static void EnsureAssetDirectory()
        {
            const string root = "Assets/NeoComposeTests";
            if (!AssetDatabase.IsValidFolder(root))
            {
                AssetDatabase.CreateFolder("Assets", "NeoComposeTests");
            }
            if (!AssetDatabase.IsValidFolder(AssetDirectory))
            {
                AssetDatabase.CreateFolder(root, "GeneratedArtifacts");
            }
        }
    }

    public sealed class NeoPostSynchronizeGeneratedArtifactFixtureTests
    {
        private const string FixtureRoot = "Assets/NeoComposeTests";
        private const string FixtureProjectPath =
            "Assets/NeoComposeTests/fixture-area-project.json";
        private const string OwnerValueId = "fixture-area-grid";

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(FixtureRoot);
            FixtureNavigationArtifactHandler.Reset();
            WriteFixture(CreateFixture());
        }

        [TearDown]
        public void TearDown()
        {
            FixtureNavigationArtifactHandler.Reset();
            AssetDatabase.DeleteAsset(FixtureRoot);
            AssetDatabase.Refresh();
        }

        [Test]
        public void Handler_FirstRunCreatesArtifactKeyedByGridValueId()
        {
            RunHandler();

            var asset = LoadArtifact();
            Assert.AreEqual(OwnerValueId, asset.OwnerValueId);
            Assert.IsNotEmpty(asset.ContentHash);
            Assert.AreEqual(1, asset.Revision);
            Assert.AreEqual(1, FixtureNavigationArtifactHandler.CommitCount);
            StringAssert.EndsWith(
                $"/{OwnerValueId}.asset",
                FixtureNavigationArtifactHandler.AssetPathFor(OwnerValueId));
        }

        [Test]
        public void Handler_SemanticChangeUpdatesInPlaceAndPreservesGuid()
        {
            RunHandler();
            string assetPath = FixtureNavigationArtifactHandler.AssetPathFor(OwnerValueId);
            string originalGuid = AssetDatabase.AssetPathToGUID(assetPath);
            var original = LoadArtifact();
            string originalHash = original.ContentHash;
            var fixture = ReadFixture();
            fixture.RequireGrid(OwnerValueId).FixtureStaticObstacleObjectLayer[0].X += 1f;
            WriteFixture(fixture);

            RunHandler();

            var updated = LoadArtifact();
            Assert.AreEqual(originalGuid, AssetDatabase.AssetPathToGUID(assetPath));
            Assert.AreNotEqual(originalHash, updated.ContentHash);
            Assert.AreEqual(2, updated.Revision);
            Assert.AreEqual(2, FixtureNavigationArtifactHandler.CommitCount);
        }

        [Test]
        public void Handler_UnchangedSemanticHashSkipsAssetWrite()
        {
            RunHandler();
            var original = LoadArtifact();
            string originalHash = original.ContentHash;
            int originalRevision = original.Revision;

            RunHandler();

            var unchanged = LoadArtifact();
            Assert.AreEqual(originalHash, unchanged.ContentHash);
            Assert.AreEqual(originalRevision, unchanged.Revision);
            Assert.AreEqual(1, FixtureNavigationArtifactHandler.CommitCount);
        }

        [Test]
        public void SemanticHashTracksNavigationLikeInputsOnly()
        {
            var baseline = CreateFixture().RequireGrid(OwnerValueId);
            string baselineHash = FixtureNavigationArtifactHandler.ComputeContentHash(
                baseline);

            AssertHashChanges(baselineHash, grid =>
                grid.FixtureGroundTileLayer[0].X += 1);
            AssertHashChanges(baselineHash, grid =>
                grid.FixtureIrregularObstacleTileLayer[0].TileIdentity = "irregular-rock-b");
            AssertHashChanges(baselineHash, grid =>
                grid.FixtureStaticObstacleObjectLayer[0].Rotation += 15f);
            AssertHashDoesNotChange(baselineHash, grid =>
                grid.FixtureGroundTileLayer[0].VisualTint = "magenta");
            AssertHashDoesNotChange(baselineHash, grid =>
                grid.FixtureStaticObstacleObjectLayer[0].VisualVariant = "gold");
            AssertHashDoesNotChange(baselineHash, grid =>
                grid.FixtureDynamicObstacleObjectLayer[0].X += 100f);
        }

        [Test]
        public void Handler_FailureBeforeCommitPreservesLastGoodArtifact()
        {
            RunHandler();
            string assetPath = FixtureNavigationArtifactHandler.AssetPathFor(OwnerValueId);
            string originalGuid = AssetDatabase.AssetPathToGUID(assetPath);
            var original = LoadArtifact();
            string originalHash = original.ContentHash;
            int originalRevision = original.Revision;
            var fixture = ReadFixture();
            fixture.RequireGrid(OwnerValueId).FixtureGroundTileLayer.Add(new()
            {
                X = 10,
                Y = 3,
                TileIdentity = "ground-extra",
            });
            WriteFixture(fixture);
            FixtureNavigationArtifactHandler.FailBeforeCommit = true;

            var error = Assert.Throws<InvalidOperationException>(() => RunHandler());

            StringAssert.Contains("before committing", error!.Message);
            var preserved = LoadArtifact();
            Assert.AreEqual(originalGuid, AssetDatabase.AssetPathToGUID(assetPath));
            Assert.AreEqual(originalHash, preserved.ContentHash);
            Assert.AreEqual(originalRevision, preserved.Revision);
            Assert.AreEqual(1, FixtureNavigationArtifactHandler.CommitCount);
        }

        [Test]
        public void ReloadRecovery_RetriesCommittedArtifactInPlaceAfterHandlerRegistrationReturns()
        {
            var persistence = new FixtureTaskPersistence();
            var generation = CreateGeneration();
            persistence.Save(generation);
            var coordinator = new NeoPostSynchronizeTaskCoordinator(persistence);
            using (coordinator.BeginCollection(generation))
            {
                CreateFixture().RequireGrid(OwnerValueId).OnDidSynchronize();
            }

            Assert.AreEqual(1, generation.Tasks.Count);
            var interrupted = generation.Tasks[0];
            interrupted.State = NeoPostSynchronizeTaskState.Running;
            interrupted.Attempt = 1;
            persistence.Save(generation);
            RunHandler(new NeoPostSynchronizeTaskContext(interrupted));
            string assetPath = FixtureNavigationArtifactHandler.AssetPathFor(OwnerValueId);
            string committedGuid = AssetDatabase.AssetPathToGUID(assetPath);
            int committedRevision = LoadArtifact().Revision;

            var recovered = persistence.Load()
                ?? throw new AssertionException("Reload simulation lost task state.");
            var recoveredCoordinator = new NeoPostSynchronizeTaskCoordinator(persistence);
            recoveredCoordinator.RecoverInterrupted(recovered);
            Assert.AreEqual(NeoPostSynchronizeTaskState.Pending, recovered.Tasks[0].State);
            using var registration = NeoPostSynchronizeTaskHandlers.Register(
                FixtureNavigationArtifactHandler.Kind,
                FixtureNavigationArtifactHandler.HandleAsync);

            recoveredCoordinator
                .DispatchAsync(recovered, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.AreEqual(NeoPostSynchronizeTaskState.Succeeded, recovered.Tasks[0].State);
            Assert.AreEqual(2, recovered.Tasks[0].Attempt);
            Assert.AreEqual(committedGuid, AssetDatabase.AssetPathToGUID(assetPath));
            Assert.AreEqual(committedRevision, LoadArtifact().Revision);
            Assert.AreEqual(
                1,
                FixtureNavigationArtifactHandler.CommitCount,
                "Retry should recognize the already-committed semantic hash.");
        }

        private static void AssertHashChanges(
            string baselineHash,
            Action<FixtureAreaTileGrid> mutate)
        {
            var grid = CreateFixture().RequireGrid(OwnerValueId);
            mutate(grid);
            Assert.AreNotEqual(
                baselineHash,
                FixtureNavigationArtifactHandler.ComputeContentHash(grid));
        }

        private static void AssertHashDoesNotChange(
            string baselineHash,
            Action<FixtureAreaTileGrid> mutate)
        {
            var grid = CreateFixture().RequireGrid(OwnerValueId);
            mutate(grid);
            Assert.AreEqual(
                baselineHash,
                FixtureNavigationArtifactHandler.ComputeContentHash(grid));
        }

        private static void RunHandler()
        {
            RunHandler(CreateContext());
        }

        private static void RunHandler(NeoPostSynchronizeTaskContext context)
        {
            FixtureNavigationArtifactHandler
                .HandleAsync(context, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        private static NeoPostSynchronizeTaskContext CreateContext() =>
            new(new NeoPostSynchronizeTaskDescriptor
            {
                GenerationId = "fixture-generation",
                ProjectId = "fixture-project",
                VersionId = "fixture-version",
                ProjectJsonPath = FixtureProjectPath,
                Kind = FixtureNavigationArtifactHandler.Kind,
                OwnerValueId = OwnerValueId,
                Name = "Generate fixture navigation-like artifact",
                Attempt = 1,
            });

        private static NeoPostSynchronizeGenerationState CreateGeneration() => new()
        {
            GenerationId = "fixture-generation",
            ProjectId = "fixture-project",
            VersionId = "fixture-version",
            ProjectJsonPath = FixtureProjectPath,
            Status = NeoPostSynchronizeGenerationStatus.Pending,
        };

        private static FixtureNavigationArtifact LoadArtifact() =>
            AssetDatabase.LoadAssetAtPath<FixtureNavigationArtifact>(
                FixtureNavigationArtifactHandler.AssetPathFor(OwnerValueId))
            ?? throw new AssertionException("Fixture navigation artifact was not created.");

        private static FixtureAreaProject ReadFixture() =>
            JsonConvert.DeserializeObject<FixtureAreaProject>(
                File.ReadAllText(FixtureProjectPath))
            ?? throw new AssertionException("Fixture area JSON could not be read.");

        private static void WriteFixture(FixtureAreaProject fixture)
        {
            if (!AssetDatabase.IsValidFolder(FixtureRoot))
            {
                AssetDatabase.CreateFolder("Assets", "NeoComposeTests");
            }
            File.WriteAllText(
                FixtureProjectPath,
                JsonConvert.SerializeObject(fixture, Formatting.Indented));
            AssetDatabase.ImportAsset(
                FixtureProjectPath,
                ImportAssetOptions.ForceSynchronousImport);
        }

        private static FixtureAreaProject CreateFixture() => new()
        {
            Grids = new()
            {
                new FixtureAreaTileGrid
                {
                    ValueId = OwnerValueId,
                    FixtureGroundTileLayer = new()
                    {
                        new FixtureTileOccupancy
                        {
                            X = 0,
                            Y = 0,
                            TileIdentity = "ground-full-cell",
                            CollisionIdentity = "full-cell",
                            VisualTint = "green",
                        },
                    },
                    FixtureIrregularObstacleTileLayer = new()
                    {
                        new FixtureTileOccupancy
                        {
                            X = 2,
                            Y = 1,
                            TileIdentity = "irregular-rock",
                            CollisionIdentity = "concave-a",
                            VisualTint = "gray",
                        },
                    },
                    FixtureStaticObstacleObjectLayer = new()
                    {
                        new FixtureObstacleObject
                        {
                            ObjectIdentity = "static-crate",
                            X = 4f,
                            Y = 2f,
                            Rotation = 0f,
                            ScaleX = 2f,
                            ScaleY = 1f,
                            VisualVariant = "wood",
                        },
                    },
                    FixtureDynamicObstacleObjectLayer = new()
                    {
                        new FixtureObstacleObject
                        {
                            ObjectIdentity = "dynamic-walker",
                            X = 8f,
                            Y = 3f,
                            Rotation = 45f,
                            VisualVariant = "blue",
                        },
                    },
                },
            },
        };

        private sealed class FixtureTaskPersistence
            : INeoPostSynchronizeTaskPersistence
        {
            private string? serialized;

            public NeoPostSynchronizeGenerationState? Load() =>
                string.IsNullOrEmpty(serialized)
                    ? null
                    : JsonConvert.DeserializeObject<NeoPostSynchronizeGenerationState>(
                        serialized!);

            public void Save(NeoPostSynchronizeGenerationState state)
            {
                serialized = JsonConvert.SerializeObject(state);
            }

            public void Clear()
            {
                serialized = null;
            }
        }
    }
}
