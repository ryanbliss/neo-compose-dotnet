// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Stamps a generated <c>Function</c> / <c>NSFunction</c> method with the
    /// Neo member it was generated from (P62 §5.2). Subscribing an action in
    /// C# is spelled as a method group — <c>enemy.OnDamaged += enemy.Ping</c>
    /// — and the resulting <see cref="Delegate"/> carries exactly two facts a
    /// listener needs: its <see cref="Delegate.Target"/> supplies the
    /// <c>valueId</c> and this attribute on its <see cref="Delegate.Method"/>
    /// supplies the <c>memberId</c>. See
    /// <c>NeoGeneratedTypesSupport.ListenerTargetOf</c>, which reads them.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class NeoMemberMethodAttribute : Attribute
    {
        public NeoMemberMethodAttribute(string memberId)
        {
            if (string.IsNullOrEmpty(memberId))
            {
                throw new ArgumentException(
                    "A NeoMemberMethod attribute requires a non-empty member id.",
                    nameof(memberId));
            }
            this.memberId = memberId;
        }

        /// <summary>The Neo member id this generated method implements.</summary>
        public string memberId { get; }
    }
}
