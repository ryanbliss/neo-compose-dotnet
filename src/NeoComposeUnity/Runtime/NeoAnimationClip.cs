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

    public enum NeoPlayDirection
    {
        Forward,
        Backward,
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
        private readonly Action<int, bool> applyFrame;
        private readonly Dictionary<int, List<Action>> frameEvents = new();
        private readonly float secondsPerFrame;
        private readonly int duration;
        private float elapsed;
        private int step;
        private int initialStep;
        private int loopsRemaining;
        private bool isOnce;
        private bool isBoomerang;
        private bool hasTurned;
        private TaskCompletionSource<object?>? completion;
        private CancellationTokenRegistration cancellationRegistration;

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
            NeoPlayDirection direction = NeoPlayDirection.Forward)
        {
            Start(mode, direction, loops: -1, once: false, asynchronous: false);
        }

        public void PlayOnce(
            NeoPlayDirection direction = NeoPlayDirection.Forward)
        {
            Start(
                NeoPlayMode.Repeat,
                direction,
                loops: 1,
                once: true,
                asynchronous: false);
        }

        public Task PlayOnceAsync(
            NeoPlayDirection direction = NeoPlayDirection.Forward,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }
            return StartAsync(
                NeoPlayMode.Repeat,
                direction,
                loops: 1,
                once: true,
                cancellationToken);
        }

        public void PlayFixedLoop(
            int loopCount,
            NeoPlayMode mode = NeoPlayMode.Repeat,
            NeoPlayDirection direction = NeoPlayDirection.Forward)
        {
            ValidateLoopCount(loopCount);
            Start(mode, direction, loopCount, once: false, asynchronous: false);
        }

        public Task PlayFixedLoopAsync(
            int loopCount,
            NeoPlayMode mode = NeoPlayMode.Repeat,
            NeoPlayDirection direction = NeoPlayDirection.Forward,
            CancellationToken cancellationToken = default)
        {
            ValidateLoopCount(loopCount);
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }
            return StartAsync(
                mode,
                direction,
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
            if (!frameEvents.TryGetValue(frameIndex, out List<Action>? handlers))
            {
                handlers = new List<Action>();
                frameEvents.Add(frameIndex, handlers);
            }
            handlers.Add(handler);
            return new NeoDisposableAction(() =>
            {
                if (!frameEvents.TryGetValue(frameIndex, out List<Action>? current)) return;
                current.Remove(handler);
                if (current.Count == 0) frameEvents.Remove(frameIndex);
            });
        }

        void INeoAnimationPlayer.Tick(float scaledDeltaTime) => Tick(scaledDeltaTime);

        internal void Tick(float scaledDeltaTime)
        {
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
            Start(mode, direction, loops, once, asynchronous: true);
            Task task = completion!.Task;
            if (cancellationToken.CanBeCanceled)
            {
                CancellationTokenRegistration registration = cancellationToken.Register(
                    static state => ((NeoAnimationClip<T>)state!).StopInternal(
                        cancelTask: true,
                        disposeCancellationRegistration: false),
                    this);
                if (IsPlaying) cancellationRegistration = registration;
                else registration.Dispose();
            }
            return task;
        }

        private void Start(
            NeoPlayMode mode,
            NeoPlayDirection direction,
            int loops,
            bool once,
            bool asynchronous)
        {
            if (!Enum.IsDefined(typeof(NeoPlayMode), mode))
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }
            if (!Enum.IsDefined(typeof(NeoPlayDirection), direction))
            {
                throw new ArgumentOutOfRangeException(nameof(direction));
            }

            StopInternal(cancelTask: true);
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
            completion = asynchronous
                ? new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously)
                : null;
            OnPlay?.Invoke();
            EnterFrame(CurrentFrame, useResolvedState: CurrentFrame != 0);
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
            if (!frameEvents.TryGetValue(frameIndex, out List<Action>? handlers)) return;
            foreach (Action handler in handlers.ToArray()) handler();
        }

        private void CompleteNaturally()
        {
            if (!IsPlaying) return;
            IsPlaying = false;
            IsPaused = false;
            coordinator.Deactivate(this);
            CancellationTokenRegistration registration = cancellationRegistration;
            cancellationRegistration = default;
            registration.Dispose();
            TaskCompletionSource<object?>? pending = completion;
            completion = null;
            pending?.TrySetResult(null);
            OnStop?.Invoke();
        }

        private void StopInternal(
            bool cancelTask,
            bool disposeCancellationRegistration = true)
        {
            CancellationTokenRegistration registration = cancellationRegistration;
            cancellationRegistration = default;
            if (disposeCancellationRegistration) registration.Dispose();
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

        private static void ValidateLoopCount(int loopCount)
        {
            if (loopCount < 1) throw new ArgumentOutOfRangeException(nameof(loopCount));
        }
    }
}
