// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;

namespace NeoCompose.Runtime
{
    public abstract class NeoGeneratedTileValue : NeoGeneratedClassValue
    {
        private readonly IReadOnlyDictionary<Type, string> classIdsByType;

        protected NeoGeneratedTileValue(
            NeoClient client,
            NeoMemberClass node,
            string fallbackClassId,
            bool isReadOnly,
            NeoValueOwnership inheritedStorageOwnership,
            IReadOnlyDictionary<Type, string> classIdsByType)
            : base(client, node, fallbackClassId, isReadOnly, inheritedStorageOwnership)
        {
            this.classIdsByType = classIdsByType;
        }

        public bool TryConvert<T>() where T : class, INeoValueReference
        {
            if (!classIdsByType.TryGetValue(typeof(T), out string targetClassId))
                throw new ArgumentException($"Type '{typeof(T).FullName}' is not a generated Neo class.");
            return TryConvertClass(targetClassId);
        }

        public bool TryConvert(INeoValueReference target)
        {
            if (target is not NeoGeneratedClassValue generated)
                throw new ArgumentException("Tile conversion target must be a generated Neo class value.", nameof(target));
            if (!ReferenceEquals(generated.Client, Client))
                throw new ArgumentException("Tile conversion target belongs to another client.", nameof(target));
            return TryConvertClass(generated.classId
                ?? throw new ArgumentException("Tile conversion target has no class.", nameof(target)));
        }

        private bool TryConvertClass(string targetClassId)
        {
            if (IsReadOnly) throw new InvalidOperationException("Cannot convert a read-only tile.");
            string rowId = valueId ?? throw new InvalidOperationException("Tile conversion requires a backing row.");
            try
            {
                Client.ConvertTile(ValueOwnership, rowId, targetClassId);
                return true;
            }
            catch (NeoPlacementValidationException)
            {
                return false;
            }
        }
    }
}
