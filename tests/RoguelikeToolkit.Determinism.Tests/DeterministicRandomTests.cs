using RoguelikeToolkit.Determinism;

namespace RoguelikeToolkit.Determinism.Tests;

public sealed class DeterministicRandomTests
{
    // ---- the sequence is a contract -------------------------------------------------------------------------------

    [Fact]
    public void Seed0_MatchesTheReferenceSplitMix64Sequence()
    {
        var rng = new DeterministicRandom(0);

        Assert.Equal(0xE220A8397B1DCDAFUL, rng.NextUInt64());
        Assert.Equal(0x6E789E6AA1B965F4UL, rng.NextUInt64());
        Assert.Equal(0x06C45D188009454FUL, rng.NextUInt64());
        Assert.Equal(0xF88BB8A8724C81ECUL, rng.NextUInt64());
    }

    [Fact]
    public void Seed42_MatchesGoldenValues()
    {
        var rng = new DeterministicRandom(42);

        Assert.Equal(0xBDD732262FEB6E95UL, rng.NextUInt64());
        Assert.Equal(0x28EFE333B266F103UL, rng.NextUInt64());
        Assert.Equal(0x47526757130F9F52UL, rng.NextUInt64());
    }

    [Fact]
    public void NextDouble_MatchesGoldenValues()
    {
        var rng = new DeterministicRandom(7);

        Assert.Equal(0.3898297483912715, rng.NextDouble());
        Assert.Equal(0.01678829452815611, rng.NextDouble());
        Assert.Equal(0.9007606806068834, rng.NextDouble());
    }

    // Values produced by StoryManager's original DeterministicRandom.Derive (which keyed off the parent's current state;
    // for a fresh parent that is the seed). Saves made with picks derived that way keep their streams.
    [Theory]
    [InlineData(0UL, "x", 0x5B8FBFE061FF4B3CUL, 0x75E0C4322B86AC60UL)]
    [InlineData(1UL, "x", 0xA3EFFC9758FE7F86UL, 0x72F3C68DD7ED17C6UL)]
    [InlineData(42UL, "pick:tavern.rumor:7", 0xFF5AF97B839D184EUL, 0x33ACEAFD0E72FDCBUL)]
    [InlineData(ulong.MaxValue, "", 0xCFDFD3F0FB1D0E4CUL, 0xFD0A7F0C1C4AB687UL)]
    public void Derive_IsCompatibleWithTheLegacyImplementation(ulong seed, string salt, ulong first, ulong second)
    {
        var derived = new DeterministicRandom(seed).Derive(salt);

        Assert.Equal(first, derived.NextUInt64());
        Assert.Equal(second, derived.NextUInt64());
    }

    [Fact]
    public void Derive_Nested_MatchesLegacy()
    {
        Assert.Equal(0xAC2FEB900A731D91UL, new DeterministicRandom(42).Derive("a").Derive("b").NextUInt64());
    }

    // ---- state / restore -------------------------------------------------------------------------------------------

    [Fact]
    public void SameSeed_SameSequence()
    {
        var a = new DeterministicRandom(123);
        var b = new DeterministicRandom(123);

        for (var i = 0; i < 1_000; i++)
        {
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
        }
    }

