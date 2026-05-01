// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Abstract base for the TS-side <c>TNSFunction</c> discriminated
    /// union. Six variants (Select / First / FirstOrDefault / Where /
    /// Contains / Count). Newtonsoft dispatches on <see cref="type"/>
    /// via {@link FunctionConverter}.
    ///
    /// Each variant carries a per-shape <c>info</c> sub-object — see
    /// the per-subclass docs.
    /// </summary>
    [JsonConverter(typeof(FunctionConverter))]
    public abstract class Function
    {
        /// <summary>One of <see cref="FunctionKind"/>.</summary>
        public string type;
    }

    // ---------- Per-info shapes ----------
    // Mirrors TS-side INSFunctionCollection*Info family.

    /// <summary>
    /// Info shape for <c>select</c>: collection + projection function.
    /// Mirrors TS-side <c>INSFunctionCollectionSelectInfo</c>.
    /// </summary>
    public class FunctionCollectionSelectInfo
    {
        public Pointer collectionPointer;
        public FunctionWithReturnType function;
    }

    /// <summary>
    /// Info shape for <c>where</c>: collection + required Bool predicate.
    /// Mirrors TS-side <c>INSFunctionCollectionBoolInfo</c>.
    /// </summary>
    public class FunctionCollectionBoolInfo
    {
        public Pointer collectionPointer;
        public FunctionWithReturnType function;
    }

    /// <summary>
    /// Info shape for <c>first</c> / <c>firstOrDefault</c>: collection +
    /// optional Bool predicate. Mirrors TS-side
    /// <c>INSFunctionCollectionOptionalBoolInfo</c>.
    /// </summary>
    public class FunctionCollectionOptionalBoolInfo
    {
        public Pointer collectionPointer;
        public FunctionWithReturnType function;
    }

    /// <summary>
    /// Info shape for <c>contains</c>: collection + value pointer to
    /// compare against each entry. Mirrors TS-side
    /// <c>INSFunctionCollectionContainsInfo</c>.
    /// </summary>
    public class FunctionCollectionContainsInfo
    {
        public Pointer collectionPointer;
        public Pointer valuePointer;
    }

    /// <summary>
    /// Info shape for <c>count</c>: collection only. Mirrors TS-side
    /// <c>INSFunctionCollectionInfo</c>.
    /// </summary>
    public class FunctionCollectionInfo
    {
        public Pointer collectionPointer;
    }

    // ---------- Per-function variants ----------

    public class SelectFunction : Function
    {
        public FunctionCollectionSelectInfo info;
    }

    public class FirstFunction : Function
    {
        public FunctionCollectionOptionalBoolInfo info;
    }

    public class FirstOrDefaultFunction : Function
    {
        public FunctionCollectionOptionalBoolInfo info;
    }

    public class WhereFunction : Function
    {
        public FunctionCollectionBoolInfo info;
    }

    public class ContainsFunction : Function
    {
        public FunctionCollectionContainsInfo info;
    }

    public class CountFunction : Function
    {
        public FunctionCollectionInfo info;
    }

    public class FunctionConverter : DiscriminatedConverter<Function>
    {
        protected override Type ResolveSubclass(JToken discriminator)
        {
            switch (discriminator.Value<string>())
            {
                case FunctionKind.Select: return typeof(SelectFunction);
                case FunctionKind.First: return typeof(FirstFunction);
                case FunctionKind.FirstOrDefault: return typeof(FirstOrDefaultFunction);
                case FunctionKind.Where: return typeof(WhereFunction);
                case FunctionKind.Contains: return typeof(ContainsFunction);
                case FunctionKind.Count: return typeof(CountFunction);
                default: return null;
            }
        }
    }
}
