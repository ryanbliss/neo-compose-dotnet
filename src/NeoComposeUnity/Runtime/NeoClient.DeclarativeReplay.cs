// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        private string? declarativeReplayReceiver;
        private List<DeclarativeReplayWrite>? declarativeReplayWrites;

        internal IDisposable CaptureDeclarativeAnswers(NeoMemberClass receiver)
        {
            string? previous = declarativeReplayReceiver;
            declarativeReplayReceiver = receiver.overrideValueId ?? receiver.value?.id;
            return new NeoDisposableAction(() => declarativeReplayReceiver = previous);
        }

        internal void RecordDeclarativeAnswer(NeoAnimationCompiledWrite write)
        {
            if (declarativeReplayWrites is null || declarativeReplayReceiver is null)
                return;
            string? id = write.ReplayParent.overrideValueId ?? write.ReplayParent.value?.id;
            if (id is not null)
                declarativeReplayWrites.Add(new DeclarativeReplayWrite(declarativeReplayReceiver, id, write.ReplayKey, write.CaptureReplayOperation()));
        }

        private sealed class DeclarativeReplayWrite
        {
            internal readonly string ReceiverId, ParentId, Key;
            internal readonly NeoAnimationCompiledWrite.ReplayOperation Operation;
            internal DeclarativeReplayWrite(string receiverId, string parentId, string key,
                NeoAnimationCompiledWrite.ReplayOperation operation)
            {
                ReceiverId = receiverId;
                ParentId = parentId;
                Key = key;
                Operation = operation;
            }
        }

        // Only the paths containing incoming answers are retained. Shared path
        // prefixes are visited once, and operations keep no temporary wrappers.
        private sealed class IncomingDeclarativeAnswers
        {
            internal object? Key;
            internal readonly List<IncomingDeclarativeAnswers> Children = new();
            internal readonly List<(string key, NeoAnimationCompiledWrite.ReplayOperation operation)> Writes = new();

            internal void Apply(NeoClient client, NeoMember node)
            {
                foreach (var (key, operation) in Writes)
                    operation.Apply(client, (NeoMemberClass)node, key);
                foreach (var child in Children)
                {
                    NeoMember next = node switch
                    {
                        NeoMemberClass obj => obj.Get<NeoMember>((string)child.Key!),
                        NeoMemberList list => list[(int)child.Key!],
                        NeoMemberDictionary dictionary => dictionary[(string)child.Key!],
                        NeoMemberTuple tuple => TupleElement(client, tuple, (int)child.Key!),
                        _ => throw new InvalidOperationException("Incoming variant answer has no containing member."),
                    };
                    child.Apply(client, next);
                }
            }
        }

        private static NeoMember TupleElement(NeoClient client, NeoMemberTuple tuple, int index)
        {
            var row = tuple.value;
            if (row?.value is null)
                throw new InvalidOperationException("Incoming variant answer traverses a null tuple.");
            if ((uint)index >= (uint)row.value.Length)
                throw new InvalidOperationException($"Incoming variant answer tuple index {index} is out of range.");
            var member = client.TryResolveTupleElementMember(tuple.member, row, index)
                ?? throw new InvalidOperationException($"Incoming variant answer tuple element {index} has no declaration.");
            return NeoMember.CreateWritable(client, member, row.value[index], client.ChildOwnership(member, tuple.ownership));
        }

        private void ComposeIndependentDeclarativeAnswers(VirtualExpansionNode graph, PreparedVirtualExpansion expansion)
        {
            bool hasAnswers = declarativeReplayWrites is { Count: > 0 };
            bool hadAnswers = nestedReplayRootsByOwner.ContainsKey(expansion.Root.id);
            if (!hasAnswers && !hadAnswers)
                return;
            var nodes = new Dictionary<string, VirtualExpansionNode>();
            var boundaries = new Dictionary<VirtualExpansionNode, VirtualExpansionNode?>();
            var positions = new Dictionary<VirtualExpansionNode, (int start, int end)>();
            var keys = new Dictionary<VirtualExpansionNode, object>();
            int position = 0;
            Index(graph, null);
            var paths = new Dictionary<VirtualExpansionNode, IncomingDeclarativeAnswers>();
            var incoming = new Dictionary<VirtualExpansionNode, IncomingDeclarativeAnswers>();
            foreach (var node in nodes.Values)
                if (node.independentRoot is { } child
                    && nestedReplayBoundaries.TryGetValue(child.id, out var previous)
                    && previous.OwnerRootId == expansion.Root.id && previous.IncomingAnswers is not null)
                    Path(node, node);
            foreach (var write in declarativeReplayWrites!)
            {
                if (!nodes.TryGetValue(write.ParentId, out var target)
                    || boundaries[target] is not { } boundary)
                    continue;
                // The child's own initialization variant belongs to its recipe.
                // Only answers supplied by an enclosing receiver cross here.
                if (nodes.TryGetValue(write.ReceiverId, out var receiver)
                    && positions[receiver].start >= positions[boundary].start
                    && positions[receiver].start < positions[boundary].end)
                    continue;
                Path(target, boundary).Writes.Add((write.Key, write.Operation));
            }
            foreach (var pair in incoming)
            {
                var node = pair.Key;
                var boundary = new NestedReplayBoundary
                {
                    Root = node.independentRoot!,
                    NamespaceRoot = node.independentRoot!,
                    OwnerRootId = expansion.Root.id,
                    Path = "$",
                    Ownership = node.effectiveOwnership,
                    IncomingAnswers = pair.Value,
                };
                candidateReplay?.Remove(boundary.Root.id);
                if (!replayingVirtualRootIds.Add(boundary.Root.id))
                    throw new InvalidOperationException($"Sparse constructor dependency cycle at '{boundary.Root.id}'.");
                try
                {
                    var nested = ExpandVirtualInstanceRootCore(boundary.Root, prepareOnly: true,
                        replayOwnership: boundary.Ownership, preparedBoundary: boundary);
                    if (pair.Value.Children.Count == 0 && pair.Value.Writes.Count == 0)
                        nested.Boundary = null;
                    expansion.Nested.Add(nested);
                }
                finally { replayingVirtualRootIds.Remove(boundary.Root.id); }
            }

            IncomingDeclarativeAnswers Path(VirtualExpansionNode node, VirtualExpansionNode boundary)
            {
                if (paths.TryGetValue(node, out var existing))
                    return existing;
                var path = new IncomingDeclarativeAnswers();
                paths[node] = path;
                if (node == boundary)
                    incoming[node] = path;
                else
                {
                    path.Key = keys[node];
                    Path(node.parent!, boundary).Children.Add(path);
                }
                return path;
            }

            void Index(VirtualExpansionNode node, VirtualExpansionNode? boundary)
            {
                int start = position++;
                nodes[node.row.id] = node;
                // Overlay stops at the first independent root. Its descendants
                // are resolved by that root's own replay, including further boundaries.
                boundary ??= node.independentRoot is null ? null : node;
                boundaries[node] = boundary;
                foreach (var pair in node.classChildren)
                {
                    keys[pair.Value] = pair.Key;
                    Index(pair.Value, boundary);
                }
                for (int i = 0; i < node.listChildren.Count; i++)
                {
                    keys[node.listChildren[i]] = i;
                    Index(node.listChildren[i], boundary);
                }
                foreach (var pair in node.dictionaryChildren)
                {
                    keys[pair.Value] = pair.Key;
                    Index(pair.Value, boundary);
                }
                positions[node] = (start, position);
            }
        }
    }
}
