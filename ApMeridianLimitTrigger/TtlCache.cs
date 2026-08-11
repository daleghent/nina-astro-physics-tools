#region "copyright"

/*
    Copyright Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

using System;

namespace DaleGhent.NINA.AstroPhysicsTools.ApMeridianLimitTrigger {

    /// <summary>
    /// A value paired with the moment it was stored, served only while it is young enough.
    ///
    /// Everything the trigger reads from APCC changes on human timescales while being asked for on the
    /// UI's cadence, so each of those reads is fronted by one of these. Holding the value, its age, and
    /// the lock that guards them in one place keeps the several caches from drifting apart in how they
    /// expire, which they previously could since each carried its own copy of this logic.
    /// </summary>
    internal sealed class TtlCache<T> {
        private readonly TimeSpan ttl;
        private readonly object sync = new();

        private T value;
        private DateTime storedAt = DateTime.MinValue;
        private bool hasValue = false;

        public TtlCache(TimeSpan ttl) {
            this.ttl = ttl;
        }

        /// <summary>The lock guarding this cache, for callers that must extend it over related state.</summary>
        public object SyncRoot => sync;

        /// <summary>
        /// The stored value when one is present and younger than the cache's time to live.
        /// </summary>
        public bool TryGet(out T cached) => TryGet(ttl, out cached);

        /// <summary>
        /// The stored value when one is present and younger than <paramref name="maxAge"/>. Used to serve
        /// a value that has expired for refresh purposes but is still recent enough to be acted upon.
        /// </summary>
        public bool TryGet(TimeSpan maxAge, out T cached) {
            lock (sync) {
                if (hasValue && DateTime.UtcNow - storedAt < maxAge) {
                    cached = value;
                    return true;
                }

                cached = default;
                return false;
            }
        }

        public void Set(T newValue) {
            lock (sync) {
                value = newValue;
                storedAt = DateTime.UtcNow;
                hasValue = true;
            }
        }

        /// <summary>
        /// Discards the stored value so the next read is served from source. Used when something the
        /// value was derived from changes, rather than merely when it ages out.
        /// </summary>
        public void Invalidate() {
            lock (sync) {
                value = default;
                storedAt = DateTime.MinValue;
                hasValue = false;
            }
        }
    }
}
