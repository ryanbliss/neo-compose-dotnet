// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// One option inside an <see cref="Enum"/>. Mirrors the TS-side
    /// <c>IEnumOption</c>.
    /// </summary>
    public class EnumOption
    {
        public string text = null!;
    }

    /// <summary>
    /// Enum declaration. Mirrors the TS-side <c>IEnum</c>. The
    /// <see cref="options"/> field is the wire-form
    /// <c>Record&lt;string, IEnumOption&gt;</c> — a plain JSON object
    /// keyed by option id, which Newtonsoft maps directly onto a
    /// <see cref="Dictionary{TKey, TValue}"/>.
    /// </summary>
    public class Enum
    {
        public string id = null!;
        public string projectId = null!;
        public string name = null!;
        public Dictionary<string, EnumOption> options = null!;

        /// <summary>
        /// Authoritative display and generated-member order for
        /// <see cref="options"/> keys. Absent means the natural key order;
        /// read it through <see cref="OptionKeyOrder"/>.
        /// </summary>
        public List<string>? optionKeyOrder;
        public NeoTimestamp createdAt;
        public NeoTimestamp updatedAt;

        /// <summary>
        /// Every option key once, in authored order: the listed keys that
        /// name an option, then any unlisted keys in natural order. Mirrors
        /// the TS-side <c>getEnumOptionKeyOrder</c>.
        /// </summary>
        public List<string> OptionKeyOrder()
        {
            var seen = new HashSet<string>();
            var order = new List<string>(options.Count);
            if (optionKeyOrder is not null)
            {
                foreach (string optionId in optionKeyOrder)
                {
                    if (options.ContainsKey(optionId) && seen.Add(optionId))
                        order.Add(optionId);
                }
            }
            foreach (string optionId in options.Keys)
            {
                if (seen.Add(optionId))
                    order.Add(optionId);
            }
            return order;
        }
    }
}
