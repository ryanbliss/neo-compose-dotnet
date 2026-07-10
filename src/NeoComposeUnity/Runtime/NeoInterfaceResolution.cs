// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Resolves custom-type interface inheritance and implementation at runtime.
    /// Mirrors the web model's <c>src/models/interfaces/interface-graph.ts</c>.
    /// </summary>
    public static class NeoInterfaceResolution
    {
        public static IReadOnlyList<Interface> ResolveClosure(
            string interfaceId,
            ProjectData projectData)
        {
            var result = new List<Interface>();
            var emitted = new HashSet<string>();
            var visiting = new HashSet<string>();
            var path = new List<string>();

            void Visit(string currentId)
            {
                if (visiting.Contains(currentId))
                {
                    path.Add(currentId);
                    throw new InvalidOperationException(
                        $"Circular interface extension detected: {string.Join(" -> ", path)}.");
                }
                if (emitted.Contains(currentId)) return;
                if (!projectData.interfaces.TryGetValue(currentId, out Interface? declaration))
                {
                    throw new InvalidOperationException(
                        $"Interface '{currentId}' is missing from project data.");
                }

                visiting.Add(currentId);
                path.Add(currentId);
                result.Add(declaration);
                emitted.Add(currentId);
                if (declaration.extendsInterfaceIds is not null)
                {
                    foreach (string parentId in declaration.extendsInterfaceIds)
                    {
                        Visit(parentId);
                    }
                }
                path.RemoveAt(path.Count - 1);
                visiting.Remove(currentId);
            }

            Visit(interfaceId);
            return result;
        }

        public static bool TypeImplements(
            string typeId,
            string interfaceId,
            ProjectData projectData)
        {
            IList<CustomType> typeChain;
            try
            {
                typeChain = CustomTypeInheritance.ResolveChain(
                    typeId,
                    id => projectData.types.TryGetValue(id, out CustomType? type)
                        ? type
                        : null);
            }
            catch (CircularInheritanceError)
            {
                return false;
            }

            foreach (CustomType type in typeChain)
            {
                if (type.implementsInterfaceIds is null) continue;
                foreach (string declaredId in type.implementsInterfaceIds)
                {
                    IReadOnlyList<Interface> closure;
                    try
                    {
                        closure = ResolveClosure(declaredId, projectData);
                    }
                    catch (InvalidOperationException)
                    {
                        return false;
                    }
                    foreach (Interface declaration in closure)
                    {
                        if (declaration.id == interfaceId) return true;
                    }
                }
            }
            return false;
        }
    }
}
