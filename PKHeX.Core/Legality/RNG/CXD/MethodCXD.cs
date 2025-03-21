using System;

namespace PKHeX.Core;

/// <summary>
/// Generator methods for <see cref="GameVersion.CXD"/>.
/// </summary>
public static class MethodCXD
{
    /// <summary>
    /// Tries to set a valid PID/IV for the requested criteria for an <see cref="IShadow3"/> object.
    /// </summary>
    /// <remarks>Only called if <see cref="EncounterCriteria.IsSpecifiedIVsAll"/> is true.</remarks>
    public static bool SetFromIVs<TEnc, TEntity>(this TEnc enc, TEntity pk, EncounterCriteria criteria, PersonalInfo3 pi, bool noShiny = false)
        where TEnc : IShadow3
        where TEntity : G3PKM
    {
        var gr = pi.Gender;
        criteria.GetCombinedIVs(out var iv1, out var iv2);
        Span<uint> all = stackalloc uint[XDRNG.MaxCountSeedsIV];
        var count = XDRNG.GetSeedsIVs(all, iv1 << 16, iv2 << 16);
        var seeds = all[..count];
        foreach (var seed in seeds)
        {
            // * => IV, IV, ability, PID, PID
            var s = XDRNG.Next3(seed);
            uint pid = GetPID(pk, s, noShiny);
            if (criteria.IsSpecifiedNature() && !criteria.IsSatisfiedNature((Nature)(pid % 25)))
                continue;

            if (criteria.IsSpecifiedGender() && !criteria.IsSatisfiedGender(EntityGender.GetFromPIDAndRatio(pid, gr)))
                continue;

            var result = LockFinder.IsAllShadowLockValid(enc, seed, pk);
            if (!result)
                continue;

            pk.PID = pid;
            pk.Ability = ((XDRNG.Next2(seed) >> 16) & 1) == 0 ? pi.Ability1 : pi.Ability2;
            criteria.SetRandomIVs(pk);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Tries to set a valid PID/IV for the requested criteria for a non-shadow encounter.
    /// </summary>
    /// <remarks>Only called if <see cref="EncounterCriteria.IsSpecifiedIVsAll"/> is true.</remarks>
    public static bool SetFromIVsCXD<TEntity>(TEntity pk, EncounterCriteria criteria, PersonalInfo3 pi, bool noShiny = true)
        where TEntity : G3PKM
    {
        var gr = pi.Gender;
        criteria.GetCombinedIVs(out var iv1, out var iv2);
        Span<uint> all = stackalloc uint[XDRNG.MaxCountSeedsIV];
        var count = XDRNG.GetSeedsIVs(all, iv1 << 16, iv2 << 16);
        var seeds = all[..count];
        foreach (var seed in seeds)
        {
            // * => IV, IV, ability, PID, PID
            var s = XDRNG.Next3(seed);
            uint pid = GetPID(pk, s, noShiny);
            if (criteria.IsSpecifiedNature() && !criteria.IsSatisfiedNature((Nature)(pid % 25)))
                continue;

            if (criteria.IsSpecifiedGender() && !criteria.IsSatisfiedGender(EntityGender.GetFromPIDAndRatio(pid, gr)))
                continue;

            if (!noShiny && criteria.Shiny.IsShiny() != ShinyUtil.GetIsShiny(pk.ID32, pid, 8))
                continue;

            pk.PID = pid;
            pk.Ability = ((XDRNG.Next2(seed) >> 16) & 1) == 0 ? pi.Ability1 : pi.Ability2;
            criteria.SetRandomIVs(pk);
            return true;
        }

        return false;
    }

    // Call structure for Colosseum Starters:
    // u16 TID
    // u16 SID
    //
    // Umbreon:
    // {u16 fakeID, u16 fakeID}
    // u16 iv1
    // u16 iv2
    // u16 ability
    // {u16 PID, u16 PID} repeat until male and not shiny
    //
    // Espeon:
    // Repeat above

    /// <summary>
    /// Get the first possible starting seed that generates the given trainer ID and secret ID.
    /// </summary>
    /// <param name="tid">Generation 3 Trainer ID</param>
    /// <param name="sid">Generation 3 Secret ID</param>
    /// <param name="origin">Possible starting seed</param>
    /// <returns>True if a seed was found, false if no seed was found</returns>
    public static bool TryGetSeedTrainerID(ushort tid, ushort sid, out uint origin)
    {
        Span<uint> seeds = stackalloc uint[XDRNG.MaxCountSeedsPID];
        var count = XDRNG.GetSeeds(seeds, (uint)tid << 16, (uint)sid << 16);
        foreach (var s in seeds[..count])
        {
            var prev1k = XDRNG.Prev1000(s);
            if (IsReachableFromNameScreen(prev1k, out origin))
                return true;
        }
        origin = 0;
        return false;
    }

    public static bool TryGetShinySID(ushort tid, out ushort sid, uint xor, uint bits)
    {
        for (int i = 0; i < 8; i++, bits++)
        {
            var newSID = (ushort)(xor ^ (bits & 7));
            if (!TryGetSeedTrainerID(tid, newSID, out _))
                continue;
            sid = newSID;
            return true;
        }
        sid = 0;
        return false;
    }

    /// <summary>
    /// Tries to set a valid PID/IV for the requested criteria for a Colosseum starter (Umbreon and Espeon), preferring to match Trainer IDs.
    /// </summary>
    public static bool SetStarterFromTrainerID(CK3 pk, EncounterCriteria criteria, ushort tid, ushort sid)
    {
        var id32 = (uint)sid << 16 | tid;
        var species = pk.Species;
        // * => TID, SID, fakepid*2, [IVs, ability, PID]
        Span<uint> seeds = stackalloc uint[XDRNG.MaxCountSeedsPID];
        var count = XDRNG.GetSeeds(seeds, (uint)tid << 16, (uint)sid << 16);
        foreach (var seed in seeds[..count])
        {
            // Seed is immediately before the TID/SID. Need to jump down to the target.
            var s = XDRNG.Next7(seed); // tid/sid (2), fake pid (2), ivs (2), ability (1)

            // Generate the first starter
            uint prePID = s; // keep track of the ability frame to get IVs later
            uint pid = GetColoStarterPID(ref s, id32);

            // If we're looking for Espeon, generate the next starter
            if (species == (int)Species.Espeon) // After Umbreon
            {
                s = XDRNG.Next5(s); // fake pid (2), ivs (2), ability (1)
                prePID = s;
                pid = GetColoStarterPID(ref s, id32);
            }

            if (criteria.IsSpecifiedNature() && !criteria.IsSatisfiedNature((Nature)(pid % 25)))
                continue;

            // prePID is the ability frame, unroll 2x to get the IVs
            var iv2 = XDRNG.Prev15(ref prePID);
            var iv1 = XDRNG.Prev15(ref prePID);
            var iv32 = iv2 << 15 | iv1;

            if (criteria.IsSpecifiedHiddenPower() && !criteria.IsSatisfiedHiddenPower(iv32))
                continue;

            pk.PID = pid;
            SetIVs(pk, iv1, iv2);
            pk.Ability = (ushort)Ability.Synchronize; // Only one ability available, don't bother checking rand().
            return true;
        }

        return false;
    }

    /// <summary>
    /// Umbreon is generated first, so no need for complicated backwards exploration.
    /// </summary>
    public static bool SetStarterFirstFromIVs(CK3 pk, EncounterCriteria criteria)
    {
        Span<uint> seeds = stackalloc uint[XDRNG.MaxCountSeedsIV];
        criteria.GetCombinedIVs(out var iv1, out var iv2);
        var count = XDRNG.GetSeedsIVs(seeds, iv1 << 16, iv2 << 16);
        foreach (var seed in seeds[..count])
        {
            // * => IV, IV, ability, PID, PID
            var s = XDRNG.Next3(seed);

            // Oh, no! we need to know the Trainer ID to generate the starters correctly.
            var tid = XDRNG.Prev3(seed) >> 16;
            var sid = XDRNG.Prev2(seed) >> 16;
            var id32 = sid << 16 | tid;

            uint pid = GetColoStarterPID(ref s, id32);

            if (criteria.IsSpecifiedNature() && !criteria.IsSatisfiedNature((Nature)(pid % 25)))
                continue;

            pk.TID16 = (ushort)tid;
            pk.SID16 = (ushort)sid;
            pk.PID = pid;
            pk.Ability = (ushort)Ability.Synchronize; // Only one ability available, don't bother checking rand().
            SetIVs(pk, iv1, iv2);
            return true;
        }

        return false;
    }

    public static bool SetStarterSecondFromIVs(CK3 pk, EncounterCriteria criteria)
    {
        Span<uint> seeds = stackalloc uint[XDRNG.MaxCountSeedsIV];
        criteria.GetCombinedIVs(out var iv1, out var iv2);
        var count = XDRNG.GetSeedsIVs(seeds, iv1 << 16, iv2 << 16);
        foreach (var seed in seeds[..count])
        {
            // The prior generated starter must be Umbreon with a valid PID lock for us to reach here.
            var prePID = XDRNG.Prev(seed) >> 16;
            var second = seed >> 16;
            var first = prePID >> 16;

            var umbreon = GetPIDRegular(first, second);
            if (!IsMaleEevee(umbreon))
                continue;

            // Lock constraint satisfied (1:8 chance), generate the PID for Espeon.
            // * => IV, IV, ability, PID, PID
            var frameEspeonPID = XDRNG.Next3(seed);
            // Oh, no! we need to know the Trainer ID to generate the starters correctly.
            // Look backwards from Umbreon to find all possible trainer IDs for that PID, each step generate an Espeon.
            uint espeonPID, sid, tid;
            while (true)
            {
                // Find Trainer ID for this Umbreon frame.
                var s = LCRNG.Prev7(prePID); // hypothetical origin seed
                sid = LCRNG.Next16(ref s);
                tid = LCRNG.Next16(ref s);
                var id32 = sid << 16 | tid;
                if (IsValidTrainerCombination(criteria, id32, umbreon, frameEspeonPID, out espeonPID))
                    break;

                // Try the next possible Trainer ID
                prePID = LCRNG.Prev2(prePID);
                umbreon = GetPID(prePID);
                if (IsMaleEevee(umbreon))
                    return false; // Interrupted.
                // Try again
            }

            pk.TID16 = (ushort)tid;
            pk.SID16 = (ushort)sid;
            pk.PID = espeonPID;
            pk.Ability = (ushort)Ability.Synchronize; // Only one ability available, don't bother checking rand().
            SetIVs(pk, iv1, iv2);
            return true;
        }

        return false;
    }

    private static bool IsValidTrainerCombination(EncounterCriteria criteria, uint id32, uint umbreon, uint pidSeed, out uint espeonPID)
    {
        espeonPID = 0;
        if (ShinyUtil.GetIsShiny(id32, umbreon))
            return false; // Umbreon cannot be shiny

        // Generate Espeon PID
        espeonPID = GetColoStarterPID(ref pidSeed, id32);

        // Check if request constraints satisfied
        if (criteria.IsSpecifiedNature() && !criteria.IsSatisfiedNature((Nature)(espeonPID % 25)))
            return false;
        return false;
    }

    // Call structure for XD Starter:
    // u16 TID
    // u16 SID
    //
    // Eevee:
    // {u16 fakeID, u16 fakeID}
    // u16 iv1
    // u16 iv2
    // u16 ability
    // {u16 PID, u16 PID}

    /// <summary>
    /// Tries to set a valid PID/IV for the requested criteria for the XD starter (Eevee), preferring to match Trainer IDs.
    /// </summary>
    public static bool SetStarterFromTrainerID(XK3 pk, EncounterCriteria criteria, ushort tid, ushort sid)
    {
        // * => TID, SID, fakepid*2, [IVs, ability, PID]
        Span<uint> all = stackalloc uint[XDRNG.MaxCountSeedsPID];
        var count = XDRNG.GetSeeds(all, (uint)tid << 16, (uint)sid << 16);
        var seeds = all[..count];
        foreach (ref var seed in seeds)
        {
            // Seed is immediately before the TID/SID. Need to jump down to the target.
            var s = XDRNG.Next7(seed); // tid/sid (2), fake pid (2), ivs (2), ability (1)

            uint pid = GetPID(s);
            if (criteria.IsSpecifiedNature() && !criteria.IsSatisfiedNature((Nature)(pid % 25)))
                continue;
            if (criteria.IsSpecifiedGender() && !criteria.IsSatisfiedGender(EntityGender.GetFromPIDAndRatio(pid, 0x1F)))
                continue;

            var ivSeed = XDRNG.Next4(seed);
            var iv1 = XDRNG.Next15(ref ivSeed);
            var iv2 = XDRNG.Next15(ref ivSeed);
            var iv32 = iv2 << 15 | iv1;

            if (criteria.IsSpecifiedHiddenPower() && !criteria.IsSatisfiedHiddenPower(iv32))
                continue;
            if (criteria.Shiny.IsShiny() != ShinyUtil.GetIsShiny(pk.ID32, pid, 8))
                continue;

            SetIVs(pk, iv1, iv2);
            pk.PID = pid;
            pk.Ability = (ushort)Ability.RunAway; // Only one ability available, don't bother checking rand().
            return true;
        }

        return false;
    }

    public static bool SetStarterFromIVs(XK3 pk, EncounterCriteria criteria)
    {
        Span<uint> seeds = stackalloc uint[XDRNG.MaxCountSeedsIV];
        criteria.GetCombinedIVs(out var iv1, out var iv2);
        var count = XDRNG.GetSeedsIVs(seeds, iv1 << 16, iv2 << 16);
        foreach (var seed in seeds[..count])
        {
            // * => IV, IV, ability, PID, PID
            var s = XDRNG.Next3(seed);

            uint pid = GetPID(s);

            if (criteria.IsSpecifiedNature() && !criteria.IsSatisfiedNature((Nature)(pid % 25)))
                continue;

            var tid = XDRNG.Prev3(seed) >> 16;
            var sid = XDRNG.Prev2(seed) >> 16;
            var id32 = sid << 16 | tid;
            if (criteria.Shiny.IsShiny() != ShinyUtil.GetIsShiny(id32, pid, 8))
                continue;

            pk.TID16 = (ushort)tid;
            pk.SID16 = (ushort)sid;
            pk.PID = pid;
            pk.Ability = (ushort)Ability.Synchronize; // Only one ability available, don't bother checking rand().
            SetIVs(pk, iv1, iv2);
            return true;
        }

        return false;
    }

    public static void SetRandomStarter(XK3 pk, EncounterCriteria criteria)
    {
        var seed = Util.Rand32();

        bool filterIVs = criteria.IsSpecifiedIVsAny(out var count) && count <= 2;
        while (true)
        {
            var start = seed;
            // Get PID
            seed = XDRNG.Next7(seed); // fakePID x2, IVs x2, ability
            var pid = GetPIDRegular(ref seed);
            if (criteria.IsSpecifiedNature() && !criteria.IsSatisfiedNature((Nature)(pid % 25)))
                continue;
            if (criteria.IsSpecifiedGender() && !criteria.IsSatisfiedGender(EntityGender.GetFromPIDAndRatio(pid, GenderRatioMale87_5)))
                continue;

            var tid = XDRNG.Next16(ref start);
            var sid = XDRNG.Next16(ref start);
            var id32 = sid << 16 | tid;
            if (criteria.Shiny.IsShiny() != ShinyUtil.GetIsShiny(id32, pid, 8))
                continue;

            // Get IVs
            seed = XDRNG.Next2(start);
            var iv1 = XDRNG.Next15(ref seed);
            var iv2 = XDRNG.Next15(ref seed);
            var iv32 = iv2 << 15 | iv1;
            if (criteria.IsSpecifiedHiddenPower() && !criteria.IsSatisfiedHiddenPower(iv32))
                continue;
            if (filterIVs && !criteria.IsSatisfiedIVs(iv32))
                continue;

            pk.TID16 = (ushort)tid;
            pk.SID16 = (ushort)sid;
            pk.PID = pid;
            SetIVs(pk, iv1, iv2);
            pk.Ability = (ushort)Ability.RunAway;
            return;
        }
    }

    public static void SetRandom(CK3 pk, EncounterCriteria criteria, byte gender)
    {
        var id32 = pk.ID32;
        var seed = Util.Rand32();

        bool filterIVs = criteria.IsSpecifiedIVsAny(out var count) && count <= 2;
        while (true)
        {
            // Get PID
            var start = seed;
            seed = XDRNG.Next5(seed); // fakePID x2, IVs x2, ability
            var pid = GetPIDRegular(ref seed);
            if (criteria.IsSpecifiedNature() && !criteria.IsSatisfiedNature((Nature)(pid % 25)))
                continue;
            if (criteria.IsSpecifiedGender() && !criteria.IsSatisfiedGender(EntityGender.GetFromPIDAndRatio(pid, gender)))
                continue;

            if (ShinyUtil.GetIsShiny(id32, pid, 8))
                continue;

            // Get IVs
            seed = XDRNG.Next2(start);
            var iv1 = XDRNG.Next15(ref seed);
            var iv2 = XDRNG.Next15(ref seed);
            var iv32 = iv2 << 15 | iv1;
            if (criteria.IsSpecifiedHiddenPower() && !criteria.IsSatisfiedHiddenPower(iv32))
                continue;
            if (filterIVs && !criteria.IsSatisfiedIVs(iv32))
                continue;

            var abit = XDRNG.Next16(ref seed);
            pk.PID = pid;
            SetIVs(pk, iv1, iv2);
            pk.RefreshAbility((int)(abit & 1));
            return;
        }
    }

    public static void SetRandom(XK3 pk, EncounterCriteria criteria, byte gender)
    {
        var id32 = pk.ID32;
        var seed = Util.Rand32();

        bool filterIVs = criteria.IsSpecifiedIVsAny(out var count) && count <= 2;
        while (true)
        {
            // Get PID
            var start = seed;
            seed = XDRNG.Next5(seed); // fakePID x2, IVs x2, ability
            var pid = GetPIDRegular(ref seed);
            if (criteria.IsSpecifiedNature() && !criteria.IsSatisfiedNature((Nature)(pid % 25)))
                continue;
            if (criteria.IsSpecifiedGender() && !criteria.IsSatisfiedGender(EntityGender.GetFromPIDAndRatio(pid, gender)))
                continue;

            if (criteria.Shiny.IsShiny() != ShinyUtil.GetIsShiny(id32, pid, 8))
                continue;

            // Get IVs
            seed = XDRNG.Next2(start);
            var iv1 = XDRNG.Next15(ref seed);
            var iv2 = XDRNG.Next15(ref seed);
            var iv32 = iv2 << 15 | iv1;
            if (criteria.IsSpecifiedHiddenPower() && !criteria.IsSatisfiedHiddenPower(iv32))
                continue;
            if (filterIVs && !criteria.IsSatisfiedIVs(iv32))
                continue;

            var abit = XDRNG.Next16(ref seed);
            pk.PID = pid;
            SetIVs(pk, iv1, iv2);
            pk.RefreshAbility((int)(abit & 1));
            return;
        }
    }

    private static uint GetPID(uint seed) => GetPIDRegular(ref seed);

    private static uint GetPID<T>(T pk, uint seed, bool noShiny) where T : ITrainerID32
    {
        while (true)
        {
            uint pid = GetPIDRegular(ref seed);
            if (!noShiny || !ShinyUtil.GetIsShiny(pk.ID32, pid))
                return pid;
        }
    }

    private static uint GetColoStarterPID(ref uint seed, uint id32)
    {
        while (true)
        {
            uint pid = GetPIDRegular(ref seed);
            if (IsValidStarterColo(id32, pid))
                return pid;
        }
    }

    private const byte GenderRatioMale87_5 = 0x1F; // 87.5%

    private static bool IsMaleEevee(uint pid) => (pid & 0xFF) >= GenderRatioMale87_5;

    private static bool IsValidStarterColo(uint id32, uint pid)
        => IsMaleEevee(pid) && !ShinyUtil.GetIsShiny(id32, pid, 8);

    private static bool IsMatchIVs(uint iv1, uint iv2, uint seed)
    {
        if (iv1 != XDRNG.Next15(ref seed))
            return false;
        if (iv2 != XDRNG.Next15(ref seed))
            return false;
        return true;
    }

    public static uint GetColoStarterPID(ref uint seed, ushort tid, ushort sid)
    {
        var id32 = (uint)sid << 16 | tid;
        return GetColoStarterPID(ref seed, id32);
    }

    private static uint GetPIDRegular(ref uint seed)
    {
        var a = XDRNG.Next16(ref seed);
        var b = XDRNG.Next16(ref seed);
        return GetPIDRegular(a, b);
    }

    private static uint GetPIDRegular(uint a, uint b) => a << 16 | b;

    private static void SetIVs<TEntity>(TEntity pk, uint iv1, uint iv2)
        where TEntity : G3PKM
        => pk.SetIVs(iv2 << 15 | iv1);

    private static (uint iv1, uint iv2) GetIVs(PKM pk)
    {
        uint iv32 = pk.GetIVs();
        var iv1 = iv32 & 0x7FFF;
        var iv2 = iv32 >> 15;
        return (iv1, iv2);
    }

    public static bool TryGetOriginSeedStarterColo(PKM pk, out uint result)
    {
        (uint iv1, uint iv2) = GetIVs(pk);
        var tid = pk.TID16;
        var sid = pk.SID16;
        var species = pk.Species;
        var expectPID = pk.EncryptionConstant;
        return TryGetOriginSeedStarterColo(iv1, iv2, expectPID, tid, sid, species, out result);
    }

    public static bool TryGetOriginSeedStarterXD(PKM pk, out uint result)
    {
        (uint iv1, uint iv2) = GetIVs(pk);
        var tid = pk.TID16;
        var sid = pk.SID16;
        var expectPID = pk.EncryptionConstant;
        return TryGetOriginSeedStarterXD(iv1, iv2, expectPID, tid, sid, out result);
    }

    public static bool TryGetOriginSeedStarterColo(uint iv1, uint iv2,
        uint expectPID,
        ushort tid, ushort sid,
        ushort species, out uint result)
    {
        var id32 = (uint)sid << 16 | tid;
        // * => TID, SID, fakepid*2, [IVs, ability, PID]
        Span<uint> seeds = stackalloc uint[XDRNG.MaxCountSeedsPID];
        var count = XDRNG.GetSeeds(seeds, (uint)tid << 16, (uint)sid << 16);
        foreach (var seed in seeds[..count])
        {
            if (species is (ushort)Species.Umbreon)
            {
                // Check IVs
                var ivSeed = XDRNG.Next4(seed);
                if (!IsMatchIVs(iv1, iv2, ivSeed))
                    continue;
            }
            var pidSeedU = XDRNG.Next7(seed);
            var pid = GetColoStarterPID(ref pidSeedU, id32);
            if (species is (ushort)Species.Umbreon)
            {
                if (pid != expectPID)
                    continue;
                result = seed;
                return true;
            }

            // Espeon
            var afterFake = XDRNG.Next2(pidSeedU);
            if (!IsMatchIVs(iv1, iv2, afterFake))
                continue;
            var pidSeedE = XDRNG.Next3(afterFake);
            pid = GetColoStarterPID(ref pidSeedE, id32);
            if (pid != expectPID)
                continue;
            result = seed;
            return true;
        }
        result = 0;
        return false;
    }

    public static bool TryGetOriginSeedStarterXD(uint iv1, uint iv2,
        uint expectPID, ushort tid, ushort sid,
        out uint result)
    {
        // * => TID, SID, fakepid*2, [IVs, ability, PID]
        Span<uint> seeds = stackalloc uint[XDRNG.MaxCountSeedsPID];
        var count = XDRNG.GetSeeds(seeds, (uint)tid << 16, (uint)sid << 16);
        foreach (var seed in seeds[..count])
        {
            // Check IVs
            var ivSeed = XDRNG.Next4(seed);
            if (!IsMatchIVs(iv1, iv2, ivSeed))
                continue;
            var pidSeed = XDRNG.Next7(seed);
            var pid = GetPID(pidSeed);
            if (pid != expectPID)
                continue;
            result = seed;
            return true;
        }
        result = 0;
        return false;
    }

    /// <summary>
    /// Checks if the provided <see cref="seed"/> can be landed at after confirming the player name.
    /// </summary>
    /// <param name="seed">Seed that goes on to generate the player TID/SID (after advancing 1000 frames)</param>
    /// <param name="origin">Current seed that confirms the player name</param>
    /// <returns>True if <see cref="seed"/> can be reached via <see cref="origin"/></returns>
    /// <remarks>https://github.com/yatsuna827/PokemonCoRNGLibrary/blob/e5255b13134ab3a2119788c40b5e71ee48d849b0/PokemonCoRNGLibrary/Util/LCGExtensions.cs#L15</remarks> 
    public static bool IsReachableFromNameScreen(uint seed, out uint origin)
    {
        // rand() / 10 == 0 is a skip
        const ushort SEED_THRESHOLD = 0x1999; // Only need the upper 16 bits

        var p1 = XDRNG.Prev16(ref seed) > SEED_THRESHOLD;
        var p2 = XDRNG.Prev16(ref seed) > SEED_THRESHOLD;
        var p3 = XDRNG.Prev16(ref seed) > SEED_THRESHOLD;
        var p4 = XDRNG.Prev16(ref seed) > SEED_THRESHOLD;

        if (p1 && p2 && p3 && p4)
        {
            origin = seed;
            return true;
        }

        if (XDRNG.Prev16(ref seed) <= SEED_THRESHOLD && IsReachableFromNameScreen(XDRNG.Prev(seed), out origin)) return true;
        if (XDRNG.Prev16(ref seed) <= SEED_THRESHOLD && p1 && IsReachableFromNameScreen(XDRNG.Prev(seed), out origin)) return true;
        if (XDRNG.Prev16(ref seed) <= SEED_THRESHOLD && p1 && p2 && IsReachableFromNameScreen(XDRNG.Prev(seed), out origin)) return true;
        if (XDRNG.Prev16(ref seed) <= SEED_THRESHOLD && p1 && p2 && p3 && IsReachableFromNameScreen(XDRNG.Prev(seed), out origin)) return true;

        origin = 0;
        return false;
    }
}
