// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Runtime view of one type-owned Custom member. The stable identity is the
    /// attribute id; <see cref="ValueId"/> is resolved live from the selected
    /// Save/Session binding map (or the authored fallback), so rebinding never
    /// requires replacing generated static API state.
    /// </summary>
    public sealed class NeoStaticBinding
    {
        private readonly NeoClient client;
        private readonly Attribute attribute;

        internal NeoStaticBinding(
            NeoClient client,
            string attributeId,
            NeoValueOwnership expectedOwnership)
        {
            this.client = client ?? throw new System.ArgumentNullException(nameof(client));
            if (!client.TryGetAttribute(attributeId, out Attribute? resolvedAttribute))
            {
                throw new System.ArgumentException(
                    $"No attribute exists for static binding '{attributeId}'.",
                    nameof(attributeId));
            }
            attribute = resolvedAttribute;
            Ownership = client.ResolveStaticOwnership(attribute);
            if (Ownership != expectedOwnership)
            {
                throw new System.InvalidOperationException(
                    $"Static member '{attribute.name}' resolves to {Ownership} storage, but generated code expected {expectedOwnership}. Regenerate the Unity types from the current project schema.");
            }
        }

        public string AttributeId => attribute.id;
        public NeoValueOwnership Ownership { get; }

        /// <summary>The currently selected target id, or null when unset.</summary>
        public string? ValueId
        {
            get
            {
                client.EnsureNotDisposed();
                client.TryResolveStaticBinding(
                    attribute.id,
                    out _,
                    out _,
                    out string? valueId);
                return valueId;
            }
        }

        /// <summary>
        /// Resolves the current target as the requested attribute wrapper.
        /// Returns false for an ordinary optional/unset binding. A dangling
        /// target or a node-kind mismatch is reported as corrupt project/save
        /// data rather than being silently materialized.
        /// </summary>
        public bool TryGetNode<TNode>(
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TNode? node)
            where TNode : NeoAttribute
        {
            node = null;
            client.EnsureNotDisposed();
            if (!client.TryResolveStaticBinding(
                    attribute.id,
                    out _,
                    out _,
                    out string? valueId))
            {
                return false;
            }
            if (!client.TryGetOverlaidValue(Ownership, valueId, out AttributeValue? _))
            {
                throw new System.InvalidOperationException(
                    $"Static member '{attribute.name}' is bound to missing value '{valueId}'.");
            }
            NeoAttribute resolved = Ownership == NeoValueOwnership.Asset
                ? NeoAttribute.Create(client, attribute, valueId)
                : NeoAttribute.CreateWritable(client, attribute, valueId, Ownership);
            if (resolved is not TNode typed)
            {
                throw new System.InvalidOperationException(
                    $"Static member '{attribute.name}' resolved {resolved.GetType().Name}; generated code expected {typeof(TNode).Name}.");
            }
            node = typed;
            return true;
        }

        public TNode GetRequiredNode<TNode>()
            where TNode : NeoAttribute
        {
            if (TryGetNode(out TNode? node)) return node;
            throw new System.InvalidOperationException(
                $"Required static member '{attribute.name}' has no bound value.");
        }

        /// <summary>
        /// Returns the current node or an unbound empty view. Collection and
        /// multiselect generated getters use this to preserve their ordinary
        /// non-null wrapper shape without turning a read into materialization;
        /// their mutation callback calls <see cref="GetOrCreateWritableNode{TNode}"/>
        /// when the first write actually occurs.
        /// </summary>
        public TNode GetNodeOrEmpty<TNode>()
            where TNode : NeoAttribute
        {
            if (TryGetNode(out TNode? node)) return node;
            string syntheticValueId = $"__neo_unset_static:{attribute.id}";
            NeoAttribute empty = Ownership == NeoValueOwnership.Asset
                ? NeoAttribute.Create(client, attribute, syntheticValueId)
                : NeoAttribute.CreateWritable(
                    client,
                    attribute,
                    syntheticValueId,
                    Ownership);
            if (empty is not TNode typed)
            {
                throw new System.InvalidOperationException(
                    $"Static member '{attribute.name}' empty view resolved {empty.GetType().Name}; generated code expected {typeof(TNode).Name}.");
            }
            return typed;
        }

        /// <summary>
        /// Returns a writable target, materializing and binding a fresh ordinary
        /// value row when the static member is currently unset. Used by mutable
        /// collection wrappers, whose first operation must create their
        /// container before applying the mutation.
        /// </summary>
        public TNode GetOrCreateWritableNode<TNode>(object? initialValue)
            where TNode : NeoAttribute
        {
            EnsureWritable();
            if (!TryGetNode(out TNode? node))
            {
                Materialize(initialValue);
                if (!TryGetNode(out node))
                {
                    throw new System.InvalidOperationException(
                        $"Static member '{attribute.name}' could not materialize a writable value.");
                }
            }
            return node;
        }

        /// <summary>
        /// Writes a scalar/struct-like payload. Existing targets keep their
        /// value id; an unset member receives a fresh ordinary id. Optional
        /// null clears the binding (a binding tombstone), rather than creating
        /// a typeless/null synthetic row.
        /// </summary>
        public void SetValue(NeoValueWritePayload? payload)
        {
            EnsureWritable();
            if (payload?.isValueReference == true)
            {
                BindValueReference(payload);
                return;
            }
            if (payload is null || payload.isNull)
            {
                Clear();
                return;
            }
            WritePayload(payload.value);
        }

        /// <summary>
        /// Rebinds a Custom static member to the incoming value's existing id.
        /// The value is never silently re-id'd or deep-copied merely because it
        /// became a static root.
        /// </summary>
        public void BindValue(INeoValueReference? value)
        {
            EnsureWritable();
            if (value is null)
            {
                Clear();
                return;
            }
            BindValueReference(NeoValueWritePayload.FromValueReference(
                NeoGeneratedTypesSupport.LookupSelectionId(value.valueId),
                value));
        }

        /// <summary>Writes an explicit null binding tombstone.</summary>
        public void Clear()
        {
            EnsureWritable();
            client.SetStaticBinding(attribute.id, Ownership, null);
        }

        /// <summary>
        /// Deletes the runtime binding entry so the authored
        /// <see cref="Attribute.valueId"/> is visible again. This is distinct
        /// from <see cref="Clear"/>.
        /// </summary>
        public bool RestoreAuthored()
        {
            EnsureWritable();
            return client.RestoreStaticBinding(attribute.id, Ownership);
        }

        private void Materialize(object? initialValue)
        {
            WritePayload(initialValue);
        }

        private void WritePayload(object? payload)
        {
            string nowIso = System.DateTime.UtcNow.ToString("o");
            string valueId;
            string createdAt = nowIso;
            AttributeValue? previous = null;
            if (client.TryResolveStaticBinding(
                    attribute.id,
                    out _,
                    out _,
                    out string? currentValueId))
            {
                valueId = currentValueId;
                if (!client.TryGetOverlaidValue(Ownership, valueId, out previous))
                {
                    throw new System.InvalidOperationException(
                        $"Static member '{attribute.name}' is bound to missing value '{valueId}'.");
                }
                createdAt = previous.createdAt;
            }
            else
            {
                valueId = System.Guid.NewGuid().ToString();
            }

            client.SetWritablePayloadRows(Ownership, payload);
            AttributeValue row = AttributeValueFactory.Create(
                attribute,
                payload,
                valueId,
                createdAt,
                nowIso);
            string? declaredMapKey = client.ResolveStaticMapKey(attribute);
            if (previous is not null)
            {
                row.containerId = previous.containerId;
                row.mapKey = previous.mapKey;
                row.genericBindings = previous.genericBindings is null
                    ? null
                    : new System.Collections.Generic.Dictionary<string, string>(
                        previous.genericBindings);
            }
            else
            {
                row.mapKey = declaredMapKey;
            }
            if (row.mapKey != declaredMapKey)
            {
                throw new System.InvalidOperationException(
                    $"Static member '{attribute.name}' resolves to storage partition '{declaredMapKey ?? "main"}', but its existing value '{valueId}' is stamped '{row.mapKey ?? "main"}'.");
            }
            client.SetWritableValue(Ownership, row);
            if (currentValueId is null)
            {
                client.SetStaticBinding(attribute.id, Ownership, valueId);
            }
        }

        private void BindValueReference(NeoValueWritePayload payload)
        {
            if (attribute is not CustomAttribute customAttribute)
            {
                throw new System.InvalidOperationException(
                    $"Static member '{attribute.name}' is not Custom-typed and cannot bind a value reference.");
            }
            string sourceValueId = payload.valueId
                ?? throw new System.InvalidOperationException(
                    $"Static member '{attribute.name}' received a value reference without an id.");
            if (!client.TryGetValue(sourceValueId, out ObjectAttributeValue? sourceRow))
            {
                throw new System.InvalidOperationException(
                    $"Cannot bind static member '{attribute.name}' to missing Custom value '{sourceValueId}'.");
            }
            string actualTypeId = sourceRow.typeId ?? customAttribute.customTypeId;
            if (!IsAssignableCustomType(actualTypeId, customAttribute.customTypeId))
            {
                throw new System.InvalidOperationException(
                    $"Cannot bind static member '{attribute.name}' ({customAttribute.customTypeId}) to incompatible Custom value type '{actualTypeId}'.");
            }

            string importedValueId = sourceValueId;
            bool sourceMoved = false;
            if (client.TryGetValueOwnership(
                    sourceValueId,
                    out NeoValueOwnership sourceOwnership)
                && sourceOwnership != NeoValueOwnership.Asset
                && sourceOwnership != Ownership)
            {
                importedValueId = client.ImportValueReference(
                    Ownership,
                    sourceValueId,
                    out sourceMoved,
                    ValueId);
            }
            string? expectedMapKey = client.ResolveStaticMapKey(attribute);
            if (!client.TryGetValue(
                    Ownership,
                    importedValueId,
                    out AttributeValue? importedRow))
            {
                throw new System.InvalidOperationException(
                    $"Static member '{attribute.name}' imported missing value '{importedValueId}'.");
            }
            if (!string.IsNullOrEmpty(importedRow!.mapKey)
                && importedRow.mapKey != expectedMapKey)
            {
                throw new System.InvalidOperationException(
                    $"Static member '{attribute.name}' cannot bind value '{importedValueId}' from storage partition '{importedRow.mapKey}' to '{expectedMapKey ?? "main"}'.");
            }
            if (importedRow.mapKey != expectedMapKey)
            {
                AttributeValue stamped = client.CloneRowForWrite(importedRow);
                stamped.mapKey = expectedMapKey;
                client.SetWritableValue(Ownership, stamped);
            }
            client.SetStaticBinding(attribute.id, Ownership, importedValueId);
            if (sourceMoved)
            {
                payload.RetargetMovedReference(
                    client,
                    attribute,
                    importedValueId,
                    Ownership);
            }
        }

        private bool IsAssignableCustomType(string actualTypeId, string expectedTypeId)
        {
            if (actualTypeId == expectedTypeId) return true;
            try
            {
                foreach (CustomType type in CustomTypeInheritance.ResolveChain(
                    actualTypeId,
                    id => client.TryGetType(id, out CustomType? candidate)
                        ? candidate
                        : null))
                {
                    if (type.id == expectedTypeId) return true;
                }
            }
            catch (CircularInheritanceError)
            {
                return false;
            }
            return false;
        }

        private void EnsureWritable()
        {
            client.EnsureNotDisposed();
            if (Ownership == NeoValueOwnership.Asset)
            {
                throw new System.InvalidOperationException(
                    $"Static member '{attribute.name}' is Immutable and cannot be written at runtime.");
            }
        }
    }
}
