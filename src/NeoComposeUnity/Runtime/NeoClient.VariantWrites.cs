// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Linq;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        internal bool IsPreparingVariant => candidateReplay?.PreparingVariant == true;

        // A variant's Apply closure and declarative override removal form one
        // data edit. Execute against scoped wrappers, then validate the complete
        // graph through the same plan used by ordinary and remote writes.
        internal void PrepareVariantApply(NeoMemberClassWritable receiver,
            Action<NeoMemberClassWritable> apply)
        {
            var plan = new NeoWritePlan(this);
            var candidate = new CandidateReplay(this, plan) { PreparingVariant = true };
            if (candidateReplay is not null) throw new InvalidOperationException("A candidate graph is already active.");
            candidateReplay = candidate;
            try
            {
                using (ReadCandidate(plan))
                {
                    var scoped = new NeoMemberClassWritable(this, receiver.member,
                        receiver.overrideValueId ?? receiver.value?.id, receiver.ownership);
                    apply(scoped);
                    foreach (var allocation in candidate.Allocations)
                        plan.Set(NeoValueOwnership.Session, allocation.Value);
                }
            }
            finally
            {
                candidate.Dispose();
                candidateReplay = null;
            }
            plan.Commit();
        }

        internal bool DeferVariantAliasRetarget(NeoGeneratedClassValue value, ClassMember member,
            string valueId, NeoValueOwnership ownership)
        {
            CandidateReplay? candidate = candidateReplay;
            if (candidate?.PreparingVariant != true
                || candidate.Nodes.Values.Any(node => ReferenceEquals(node, value.BackingNode))) return false;
            candidate.Plan.AfterCommit(() => value.RetargetWritableReference(member, valueId, ownership));
            return true;
        }

        private void RefreshPreparedVariant(NeoMemberClassWritable node, ObjectMemberValue root)
        {
            CandidateReplay candidate = candidateReplay!;
            candidate.AffectedRoots.Add(root.id);
            if (virtualValueIdsByRoot.TryGetValue(root.id, out var previous))
                candidate.HiddenVirtualIds.UnionWith(previous);
            if (!replayingVirtualRootIds.Add(root.id))
                throw new InvalidOperationException($"Sparse constructor dependency cycle at '{root.id}'.");
            try { candidate.Add(ExpandVirtualInstanceRootCore(root, prepareOnly: true)); }
            finally { replayingVirtualRootIds.Remove(root.id); }
            node.RefreshCommittedValue();
            RefreshVirtualWrapperTree(node);
        }
    }
}
