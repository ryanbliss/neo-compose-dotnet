// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Abstract base for the TS-side <c>TNSInstruction</c>
    /// discriminated union. Four variants:
    /// <see cref="VariableInstruction"/>, <see cref="IfInstruction"/>,
    /// <see cref="ReturnInstruction"/>, <see cref="ThrowInstruction"/>.
    /// Newtonsoft dispatches on the string <see cref="type"/> via
    /// {@link InstructionConverter}.
    /// </summary>
    [JsonConverter(typeof(InstructionConverter))]
    public abstract class Instruction
    {
        /// <summary>One of <see cref="InstructionKind"/>.</summary>
        public string type = null!;
    }

    /// <summary>
    /// Local variable declaration. Mirrors TS-side
    /// <c>INSInstructionVariable</c>.
    /// </summary>
    public class VariableInstruction : Instruction
    {
        public Variable variable = null!;
    }

    /// <summary>
    /// Mirror of <c>INSInstructionIfBranch</c>. The TS-side field
    /// <c>else</c> is a C# reserved word; <see cref="JsonPropertyAttribute"/>
    /// keeps the wire form unchanged while exposing it under
    /// <see cref="elseInstructions"/> on the C# side. The TS field is
    /// <c>else?: TNSInstructions | null</c> — nullable here.
    /// </summary>
    public class IfInstruction : Instruction
    {
        public ConditionalBranch[] branches = null!;

        [JsonProperty("else")]
        public Instruction[]? elseInstructions;
    }

    /// <summary>
    /// Mirror of <c>INSInstructionReturn</c>. The TS-side
    /// <c>pointer</c> field is <c>TNSPointer | null</c> — <c>null</c>
    /// represents a bare <c>return;</c>. Newtonsoft passes JSON
    /// <c>null</c> through to a C# <c>null</c> reference, so callers
    /// can distinguish "bare return" from "return X" by checking
    /// whether <see cref="pointer"/> is null.
    /// </summary>
    public class ReturnInstruction : Instruction
    {
        public Pointer? pointer;
    }

    /// <summary>
    /// Mirror of <c>INSInstructionThrow</c>. The TS-side
    /// <c>pointer</c> is the message to surface as the cell's error
    /// string at runtime — required, never null.
    /// </summary>
    public class ThrowInstruction : Instruction
    {
        public Pointer pointer = null!;
    }

    /// <summary>
    /// Mirror of <c>INSInstructionAssign</c>. The TS-side
    /// <c>operator</c> field is a C# keyword; expose it as
    /// <see cref="operatorValue"/> while keeping the wire name stable.
    /// </summary>
    public class AssignInstruction : Instruction
    {
        public WriteTarget target = null!;
        [JsonProperty("operator")]
        public string operatorValue = null!;
        public Pointer pointer = null!;
    }

    /// <summary>Mirror of <c>INSInstructionCollectionCall</c>.</summary>
    public class CollectionCallInstruction : Instruction
    {
        public WriteTarget target = null!;
        /// <summary>One of <see cref="CollectionMutationKind"/>.</summary>
        public string mutation = null!;
        public Pointer[] args = null!;
    }

    public class InstructionConverter : DiscriminatedConverter<Instruction>
    {
        protected override Type? ResolveSubclass(JToken discriminator)
        {
            switch (discriminator.Value<string>())
            {
                case InstructionKind.Variable: return typeof(VariableInstruction);
                case InstructionKind.If: return typeof(IfInstruction);
                case InstructionKind.Return: return typeof(ReturnInstruction);
                case InstructionKind.Throw: return typeof(ThrowInstruction);
                case InstructionKind.Assign: return typeof(AssignInstruction);
                case InstructionKind.CollectionCall: return typeof(CollectionCallInstruction);
                default: return null;
            }
        }
    }
}
