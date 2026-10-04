// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// The P98 lifecycle interfaces a class implements, one bit per
    /// <c>@system(kind: .ScriptRuntime)</c> interface.
    /// </summary>
    [Flags]
    internal enum NeoLifecycleHooks : uint
    {
        None = 0,
        OnLoad = 1u << 0,
        OnUnload = 1u << 1,
        Awake = 1u << 2,
        OnEnable = 1u << 3,
        Start = 1u << 4,
        FixedUpdate = 1u << 5,
        Update = 1u << 6,
        LateUpdate = 1u << 7,
        OnTriggerEnter2D = 1u << 8,
        OnTriggerStay2D = 1u << 9,
        OnTriggerExit2D = 1u << 10,
        OnCollisionEnter2D = 1u << 11,
        OnCollisionStay2D = 1u << 12,
        OnCollisionExit2D = 1u << 13,
        OnApplicationPause = 1u << 14,
        OnApplicationQuit = 1u << 15,
        OnDisable = 1u << 16,
        OnDestroy = 1u << 17,

        Data = OnLoad | OnUnload,
        Physics = OnTriggerEnter2D | OnTriggerStay2D | OnTriggerExit2D
            | OnCollisionEnter2D | OnCollisionStay2D | OnCollisionExit2D,
        Unity = Awake | OnEnable | Start | FixedUpdate | Update | LateUpdate | Physics
            | OnApplicationPause | OnApplicationQuit | OnDisable | OnDestroy,
        /// <summary>The hooks that may read their instance's departed rows (P98 §2.4).</summary>
        Departure = OnUnload | OnDisable | OnDestroy,
    }

    /// <summary>The canonical system interface and member name of each hook.</summary>
    internal static class NeoLifecycleHookTable
    {
        internal const int Count = 18;

        // Indexed by bit position.
        private static readonly (string interfaceId, string memberName)[] Hooks =
        {
            ("system_c1b7f48c-5615-4e61-a071-5ff8b2a70c97", "OnLoad"),
            ("system_c64e99f9-e8be-4d0a-927f-de20bdf9cdeb", "OnUnload"),
            ("system_e080a0a4-bb13-42a8-a1c5-17548dc5ccdb", "Awake"),
            ("system_3c3a67d4-9926-4b11-ac89-bcf30c2b5d90", "OnEnable"),
            ("system_b7e94d04-1c00-4e17-afb2-3de8b40fc5eb", "Start"),
            ("system_23677dec-fa02-4a09-8b0a-f762b10e467e", "FixedUpdate"),
            ("system_9e3923ce-a569-491f-95d8-dca8411611eb", "Update"),
            ("system_f2ecf221-8b14-44b1-b871-24c10c401b4b", "LateUpdate"),
            ("system_9faab5e9-701c-4cd8-8723-5f9794b8e8c0", "OnTriggerEnter2D"),
            ("system_a66ca3a6-3df3-4cb2-a9ba-7f63c494932c", "OnTriggerStay2D"),
            ("system_9da00dcd-4035-46cc-8d98-00f5b8d91545", "OnTriggerExit2D"),
            ("system_6f4099d5-3d13-4bea-b432-3069cbd8b18d", "OnCollisionEnter2D"),
            ("system_75350119-0b1e-4560-bf94-0a34590f4cc2", "OnCollisionStay2D"),
            ("system_a6157ec0-e5e1-4f77-9ba1-8f5eb022e6a4", "OnCollisionExit2D"),
            ("system_f1ab6738-fb6d-4d74-95cc-b4681f3feb85", "OnApplicationPause"),
            ("system_9156948c-a575-462b-9981-9d22bd262079", "OnApplicationQuit"),
            ("system_81750392-22a9-4881-a117-8d9de3e200cd", "OnDisable"),
            ("system_fe306522-8df2-4f30-8931-204d8dab4d96", "OnDestroy"),
        };

        private static readonly Dictionary<string, NeoLifecycleHooks> ByInterfaceId = BuildByInterfaceId();

        private static Dictionary<string, NeoLifecycleHooks> BuildByInterfaceId()
        {
            var byId = new Dictionary<string, NeoLifecycleHooks>(Count, StringComparer.Ordinal);
            for (int i = 0; i < Count; i++)
                byId.Add(Hooks[i].interfaceId, (NeoLifecycleHooks)(1u << i));
            return byId;
        }

        /// <summary>The hook <paramref name="interfaceId"/> declares, or None for any other interface.</summary>
        internal static NeoLifecycleHooks ForInterface(string interfaceId) =>
            ByInterfaceId.TryGetValue(interfaceId, out NeoLifecycleHooks hook) ? hook : NeoLifecycleHooks.None;

        internal static string MemberName(int index) => Hooks[index].memberName;

        /// <summary>The bit position of a single hook.</summary>
        internal static int IndexOf(NeoLifecycleHooks hook)
        {
            uint bits = (uint)hook;
            int index = 0;
            while ((bits >>= 1) != 0)
                index++;
            return index;
        }
    }
}
