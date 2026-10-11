#nullable enable

using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.Determinism
{
    /// <summary>
    /// Seeded, snapshot-able random generator (SplitMix64). The same seed produces the same sequence on every
    /// platform and runtime, and the sequence is part of this type's contract: changing it would break saved games
    /// and replays, so it only ever changes in a major version.
    /// <para>
    /// <b>Independent streams.</b> <see cref="Derive"/> keys a new generator off the immutable <see cref="Seed"/>,
    /// never off the current <see cref="State"/>. A derived stream therefore does not depend on how many numbers the
    /// parent has produced, and adding a consumer of the parent never reshuffles anyone's derived stream.
    /// </para>
    /// <para>Not thread-safe (like <see cref="Random"/>); <see cref="Derive"/> is, because it only reads the seed.
    /// Not cryptographically secure.</para>
    /// </summary>
    public sealed class DeterministicRandom
    {
        private const ulong Gamma = 0x9E3779B97F4A7C15UL;
        private ulong _state;

        /// <summary>Creates a generator for <paramref name="seed"/>.</summary>
        /// <param name="seed">The seed. Also the key for <see cref="Derive"/>.</param>
        public DeterministicRandom(ulong seed)
        {
            Seed = seed;
            _state = seed;
        }

        /// <summary>Gets the seed this generator was created with. Never changes.</summary>
        public ulong Seed { get; }

        /// <summary>Gets the opaque internal state, for save files. Pair with <see cref="Restore"/>.</summary>
        public ulong State => _state;

        /// <summary>
        /// Puts the generator back to a state previously read from <see cref="State"/>. Create the generator with the
        /// original seed first so that <see cref="Derive"/> keeps producing the same streams.
        /// </summary>
        /// <param name="state">A value obtained from <see cref="State"/>.</param>
        public void Restore(ulong state)
        {
            _state = state;
        }

        /// <summary>Returns the next 64 random bits.</summary>
        /// <returns>A uniformly distributed value.</returns>
        public ulong NextUInt64()
        {
            var z = _state += Gamma;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Returns the next 32 random bits (the high half of <see cref="NextUInt64"/>).</summary>
        /// <returns>A uniformly distributed value.</returns>
        public uint NextUInt32()
        {
            return (uint)(NextUInt64() >> 32);
        }

        /// <summary>Returns a uniform double in [0, 1) with 53 bits of precision.</summary>
        /// <returns>A value that is at least 0 and less than 1.</returns>
        public double NextDouble()
        {
            return (NextUInt64() >> 11) * (1.0 / (1UL << 53));
        }

        /// <summary>Returns a uniform int in [0, <paramref name="maxExclusive"/>), without modulo bias.</summary>
        /// <param name="maxExclusive">The exclusive upper bound; must be positive.</param>
        /// <returns>A value that is at least 0 and less than <paramref name="maxExclusive"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxExclusive"/> is not positive.</exception>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Must be positive.");
            }

            return (int)Bounded((uint)maxExclusive);
        }

        /// <summary>Returns a uniform int in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>), without modulo bias.</summary>
        /// <param name="minInclusive">The inclusive lower bound.</param>
        /// <param name="maxExclusive">The exclusive upper bound; must be greater than <paramref name="minInclusive"/>.</param>
        /// <returns>A value in the requested range.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxExclusive"/> is not greater than <paramref name="minInclusive"/>.</exception>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Must be greater than minInclusive.");
            }

            var range = (uint)((long)maxExclusive - minInclusive);
            return (int)(minInclusive + (long)Bounded(range));
        }

        /// <summary>Returns <c>true</c> or <c>false</c> with equal probability.</summary>
        /// <returns>A random boolean.</returns>
        public bool NextBool()
        {
            return (NextUInt64() & 1UL) != 0;
        }

        /// <summary>Returns <c>true</c> with the given probability.</summary>
        /// <param name="probability">A probability from 0 (never) to 1 (always).</param>
        /// <returns>Whether the event happened.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="probability"/> is outside [0, 1] or NaN.</exception>
        public bool Chance(double probability)
        {
            if (!(probability >= 0.0 && probability <= 1.0))
            {
                throw new ArgumentOutOfRangeException(nameof(probability), probability, "Must be between 0 and 1.");
            }

            return NextDouble() < probability;
        }

        /// <summary>
        /// Picks an index with probability proportional to its weight. Consumes exactly one <see cref="NextDouble"/>,
        /// so a weighted pick advances the stream by the same amount whatever the weights are.
        /// </summary>
        /// <param name="weights">Non-negative, finite weights; at least one must be positive.</param>
        /// <returns>The chosen index. An index with weight 0 is never chosen.</returns>
        /// <exception cref="ArgumentException">No weights, a negative or non-finite weight, or all weights zero.</exception>
        public int WeightedIndex(IReadOnlyList<double> weights)
        {
            if (weights is null)
            {
                throw new ArgumentNullException(nameof(weights));
            }

            double total = 0;
            var lastPositive = -1;
            for (var i = 0; i < weights.Count; i++)
            {
                var w = weights[i];
                if (double.IsNaN(w) || double.IsInfinity(w) || w < 0)
                {
                    throw new ArgumentException($"Weight at index {i} must be finite and not negative.", nameof(weights));
                }

                if (w > 0)
                {
                    total += w;
                    lastPositive = i;
                }
            }

            if (lastPositive < 0)
            {
                throw new ArgumentException("At least one weight must be positive.", nameof(weights));
            }

            var r = NextDouble() * total;
            for (var i = 0; i < lastPositive; i++)
            {
                r -= weights[i];
                if (r < 0)
                {
                    return i;
                }
            }

            return lastPositive;
        }

        /// <summary>Shuffles <paramref name="list"/> in place (Fisher-Yates).</summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="list">The list to shuffle.</param>
        public void Shuffle<T>(IList<T> list)
        {
            if (list is null)
            {
                throw new ArgumentNullException(nameof(list));
            }

            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = NextInt(i + 1);
                var tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        /// <summary>
        /// Creates an independent generator keyed by <paramref name="salt"/> and this generator's <see cref="Seed"/>.
        /// Does not advance this generator and does not depend on how far it has advanced, so deriving is
        /// order-independent: the same (seed, salt) always gives the same stream.
        /// </summary>
        /// <param name="salt">Names the stream, for example <c>"loot:" + chestId</c>. Different salts give uncorrelated streams.</param>
        /// <returns>A new generator whose <see cref="Seed"/> is the derived seed.</returns>
        public DeterministicRandom Derive(string salt)
        {
            if (salt is null)
            {
                throw new ArgumentNullException(nameof(salt));
            }

            // FNV-1a over UTF-16 code units: platform independent, unlike string.GetHashCode.
            var h = 14695981039346656037UL;
            for (var i = 0; i < salt.Length; i++)
            {
                h ^= salt[i];
                h *= 1099511628211UL;
            }

            return new DeterministicRandom(new DeterministicRandom(Seed ^ h).NextUInt64());
        }

        // Lemire's multiply-shift with rejection: uniform in [0, range) using 32-bit draws.
        internal uint Bounded(uint range)
        {
            var m = (ulong)NextUInt32() * range;
            var low = (uint)m;
            if (low < range)
            {
                var threshold = (uint)((0x1_0000_0000UL - range) % range);
                while (low < threshold)
                {
                    m = (ulong)NextUInt32() * range;
                    low = (uint)m;
                }
            }

            return (uint)(m >> 32);
        }
    }
}
