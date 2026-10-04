// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Logged, never thrown, when a run of an <c>@effect</c> function fails
    /// (P97 §2.6). Names the effect's class, member and instance; the run's
    /// own exception is the inner exception.
    /// </summary>
    public class NeoEffectException : Exception
    {
        public NeoEffectException(string classId, string memberId, string instanceId, string message, Exception? inner = null)
            : base(message, inner)
        {
            ClassId = classId;
            MemberId = memberId;
            InstanceId = instanceId;
        }

        public string ClassId
        {
            get;
        }

        public string MemberId
        {
            get;
        }

        public string InstanceId
        {
            get;
        }
    }

    /// <summary>
    /// Logged when an effect stops for the rest of a drain after running
    /// <see cref="NeoClient.EffectRunsPerDrain"/> times in it (P97 §2.5).
    /// </summary>
    public sealed class NeoEffectCycleException : NeoEffectException
    {
        public NeoEffectCycleException(string classId, string memberId, string instanceId, string message)
            : base(classId, memberId, instanceId, message)
        {
        }
    }

    /// <summary>
    /// Logged, never thrown, when a lifecycle hook fails (P98 §2.5). Names the
    /// hook's class, member and instance; the run's own exception is the
    /// inner exception.
    /// </summary>
    public sealed class NeoLifecycleHookException : NeoEffectException
    {
        public NeoLifecycleHookException(string classId, string memberId, string instanceId, string message, Exception inner)
            : base(classId, memberId, instanceId, message, inner)
        {
        }
    }
}