    [Fact]
    public void Restore_ContinuesExactlyWhereTheStateWasTaken()
    {
        var original = new DeterministicRandom(9);
        for (var i = 0; i < 17; i++)
        {
            original.NextUInt64();
        }

        var saved = original.State;
        var reloaded = new DeterministicRandom(9);
        reloaded.Restore(saved);

        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(original.NextUInt64(), reloaded.NextUInt64());
        }
    }

    [Fact]
    public void Seed_IsFixed_EvenAsTheStateAdvances()
    {
        var rng = new DeterministicRandom(55);
        rng.NextUInt64();
        rng.Restore(1234);

        Assert.Equal(55UL, rng.Seed);
    }

    // ---- Derive ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Derive_DoesNotDependOnHowFarTheParentHasAdvanced()
    {
        var fresh = new DeterministicRandom(1);
        var advanced = new DeterministicRandom(1);
        for (var i = 0; i < 500; i++)
        {
            advanced.NextUInt64();
        }

        Assert.Equal(fresh.Derive("loot").NextUInt64(), advanced.Derive("loot").NextUInt64());
    }

    [Fact]
    public void Derive_DoesNotAdvanceTheParent()
    {
        var rng = new DeterministicRandom(1);
        var before = rng.State;

        rng.Derive("a");
        rng.Derive("b");

        Assert.Equal(before, rng.State);
    }

    [Fact]
    public void Derive_DifferentSalts_GiveDifferentStreams()
    {
        var rng = new DeterministicRandom(1);

        Assert.NotEqual(rng.Derive("x").NextUInt64(), rng.Derive("y").NextUInt64());
        Assert.NotEqual(rng.Derive("x").NextUInt64(), new DeterministicRandom(2).Derive("x").NextUInt64());
    }

    [Fact]
    public void Derive_SurvivesSaveAndLoad()
    {
        var original = new DeterministicRandom(77);
        original.NextInt(100);
        var reloaded = new DeterministicRandom(77);
        reloaded.Restore(original.State);

        Assert.Equal(original.Derive("pick:1").NextUInt64(), reloaded.Derive("pick:1").NextUInt64());
    }

    [Fact]
    public void Derive_IsSafeFromManyThreads()
    {
        var rng = new DeterministicRandom(5);
        var expected = rng.Derive("shared").NextUInt64();

        Parallel.For(0, 200, _ => Assert.Equal(expected, rng.Derive("shared").NextUInt64()));
    }

    [Fact]
    public void Derive_NullSalt_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DeterministicRandom(1).Derive(null!));
    }

    // ---- bounded ints ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(1000)]
    [InlineData(int.MaxValue)]
    public void NextInt_StaysInRange(int max)
    {
        var rng = new DeterministicRandom(3);

        for (var i = 0; i < 2_000; i++)
        {
            var v = rng.NextInt(max);
            Assert.InRange(v, 0, max - 1);
        }
    }

    [Fact]
    public void NextInt_IsUniform_WithNoModuloBias()
    {
        // A range that does not divide 2^32 evenly: modulo reduction would visibly favour small values.
        const int buckets = 6;
        const int draws = 600_000;
        var counts = new int[buckets];
        var rng = new DeterministicRandom(2024);

        for (var i = 0; i < draws; i++)
        {
            counts[rng.NextInt(buckets)]++;
        }

        var expected = draws / (double)buckets;
        foreach (var c in counts)
        {
            Assert.InRange(c, expected * 0.98, expected * 1.02);
        }
    }

    [Fact]
    public void Bounded_HasNoModuloBias_WhereItWouldBeLarge()
    {
        // range = 3e9 leaves 2^32 mod range = 1,294,967,296 values that plain `x % range` would produce twice as
        // often: ~60% of results would fall below that threshold instead of the uniform ~43.2%.
        const uint range = 3_000_000_000;
        const uint threshold = 1_294_967_296;
        const int draws = 200_000;
        var rng = new DeterministicRandom(99);

        var below = 0;
        for (var i = 0; i < draws; i++)
        {
            if (rng.Bounded(range) < threshold)
            {
                below++;
            }
        }

        var fraction = below / (double)draws;
        Assert.InRange(fraction, threshold / (double)range - 0.01, threshold / (double)range + 0.01);
    }

    [Fact]
    public void NextInt_MinMax_CoversTheWholeIntRange()
    {
        var rng = new DeterministicRandom(11);

        Assert.InRange(rng.NextInt(-5, 5), -5, 4);
        Assert.Equal(7, rng.NextInt(7, 8));
        for (var i = 0; i < 1_000; i++)
        {
            Assert.InRange(rng.NextInt(int.MinValue, int.MaxValue), int.MinValue, int.MaxValue - 1);
        }
    }

    [Fact]
    public void NextInt_MinMax_ReachesBothEnds()
    {
        var rng = new DeterministicRandom(8);
        var seen = new HashSet<int>();

        for (var i = 0; i < 500; i++)
        {
            seen.Add(rng.NextInt(-2, 3));
        }

        Assert.Equal(new[] { -2, -1, 0, 1, 2 }, seen.OrderBy(x => x).ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NextInt_NonPositiveMax_Throws(int max)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeterministicRandom(1).NextInt(max));
    }

    [Fact]
    public void NextInt_EmptyOrReversedRange_Throws()
    {
        var rng = new DeterministicRandom(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(5, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(5, 4));
    }

    // ---- doubles / bools -------------------------------------------------------------------------------------------

    [Fact]
    public void NextDouble_IsInHalfOpenUnitInterval()
    {
        var rng = new DeterministicRandom(4);

        for (var i = 0; i < 10_000; i++)
        {
            var d = rng.NextDouble();
            Assert.True(d >= 0.0 && d < 1.0);
        }
    }

    [Fact]
    public void Chance_RespectsTheExtremesAndRoughlyTheProbability()
    {
        var rng = new DeterministicRandom(6);

        Assert.DoesNotContain(true, Enumerable.Range(0, 1_000).Select(_ => rng.Chance(0.0)));
        Assert.DoesNotContain(false, Enumerable.Range(0, 1_000).Select(_ => rng.Chance(1.0)));
        var hits = Enumerable.Range(0, 100_000).Count(_ => rng.Chance(0.25));
        Assert.InRange(hits, 24_000, 26_000);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void Chance_OutOfRange_Throws(double p)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeterministicRandom(1).Chance(p));
    }

    [Fact]
    public void NextBool_ProducesBothValues()
    {
        var rng = new DeterministicRandom(10);
        var trues = Enumerable.Range(0, 10_000).Count(_ => rng.NextBool());

        Assert.InRange(trues, 4_700, 5_300);
    }

    // ---- weighted pick ---------------------------------------------------------------------------------------------

    [Fact]
    public void WeightedIndex_MatchesTheLegacyPickAlgorithm_ForPositiveWeights()
    {
        // StoryManager's MachineEngine.Pick: r = NextDouble * total; subtract weights until negative.
        static int Legacy(DeterministicRandom rng, double[] w)
        {
            var r = rng.NextDouble() * w.Sum();
            int chosen;
            for (chosen = 0; chosen < w.Length - 1; chosen++)
            {
                r -= w[chosen];
                if (r < 0)
                {
                    break;
                }
            }

            return chosen;
        }

        var weights = new[] { 5.0, 1.0, 3.0, 0.5 };
        var a = new DeterministicRandom(99);
        var b = new DeterministicRandom(99);

        for (var i = 0; i < 5_000; i++)
        {
            Assert.Equal(Legacy(a, weights), b.WeightedIndex(weights));
        }
    }

    [Fact]
    public void WeightedIndex_FollowsTheWeights()
    {
        var rng = new DeterministicRandom(31);
        var counts = new int[3];

        for (var i = 0; i < 100_000; i++)
        {
            counts[rng.WeightedIndex(new[] { 1.0, 2.0, 7.0 })]++;
        }

        Assert.InRange(counts[0], 9_300, 10_700);
        Assert.InRange(counts[1], 19_000, 21_000);
        Assert.InRange(counts[2], 68_500, 71_500);
    }

    [Fact]
    public void WeightedIndex_NeverChoosesAZeroWeight_EvenAtTheEnds()
    {
        var rng = new DeterministicRandom(32);

        for (var i = 0; i < 20_000; i++)
        {
            var idx = rng.WeightedIndex(new[] { 0.0, 3.0, 0.0, 1.0, 0.0 });
            Assert.True(idx == 1 || idx == 3, $"chose zero-weight index {idx}");
        }
    }

    [Fact]
    public void WeightedIndex_ConsumesExactlyOneDraw()
    {
        var a = new DeterministicRandom(5);
        var b = new DeterministicRandom(5);

        a.WeightedIndex(new[] { 1.0, 1.0 });
        a.WeightedIndex(new[] { 9.0, 0.0, 4.0, 1.0 });
        b.NextDouble();
        b.NextDouble();

        Assert.Equal(b.State, a.State);
    }

    [Fact]
    public void WeightedIndex_NullWeights_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DeterministicRandom(1).WeightedIndex(null!));
    }

    [Fact]
    public void WeightedIndex_SingleCandidate_IsIndexZero()
    {
        Assert.Equal(0, new DeterministicRandom(1).WeightedIndex(new[] { 2.5 }));
    }

    [Theory]
    [InlineData(new double[0])]
    [InlineData(new[] { 0.0, 0.0 })]
    [InlineData(new[] { 1.0, -1.0 })]
    [InlineData(new[] { 1.0, double.NaN })]
    [InlineData(new[] { 1.0, double.PositiveInfinity })]
    public void WeightedIndex_InvalidWeights_Throw(double[] weights)
    {
        Assert.Throws<ArgumentException>(() => new DeterministicRandom(1).WeightedIndex(weights));
    }

    // ---- shuffle ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Shuffle_IsAPermutation_AndDeterministic()
    {
        var a = Enumerable.Range(0, 52).ToList();
        var b = Enumerable.Range(0, 52).ToList();

        new DeterministicRandom(1).Shuffle(a);
        new DeterministicRandom(1).Shuffle(b);

        Assert.Equal(a, b);
        Assert.Equal(Enumerable.Range(0, 52), a.OrderBy(x => x));
        Assert.NotEqual(Enumerable.Range(0, 52), a);
    }

    [Fact]
    public void Shuffle_EveryPositionGetsEveryValue()
    {
        const int n = 4;
        const int rounds = 24_000;
        var counts = new int[n, n];
        var rng = new DeterministicRandom(17);

        for (var r = 0; r < rounds; r++)
        {
            var list = Enumerable.Range(0, n).ToList();
            rng.Shuffle(list);
            for (var pos = 0; pos < n; pos++)
            {
                counts[pos, list[pos]]++;
            }
        }

        for (var pos = 0; pos < n; pos++)
        {
            for (var value = 0; value < n; value++)
            {
                Assert.InRange(counts[pos, value], rounds / n * 0.93, rounds / n * 1.07);
            }
        }
    }

    [Fact]
    public void Shuffle_EmptyAndSingle_AreNoOps()
    {
        var rng = new DeterministicRandom(1);
        var state = rng.State;

        rng.Shuffle(new List<int>());
        rng.Shuffle(new List<int> { 1 });

        Assert.Equal(state, rng.State);
        Assert.Throws<ArgumentNullException>(() => rng.Shuffle<int>(null!));
    }
}
