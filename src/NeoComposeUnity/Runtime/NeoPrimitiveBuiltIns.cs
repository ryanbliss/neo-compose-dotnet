// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
namespace NeoCompose.Runtime
{
    // Canonical interface signatures, cached for each schema generation.
    internal sealed class NeoPrimitiveBuiltIns
    {
        private static readonly ConditionalWeakTable<NeoInterfaceDispatch, NeoPrimitiveBuiltIns> Tables = new();
        private readonly Dictionary<NeoBuiltInTarget, (TypeBinding Owner, string Member)> byTarget = new();
        private readonly Dictionary<(MemberKind, string), NeoBuiltInTarget> byMember = new();
        private readonly HashSet<MemberKind> indexed = new();
        private static readonly HashSet<string> NumberPublic = new("Abs Clamp CopySign Max Min Sign CreateChecked CreateSaturating CreateTruncating IsEvenInteger IsNegative IsOddInteger IsPositive MaxMagnitude MinMagnitude Parse TryParse".Split(' '));
        private static readonly HashSet<string> FloatPublic = new("ClampNative MaxNative MaxNumber MinNative MinNumber IsFinite IsInfinity IsInteger IsNaN IsNegativeInfinity IsNormal IsPositiveInfinity IsRealNumber IsSubnormal MaxMagnitudeNumber MinMagnitudeNumber MultiplyAddEstimate".Split(' '));
        internal static NeoPrimitiveBuiltIns Of(NeoClient client) => Tables.GetValue(NeoInterfaceDispatch.Of(client), _ => new NeoPrimitiveBuiltIns());
        private static NeoBuiltInTarget Key(NeoBuiltInTarget target) => new(target.DeclaringType, target.DeclaringClass, target.Name, target.Arity, target.ParameterTypes, target.ExplicitInterface);
        private void Index(NeoClient client, MemberKind kind)
        {
            if (!indexed.Add(kind) || kind is not (MemberKind.Int or MemberKind.Float or MemberKind.Decimal or MemberKind.Bool or MemberKind.String))
                return;
            var self = TypeBinding.Closed(new TypeHead(kind, true));
            foreach (var owner in NeoInterfaceDispatch.Of(client).PrimitiveClosure(self))
            {
                TypeBinding Signature(TypeBinding binding)
                {
                    if (binding.Kind == TypeBindingKind.MethodParam)
                        return binding;
                    if (binding.IsOpen)
                        return NeoInterfaceDispatch.Identity(owner.Bindings![binding.Param!]);
                    var head = binding.Head!.WithoutNames();
                    if (!TypeBindings.IsValueType(head))
                        head = head.WithRequired(true);
                    return TypeBinding.Closed(head, binding.Bindings?.ToDictionary(p => p.Key, p => Signature(p.Value)));
                }
                foreach (var entry in client.ProjectDataForRuntime.interfaces[owner.Head!.InterfaceId!].members)
                {
                    var member = entry.Value;
                    if (member.kind != NeoInterfaceMemberKind.Function || !member.IsStatic || member.IsOperator || member.argumentTypes!.Any(a => a.mode == NeoParameterModeKind.Out))
                        continue;
                    string name = NeoCallableSignature.Name(entry.Key);
                    if (kind is MemberKind.Int or MemberKind.Decimal && name is "ClampNative" or "MaxNative" or "MinNative")
                        continue;
                    bool isPublic = kind is MemberKind.Int or MemberKind.Float or MemberKind.Decimal && NumberPublic.Contains(name)
                        || kind == MemberKind.Float && FloatPublic.Contains(name)
                        || kind == MemberKind.Decimal && name is "IsCanonical" or "IsInteger";
                    var target = new NeoBuiltInTarget(self, null, name, member.genericParams?.Length ?? 0,
                        member.argumentTypes.Select(a => Signature(NeoTypeBindings.SignatureOf(a))).ToArray(), isPublic ? null : NeoInterfaceDispatch.Identity(owner));
                    byTarget[target] = (owner, member.id!);
                    byMember[(kind, member.id!)] = target;
                }
            }
        }
        internal NeoBuiltInTarget? Bind(NeoClient client, TypeBinding self, string memberId, TypeBinding[]? arguments, TypeBinding? delegateType)
        {
            Index(client, self.Head!.Type);
            if (!byMember.TryGetValue((self.Head.Type, memberId), out var target))
                return null;
            if (target.Arity > 0 && arguments?.Length != target.Arity)
                throw new NeoScriptPreExecutionValidationError($"Built-in '{target.Name}' needs {target.Arity} type argument(s).");
            return new NeoBuiltInTarget(target.DeclaringType, null, target.Name, target.Arity, target.ParameterTypes, target.ExplicitInterface, target.Arity == 0 ? null : arguments, delegateType);
        }
        internal NeoInterfaceDispatch.Implementation Resolve(NeoClient client, NeoBuiltInTarget target)
        {
            if (target.DeclaringType?.Head is { } head)
                Index(client, head.Type);
            if (byTarget.TryGetValue(Key(target), out var found))
                return NeoInterfaceDispatch.Of(client).Resolve(target.DeclaringType!, found.Owner, found.Member, "call");
            string name = target.DeclaringClass ?? target.DeclaringType?.Head?.Type switch
            {
                MemberKind.Int => "int",
                MemberKind.Float => "float",
                MemberKind.Decimal => "decimal",
                MemberKind.Bool => "bool",
                MemberKind.String => "string",
                _ => "a non-primitive type",
            };
            throw new NeoScriptPreExecutionValidationError($"Built-in target '{name}.{target.Name}' names no built-in method with its signature.");
        }
    }
}
