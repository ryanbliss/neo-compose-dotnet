// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Abstract base for the TS-side <c>TNSOperation</c> discriminated
    /// union. Two variants: <see cref="ArithmeticOperation"/> and
    /// <see cref="BooleanOperation"/>. Newtonsoft dispatches on
    /// <see cref="type"/> via {@link OperationConverter}.
    /// </summary>
    [JsonConverter(typeof(OperationConverter))]
    public abstract class Operation
    {
        /// <summary>One of <see cref="OperationKind"/>.</summary>
        public string type = null!;
    }

    /// <summary>
    /// Arithmetic operation — a `+`/`-`/etc. operator over a list of
    /// pointers. Mirrors TS-side <c>INSArithmeticOperation</c>.
    /// </summary>
    public class ArithmeticOperation : Operation
    {
        public ArithmeticOpInfo arithmetic = null!;
    }

    /// <summary>
    /// Boolean operation — a single boolean expression (which may chain
    /// via `&amp;&amp;`/`||`). Mirrors TS-side
    /// <c>INSBooleanOperation</c>.
    /// </summary>
    public class BooleanOperation : Operation
    {
        public BooleanExpression expression = null!;
    }

    public class OperationConverter : DiscriminatedConverter<Operation>
    {
        protected override Type? ResolveSubclass(JToken discriminator)
        {
            switch (discriminator.Value<string>())
            {
                case OperationKind.Arithmetic: return typeof(ArithmeticOperation);
                case OperationKind.Boolean: return typeof(BooleanOperation);
                default: return null;
            }
        }
    }
}
