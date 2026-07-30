// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NeoCompose.Runtime
{
    public enum NeoPlayMode
    {
        Repeat,
        Boomerang,
    }

    /// <summary>
    /// Playback order — the one direction type game code sees, at both
    /// altitudes: the root call site (<c>PlayOnce</c>, <c>PlayLoop</c>, and
    /// their async / fixed-loop siblings) and an authored track row's
    /// <c>Direction</c> member. The P48 §2.1 system enum has contract ids, so
    /// its generated wrapper would be byte-identical in every project; the SDK
    /// ships that exact shape once instead, and codegen skips emitting it.
    /// The body below must stay identical to what the generator would emit —
    /// the web repo's sdk-runtime-enums binding pins the ids and member names.
    /// (<c>Backward</c> was renamed <c>Reverse</c> so the authored vocabulary
    /// and the C# vocabulary are the same word.)
    /// </summary>
    public sealed class NeoPlayDirection : IEquatable<NeoPlayDirection>, INeoEnumOption
    {
        private static readonly Dictionary<string, NeoPlayDirection> values = new Dictionary<string, NeoPlayDirection>();
        public string optionId { get; }
        public string Text => TextForOptionId(optionId);
        public string TextId => TextIdForOptionId(optionId);

        private NeoPlayDirection(string optionId)
        {
            this.optionId = optionId;
        }

        public static readonly NeoPlayDirection Forward = FromOptionId("system_2e4ca40e-f305-49c6-a91b-b99d56239ba0");
        public static readonly NeoPlayDirection Reverse = FromOptionId("system_6478d195-3905-48db-befe-d276eb5478f0");

        public static NeoPlayDirection FromOptionId(string optionId)
        {
            if (values.TryGetValue(optionId, out var known)) return known;
            var created = new NeoPlayDirection(optionId);
            values[optionId] = created;
            return created;
        }

        public static string[] ToOptionIds(IEnumerable<NeoPlayDirection>? options)
        {
            if (options is null) return Array.Empty<string>();
            var ids = new List<string>();
            foreach (var option in options) ids.Add(option.optionId);
            return ids.ToArray();
        }

        public static bool IsKnown(string id)
        {
            return id switch
            {
                "system_2e4ca40e-f305-49c6-a91b-b99d56239ba0" => true,
                "system_6478d195-3905-48db-befe-d276eb5478f0" => true,
                _ => false,
            };
        }

        public static string TextIdForOptionId(string optionId)
        {
            return optionId switch
            {
                "system_2e4ca40e-f305-49c6-a91b-b99d56239ba0" => "Forward",
                "system_6478d195-3905-48db-befe-d276eb5478f0" => "Reverse",
                _ => optionId,
            };
        }

        public static string TextForOptionId(string optionId, NeoClient? client = null)
        {
            return client is null ? TextIdForOptionId(optionId) : client.Localization.ResolveText(TextIdForOptionId(optionId));
        }

        public static implicit operator string(NeoPlayDirection value) => value.optionId;
        public static implicit operator NeoPlayDirection(string optionId) => FromOptionId(optionId);
        public override string ToString() => optionId;
        public bool Equals(NeoPlayDirection? other) => other is not null && optionId == other.optionId;
        public override bool Equals(object? obj) => Equals(obj as NeoPlayDirection);
        public override int GetHashCode() => optionId.GetHashCode();
        public static bool operator ==(NeoPlayDirection? left, NeoPlayDirection? right) => ReferenceEquals(left, right) || (left is not null && left.Equals(right));
        public static bool operator !=(NeoPlayDirection? left, NeoPlayDirection? right) => !(left == right);
    }

    internal interface INeoAnimationPlayer
    {
        string InstanceIdentity { get; }
        bool IsPlaying { get; }
        void Tick(float scaledDeltaTime);
        void StopFromCoordinator();
    }

    /// <summary>
    /// Per-target playback handle returned by generated animation-clip
    /// properties. A client coordinator enforces one active clip per target
    /// identity while the handle owns playback state and frame events.
    /// </summary>
    public sealed class NeoAnimationClip<T> : INeoAnimationPlayer
        where T : NeoGeneratedClassValue
    {
        private const int MaxFrameStepsPerTick = 10000;

        private readonly NeoAnimationCoordinator coordinator;
        private readonly Action preparePlayback;
        private readonly Action<int, bool> applyFrame;
        private readonly Dictionary<int, Action[]> frameEvents = new();
        private readonly float secondsPerFrame;
        private readonly int duration;
        private float elapsed;
        private int step;
        private int initialStep;
        private int loopsRemaining;
        private bool isOnce;
        private bool isBoomerang;
        private bool hasTurned;
        private long playbackGeneration;
        private TaskCompletionSource<object?>? completion;
        private PlaybackCancellation? playbackCancellation;

        internal NeoAnimationClip(
            T target,
            string instanceIdentity,
            int fps,
            int duration,
            NeoAnimationCoordinator coordinator,
            Action<int>? applyFrame = null)
            : this(
                target,
                instanceIdentity,
                fps,
                duration,
                coordinator,
                preparePlayback: null,
                applyFrame is null ? null : (frame, _) => applyFrame(frame))
        {
        }

        internal NeoAnimationClip(
            T target,
            string instanceIdentity,
            int fps,
            int duration,
            NeoAnimationCoordinator coordinator,
            Action<int, bool>? applyFrame)
            : this(
                target,
                instanceIdentity,
                fps,
                duration,
                coordinator,
                preparePlayback: null,
                applyFrame)
        {
        }

        internal NeoAnimationClip(
            T target,
            string instanceIdentity,
            int fps,
            int duration,
            NeoAnimationCoordinator coordinator,
            Action? preparePlayback,
            Action<int, bool>? applyFrame)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            if (string.IsNullOrWhiteSpace(instanceIdentity))
            {
                throw new ArgumentException(
                    "Animation instance identity cannot be empty.",
                    nameof(instanceIdentity));
            }
            if (fps < 1) throw new ArgumentOutOfRangeException(nameof(fps));
            if (duration < 1) throw new ArgumentOutOfRangeException(nameof(duration));
            this.coordinator = coordinator
                ?? throw new ArgumentNullException(nameof(coordinator));
            this.preparePlayback = preparePlayback ?? (() => { });
            this.applyFrame = applyFrame ?? ((_, _) => { });
            InstanceIdentity = instanceIdentity;
            this.duration = duration;
            secondsPerFrame = 1f / fps;
            CurrentFrame = 0;
        }

        public T Target { get; }
        public bool IsPlaying { get; private set; }
        public bool IsPaused { get; private set; }
        public int CurrentFrame { get; private set; }

        public event Action? OnPlay;
        public event Action? OnPause;
        public event Action? OnResume;
        public event Action? OnStop;

        string INeoAnimationPlayer.InstanceIdentity => InstanceIdentity;
        bool INeoAnimationPlayer.IsPlaying => IsPlaying;
        internal string InstanceIdentity { get; }

        public void PlayLoop(
            NeoPlayMode mode = NeoPlayMode.Repeat,
            NeoPlayDirection? direction = null)
        {
            Start(mode, direction ?? NeoPlayDirection.Forward, loops: -1, once: false, pendingCompletion: null);
        }

        public void PlayOnce(
            NeoPlayDirection? direction = null)
        {
            Start(
                NeoPlayMode.Repeat,
                direction ?? NeoPlayDirection.Forward,
                loops: 1,
                once: true,
                pendingCompletion: null);
        }

        public Task PlayOnceAsync(
            NeoPlayDirection? direction = null,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }
            return StartAsync(
                NeoPlayMode.Repeat,
                direction ?? NeoPlayDirection.Forward,
                loops: 1,
                once: true,
                cancellationToken);
        }

        public void PlayFixedLoop(
            int loopCount,
            NeoPlayMode mode = NeoPlayMode.Repeat,
            NeoPlayDirection? direction = null)
        {
            ValidateLoopCount(loopCount);
            Start(mode, direction ?? NeoPlayDirection.Forward, loopCount, once: false, pendingCompletion: null);
        }

        public Task PlayFixedLoopAsync(
            int loopCount,
            NeoPlayMode mode = NeoPlayMode.Repeat,
            NeoPlayDirection? direction = null,
            CancellationToken cancellationToken = default)
        {
            ValidateLoopCount(loopCount);
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }
            return StartAsync(
                mode,
                direction ?? NeoPlayDirection.Forward,
                loopCount,
                once: false,
                cancellationToken);
        }

        public void Pause()
        {
            if (!IsPlaying || IsPaused) return;
            IsPaused = true;
            OnPause?.Invoke();
        }

        public void Resume()
        {
            if (!IsPlaying || !IsPaused) return;
            IsPaused = false;
            OnResume?.Invoke();
        }

        public void Stop()
        {
            StopInternal(cancelTask: true);
        }

        public IDisposable AddFrameEvent(int frameIndex, Action handler)
        {
            if (frameIndex < 0 || frameIndex >= duration)
            {
                throw new ArgumentOutOfRangeException(nameof(frameIndex));
            }
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            if (!frameEvents.TryGetValue(frameIndex, out Action[]? handlers))
            {
                handlers = Array.Empty<Action>();
            }
            var added = new Action[handlers.Length + 1];
            Array.Copy(handlers, added, handlers.Length);
            added[handlers.Length] = handler;
            frameEvents[frameIndex] = added;
            return new NeoDisposableAction(() =>
            {
                if (!frameEvents.TryGetValue(frameIndex, out Action[]? current)) return;
                int removeIndex = Array.IndexOf(current, handler);
                if (removeIndex < 0) return;
                if (current.Length == 1)
                {
                    frameEvents.Remove(frameIndex);
                    return;
                }
                var removed = new Action[current.Length - 1];
                if (removeIndex > 0)
                {
                    Array.Copy(current, 0, removed, 0, removeIndex);
                }
                if (removeIndex < current.Length - 1)
                {
                    Array.Copy(
                        current,
                        removeIndex + 1,
                        removed,
                        removeIndex,
                        current.Length - removeIndex - 1);
                }
                frameEvents[frameIndex] = removed;
            });
        }

        void INeoAnimationPlayer.Tick(float scaledDeltaTime) => Tick(scaledDeltaTime);

        internal void Tick(float scaledDeltaTime)
        {
            PlaybackCancellation? cancellation = playbackCancellation;
            if (cancellation?.IsCancellationRequested == true)
            {
                StopInternal(cancelTask: true);
                return;
            }
            if (!IsPlaying || IsPaused || scaledDeltaTime <= 0f) return;
            elapsed += scaledDeltaTime;
            int steps = 0;
            while (IsPlaying && elapsed >= secondsPerFrame)
            {
                elapsed -= secondsPerFrame;
                AdvanceOneFrame();
                steps += 1;
                if (steps >= MaxFrameStepsPerTick)
                {
                    elapsed = 0f;
                    break;
                }
            }
        }

        void INeoAnimationPlayer.StopFromCoordinator() =>
            StopInternal(cancelTask: true);

        private Task StartAsync(
            NeoPlayMode mode,
            NeoPlayDirection direction,
            int loops,
            bool once,
            CancellationToken cancellationToken)
        {
            var pending = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            long generation = Start(mode, direction, loops, once, pending);
            Task task = pending.Task;
            if (cancellationToken.CanBeCanceled)
            {
                var cancellation = new PlaybackCancellation(cancellationToken);
                if (IsPlaying && playbackGeneration == generation)
                {
                    playbackCancellation = cancellation;
                }
                else
                {
                    cancellation.Dispose();
                }
            }
            return task;
        }

        private long Start(
            NeoPlayMode mode,
            NeoPlayDirection direction,
            int loops,
            bool once,
            TaskCompletionSource<object?>? pendingCompletion)
        {
            if (!Enum.IsDefined(typeof(NeoPlayMode), mode))
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }
            if (!NeoPlayDirection.IsKnown(direction))
            {
                throw new ArgumentOutOfRangeException(nameof(direction));
            }

            StopInternal(cancelTask: true);
            long generation = ++playbackGeneration;
            preparePlayback();
            coordinator.Activate(this);
            elapsed = 0f;
            isOnce = once;
            isBoomerang = !once && mode == NeoPlayMode.Boomerang;
            loopsRemaining = loops;
            initialStep = direction == NeoPlayDirection.Forward ? 1 : -1;
            step = initialStep;
            hasTurned = false;
            CurrentFrame = direction == NeoPlayDirection.Forward ? 0 : duration - 1;
            IsPaused = false;
            IsPlaying = true;
            completion = pendingCompletion;
            OnPlay?.Invoke();
            if (IsPlaying && playbackGeneration == generation)
            {
                EnterFrame(
                    CurrentFrame,
                    useResolvedState: direction == NeoPlayDirection.Reverse);
            }
            return generation;
        }

        private void AdvanceOneFrame()
        {
            if (duration == 1)
            {
                if (isOnce || CompleteLoop()) CompleteNaturally();
                else EnterFrame(0, useResolvedState: true);
                return;
            }

            if (isOnce)
            {
                int onceNext = CurrentFrame + step;
                if (onceNext < 0 || onceNext >= duration)
                {
                    CompleteNaturally();
                    return;
                }
                EnterFrame(onceNext, useResolvedState: step < 0);
                return;
            }

            if (!isBoomerang)
            {
                int repeatNext = CurrentFrame + step;
                if (repeatNext >= 0 && repeatNext < duration)
                {
                    EnterFrame(repeatNext, useResolvedState: step < 0);
                    return;
                }
                if (CompleteLoop())
                {
                    CompleteNaturally();
                    return;
                }
                EnterFrame(
                    initialStep > 0 ? 0 : duration - 1,
                    useResolvedState: true);
                return;
            }

            int next = CurrentFrame + step;
            EnterFrame(next, useResolvedState: step < 0);
            int farEnd = initialStep > 0 ? duration - 1 : 0;
            int startEnd = initialStep > 0 ? 0 : duration - 1;
            if (!hasTurned && next == farEnd)
            {
                hasTurned = true;
                step = -initialStep;
                return;
            }
            if (hasTurned && next == startEnd)
            {
                hasTurned = false;
                step = initialStep;
                if (CompleteLoop()) CompleteNaturally();
            }
        }

        private bool CompleteLoop()
        {
            if (loopsRemaining < 0) return false;
            loopsRemaining -= 1;
            return loopsRemaining == 0;
        }

        private void EnterFrame(int frameIndex, bool useResolvedState)
        {
            CurrentFrame = frameIndex;
            applyFrame(frameIndex, useResolvedState);
            if (!frameEvents.TryGetValue(frameIndex, out Action[]? handlers)) return;
            foreach (Action handler in handlers) handler();
        }

        private void CompleteNaturally()
        {
            if (!IsPlaying) return;
            IsPlaying = false;
            IsPaused = false;
            coordinator.Deactivate(this);
            PlaybackCancellation? cancellation = playbackCancellation;
            playbackCancellation = null;
            cancellation?.Dispose();
            TaskCompletionSource<object?>? pending = completion;
            completion = null;
            pending?.TrySetResult(null);
            OnStop?.Invoke();
        }

        private void StopInternal(bool cancelTask)
        {
            PlaybackCancellation? cancellation = playbackCancellation;
            playbackCancellation = null;
            cancellation?.Dispose();
            if (!IsPlaying)
            {
                if (cancelTask && completion is not null)
                {
                    completion.TrySetCanceled();
                    completion = null;
                }
                return;
            }
            IsPlaying = false;
            IsPaused = false;
            coordinator.Deactivate(this);
            TaskCompletionSource<object?>? pending = completion;
            completion = null;
            if (cancelTask) pending?.TrySetCanceled();
            OnStop?.Invoke();
        }

        private sealed class PlaybackCancellation : IDisposable
        {
            private readonly CancellationTokenRegistration registration;
            private int isCancellationRequested;

            internal PlaybackCancellation(CancellationToken cancellationToken)
            {
                registration = cancellationToken.Register(
                    static state =>
                    {
                        var cancellation = (PlaybackCancellation)state!;
                        Interlocked.Exchange(
                            ref cancellation.isCancellationRequested,
                            1);
                    },
                    this);
            }

            internal bool IsCancellationRequested =>
                Volatile.Read(ref isCancellationRequested) != 0;

            public void Dispose() => registration.Dispose();
        }

        private static void ValidateLoopCount(int loopCount)
        {
            if (loopCount < 1) throw new ArgumentOutOfRangeException(nameof(loopCount));
        }
    }
}
