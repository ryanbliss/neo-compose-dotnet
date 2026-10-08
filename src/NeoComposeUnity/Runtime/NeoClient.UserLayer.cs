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
        // P104 §4.3: one User layer. The user client owns the user file's rows
        // and every index derived from them; an attached save client reads
        // them in place through userSource and never writes them.

        internal const string UserDataWriteError =
            "Only User code can change User data. C# changes it through the generated user client.";
        internal const string UserDelegateProvenanceError =
            "Delegates in User data must come from User members.";
        internal const string SaveHoldsUserReferenceError =
            "Save data can't hold a reference to `root.User` data.";

        // User client: the save clients reading this layer.
        private readonly List<NeoClient> attachedSaveClients = new();
        // User client: the User ids written since the outermost write
        // boundary, in write order, for the external change.
        private readonly List<string> pendingUserIds = new();
        private readonly HashSet<string> pendingUserIdSet = new(StringComparer.Ordinal);
        private bool userChangesPending;
        // Write plans committing now: their rows publish before the flush.
        private int userFlushHolds;
        // Save client: each pending id's row before the first write in the
        // boundary, for its listener dispatch.
        private readonly Dictionary<string, MemberValue?> userRowsBefore = new(StringComparer.Ordinal);

        /// <summary>The attached user client, or null in a user client and a detached save client.</summary>
        internal NeoClient? AttachedUserClient => ReferenceEquals(userSource, this) ? null : userSource;

        /// <summary>
        /// Whether this save client reads the current store's user file. False
        /// for a save from a <c>loadUserFile: false</c> store, which reads
        /// authored User defaults.
        /// </summary>
        public bool ReadsUserFile => AttachedUserClient != null;

        /// <summary>
        /// Reads <paramref name="userClient"/>'s User layer from now on and
        /// hears its writes (P104 §4.3). O(1): nothing is copied.
        /// </summary>
        internal void AttachUserClient(NeoClient userClient)
        {
            EnsureNotDisposed();
            if (IsUserClient || !userClient.IsUserClient)
                throw new InvalidOperationException("A save client attaches to a user client.");
            if (!ReferenceEquals(userSource, this))
                throw new InvalidOperationException("This client is already attached to a user client.");
            if (!ReferenceEquals(userClient.data, data))
                throw new InvalidOperationException("A save client attaches to a user client of its own project data.");
            userClient.EnsureNotDisposed();
            userSource = userClient;
            userClient.attachedSaveClients.Add(this);
            // Nothing read the empty layer through a node yet, except during
            // construction; refresh what did.
            foreach (NeoValueNode node in valueNodes.Values)
                userClient.userData.values.TryGetValue(node.id, out node.user);
            InvalidateSharedEvaluationContext();
            InvalidateGetterMemo();
            WriteRevision++;
            foreignWriteRevision = WriteRevision;
        }

        /// <summary>Stops hearing the user client. The layer reference stays, frozen.</summary>
        private void DetachFromUserClient()
        {
            if (!ReferenceEquals(userSource, this))
                userSource.attachedSaveClients.Remove(this);
        }

        /// <summary>
        /// Whether behaviour on <paramref name="ownership"/> rows (effects,
        /// hooks, authored listeners) runs in this client: User behaviour runs
        /// only in the user client, which runs nothing else (§4.2).
        /// </summary>
        internal bool RunsBehaviourFor(NeoValueOwnership ownership) =>
            (ownership == NeoValueOwnership.User) == IsUserClient;

        /// <summary>
        /// Rejects a write to User data outside the user client (§3.1). Cheap
        /// enough for every store write.
        /// </summary>
        internal void ThrowIfUserWrite(NeoValueOwnership ownership)
        {
            if (ownership == NeoValueOwnership.User && !IsUserClient)
                throw new InvalidOperationException(UserDataWriteError);
        }

        /// <summary>
        /// Checks a delegate C# writes into <paramref name="ownership"/>
        /// (§3.3). User data takes only User members, named through a User
        /// row, the owning User row, or a User class's static; Save and
        /// Immutable data take no User row.
        /// </summary>
        internal void CheckHostDelegate(NeoValueOwnership ownership, NeoDelegateValue? value)
        {
            if (value is null)
                return;
            if (ownership != NeoValueOwnership.User)
            {
                CheckDelegateReferences(ownership, value);
                return;
            }
            if (!value.IsMemberTarget || !NamesUserMember(value))
                throw new InvalidOperationException(UserDelegateProvenanceError);
        }

        private bool NamesUserMember(NeoDelegateValue value)
        {
            if (value.valueId is string receiverId)
                return TryGetValueOwnership(receiverId, out NeoValueOwnership receiver)
                    && receiver == NeoValueOwnership.User;
            if (!TryGetMember(value.memberId!, out Member? member))
                return false;
            // A null receiver binds the row that owns the action.
            return member.Modifier != NeoMemberModifierKind.Static
                || ResolveStaticOwnership(member) == NeoValueOwnership.User;
        }

        /// <summary>
        /// Rejects a Save or Immutable delegate that names a User row, directly
        /// or through its captures (§3.3): a durable reference across files.
        /// Session may hold one.
        /// </summary>
        internal void CheckDelegateReferences(NeoValueOwnership ownership, NeoDelegateValue? value)
        {
            if (value is null
                || ownership is not (NeoValueOwnership.Save or NeoValueOwnership.Asset))
                return;
            if (ReferencesUserRow(value, 0))
                throw new InvalidOperationException(SaveHoldsUserReferenceError);
        }

        /// <summary><see cref="CheckDelegateReferences"/> over a staged delegate or action row.</summary>
        internal void CheckDelegateRow(NeoValueOwnership ownership, MemberValue row)
        {
            if (ownership != NeoValueOwnership.Save)
                return;
            if (row is DelegateMemberValue delegateRow)
                CheckDelegateReferences(ownership, delegateRow.value);
            else if (row is ActionMemberValue { value: { } action })
                foreach (NeoDelegateValue listener in action.listeners)
                    CheckDelegateReferences(ownership, listener);
        }

        private bool ReferencesUserRow(NeoDelegateValue value, int depth)
        {
            if (value.valueId is string receiverId && IsUserRow(receiverId))
                return true;
            if (value.captures is null || depth > 64)
                return false;
            foreach (object? capture in value.captures)
            {
                if (capture is NeoDelegateValue nested && ReferencesUserRow(nested, depth + 1))
                    return true;
                if (capture is INeoValueReference { valueId: string id } && IsUserRow(id))
                    return true;
            }
            return false;
        }

        private bool IsUserRow(string id) =>
            TryGetValueOwnership(id, out NeoValueOwnership ownership) && ownership == NeoValueOwnership.User;

        /// <summary>
        /// User client: a User row was stored (<paramref name="row"/> set) or
        /// removed (null). Attached save clients run their per-row step now
        /// and hear the external change at the outermost write boundary.
        /// </summary>
        private void NoteUserRowChanged(string id, MemberValue? row)
        {
            if (attachedSaveClients.Count == 0)
                return;
            foreach (NeoClient client in attachedSaveClients)
                client.ApplyUserRow(id, row);
            if (pendingUserIdSet.Add(id))
                pendingUserIds.Add(id);
            userChangesPending = true;
            // A removal cascade can run outside any boundary.
            if (getterChangeHolds == 0 && userFlushHolds == 0)
                FlushUserChanges();
        }

        /// <summary>Save client: the per-row step its own writes run, without dirtying the save.</summary>
        private void ApplyUserRow(string id, MemberValue? row)
        {
            if (isDisposed)
                return;
            NeoValueNode? node = ExistingValueNode(id);
            if (!userRowsBefore.ContainsKey(id))
            {
                MemberValue? before = node is not null
                    ? node.user ?? node.Asset(data) ?? node.virtualRow
                    : data.values.GetValueOrDefault(id);
                userRowsBefore[id] = before;
            }
            if (row is null)
                SyncValueNode(id);
            else if (node is not null)
                node.user = row;
            RefreshSharedEvaluationRow(NeoValueOwnership.User, id);
            InvalidateGetterMemoForRow(id);
            if (!string.IsNullOrEmpty(row?.containerId))
                InvalidateGetterMemoForRow(row!.containerId!);
            WriteRevision++;
            foreignWriteRevision = WriteRevision;
        }

        /// <summary>User client: hands the boundary's User ids to attached save clients.</summary>
        private void FlushUserChanges()
        {
            if (!userChangesPending || userFlushHolds != 0)
                return;
            userChangesPending = false;
            var ids = pendingUserIds.ToArray();
            pendingUserIds.Clear();
            pendingUserIdSet.Clear();
            List<Exception>? failures = null;
            foreach (NeoClient client in attachedSaveClients.ToArray())
            {
                try
                {
                    client.ApplyExternalUserChange(ids);
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }
            if (failures is not null)
                throw failures.Count == 1 ? failures[0] : new AggregateException(failures);
        }

        /// <summary>
        /// Save client: raises the user client's committed User writes as
        /// one external change, in write order (§4.3 step 2).
        /// </summary>
        private void ApplyExternalUserChange(IReadOnlyList<string> ids)
        {
            if (isDisposed)
            {
                userRowsBefore.Clear();
                return;
            }
            NeoChangeSource previousSource = CurrentChangeSource;
            CurrentChangeSource = NeoChangeSource.External;
            BeginChangeBatch();
            HoldGetterChanges();
            try
            {
                if (hasListenerSources)
                    foreach (string id in ids)
                    {
                        userRowsBefore.TryGetValue(id, out MemberValue? before);
                        userSource.userData.values.TryGetValue(id, out MemberValue? after);
                        RecordListenerRow(NeoValueOwnership.User, before, after);
                    }
                userRowsBefore.Clear();
                using (SuspendContainerNotifications())
                {
                    foreach (string id in ids)
                    {
                        PublishWritableValueChange(NeoValueOwnership.User, id);
                        NotifyContainerMembershipChanged(NeoValueOwnership.User, id, null);
                    }
                }
            }
            finally
            {
                try
                {
                    ReleaseGetterChanges();
                }
                finally
                {
                    EndChangeBatch();
                    CurrentChangeSource = previousSource;
                }
            }
        }
    }
}
