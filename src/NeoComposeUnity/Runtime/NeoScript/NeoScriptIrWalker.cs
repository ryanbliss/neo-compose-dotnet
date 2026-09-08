// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime.NeoScript
{
    /// <summary>
    /// Traverses deserialized NeoScript IR without inspecting literal payloads.
    /// </summary>
    internal static class NeoScriptIrWalker
    {
        internal static bool AnyPointer(
            Instruction[]? instructions,
            Func<Pointer, bool> predicate)
        {
            foreach (Instruction instruction in instructions ?? Array.Empty<Instruction>())
            {
                if (AnyPointer(instruction, predicate)) return true;
            }
            return false;
        }

        private static bool AnyPointer(
            Instruction instruction,
            Func<Pointer, bool> predicate)
        {
            switch (instruction)
            {
                case VariableInstruction variable:
                    return AnyPointer(variable.variable.pointer, predicate);
                case IfInstruction conditional:
                    foreach (ConditionalBranch branch in
                        conditional.branches ?? Array.Empty<ConditionalBranch>())
                    {
                        if (AnyPointer(branch.expression, predicate)
                            || AnyPointer(branch.instructions, predicate)) return true;
                    }
                    return AnyPointer(conditional.elseInstructions, predicate);
                case ReturnInstruction result:
                    return result.pointer is not null
                        && AnyPointer(result.pointer, predicate);
                case ThrowInstruction thrown:
                    return AnyPointer(thrown.pointer, predicate);
                case AssignInstruction assign:
                    return AnyPointer(assign.target.pointer, predicate)
                        || AnyPointer(assign.pointer, predicate);
                case CollectionCallInstruction collectionCall:
                    if (AnyPointer(collectionCall.target.pointer, predicate)) return true;
                    return AnyPointer(collectionCall.args, predicate);
                case FunctionCallInstruction functionCall:
                    return AnyPointer(functionCall.call, predicate);
                case ForInstruction loop:
                    return AnyPointer(loop.initializer.pointer, predicate)
                        || AnyPointer(loop.condition, predicate)
                        || AnyPointer(loop.iterator, predicate)
                        || AnyPointer(loop.instructions, predicate);
                case ForEachInstruction loop:
                    return AnyPointer(loop.collectionPointer, predicate)
                        || AnyPointer(loop.instructions, predicate);
                case SwitchInstruction selection:
                    if (AnyPointer(selection.selector, predicate)) return true;
                    foreach (SwitchSection section in
                        selection.sections ?? Array.Empty<SwitchSection>())
                    {
                        if (AnyPointer(section.instructions, predicate)) return true;
                    }
                    return AnyPointer(selection.defaultInstructions, predicate);
                case TryInstruction guarded:
                    if (AnyPointer(guarded.instructions, predicate)) return true;
                    foreach (CatchClause clause in
                        guarded.catches ?? Array.Empty<CatchClause>())
                    {
                        if ((clause.filter is not null
                                && AnyPointer(clause.filter, predicate))
                            || AnyPointer(clause.instructions, predicate)) return true;
                    }
                    return false;
                case ActionListenerInstruction listener:
                    return AnyPointer(listener.target.pointer, predicate)
                        || AnyPointer(listener.listener, predicate);
                case BreakInstruction:
                case ContinueInstruction:
                    return false;
                default:
                    throw new NotSupportedException(
                        $"Unsupported NeoScript instruction {instruction.GetType().Name}.");
            }
        }

        private static bool AnyPointer(
            BooleanExpression expression,
            Func<Pointer, bool> predicate)
        {
            if (AnyPointer(expression.condition.operand1, predicate)
                || AnyPointer(expression.condition.operand2, predicate)) return true;
            return expression.connective is not null
                && AnyPointer(expression.connective.to, predicate);
        }

        private static bool AnyPointer(
            Pointer[]? pointers,
            Func<Pointer, bool> predicate)
        {
            foreach (Pointer pointer in pointers ?? Array.Empty<Pointer>())
            {
                if (AnyPointer(pointer, predicate)) return true;
            }
            return false;
        }

        private static bool AnyPointer(
            Pointer pointer,
            Func<Pointer, bool> predicate)
        {
            if (predicate(pointer)) return true;
            switch (pointer)
            {
                case OperationPointer operation:
                    return AnyPointer(operation.operation, predicate);
                case FunctionPointer function:
                    return AnyPointer(function.function, predicate);
                case KeyOfPointer keyOf:
                    return AnyPointer(keyOf.keyOf.pointer, predicate)
                        || AnyPointer(keyOf.keyOf.key, predicate);
                case ListLiteralPointer list:
                    return AnyPointer(list.entries, predicate);
                case DictLiteralPointer dictionary:
                    foreach (DictLiteralPair entry in
                        dictionary.entries ?? Array.Empty<DictLiteralPair>())
                    {
                        if (AnyPointer(entry.key, predicate)
                            || AnyPointer(entry.value, predicate)) return true;
                    }
                    return false;
                case ForceUnwrapPointer unwrap:
                    return AnyPointer(unwrap.pointer, predicate);
                case IsCheckPointer check:
                    return AnyPointer(check.pointer, predicate);
                case CallGetterPointer getter:
                    return AnyPointer(getter.receiver, predicate);
                case CoalescePointer coalesce:
                    return AnyPointer(coalesce.left, predicate)
                        || AnyPointer(coalesce.right, predicate);
                case ConditionalPointer conditional:
                    return AnyPointer(conditional.condition, predicate)
                        || AnyPointer(conditional.whenTrue, predicate)
                        || AnyPointer(conditional.whenFalse, predicate);
                case DelegateClosurePointer closure:
                    return AnyPointer(closure.captures, predicate)
                        || AnyPointer(closure.action.instructions, predicate);
                case ToBoolPointer boolean:
                    return AnyPointer(boolean.pointer, predicate);
                case StringifyPointer stringify:
                    return AnyPointer(stringify.pointer, predicate);
                case CallFunctionPointer call:
                    return AnyPointer(call.receiver, predicate)
                        || AnyPointer(call.args, predicate);
                case CallDelegatePointer call:
                    return AnyPointer(call.@delegate, predicate)
                        || AnyPointer(call.args, predicate);
                case CallActionPointer call:
                    return AnyPointer(call.action, predicate)
                        || AnyPointer(call.args, predicate);
                case FunctionErrorCheckPointer errorCheck:
                    return AnyPointer(errorCheck.call, predicate);
                case ReferencePointer:
                case VariablePointer:
                case ValuePointer:
                case StaticMemberPointer:
                case VariantPointer:
                    return false;
                default:
                    throw new NotSupportedException(
                        $"Unsupported NeoScript pointer {pointer.GetType().Name}.");
            }
        }

        private static bool AnyPointer(
            CallReceiver receiver,
            Func<Pointer, bool> predicate) =>
            !receiver.IsStatic
            && receiver.pointer is not null
            && AnyPointer(receiver.pointer, predicate);

        private static bool AnyPointer(
            Operation operation,
            Func<Pointer, bool> predicate) => operation switch
            {
                ArithmeticOperation arithmetic =>
                    AnyPointer(arithmetic.arithmetic.pointers, predicate),
                BooleanOperation boolean =>
                    AnyPointer(boolean.expression, predicate),
                _ => throw new NotSupportedException(
                    $"Unsupported NeoScript operation {operation.GetType().Name}."),
            };

        private static bool AnyPointer(
            Function function,
            Func<Pointer, bool> predicate)
        {
            switch (function)
            {
                case ClassCloneFunction clone:
                    return AnyPointer(clone.info.receiverPointer, predicate);
                case ClassConstructorFunction constructor:
                    return AnyPointer(constructor.info.fields, predicate);
                case DeclaredConstructorFunction constructor:
                    foreach (DeclaredConstructorArgument argument in
                        constructor.info.args ?? Array.Empty<DeclaredConstructorArgument>())
                    {
                        if (AnyPointer(argument.valuePointer, predicate)) return true;
                    }
                    return AnyPointer(constructor.info.fields, predicate);
                case SelectFunction select:
                    return AnyPointer(select.info.collectionPointer, predicate)
                        || AnyPointer(select.info.function.instructions, predicate);
                case WhereFunction where:
                    return AnyPointer(where.info.collectionPointer, predicate)
                        || AnyPointer(where.info.function.instructions, predicate);
                case FirstFunction first:
                    return AnyPointer(first.info.collectionPointer, predicate)
                        || (first.info.function is not null
                            && AnyPointer(first.info.function.instructions, predicate));
                case FirstOrDefaultFunction first:
                    return AnyPointer(first.info.collectionPointer, predicate)
                        || (first.info.function is not null
                            && AnyPointer(first.info.function.instructions, predicate));
                case CountFunction count:
                    return AnyPointer(count.info.collectionPointer, predicate)
                        || (count.info.function is not null
                            && AnyPointer(count.info.function.instructions, predicate));
                case ContainsFunction contains:
                    return AnyPointer(contains.info.collectionPointer, predicate)
                        || AnyPointer(contains.info.valuePointer, predicate);
                case IndexOfFunction indexOf:
                    return AnyPointer(indexOf.info.collectionPointer, predicate)
                        || AnyPointer(indexOf.info.valuePointer, predicate);
                case VisitCountFunction visitCount:
                    return AnyPointer(visitCount.info.pointer, predicate);
                case HasVisitedFunction hasVisited:
                    return AnyPointer(hasVisited.info.pointer, predicate);
                case VectorConstructorFunction vector:
                    return AnyPointer(vector.info.componentPointers, predicate);
                case ImageSliceFunction slice:
                    return AnyPointer(slice.info.filePointer, predicate)
                        || AnyPointer(slice.info.sliceIndexPointer, predicate);
                case StringOpFunction text:
                    return AnyPointer(text.info.receiverPointer, predicate)
                        || (text.info.argPointer is not null
                            && AnyPointer(text.info.argPointer, predicate));
                case DecimalOpFunction number:
                    return AnyPointer(number.info.receiverPointer, predicate)
                        || (number.info.argPointer is not null
                            && AnyPointer(number.info.argPointer, predicate))
                        || (number.info.digitsPointer is not null
                            && AnyPointer(number.info.digitsPointer, predicate));
                case MathOpFunction math:
                    return AnyPointer(math.info.argPointers, predicate);
                case ListRepeatFunction repeat:
                    return AnyPointer(repeat.info.valuePointer, predicate)
                        || AnyPointer(repeat.info.countPointer, predicate);
                case ListIndexFunction index:
                    return AnyPointer(index.info.collectionPointer, predicate)
                        || (index.info.keyPointer is not null
                            && AnyPointer(index.info.keyPointer, predicate));
                case VariantInitializeFunction initialize:
                    return AnyPointer(initialize.info.variantPointer, predicate)
                        || (initialize.info.rowPointer is not null
                            && AnyPointer(initialize.info.rowPointer, predicate));
                case VariantApplyFunction apply:
                    return AnyPointer(apply.info.receiverPointer, predicate)
                        || AnyPointer(apply.info.variantPointer, predicate)
                        || (apply.info.rowPointer is not null
                            && AnyPointer(apply.info.rowPointer, predicate));
                default:
                    throw new NotSupportedException(
                        $"Unsupported NeoScript function {function.GetType().Name}.");
            }
        }

        private static bool AnyPointer(
            FunctionClassConstructorField[]? fields,
            Func<Pointer, bool> predicate)
        {
            foreach (FunctionClassConstructorField field in
                fields ?? Array.Empty<FunctionClassConstructorField>())
            {
                if (AnyPointer(field.valuePointer, predicate)) return true;
            }
            return false;
        }
    }
}
