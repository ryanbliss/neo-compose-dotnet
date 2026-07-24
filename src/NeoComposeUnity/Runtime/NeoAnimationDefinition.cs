// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    internal sealed class NeoAnimationDefinition
    {
        private readonly IReadOnlyDictionary<int, NeoAnimationCompiledWrite[]> sparseWrites;
        private IReadOnlyDictionary<int, NeoAnimationCompiledWrite[]> resolvedWrites;
        private readonly IReadOnlyDictionary<int, Action[]> actions;
        private readonly Action[] prepareActions;

        internal NeoAnimationDefinition(
            int fps,
            int duration,
            IReadOnlyDictionary<int, NeoAnimationCompiledWrite[]> sparseWrites,
            IReadOnlyDictionary<int, NeoAnimationCompiledWrite[]> resolvedWrites,
            IReadOnlyDictionary<int, Action[]> actions,
            Action[] prepareActions)
        {
            FPS = fps;
            Duration = duration;
            this.sparseWrites = sparseWrites;
            this.resolvedWrites = resolvedWrites;
            this.actions = actions;
            this.prepareActions = prepareActions;
        }

        internal int FPS { get; }
        internal int Duration { get; }

        internal void PreparePlayback()
        {
            // Root fallback is captured when playback starts, not when the
            // generated clip handle is first requested. A pass then reuses
            // that stable snapshot for wraps and reverse traversal.
            resolvedWrites = NeoAnimationCompiler.ResolveFrames(Duration, sparseWrites);
            foreach (Action prepare in prepareActions) prepare();
        }

        internal void ApplyFrame(int frameIndex, bool useResolvedState)
        {
            IReadOnlyDictionary<int, NeoAnimationCompiledWrite[]> source =
                useResolvedState ? resolvedWrites : sparseWrites;
            if (source.TryGetValue(frameIndex, out NeoAnimationCompiledWrite[] writes))
            {
                foreach (NeoAnimationCompiledWrite write in writes) write.Apply();
            }
            if (actions.TryGetValue(frameIndex, out Action[] frameActions))
            {
                foreach (Action action in frameActions) action();
            }
        }
    }

    internal sealed class NeoAnimationCompiledWrite
    {
        private readonly NeoMemberClass target;
        private readonly string[] path;
        private readonly NeoValueWritePayload payload;
        private readonly NeoMemberClassWritable writableParent;
        private readonly string writableKey;

        internal NeoAnimationCompiledWrite(
            NeoMemberClass target,
            string[] path,
            NeoValueWritePayload payload)
        {
            this.target = target;
            this.path = path;
            this.payload = payload;
            writableParent = ResolveWritableParent(target, path);
            writableKey = path[path.Length - 1];
        }

        internal string PathKey =>
            $"{target.overrideValueId ?? target.value?.id ?? target.member.id}\u001e{string.Join("\u001f", path)}";

        internal NeoAnimationCompiledWrite ResolveRoot()
        {
            NeoMemberClass parent = target;
            for (int index = 0; index < path.Length - 1; index++)
            {
                if (!parent.TryGet(path[index], out NeoMemberClass? child)
                    || child.value is null)
                {
                    throw new InvalidOperationException(
                        $"Animation path '{string.Join(".", path)}' cannot resolve against target '{target.value?.classId ?? target.member.classId}'.");
                }
                parent = child;
            }
            if (!parent.TryGet(path[path.Length - 1], out NeoMember? leaf))
            {
                throw new InvalidOperationException(
                    $"Animation path '{string.Join(".", path)}' cannot resolve against target '{target.value?.classId ?? target.member.classId}'.");
            }
            return new NeoAnimationCompiledWrite(target, path, NeoAnimationCompiler.Payload(leaf.value));
        }

        internal void Apply()
        {
            if (writableParent.value is null) return;
            NeoGeneratedTypesSupport.SetValue(writableParent, writableKey, payload);
        }

        private static NeoMemberClassWritable ResolveWritableParent(
            NeoMemberClass target,
            string[] path)
        {
            NeoMemberClassWritable parent = target.AsWritableView();
            for (int index = 0; index < path.Length - 1; index++)
            {
                if (!parent.TryGet(path[index], out NeoMemberClass? child)
                    || child.value is null)
                {
                    throw new InvalidOperationException(
                        $"Animation path '{string.Join(".", path)}' cannot resolve against target '{target.value?.classId ?? target.member.classId}'.");
                }
                parent = child.AsWritableView();
            }
            return parent;
        }
    }

    internal static class NeoAnimationCompiler
    {
        private const string AnimationClipWorldKind = "animationClip";

        internal static void ValidateProject(NeoClient client)
        {
            var validated = new HashSet<string>(StringComparer.Ordinal);
            foreach (NeoSchemaClass owner in client.classes.Values)
            {
                IReadOnlyDictionary<string, NeoGenericEnvEntry> env =
                    NeoGenericResolution.ResolveEnv(client, owner.id);
                foreach (MergedSchemaEntry placement in
                    client.ResolveInstanceSurfaceSchema(owner.id))
                {
                    if (!client.TryGetMember(placement.memberId, out Member? rawMember))
                    {
                        continue;
                    }
                    if (rawMember is GenericMember genericMember
                        && env.TryGetValue(genericMember.genericParamId, out NeoGenericEnvEntry entry)
                        && !entry.IsBound)
                    {
                        // Open generic class declarations are not instantiable.
                        // Their inherited placement is validated through each
                        // closed subclass, after TTarget substitution.
                        continue;
                    }
                    Member resolvedMember = NeoGenericResolution.SubstituteMember(
                        client,
                        rawMember,
                        env);
                    if (resolvedMember is not ClassMember clipMember) continue;
                    if (!string.Equals(
                            ResolveWorldKind(client, clipMember.classId),
                            AnimationClipWorldKind,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                    ValidateExportClip(
                        client,
                        owner,
                        placement.schemaKey,
                        clipMember,
                        validated,
                        new HashSet<string>(StringComparer.Ordinal));
                }
            }
        }

        private static (int fps, int duration) ValidateExportClip(
            NeoClient client,
            NeoSchemaClass targetClass,
            string clipKey,
            ClassMember clipMember,
            HashSet<string> validated,
            HashSet<string> stack)
        {
            string validationKey = $"{targetClass.id}\u001f{clipMember.id}";
            if (!stack.Add(validationKey))
            {
                throw new InvalidOperationException(
                    $"Animation child-track cycle reaches clip '{clipKey}' on class '{targetClass.name}'.");
            }
            try
            {
                var clipNode = new NeoMemberClass(client, clipMember, null);
                int fps = ReadRequiredInt(clipNode, "FPS", clipKey);
                int duration = ReadRequiredInt(clipNode, "Duration", clipKey);
                if (fps < 1)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' FPS must be at least 1; found {fps}.");
                }
                if (duration < 1)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' Duration must be at least 1; found {duration}.");
                }
                if (!validated.Add(validationKey)) return (fps, duration);

                var frameIndexes = new HashSet<int>();
                if (clipNode.TryGet("Frames", out NeoMemberList? frames))
                {
                    foreach (NeoMember item in frames)
                    {
                        if (item is not NeoMemberClass frame)
                        {
                            throw new InvalidOperationException(
                                $"Animation clip '{clipKey}' contains a non-Class frame row.");
                        }
                        int frameIndex = ReadRequiredInt(frame, "Index", clipKey);
                        if (frameIndex < 0 || frameIndex >= duration)
                        {
                            throw new InvalidOperationException(
                                $"Animation clip '{clipKey}' frame index {frameIndex} is outside [0, {duration - 1}].");
                        }
                        if (!frameIndexes.Add(frameIndex))
                        {
                            throw new InvalidOperationException(
                                $"Animation clip '{clipKey}' has duplicate frame index {frameIndex}.");
                        }
                        if (frame.TryGet("Overrides", out NeoMemberClass? overrides)
                            && overrides.value is not null)
                        {
                            ValidateExportOverrides(
                                client,
                                overrides,
                                Array.Empty<string>(),
                                clipKey,
                                frameIndex);
                        }
                        ValidateExportActions(
                            client,
                            targetClass.id,
                            frame,
                            clipKey,
                            frameIndex);
                        ValidateExportChildOverrides(client, frame, clipKey, frameIndex);
                    }
                }

                if (clipNode.TryGet("Tracks", out NeoMemberList? tracks))
                {
                    foreach (NeoMember item in tracks)
                    {
                        if (item is not NeoMemberClass track)
                        {
                            throw new InvalidOperationException(
                                $"Animation clip '{clipKey}' contains a non-Class child track row.");
                        }
                        string childId = ReadRequiredLookupId(
                            track,
                            "Child",
                            clipKey,
                            frameIndex: null);
                        string childClipKey = ReadRequiredString(track, "ClipKey", clipKey);
                        int startFrame = ReadRequiredInt(track, "StartFrame", clipKey);
                        if (startFrame < 0)
                        {
                            throw new InvalidOperationException(
                                $"Animation clip '{clipKey}' child track '{childClipKey}' StartFrame must be non-negative; found {startFrame}.");
                        }
                        if (client.ResolveEffectiveRow(childId) is not ObjectMemberValue child
                            || string.IsNullOrWhiteSpace(child.classId)
                            || !client.TryGetClass(child.classId!, out NeoSchemaClass? childClass))
                        {
                            throw new InvalidOperationException(
                                $"Animation clip '{clipKey}' child track '{childClipKey}' references missing child '{childId}'.");
                        }
                        ClassMember? childClipMember = null;
                        IReadOnlyDictionary<string, NeoGenericEnvEntry> childEnv =
                            NeoGenericResolution.ResolveEnv(client, childClass.id);
                        foreach (MergedSchemaEntry entry in
                            client.ResolveInstanceSurfaceSchema(childClass.id))
                        {
                            if (!string.Equals(entry.schemaKey, childClipKey, StringComparison.Ordinal))
                            {
                                continue;
                            }
                            if (client.TryGetMember(entry.memberId, out Member? childRawMember))
                            {
                                childClipMember = NeoGenericResolution.SubstituteMember(
                                    client,
                                    childRawMember,
                                    childEnv) as ClassMember;
                            }
                            break;
                        }
                        if (childClipMember is null
                            || !string.Equals(
                                ResolveWorldKind(client, childClipMember.classId),
                                AnimationClipWorldKind,
                                StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                $"Animation clip '{clipKey}' child track '{childClipKey}' does not resolve to an animation clip on child class '{childClass.name}'.");
                        }
                        (int childFps, int childDuration) = ValidateExportClip(
                            client,
                            childClass,
                            childClipKey,
                            childClipMember,
                            validated,
                            stack);
                        int parentFrameLength = checked((int)Math.Ceiling(
                            childDuration * (double)fps / childFps));
                        if (startFrame + parentFrameLength > duration)
                        {
                            throw new InvalidOperationException(
                                $"Animation clip '{clipKey}' child track '{childClipKey}' ends at parent frame {startFrame + parentFrameLength}, past Duration {duration}.");
                        }
                    }
                }
                return (fps, duration);
            }
            finally
            {
                stack.Remove(validationKey);
            }
        }

        private static void ValidateExportOverrides(
            NeoClient client,
            NeoMemberClass partial,
            string[] prefix,
            string clipKey,
            int frameIndex)
        {
            if (partial.value?.value is null) return;
            foreach (var pair in partial.value.value)
            {
                if (!partial.TryGet(pair.Key, out NeoMember? child))
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' frame {frameIndex} contains unknown override key '{pair.Key}'.");
                }
                string[] path = Append(prefix, pair.Key);
                if (child is NeoMemberClass childClass)
                {
                    if (childClass.value is null)
                    {
                        throw new InvalidOperationException(
                            $"Animation clip '{clipKey}' frame {frameIndex} cannot descend through null Class path '{string.Join(".", path)}'.");
                    }
                    ValidateExportOverrides(client, childClass, path, clipKey, frameIndex);
                    continue;
                }
                if (child is NeoMemberList or NeoMemberDictionary
                    || child.member is FunctionMember or NSFunctionMember or NSPropertyMember
                    || child.member.isReadOnly == true
                    || child.member.isStatic)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' frame {frameIndex} path '{string.Join(".", path)}' is not an eligible runtime-writable leaf.");
                }
                NeoValueOwnership? ownership = client.DeclaredOwnership(child.member);
                if (ownership != NeoValueOwnership.Save
                    && ownership != NeoValueOwnership.Session)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' frame {frameIndex} path '{string.Join(".", path)}' resolves Immutable storage.");
                }
            }
        }

        private static void ValidateExportActions(
            NeoClient client,
            string targetClassId,
            NeoMemberClass frame,
            string clipKey,
            int frameIndex)
        {
            if (!frame.TryGet("Actions", out NeoMemberList? actions)) return;
            foreach (NeoMember actionNode in actions)
            {
                if (actionNode is not NeoMemberFunctionRef functionRef
                    || string.IsNullOrWhiteSpace(functionRef.FunctionMemberId))
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' frame {frameIndex} contains an invalid FunctionRef action.");
                }
                string memberId = functionRef.FunctionMemberId!;
                EnsureTargetActionMember(
                    client,
                    targetClassId,
                    memberId,
                    clipKey,
                    frameIndex);
                if (client.TryResolveFunctionMember(memberId, out FunctionMember? native))
                {
                    ValidateActionSignature(
                        native.returnTypeInfo,
                        native.argumentTypes,
                        native.deferred == true,
                        memberId,
                        clipKey,
                        frameIndex);
                    continue;
                }
                NeoResolvedNSFunction? script = NeoNSFunctionRuntime.TryResolve(client, memberId);
                if (script is null)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' frame {frameIndex} action '{memberId}' does not resolve to a Function or NSFunction.");
                }
                ValidateActionSignature(
                    script.ReturnTypeInfo,
                    script.ArgumentTypes,
                    script.Deferred,
                    memberId,
                    clipKey,
                    frameIndex);
            }
        }

        private static void ValidateExportChildOverrides(
            NeoClient client,
            NeoMemberClass frame,
            string clipKey,
            int frameIndex)
        {
            if (!frame.TryGet("ChildOverrides", out NeoMemberList? childOverrides)) return;
            foreach (NeoMember item in childOverrides)
            {
                if (item is not NeoMemberClass childOverride)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' frame {frameIndex} contains a non-Class child override row.");
                }
                string childId = ReadRequiredLookupId(
                    childOverride,
                    "Child",
                    clipKey,
                    frameIndex);
                if (client.ResolveEffectiveRow(childId) is not ObjectMemberValue child)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' frame {frameIndex} references missing child '{childId}'.");
                }
                if (!childOverride.TryGet("Overrides", out NeoMemberClass? overrides)
                    || overrides.value is null)
                {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(child.classId)
                    && !string.Equals(overrides.value.classId, child.classId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' frame {frameIndex} child '{childId}' override class '{overrides.value.classId}' does not match '{child.classId}'.");
                }
                ValidateExportOverrides(
                    client,
                    overrides,
                    Array.Empty<string>(),
                    clipKey,
                    frameIndex);
            }
        }

        internal static NeoAnimationDefinition Compile<T>(
            T target,
            string schemaKey)
            where T : NeoGeneratedClassValue
        {
            return Compile(target, schemaKey, new HashSet<string>(StringComparer.Ordinal));
        }

        private static NeoAnimationDefinition Compile(
            NeoGeneratedClassValue target,
            string schemaKey,
            HashSet<string> compileStack)
        {
            if (target is null) throw new ArgumentNullException(nameof(target));
            if (string.IsNullOrWhiteSpace(schemaKey))
            {
                throw new ArgumentException("Clip schema key cannot be empty.", nameof(schemaKey));
            }
            string compileKey = $"{target.AnimationInstanceIdentity}\u001f{schemaKey}";
            if (!compileStack.Add(compileKey))
            {
                throw new InvalidOperationException(
                    $"Animation child-track cycle reaches clip '{schemaKey}' on value '{target.valueId ?? target.classId}'.");
            }
            try
            {
                if (!target.BackingNode.TryGet(schemaKey, out NeoMemberClass? clipNode))
                {
                    throw new InvalidOperationException(
                        $"Generated animation clip member '{schemaKey}' was not found on target class '{target.classId}'. Regenerate the project's C# types.");
                }
                string? worldKind = ResolveWorldKind(target.Client, clipNode.member.classId);
                if (!string.Equals(worldKind, AnimationClipWorldKind, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Member '{schemaKey}' targets class '{clipNode.member.classId}', whose inherited world kind is '{worldKind ?? "<missing>"}' instead of '{AnimationClipWorldKind}'.");
                }

                int fps = ReadRequiredInt(clipNode, "FPS", schemaKey);
                int duration = ReadRequiredInt(clipNode, "Duration", schemaKey);
                if (fps < 1)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{schemaKey}' FPS must be at least 1; found {fps}.");
                }
                if (duration < 1)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{schemaKey}' Duration must be at least 1; found {duration}.");
                }

                var sparseByIndex = new Dictionary<int, List<NeoAnimationCompiledWrite>>();
                var actionsByIndex = new Dictionary<int, Action[]>();
                var prepareActions = new List<Action>();
                var seenFrames = new HashSet<int>();
                if (clipNode.TryGet("Frames", out NeoMemberList? frames))
                {
                    foreach (NeoMember frameMember in frames)
                    {
                        if (frameMember is not NeoMemberClass frame)
                        {
                            throw new InvalidOperationException(
                                $"Animation clip '{schemaKey}' contains a non-Class frame row.");
                        }
                        int frameIndex = ReadRequiredInt(frame, "Index", schemaKey);
                        if (frameIndex < 0 || frameIndex >= duration)
                        {
                            throw new InvalidOperationException(
                                $"Animation clip '{schemaKey}' frame index {frameIndex} is outside [0, {duration - 1}].");
                        }
                        if (!seenFrames.Add(frameIndex))
                        {
                            throw new InvalidOperationException(
                                $"Animation clip '{schemaKey}' has duplicate frame index {frameIndex}.");
                        }

                        var writes = new List<NeoAnimationCompiledWrite>();
                        if (frame.TryGet("Overrides", out NeoMemberClass? overrides)
                            && overrides.value is not null)
                        {
                            FlattenOverrides(
                                target.Client,
                                target.BackingNode,
                                overrides,
                                Array.Empty<string>(),
                                target.ValueOwnership,
                                writes,
                                schemaKey,
                                frameIndex);
                        }
                        sparseByIndex[frameIndex] = writes;
                        actionsByIndex[frameIndex] = CompileActions(
                            target,
                            frame,
                            schemaKey,
                            frameIndex);

                        if (frame.TryGet("ChildOverrides", out NeoMemberList? childOverrides))
                        {
                            CompileChildOverrides(
                                target,
                                childOverrides,
                                writes,
                                schemaKey,
                                frameIndex);
                        }
                    }
                }
                if (clipNode.TryGet("Tracks", out NeoMemberList? tracks))
                {
                    CompileChildTracks(
                        target,
                        tracks,
                        fps,
                        duration,
                        actionsByIndex,
                        prepareActions,
                        schemaKey,
                        compileStack);
                }

                var sparse = new Dictionary<int, NeoAnimationCompiledWrite[]>();
                foreach (var pair in sparseByIndex) sparse[pair.Key] = pair.Value.ToArray();
                Dictionary<int, NeoAnimationCompiledWrite[]> resolved = ResolveFrames(
                    duration,
                    sparse);
                return new NeoAnimationDefinition(
                    fps,
                    duration,
                    sparse,
                    resolved,
                    actionsByIndex,
                    prepareActions.ToArray());
            }
            finally
            {
                compileStack.Remove(compileKey);
            }
        }

        private static void FlattenOverrides(
            NeoClient client,
            NeoMemberClass target,
            NeoMemberClass partial,
            string[] prefix,
            NeoValueOwnership inheritedOwnership,
            List<NeoAnimationCompiledWrite> writes,
            string clipKey,
            int frameIndex)
        {
            if (partial.value?.value is null) return;
            foreach (var pair in partial.value.value)
            {
                if (!partial.TryGet(pair.Key, out NeoMember? child))
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' frame {frameIndex} contains unknown override key '{pair.Key}'.");
                }
                string[] path = Append(prefix, pair.Key);
                NeoValueOwnership ownership =
                    client.DeclaredOwnership(child.member) ?? inheritedOwnership;
                if (child is NeoMemberClass childClass)
                {
                    if (childClass.value is null)
                    {
                        throw new InvalidOperationException(
                            $"Animation clip '{clipKey}' frame {frameIndex} cannot descend through null Class path '{string.Join(".", path)}'.");
                    }
                    FlattenOverrides(
                        client,
                        target,
                        childClass,
                        path,
                        ownership,
                        writes,
                        clipKey,
                        frameIndex);
                    continue;
                }
                if (child is NeoMemberList or NeoMemberDictionary
                    || child.member is FunctionMember or NSFunctionMember
                    || child.member is NSPropertyMember
                    || child.member.isReadOnly == true
                    || child.member.isStatic)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' frame {frameIndex} path '{string.Join(".", path)}' is not an eligible runtime-writable leaf.");
                }
                if (ownership != NeoValueOwnership.Save
                    && ownership != NeoValueOwnership.Session)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' frame {frameIndex} path '{string.Join(".", path)}' resolves Immutable storage.");
                }
                if (child.value is null)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' frame {frameIndex} path '{string.Join(".", path)}' has no override payload row.");
                }
                EnsurePlacementPathIsIsolated(
                    client,
                    target,
                    path,
                    clipKey,
                    frameIndex);
                writes.Add(new NeoAnimationCompiledWrite(
                    target,
                    path,
                    Payload(child.value)));
            }
        }

        private static void EnsurePlacementPathIsIsolated(
            NeoClient client,
            NeoMemberClass target,
            string[] path,
            string clipKey,
            int frameIndex)
        {
            if (path.Length == 0
                || target.value?.value is null
                || !target.value.value.TryGetValue("assetValueId", out string assetValueId)
                || client.ResolveEffectiveRow(assetValueId) is not ObjectMemberValue asset
                || asset.value is null
                || !target.value.value.TryGetValue(path[0], out string placedChildId)
                || !asset.value.TryGetValue(path[0], out string authoredChildId)
                || !string.Equals(placedChildId, authoredChildId, StringComparison.Ordinal))
            {
                return;
            }
            throw new InvalidOperationException(
                $"Animation clip '{clipKey}' frame {frameIndex} path '{string.Join(".", path)}' still references shared authored row '{authoredChildId}' on placement '{target.value.id}'. Re-export with a placement-owned clone carrying sourceValueId before playback.");
        }

        private static void CompileChildOverrides(
            NeoGeneratedClassValue target,
            NeoMemberList childOverrides,
            List<NeoAnimationCompiledWrite> writes,
            string clipKey,
            int frameIndex)
        {
            foreach (NeoMember item in childOverrides)
            {
                if (item is not NeoMemberClass childOverride)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' frame {frameIndex} contains a non-Class child override row.");
                }
                string sourceChildId = ReadRequiredLookupId(
                    childOverride,
                    "Child",
                    clipKey,
                    frameIndex);
                NeoMemberClass placedChild = ResolvePlacedChild(
                    target.BackingNode,
                    sourceChildId,
                    clipKey,
                    $"frame {frameIndex} child override");
                if (!childOverride.TryGet("Overrides", out NeoMemberClass? overrides)
                    || overrides.value is null)
                {
                    continue;
                }
                FlattenOverrides(
                    target.Client,
                    placedChild,
                    overrides,
                    Array.Empty<string>(),
                    placedChild.ownership,
                    writes,
                    clipKey,
                    frameIndex);
            }
        }

        private static void CompileChildTracks(
            NeoGeneratedClassValue target,
            NeoMemberList tracks,
            int parentFps,
            int parentDuration,
            Dictionary<int, Action[]> actionsByIndex,
            List<Action> prepareActions,
            string clipKey,
            HashSet<string> compileStack)
        {
            foreach (NeoMember item in tracks)
            {
                if (item is not NeoMemberClass track)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' contains a non-Class child track row.");
                }
                string sourceChildId = ReadRequiredLookupId(
                    track,
                    "Child",
                    clipKey,
                    frameIndex: null);
                string childClipKey = ReadRequiredString(track, "ClipKey", clipKey);
                int startFrame = ReadRequiredInt(track, "StartFrame", clipKey);
                if (startFrame < 0)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' child track '{childClipKey}' StartFrame must be non-negative; found {startFrame}.");
                }
                NeoMemberClass placedChild = ResolvePlacedChild(
                    target.BackingNode,
                    sourceChildId,
                    clipKey,
                    $"child track '{childClipKey}'");
                if (string.IsNullOrWhiteSpace(placedChild.value?.id))
                {
                    throw MissingPlacementGraph(clipKey, $"child track '{childClipKey}'");
                }
                NeoGeneratedClassValue? childTarget =
                    target.Client.ResolveRegisteredGeneratedClassValue(placedChild.value!.id);
                if (childTarget is null)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' child track '{childClipKey}' cannot create a generated wrapper for placed child '{placedChild.value.id}'. Regenerate the project's C# types.");
                }
                NeoAnimationDefinition childDefinition = Compile(
                    childTarget,
                    childClipKey,
                    compileStack);
                int parentFrameLength = checked((int)Math.Ceiling(
                    childDefinition.Duration * (double)parentFps / childDefinition.FPS));
                if (startFrame + parentFrameLength > parentDuration)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' child track '{childClipKey}' ends at parent frame {startFrame + parentFrameLength}, past Duration {parentDuration}.");
                }
                int lastAppliedChildFrame = -1;
                prepareActions.Add(() =>
                {
                    lastAppliedChildFrame = -1;
                    childDefinition.PreparePlayback();
                });
                for (int parentFrame = startFrame; parentFrame < parentDuration; parentFrame++)
                {
                    int elapsed = parentFrame - startFrame;
                    int childFrame = Math.Min(
                        childDefinition.Duration - 1,
                        (int)Math.Floor(elapsed * (double)childDefinition.FPS / parentFps));
                    int capturedChildFrame = childFrame;
                    AddFrameAction(
                        actionsByIndex,
                        parentFrame,
                        () =>
                        {
                            if (capturedChildFrame == lastAppliedChildFrame) return;
                            lastAppliedChildFrame = capturedChildFrame;
                            childDefinition.ApplyFrame(
                                capturedChildFrame,
                                useResolvedState: true);
                        });
                }
            }
        }

        private static NeoMemberClass ResolvePlacedChild(
            NeoMemberClass target,
            string sourceChildId,
            string clipKey,
            string usage)
        {
            if (!target.TryGet("Children", out NeoMemberList? children))
            {
                throw MissingPlacementGraph(clipKey, usage);
            }
            NeoMemberClass? match = null;
            bool hasLegacyChildWithoutProvenance = false;
            foreach (NeoMember child in children)
            {
                if (child is not NeoMemberClass childClass) continue;
                if (string.IsNullOrWhiteSpace(childClass.value?.sourceValueId))
                {
                    hasLegacyChildWithoutProvenance = true;
                    continue;
                }
                if (!string.Equals(
                        childClass.value?.sourceValueId,
                        sourceChildId,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                if (match is not null)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' {usage} source child '{sourceChildId}' maps to multiple placed Children rows.");
                }
                match = childClass;
            }
            if (match is null)
            {
                if (hasLegacyChildWithoutProvenance)
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' {usage} cannot run on legacy pre-0.7 placement '{target.value?.id ?? "<unmaterialized>"}': its Children rows do not carry sourceValueId placement-clone provenance. Migrate or recreate the persisted placement; re-exporting alone cannot upgrade saved placement rows.");
                }
                throw new InvalidOperationException(
                    $"Animation clip '{clipKey}' {usage} cannot resolve authored child '{sourceChildId}' on placement '{target.value?.id ?? "<unmaterialized>"}'. Every placed child row must carry its exact sourceValueId; name/index matching is not supported.");
            }
            return match;
        }

        private static string ReadRequiredLookupId(
            NeoMemberClass node,
            string key,
            string clipKey,
            int? frameIndex)
        {
            string[]? selected = node.TryGet(key, out NeoMemberLookup? lookup)
                ? lookup.value?.value
                : null;
            if (selected is null
                || selected.Length != 1
                || string.IsNullOrWhiteSpace(selected[0]))
            {
                string frame = frameIndex.HasValue ? $" frame {frameIndex.Value}" : "";
                throw new InvalidOperationException(
                    $"Animation clip '{clipKey}'{frame} requires '{key}' to select exactly one authored child entry.");
            }
            return selected[0];
        }

        private static string ReadRequiredString(
            NeoMemberClass node,
            string key,
            string clipKey)
        {
            if (!node.TryGet(key, out NeoMemberString? value)
                || string.IsNullOrWhiteSpace(value.value?.value))
            {
                throw new InvalidOperationException(
                    $"Animation clip '{clipKey}' is missing required String member '{key}'.");
            }
            return value.value!.value!;
        }

        private static void AddFrameAction(
            Dictionary<int, Action[]> actionsByIndex,
            int frameIndex,
            Action action)
        {
            if (!actionsByIndex.TryGetValue(frameIndex, out Action[] existing))
            {
                actionsByIndex[frameIndex] = new[] { action };
                return;
            }
            var combined = new Action[existing.Length + 1];
            Array.Copy(existing, combined, existing.Length);
            combined[existing.Length] = action;
            actionsByIndex[frameIndex] = combined;
        }

        private static Action[] CompileActions(
            NeoGeneratedClassValue target,
            NeoMemberClass frame,
            string clipKey,
            int frameIndex)
        {
            if (!frame.TryGet("Actions", out NeoMemberList? actions))
            {
                return Array.Empty<Action>();
            }
            var compiled = new List<Action>();
            foreach (NeoMember actionNode in actions)
            {
                if (actionNode is not NeoMemberFunctionRef functionRef
                    || string.IsNullOrWhiteSpace(functionRef.FunctionMemberId))
                {
                    throw new InvalidOperationException(
                        $"Animation clip '{clipKey}' frame {frameIndex} contains an invalid FunctionRef action.");
                }
                string memberId = functionRef.FunctionMemberId!;
                compiled.Add(CompileAction(target, memberId, clipKey, frameIndex));
            }
            return compiled.ToArray();
        }

        private static Action CompileAction(
            NeoGeneratedClassValue target,
            string memberId,
            string clipKey,
            int frameIndex)
        {
            if (string.IsNullOrWhiteSpace(target.classId))
            {
                throw new InvalidOperationException(
                    $"Animation clip '{clipKey}' frame {frameIndex} cannot resolve action '{memberId}' without a concrete target class.");
            }
            EnsureTargetActionMember(
                target.Client,
                target.classId!,
                memberId,
                clipKey,
                frameIndex);
            if (target.Client.TryResolveFunctionMember(memberId, out FunctionMember? native))
            {
                ValidateActionSignature(
                    native.returnTypeInfo,
                    native.argumentTypes,
                    native.deferred == true,
                    memberId,
                    clipKey,
                    frameIndex);
                return () => target.Client.InvokeNativeFunction(
                    memberId,
                    target,
                    Array.Empty<object?>());
            }
            NeoResolvedNSFunction? script = NeoNSFunctionRuntime.TryResolve(
                target.Client,
                memberId);
            if (script is null)
            {
                throw new InvalidOperationException(
                    $"Animation clip '{clipKey}' frame {frameIndex} action '{memberId}' does not resolve to a Function or NSFunction.");
            }
            ValidateActionSignature(
                script.ReturnTypeInfo,
                script.ArgumentTypes,
                script.Deferred,
                memberId,
                clipKey,
                frameIndex);
            if (string.IsNullOrWhiteSpace(target.valueId))
            {
                throw new InvalidOperationException(
                    $"Animation clip '{clipKey}' frame {frameIndex} NSFunction action '{memberId}' requires a materialized per-instance target value id.");
            }
            return () => new NeoMemberNSFunction(
                    target.Client,
                    script.Member,
                    target.valueId,
                    target.ValueOwnership)
                .Invoke(target.valueId!, Array.Empty<object?>());
        }

        private static void ValidateActionSignature(
            TypeInfo returnType,
            FunctionArgumentTypeInfo[] arguments,
            bool deferred,
            string memberId,
            string clipKey,
            int frameIndex)
        {
            if (returnType is not VoidTypeInfo
                || arguments.Length != 0
                || deferred)
            {
                throw new InvalidOperationException(
                    $"Animation clip '{clipKey}' frame {frameIndex} action '{memberId}' must be void-returning, zero-parameter, and non-deferred.");
            }
        }

        private static void EnsureTargetActionMember(
            NeoClient client,
            string targetClassId,
            string memberId,
            string clipKey,
            int frameIndex)
        {
            foreach (MergedSchemaEntry entry in
                client.ResolveInstanceSurfaceSchema(targetClassId))
            {
                if (string.Equals(entry.memberId, memberId, StringComparison.Ordinal))
                {
                    return;
                }
            }
            throw new InvalidOperationException(
                $"Animation clip '{clipKey}' frame {frameIndex} action '{memberId}' is outside target class '{targetClassId}' merged schema.");
        }

        internal static Dictionary<int, NeoAnimationCompiledWrite[]> ResolveFrames(
            int duration,
            IReadOnlyDictionary<int, NeoAnimationCompiledWrite[]> sparse)
        {
            var rootByPath = new Dictionary<string, NeoAnimationCompiledWrite>();
            foreach (NeoAnimationCompiledWrite[] frameWrites in sparse.Values)
            {
                foreach (NeoAnimationCompiledWrite write in frameWrites)
                {
                    if (rootByPath.ContainsKey(write.PathKey)) continue;
                    rootByPath[write.PathKey] = write.ResolveRoot();
                }
            }
            var current = new Dictionary<string, NeoAnimationCompiledWrite>(rootByPath);
            var resolved = new Dictionary<int, NeoAnimationCompiledWrite[]>();
            for (int frameIndex = 0; frameIndex < duration; frameIndex++)
            {
                if (sparse.TryGetValue(frameIndex, out NeoAnimationCompiledWrite[]? writes))
                {
                    foreach (NeoAnimationCompiledWrite write in writes)
                    {
                        current[write.PathKey] = write;
                    }
                }
                var ordered = new List<NeoAnimationCompiledWrite>();
                foreach (var pair in current) ordered.Add(pair.Value);
                resolved[frameIndex] = ordered.ToArray();
            }
            return resolved;
        }

        internal static NeoValueWritePayload Payload(MemberValue? row)
        {
            object? value = row switch
            {
                null => null,
                NullMemberValue => null,
                BoolMemberValue typed => typed.value,
                NumberMemberValue typed => typed.value,
                StringMemberValue typed => typed.value,
                ArrayMemberValue typed => typed.value,
                FileMemberValue typed => typed.value,
                SpriteMemberValue typed => typed.value,
                Vector2MemberValue typed => typed.value,
                Vector3MemberValue typed => typed.value,
                ColorMemberValue typed => typed.value,
                ObjectMemberValue => throw new InvalidOperationException(
                    "Animation Class values are path segments and cannot be written as leaf payloads."),
                _ => throw new InvalidOperationException(
                    $"Unsupported animation payload row '{row.GetType().Name}'."),
            };
            return NeoValueWritePayload.FromValue(value);
        }

        private static int ReadRequiredInt(
            NeoMemberClass node,
            string key,
            string clipKey)
        {
            if (!node.TryGet(key, out NeoMemberInt? value)
                || value.value?.value is not double raw)
            {
                throw new InvalidOperationException(
                    $"Animation clip '{clipKey}' is missing required Int member '{key}'.");
            }
            if (raw != Math.Truncate(raw))
            {
                throw new InvalidOperationException(
                    $"Animation clip '{clipKey}' Int member '{key}' must be an integer; found {raw}.");
            }
            return checked((int)raw);
        }

        private static string? ResolveWorldKind(NeoClient client, string classId)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string? cursor = classId;
            while (!string.IsNullOrWhiteSpace(cursor) && visited.Add(cursor!))
            {
                if (!client.TryGetClass(cursor!, out NeoSchemaClass? schemaClass)) return null;
                string? worldKind = schemaClass.system?["worldKind"]?.ToString();
                if (!string.IsNullOrWhiteSpace(worldKind)) return worldKind;
                cursor = schemaClass.extendsClassId;
            }
            return null;
        }

        private static string[] Append(string[] prefix, string value)
        {
            var result = new string[prefix.Length + 1];
            Array.Copy(prefix, result, prefix.Length);
            result[prefix.Length] = value;
            return result;
        }

        private static InvalidOperationException MissingPlacementGraph(
            string clipKey,
            string feature)
        {
            return new InvalidOperationException(
                $"Animation clip '{clipKey}' uses {feature}, but this export does not materialize a per-placement object graph with durable authored-child provenance. Re-export after the placement graph contract is available.");
        }
    }
}
