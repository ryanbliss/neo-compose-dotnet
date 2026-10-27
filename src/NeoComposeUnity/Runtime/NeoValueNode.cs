// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Linq;
using System.Runtime.CompilerServices;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// One value id's rows across the client's stores, found with one lookup
    /// instead of one per store. The client refreshes the writable and
    /// virtual rows wherever those stores change. Authored rows live in a
    /// <see cref="ProjectData"/> that sibling clients share, so the node
    /// re-reads its authored row whenever that map's epoch moves.
    /// </summary>
    internal sealed class NeoValueNode
    {
        internal readonly string id;
        internal MemberValue? session;
        internal MemberValue? save;
        /// <summary>The User layer's row, which a save client reads from its user client (P104 §4.3).</summary>
        internal MemberValue? user;
        internal MemberValue? virtualRow;
        internal NeoValueOwnership virtualOwnership;
        /// <summary>False once the client dropped the node; a holder resolves the id again.</summary>
        internal bool live = true;
        /// <summary>A materialized getter temporary is copied when assigned to a stored owner.</summary>
        internal bool sharedGetterResult;
        // Only a successfully retained, parentless Session aggregate needs this
        // contract. Ordinary placement inference always takes precedence.
        internal RetainedRootContract? retainedRoot;

        internal sealed class RetainedRootContract
        {
            internal readonly Member member;
            internal readonly object schema;
            // P127 §12.1: a List or Dictionary root's type is the one its typeSource
            // resolves to. Re-placement moves the pointer but must not change that
            // type, so the contract compares resolved types. A type resolved at
            // creation accepts an unresolved row only with its source unchanged;
            // a source unresolved at creation, such as a row unplaced mid-batch,
            // falls back to the stored source's type or path.
            internal readonly NeoTypeSource? typeSource;
            internal readonly TypeBinding? type;

            internal RetainedRootContract(Member member, object schema, MemberValue row, System.Func<string, MemberValueBase?> lookup)
            {
                this.member = member;
                this.schema = schema;
                typeSource = row.typeSource;
                type = row.typeSource is { } source ? NeoTypeSources.TryTypeSourceType(source, lookup) : null;
            }

            internal bool MatchesType(MemberValue row, System.Func<string, MemberValueBase?> lookup)
            {
                TypeBinding? expected = type ?? (typeSource is null ? null : NeoTypeSources.TryTypeSourceType(typeSource, lookup));
                NeoTypeSource? current = row.typeSource;
                TypeBinding? resolved = current is null ? null : NeoTypeSources.TryTypeSourceType(current, lookup);
                if (expected is not null && resolved is not null)
                    return Equals(expected, resolved);
                // A contract that knew its type accepts a row that resolves to
                // nothing only while its source is unchanged: a moved pointer
                // that resolves nothing can't show the type held.
                if (type is not null)
                    return Equals(typeSource, current);
                if (typeSource is null || current is null)
                    return typeSource is null && current is null;
                if (!Equals(typeSource.Type, current.Type))
                    return false;
                if (typeSource.Path is null || current.Path is null)
                    return typeSource.Path is null && current.Path is null;
                return typeSource.Path.SequenceEqual(current.Path, System.StringComparer.Ordinal);
            }
        }
        /// <summary>The evaluator's canonical unwraps of this row.</summary>
        internal NeoScript.NSGetterEvaluator.UnwrapMemo? unwrapMemo;
        /// <summary>The getter capture that last recorded a read of this row.</summary>
        internal long capturedIn;
        /// <summary>The getter memo entry holding this NeoCellPattern row's offsets (<see cref="NeoCellPatternOffsets"/>).</summary>
        internal object? nativeRead;
        /// <summary>A single-selection lookup row's selected id and that id's node.</summary>
        internal string? selectedId;
        internal NeoValueNode? selectedNode;
        /// <summary>The id's writable-value listeners (one, or a list), mirrored from the client's subscription map.</summary>
        internal object? subscribers;
        private MemberValue? asset;
        private int assetEpoch = -1;
        // Rows an id held before it left the data: the asset row never refreshes.
        private bool departed;
        // The client's authored-ownership entry for this id, as of the map
        // rebuild numbered authoredOwnershipEpoch.
        internal bool hasAuthoredOwnership;
        internal NeoValueOwnership authoredOwnership;
        internal int authoredOwnershipEpoch = -1;

        internal NeoValueNode(string id)
        {
            this.id = id;
        }

        /// <summary>The rows <paramref name="id"/> held before it left the data, frozen (P98 §2.4).</summary>
        internal static NeoValueNode Departed(
            string id,
            MemberValue? session,
            MemberValue? save,
            MemberValue? user,
            MemberValue? asset,
            MemberValue? virtualRow,
            NeoValueOwnership virtualOwnership)
        {
            return new NeoValueNode(id)
            {
                session = session,
                save = save,
                user = user,
                asset = asset,
                virtualRow = virtualRow,
                virtualOwnership = virtualOwnership,
                live = false,
                departed = true,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal MemberValue? Asset(ProjectData data) =>
            assetEpoch == data.valuesEpoch ? asset : RefreshAsset(data);

        private MemberValue? RefreshAsset(ProjectData data)
        {
            if (departed)
                return asset;
            data.values.TryGetValue(id, out asset);
            assetEpoch = data.valuesEpoch;
            return asset;
        }
    }

    /// <summary>
    /// The value node an IR site last read, kept for one client's schema
    /// resolution: sibling clients share the IR but not their nodes.
    /// </summary>
    internal sealed class NeoValueNodeSite
    {
        internal readonly object schemaResolution;
        internal NeoValueNode? node;

        internal NeoValueNodeSite(object schemaResolution)
        {
            this.schemaResolution = schemaResolution;
        }
    }
}
