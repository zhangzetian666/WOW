using System;
namespace WoWProbe
{
    public enum FarmAction { None, Forward, TurnLeft, TurnRight, StrafeLeft, StrafeRight, Jump, SelectTarget, Skill1, Skill2, Skill3, Skill4, Interact, Skin, Heal, Mana, Stop, Completed }
    public sealed class FarmCommand
    {
        public FarmAction Action;
        public int HoldMilliseconds;
        public uint ExpectedTargetToken;
        public bool RequireDeadTarget, RequireAliveTarget;
        public string Reason;
    }
    internal static class FarmVitals
    {
        internal static bool Percent(int value, bool blind)
        { return value >= 0 && value <= 100 || blind && value == 255; }
        internal static bool Power(FarmObservation o, bool blind)
        { return (o.PowerType >= 0 && o.PowerType < 255 || blind && o.PowerType == 255) && (o.PowerType != 0 || Percent(o.Power, blind)); }
        internal static bool Unreadable(FarmObservation o)
        { return o.Health == 255 || o.PowerType == 255 || o.PowerType == 0 && o.Power == 255; }
    }
    public sealed class FarmEngine
    {
        private enum Phase { Patrol, Scan, Approach, Combat, Loot, Skin, Rest, Stopped, Completed }
        private readonly Navigator navigator;
        private readonly BattleProfile profile;
        private readonly RouteDocument route;
        private Phase phase;
        private bool hasTime, hasSequence, scanScheduled, skillIssued, combatClock, healIssued, manaIssued, skinIssued, approachingAnchor;
        private int sequence, scans, skillIndex, unavailable, deathFrames, lootAttempts, targetHealth;
        private long lastNow, navNow, stateStarted, nextScan, nextSkill, encounterStarted, lastEvidence, skinIssuedAt, pendingForwardAt, approachForwardMs, nextBlindAdvance;
        // Blind combat progress. Live 01:55 found the real reason for zero kills: all 104 skill
        // pulses were slot 1, which is the plain "Attack" toggle (spell 6603) on this rogue.
        // Pressing it repeatedly switches auto-attack off and deals no damage. With the WC3
        // CheckInteractDistance fallback the client now reports a readable melee-ish distance,
        // so the chase and the melee phase get separate, much longer deadlines: walking away
        // after 16s never gave a 45-energy Sinister Strike time to land.
        private const int BlindNoDamageMs = 8000, BlindChaseMs = 30000, BlindMeleeMs = 45000, BlindAbandonMs = 30000;
        private int lastDamagedHealth = 255, blindRetries;
        private long lastDamageAt, abandonedUntil, meleeReachedAt, nextBlindTurn;
        private bool meleeReached;
        private uint abandonedToken;
        private int pendingForwardMs;
        private uint lockedHash, lootOpenBase, lootCloseBase, skinBase, skinFailedBase, castSuccess;
        private double approachOriginX, approachOriginY, anchorX, anchorY;
        private bool Blind { get { return profile.BlindGrind; } }

        public FarmEngine(RouteDocument route, BattleProfile profile, bool reverse, bool loop, int startIndex)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            this.profile = profile.Clone(); this.profile.Validate();
            navigator = new Navigator(route, this.profile.Calibration, reverse, loop, startIndex,
                NavigationPurpose.Patrol, this.profile.BlindGrind ? 300000L : 120000L);
            this.route = navigator.WorkingRoute;
            Completed = navigator.Completed; phase = Completed ? Phase.Completed : Phase.Patrol;
            LastReason = Completed ? "The supplied checkpoint is complete." : "Awaiting a healthy farming observation.";
        }
        public string Status { get { return phase.ToString(); } }
        public string LastReason { get; private set; }
        public int RouteIndex { get { return navigator.TargetIndex; } }
        public uint LockedTargetToken { get; private set; }
        public bool EncounterActive { get { return LockedTargetToken != 0; } }
        public bool Completed { get; private set; }
        public RouteDocument WorkingRoute { get { return route.Clone(); } }
        public FarmCommand Step(FarmObservation observation, long nowMs)
        {
            if (phase == Phase.Stopped) return Command(FarmAction.Stop, 0, LastReason, false, false);
            if (Completed) return Command(FarmAction.Completed, 0, LastReason, false, false);
            if (nowMs < 0 || (hasTime && nowMs < lastNow)) return Fail("Monotonic farming clock is invalid or moved backwards.");
            if (hasTime && phase == Phase.Patrol) navNow += nowMs - lastNow;
            hasTime = true; lastNow = nowMs;
            FarmObservation o = observation;
            bool distinct = o != null && (!hasSequence || o.Sequence != sequence);
            // Replanning may adopt a newly observed verified unit between commands. A pending
            // input retains its original unit token and is guarded separately by FarmSession.
            bool relock = Blind && distinct && EncounterActive && phase != Phase.Loot && phase != Phase.Skin &&
                CanBlindLock(o) && o.TargetToken != LockedTargetToken;
            string unsafeState = UnsafeReason(observation, relock);
            if (unsafeState != null) return Fail(unsafeState);
            if (relock) { hasSequence = true; sequence = o.Sequence; return Lock(o, BlindTarget(o), nowMs); }
            if (distinct && EncounterActive && !o.TargetDead && (phase == Phase.Combat || phase == Phase.Approach))
            {
                deathFrames = 0;
                // Keep the last living-target baseline, so earlier loot events cannot prove this later corpse was looted.
                // Do not overwrite it on the first dead frame: instant auto-loot may already have opened and closed.
                if (o.LootEventsKnown) { lootOpenBase = o.LootOpened; lootCloseBase = o.LootClosed; }
            }
            if (distinct && phase == Phase.Combat)
            {
                if ((o.TargetHealth >= 0 && o.TargetHealth <= 100 && o.TargetHealth != targetHealth) ||
                    (o.CastEventsKnown && o.CastSuccess != castSuccess) || o.Casting || o.Channeling ||
                    (Blind && o.InCombat)) lastEvidence = nowMs;
                targetHealth = o.TargetHealth; castSuccess = o.CastSuccess;
            }
            // A blind corpse timeout abandons only the corpse, including on a repeated frame;
            // the generic strict-mode deadline must not stop the whole grind first.
            if (Blind && phase == Phase.Loot && nowMs - stateStarted >= profile.LootTimeoutMs) return Loot(o, nowMs);
            string timeout = TimeoutReason(nowMs);
            if (timeout != null) return Fail(timeout);
            // Safety and clocks are always checked, including a repeated frame. Only distinct frames advance state.
            if (!distinct) return None("Awaiting a distinct observation.");
            hasSequence = true; sequence = o.Sequence;
            if (!scanScheduled) { nextScan = nowMs + profile.ScanIntervalMs; scanScheduled = true; }

            switch (phase)
            {
                case Phase.Patrol:
                case Phase.Scan:
                    if (NeedsRest(o)) { Enter(Phase.Rest, nowMs); healIssued = manaIssued = false; return None("Low health or mana requires observed recovery before patrol."); }
                    if (o.Casting || o.Channeling) return None("Waiting for the current cast before patrol or target selection.");
                    BattleTarget match = TargetCatalog.Match(profile, o);
                    // After a blind disengage, do not immediately re-lock the same mob: it would
                    // undo the retreat on the very next frame and re-create the standing-still death.
                    bool abandoned = Blind && nowMs < abandonedUntil && o.TargetIdentityKnown && o.TargetToken == abandonedToken && abandonedToken != 0;
                    if (abandoned) match = null;
                    // Blind grind: any attackable non-player target is eligible, catalog match is preferred.
                    // Live 02:15: after /reload the client reported a target with identity and
                    // UnitCanAttack but no full-name hash, so requiring NameHashKnown left the
                    // engine scanning TAB forever with token=0 while the client held a valid mob.
                    // TargetToken is a per-unit GUID hash from WC3 and is a better identity anyway.
                    if (match == null && !abandoned && Blind && CanBlindLock(o)) match = BlindTarget(o);
                    if (match != null) return Lock(o, match, nowMs);
                    if (phase == Phase.Scan)
                    {
                        if (nowMs < nextScan) return None("Waiting between target scans.");
                        if (scans >= profile.ScanLimit)
                        { Enter(Phase.Patrol, nowMs); nextScan = nowMs + profile.ScanIntervalMs; return None("Target scan limit reached; resuming the same patrol waypoint."); }
                        return Select(nowMs);
                    }
                    if (nowMs >= nextScan) { Enter(Phase.Scan, nowMs); scans = 0; return Select(nowMs); }
                    NavigationCommand movement = navigator.Step(new NavigationSample {
                        X = o.X, Y = o.Y, Facing = o.Facing, MapId = o.MapId, MapMode = o.MapMode, Zone = o.Zone,
                        Sequence = o.Sequence, Fresh = o.Fresh, KnownCore = o.KnownCore, ReadFailures = o.ReadFailures,
                        Dead = o.PlayerDead, Ghost = o.PlayerGhost, Combat = o.InCombat && !Blind, BagFull = o.BagFull }, navNow);
                    if (movement.Action == NavigationAction.Stop) return Fail(movement.Reason);
                    if (movement.Action == NavigationAction.Completed)
                    { Completed = true; phase = Phase.Completed; return Command(FarmAction.Completed, 0, "Farming patrol completed.", false, false); }
                    return Command((FarmAction)(int)movement.Action, movement.HoldMilliseconds, movement.Reason, false, false);
                case Phase.Approach: return Approach(o, nowMs);
                case Phase.Combat: return Combat(o, nowMs);
                case Phase.Loot: return Loot(o, nowMs);
                case Phase.Skin: return Skin(o, nowMs);
                case Phase.Rest: return Rest(o, nowMs);
                default: return Fail("Unknown farming phase.");
            }
        }
        public void Stop(string reason)
        {
            if (phase == Phase.Stopped) return;
            phase = Phase.Stopped; LastReason = String.IsNullOrWhiteSpace(reason) ? "Farming stopped by request." : reason;
            navigator.Stop(LastReason); pendingForwardMs = 0;
        }
        private FarmCommand Lock(FarmObservation o, BattleTarget target, long now)
        {
            LockedTargetToken = o.TargetToken; lockedHash = target.NameHash; encounterStarted = now;
            lootOpenBase = o.LootOpened; lootCloseBase = o.LootClosed; deathFrames = 0; lootAttempts = 0;
            skillIndex = unavailable = 0; skillIssued = false; combatClock = false;
            targetHealth = o.TargetHealth; castSuccess = o.CastSuccess; lastEvidence = now; nextSkill = now;
            lastDamagedHealth = o.TargetHealth; lastDamageAt = now; blindRetries = 0; nextBlindAdvance = now + 600;
            meleeReached = false; meleeReachedAt = now; nextBlindTurn = now + 1800;
            if (!Blind && (!o.RangeKnown || o.RangeSlot != profile.RangeSlot)) return Fail("The selected range action is unknown or does not match the profile.");
            // Blind + secret range: "unknown" is not "out of range". Entering Approach here used to
            // trap the engine in endless forward pulses (and Combat bounced back after 4s), so the
            // character walked into the mob without ever swinging. Fight immediately instead; short
            // advance pulses inside Combat close the remaining distance.
            if (Blind && !o.RangeKnown) { nextBlindAdvance = now + 600; EnterCombat(now); return None("Blind mode: range is secret, entering combat while closing distance."); }
            // Live 2026-11-16 21:35: the verified client also reports the range slot as KNOWN
            // but false forever. With the target position secret there is nothing to steer an
            // approach with (the blind chase below sweeps blind arcs waiting for a distance bit
            // that never turns true), so the encounter died in approach without a single swing.
            // A false range bit is only trusted as "genuinely far" when the position is known.
            if (Blind && !o.TargetInRange && !o.TargetPositionKnown) { nextBlindAdvance = now + 600; EnterCombat(now); return None("Blind mode: target position is secret and the range bit is unreliable; entering combat while closing distance."); }
            if (o.TargetInRange) { EnterCombat(now); return None("Saved, attackable non-player target locked in range."); }
            return EnterApproach(o, now);
        }
        private FarmCommand EnterApproach(FarmObservation o, long now)
        {
            if (!profile.AllowApproach) return Fail("Saved target is out of range and approach is disabled.");
            Enter(Phase.Approach, now); approachOriginX = o.X; approachOriginY = o.Y;
            ResetApproachProgress();
            string invalid = ApproachReason(o); if (invalid != null) return Fail(invalid);
            return None("Saved target locked; approaching only its known calibrated position.");
        }
        private void EnterCombat(long now)
        {
            Enter(Phase.Combat, now); ResetApproachProgress();
            if (!combatClock) { combatClock = true; encounterStarted = now; lastEvidence = now; }
        }
        private FarmCommand Approach(FarmObservation o, long now)
        {
            if (o.TargetDead) return ConfirmDeath(o, now);
            if (o.TargetInRange) { EnterCombat(now); return None("New range evidence permits combat."); }
            // Blind survival: if something is already hitting us, fight back immediately
            // instead of walking into it defenceless.
            if (Blind && o.InCombat) { EnterCombat(now); return None("Blind mode entered combat because the player is under attack."); }
            // Blind chase cap: walking far from the encounter origin into a hostile camp is how
            // blind characters die; drop the target and let the next scan pick a closer one.
            if (Blind && NavigationMath.Distance(o.X - approachOriginX, o.Y - approachOriginY, profile.Calibration.AxisScale) > 0.03)
            {
                LockedTargetToken = 0; lockedHash = 0; Enter(Phase.Patrol, now); nextScan = now;
                return None("Blind chase exceeded its 0.03 distance cap; target dropped for a closer scan.");
            }
            if (Blind && !o.TargetPositionKnown)
            {
                // Blind chase: no verified target map position. /target <name> can select a mob
                // 20-40 yards away in any direction, so walking straight ahead used to miss it.
                // Sweep a slow arc while advancing at high duty cycle; the WC3 distance bit is
                // the feedback that ends the chase. A failed chase abandons only this mob, it
                // must not stop the whole patrol.
                if (o.Casting || o.Channeling) return None("Waiting for casting before a blind approach pulse.");
                if (pendingForwardMs != 0)
                { approachForwardMs += Math.Min((long)pendingForwardMs, now - pendingForwardAt); pendingForwardMs = 0; }
                if (approachForwardMs >= profile.ApproachTimeoutMs)
                    return AbortEncounter(now, "blind chase reached its deadline without entering melee range");
                if (now >= nextBlindTurn)
                {
                    nextBlindTurn = now + 4500;
                    return Command(FarmAction.TurnRight, 100, "Blind chase: sweeping for the locked target.", false, false);
                }
                pendingForwardAt = now; pendingForwardMs = 240;
                return Command(FarmAction.Forward, 240, "Blind chase advance pulse; distance feedback decides when to fight.", true, false);
            }
            if (pendingForwardMs != 0)
            { approachForwardMs += Math.Min((long)pendingForwardMs, now - pendingForwardAt); pendingForwardMs = 0; }
            NavigationCalibration c = profile.Calibration;
            if (approachingAnchor && NavigationMath.Distance(o.X - anchorX, o.Y - anchorY, c.AxisScale) >= 0.0001) ResetApproachProgress();
            if (approachForwardMs >= 2000) return Fail("Approach made no observed progress after two seconds of forward pulses.");
            if (o.Casting || o.Channeling) return None("Waiting for casting before approach movement.");
            double bearing = Math.Atan2((o.TargetX - o.X) * c.AxisScale, -(o.TargetY - o.Y));
            double facing = NavigationMath.Normalize(NavigationMath.Normalize(c.FacingSign * o.Facing) + NavigationMath.Normalize(c.FacingOffset));
            double delta = NavigationMath.Normalize(bearing - facing);
            if (Math.Abs(delta) > 0.12)
                return Command(delta > 0 ? FarmAction.TurnRight : FarmAction.TurnLeft,
                    NavigationMath.Pulse(Math.Abs(delta) / c.TurnRadiansPerSecond * 1000, 60, 250), "Turning toward the locked target's known map position.", true, false);
            double distance = NavigationMath.Distance(o.TargetX - o.X, o.TargetY - o.Y, c.AxisScale);
            int hold = NavigationMath.Pulse(distance / c.ForwardUnitsPerSecond * 500, 60, 200);
            if (!approachingAnchor) { approachingAnchor = true; anchorX = o.X; anchorY = o.Y; }
            pendingForwardAt = now; pendingForwardMs = hold;
            return Command(FarmAction.Forward, hold, "Bounded approach pulse; a new range observation is required.", true, false);
        }
        private FarmCommand Combat(FarmObservation o, long now)
        {
            // Death beats everything: report it as the actual stop reason instead of
            // letting the unusable-skill counter mask it (dead players can use no skill).
            if (o.PlayerDead || o.PlayerGhost) return Fail("Player death or ghost stopped combat.");
            if (o.TargetDead) return ConfirmDeath(o, now);
            deathFrames = 0;
            if (!Blind && !o.TargetInRange) return EnterApproach(o, now);
            // With a readable distance bit, Combat owns the chase too. Bouncing back to Approach
            // every few seconds resets approach progress and used to leave the rogue walking
            // forever; the forward/turn pulses below close the gap without a state switch.
            // Blind mode cannot read player health or target position. A falling target health
            // is the only proof our swings landed; when it is secret, use separate deadlines for
            // "still chasing" and "in melee" so a 45-energy skill has time to actually land.
            if (Blind)
            {
                bool healthReadable = o.TargetHealth >= 0 && o.TargetHealth <= 100;
                if (healthReadable && o.TargetHealth < lastDamagedHealth)
                { lastDamagedHealth = o.TargetHealth; lastDamageAt = now; blindRetries = 0; }
                if (!meleeReached && o.RangeKnown && o.TargetInRange)
                { meleeReached = true; meleeReachedAt = now; }
                // Verified client 2026-11-16: target health is secret (255) AND the range bit is
                // known-but-false forever. Neither deadline signal works there — the only kill
                // evidence is TargetDead frames, which DeadKnown=True does deliver. Running the
                // 30s chase abort in that state stopped every encounter before a swing could
                // kill, so the encounter instead runs to TargetDead or the combat deadline,
                // with the existing closing/hold-and-sweep pulses keeping roughly faced.
                bool bitStuck = !healthReadable && o.RangeKnown && !o.TargetInRange;
                long since = meleeReached ? now - meleeReachedAt : now - encounterStarted;
                long stalled = now - lastDamageAt;
                bool noProgress = healthReadable ? stalled >= BlindNoDamageMs
                    : bitStuck ? false
                    : since >= (meleeReached ? BlindMeleeMs : BlindChaseMs);
                if (noProgress)
                {
                    if (!healthReadable || blindRetries >= 2)
                        return AbortEncounter(now, healthReadable ? "target stopped taking damage after two repositionings" :
                            (meleeReached ? "melee phase ran out of time without a kill" : "could not close to melee range before the chase deadline"));
                    blindRetries++; lastDamageAt = now;
                    return Command(blindRetries == 1 ? FarmAction.TurnRight : FarmAction.TurnLeft,
                        blindRetries == 1 ? 130 : 240,
                        "Blind combat: no target damage for " + stalled + "ms; repositioning (attempt " + blindRetries + ").", false, false);
                }
            }
            if (o.Casting || o.Channeling) return None("Waiting for casting or channeling to finish.");
            // Distance-driven chase. While out of range sweep and advance at a high duty cycle;
            // FarmSession rejects any pulse outside 60..250ms, so raise the duty cycle by
            // shortening the gap, never by lengthening an individual keypress.
            if (Blind && !o.TargetPositionKnown && now >= nextBlindAdvance)
            {
                // Live 02:23: 52 consecutive forward pulses walked the rogue away from a mob
                // that was already hitting it. Auto-attack and Sinister Strike both need a
                // facing target, so a permanently moving back never landed a blow. Close hard
                // for the first 8 seconds, then stand and turn slowly while swinging.
                bool closing = now - encounterStarted < 8000;
                if (now >= nextBlindTurn)
                {
                    nextBlindTurn = now + (closing ? 4500 : 2500);
                    return Command(FarmAction.TurnRight, closing ? 100 : 250,
                        closing ? "Blind combat: sweeping while closing the gap." : "Blind combat: turning to face the mob that closed on us.", false, false);
                }
                nextBlindAdvance = now + (closing ? 250 : 900);
                return Command(FarmAction.Forward, closing ? 250 : 200,
                    closing ? "Blind combat closing pulse." : "Blind combat hold-and-swing pulse.", true, false);
            }
            if (skillIssued && now < nextSkill) return None("Waiting for the configured skill interval.");
            int slot = profile.SkillSequence[skillIndex] - '1';
            if (slot < 0 || slot > 3) slot = 0;
            int mask = 1 << slot;
            skillIndex = (skillIndex + 1) % profile.SkillSequence.Length;
            // Keep the rotation at its configured cadence. Slowing it while "far" produced
            // only 7 swings in 35s on a client whose 10yd distance bit never turns true.
            skillIssued = true; nextSkill = now + profile.SkillIntervalMs;
            // Slot 1 on this character is the plain "Attack" toggle (spell 6603). All 104 live
            // skill pulses landed there, dealing zero damage while switching auto-attack off.
            // /startattack (issued on lock) owns auto-attack; never press this slot again.
            if (Blind && slot == 0) return None("Slot 1 is the Attack toggle; auto-attack is preserved instead.");
            if (!o.ActionsKnown || (o.KnownActionsMask & mask) == 0 || (o.ReadyActionsMask & mask) == 0 || (o.UsableActionsMask & mask) == 0)
            {
                if (Blind)
                {
                    // IsUsableAction reports only slot 1 while the rogue sits below 45 energy,
                    // and on Forever clients the masks can be secret altogether. Press the
                    // configured damaging skill anyway: skill_interval_ms is long enough for
                    // energy to regenerate, and an uncastable keypress is harmless.
                    unavailable = 0;
                    return Command((FarmAction)((int)FarmAction.Skill1 + slot), 120,
                        "Blind swing: damaging slot " + (slot + 1) + " despite secret/negative usability.", true, false);
                }
                unavailable++;
                if (unavailable >= 3) return Fail("Three consecutive skill checks had no known, ready and usable action.");
                return None("Configured skill is unknown, cooling down or unusable; no input sent.");
            }
            unavailable = 0;
            return Command((FarmAction)((int)FarmAction.Skill1 + slot), 120, "Using a known, ready and usable skill against the locked live target.", true, false);
        }
        // Give up on a mob we cannot damage and resume the patrol. Stopping the whole session
        // here would end the run; the abandoned hash prevents an immediate re-lock of the same mob.
        private FarmCommand AbortEncounter(long now, string why)
        {
            abandonedToken = LockedTargetToken; abandonedUntil = now + BlindAbandonMs;
            LockedTargetToken = 0; lockedHash = 0;
            Enter(Phase.Patrol, now); nextScan = now + 9000;
            // Maximum allowed turn pulse (250ms) plus the following patrol forward pulse
            // breaks the mob's leash; a short nudge left the rogue in melee until it died.
            return Command(FarmAction.TurnRight, 250,
                "Blind disengage: " + why + "; abandoning this target and resuming patrol.", false, false);
        }
        // Lowest slot the client currently reports as known, ready and usable.
        private static int FirstUsableSlot(FarmObservation o)
        {
            int usable = o.UsableActionsMask & o.ReadyActionsMask & o.KnownActionsMask & 15;
            if (usable == 0) return -1;
            for (int i = 0; i < 4; i++) if ((usable & (1 << i)) != 0) return i;
            return -1;
        }
        private FarmCommand ConfirmDeath(FarmObservation o, long now)
        {
            deathFrames++; pendingForwardMs = 0;
            if (deathFrames < 2) return None("Confirming target death with a second distinct observation.");
            Enter(Phase.Loot, now); lootAttempts = 0;
            return None("Locked target death confirmed; awaiting loot evidence.");
        }
        private FarmCommand Loot(FarmObservation o, long now)
        {
            // Blind grind: survival first. Anything still fighting means skip this corpse.
            if (Blind && o.InCombat) { LockedTargetToken = 0; lockedHash = 0; Enter(Phase.Patrol, now); nextScan = now + 4000;
                return None("Combat is still active in blind mode; corpse abandoned without loot."); }
            if (Blind && now - stateStarted >= profile.LootTimeoutMs)
            { LockedTargetToken = 0; lockedHash = 0; Enter(Phase.Patrol, now); nextScan = now + 4000;
                return None("Blind loot deadline elapsed; corpse abandoned without loot."); }
            if (!o.TargetDead) return Fail("The locked corpse is no longer confirmed dead.");
            if (!o.LootEventsKnown || !o.LootKnown) return Fail("Loot state and event evidence must be known.");
            bool opened = o.LootOpened != lootOpenBase, closed = o.LootClosed != lootCloseBase;
            if (opened && closed)
            {
                if (o.Lootable) return Fail("Loot remains on the corpse after the observed loot window closed.");
                if (!profile.Skinning) return FinishEncounter(o, now);
                if (!o.SkinKnown) return Fail("Skinning is enabled but this corpse's skin state is unknown.");
                if (!o.Skinnable) return FinishEncounter(o, now);
                Enter(Phase.Skin, now); skinIssued = false;
                return None("Loot confirmed; preparing optional skinning.");
            }
            if (o.Casting || o.Channeling || opened || !o.Lootable) return None("Waiting for loot open and close event evidence.");
            if (lootAttempts == 0 || (lootAttempts == 1 && now - stateStarted >= profile.LootTimeoutMs / 2))
            { lootAttempts++; return Command(FarmAction.Interact, 120, lootAttempts == 1 ? "Opening loot for the confirmed locked corpse." : "One bounded loot retry; event evidence is still required.", false, true); }
            return None("Waiting for loot confirmation; no further interaction retries.");
        }
        private FarmCommand Skin(FarmObservation o, long now)
        {
            if (!o.TargetDead || !o.SkinKnown || !o.SkinSuccessKnown || !o.CastEventsKnown)
                return Fail("Skinning requires the same confirmed corpse and known dedicated skin/cast events.");
            if (skinIssued)
            {
                if (o.CastFailed != skinFailedBase) return Fail("Skinning cast failed; automatic retry is disabled.");
                if (now - skinIssuedAt >= profile.SkinWaitMs && o.SkinSuccess != skinBase) return FinishEncounter(o, now);
                return None("Waiting for the minimum skin duration and dedicated skin-success evidence.");
            }
            if (!o.Skinnable) return Fail("Corpse stopped being skinnable before a confirmed skin attempt.");
            if (o.Casting || o.Channeling) return None("Waiting for the current cast before skinning.");
            skinBase = o.SkinSuccess; skinFailedBase = o.CastFailed; skinIssued = true; skinIssuedAt = now;
            return Command(FarmAction.Skin, 120, "Skinning the confirmed locked corpse once.", false, true);
        }
        private FarmCommand Rest(FarmObservation o, long now)
        {
            bool skipped = Blind && FarmVitals.Unreadable(o);
            bool healthReady = Blind && o.Health == 255 || o.Health >= profile.RecoverTo;
            bool manaReady = Blind && (o.PowerType == 255 || o.PowerType == 0 && o.Power == 255) || o.PowerType != 0 || o.Power >= profile.RecoverTo;
            if (healthReady && manaReady)
            { Enter(Phase.Patrol, now); nextScan = now; return None(skipped ? "Blind mode: unreadable vital thresholds skipped; restoration was not observed. Resuming the same route index." : "Observed health and applicable mana recovered; resuming the same route index."); }
            if (o.Casting || o.Channeling) return None("Waiting for the current cast while resting.");
            if (!healthReady && !healIssued) { healIssued = true; return Command(FarmAction.Heal, 120, "Using food once; observed recovery is required.", false, false); }
            if (!manaReady && !manaIssued) { manaIssued = true; return Command(FarmAction.Mana, 120, "Using water once for a mana power type; observed recovery is required.", false, false); }
            return None("Waiting for observed recovery; supplies are not repeated.");
        }
        private FarmCommand FinishEncounter(FarmObservation o, long now)
        {
            if (o.InCombat && !Blind) return Fail("Combat remains active after the locked corpse interaction finished.");
            LockedTargetToken = 0; lockedHash = 0; Enter(Phase.Patrol, now);
            nextScan = Blind ? now + 5000 : now + profile.ScanIntervalMs; // short blind pause: out-of-combat regen between kills
            return None("Encounter evidence complete; resuming the saved patrol index.");
        }
        private FarmCommand Select(long now)
        { scans++; nextScan = now + profile.ScanIntervalMs; return Command(FarmAction.SelectTarget, 120, "Bounded target scan " + scans + "/" + profile.ScanLimit + ".", false, false); }
        private bool NeedsRest(FarmObservation o) { return o.Health < profile.HealBelow || (o.PowerType == 0 && o.Power < profile.ManaBelow); }
        private static bool CanBlindLock(FarmObservation o)
        {
            return o != null && o.Fresh && o.KnownCore && o.ReadFailures == 0 && o.TargetExists &&
                o.TargetIdentityKnown && o.TargetToken != 0 && o.AttackabilityKnown && !o.TargetIsPlayer &&
                o.TargetCanAttack && o.TargetDeadKnown && !o.TargetDead && (!o.NameHashKnown || o.TargetNameHash != 0);
        }
        private static BattleTarget BlindTarget(FarmObservation o)
        {
            return new BattleTarget { Name = String.IsNullOrEmpty(o.TargetName) ? "unknown" : o.TargetName,
                NameHash = o.NameHashKnown ? o.TargetNameHash : 0, Kind = "monster" };
        }
        private string UnsafeReason(FarmObservation o, bool relock)
        {
            if (o == null || !o.Fresh || !o.KnownCore || o.ReadFailures != 0) return "Fresh, known core and matching combat observations without read failures are required.";
            if (o.PlayerDead || o.PlayerGhost || o.BagFull) return "Player death, ghost or full bags stopped farming.";
            // The unknown sentinel is permitted only by explicit blind-mode opt-in.
            if (!FarmVitals.Percent(o.Health, Blind)) return "Health data is invalid or unknown in strict mode.";
            if (!FarmVitals.Power(o, Blind)) return "Power type or applicable mana is invalid or unknown in strict mode.";
            if (o.Health != 255 && o.Health <= profile.CriticalHealth) return "Critical player health stopped farming immediately.";
            if (o.MapId != route.MapId || o.MapMode != route.MapMode || !String.Equals(o.Zone, route.Zone, StringComparison.Ordinal)) return "Farming map, mode or zone changed.";
            if (!NavigationMath.InRange(o.X, 0, 1) || !NavigationMath.InRange(o.Y, 0, 1) || !NavigationMath.Finite(o.Facing)) return "Player position or facing is unknown or invalid.";
            if (!Blind && RouteDistance(o.X, o.Y) > 0.02) return "Player moved outside the calibrated 0.02 safety corridor around the recorded route.";
            if (!o.CastingKnown) return "Casting state is unknown; farming input is paused by stopping the engine.";
            if (o.InCombat && !EncounterActive && !Blind) return "Unexpected combat occurred before a saved target was locked.";
            if (EncounterActive)
            {
                if (!o.TargetExists || !o.TargetIdentityKnown || o.TargetToken == 0 ||
                    (!relock && (o.TargetToken != LockedTargetToken || ((!Blind || lockedHash != 0) && (!o.NameHashKnown || o.TargetNameHash != lockedHash)))))
                    return "Locked target disappeared or its identity changed.";
                if (!o.TargetDeadKnown || !o.AttackabilityKnown || o.TargetIsPlayer || (!o.TargetDead && !o.TargetCanAttack)) return "Locked target death or attackability is unknown or unsafe.";
                if (!Blind && (phase == Phase.Combat || phase == Phase.Approach) && !o.TargetDead && (!o.RangeKnown || o.RangeSlot != profile.RangeSlot)) return "Locked target range is unknown or the range slot changed.";
                if (phase == Phase.Approach && !o.TargetDead) return ApproachReason(o);
            }
            return null;
        }
        private string ApproachReason(FarmObservation o)
        {
            if (Blind) return null; // Blind approach steers by whatever target evidence exists; corridor limits are suspended.
            if (!o.TargetPositionKnown || !NavigationMath.InRange(o.TargetX, 0, 1) || !NavigationMath.InRange(o.TargetY, 0, 1)) return "A verified target map position is required for approach.";
            if (!o.RangeKnown || o.RangeSlot != profile.RangeSlot) return "A matching known range action is required for approach.";
            double scale = profile.Calibration.AxisScale;
            if (NavigationMath.Distance(o.TargetX - o.X, o.TargetY - o.Y, scale) > 0.02 || NavigationMath.Distance(o.X - approachOriginX, o.Y - approachOriginY, scale) > 0.02)
                return "Approach exceeded its calibrated 0.02 distance limit.";
            return null;
        }
        private string TimeoutReason(long now)
        {
            if (phase == Phase.Approach && now - stateStarted >= profile.ApproachTimeoutMs) return "Approach deadline elapsed.";
            if ((phase == Phase.Combat || phase == Phase.Approach) && combatClock && now - encounterStarted >= profile.CombatTimeoutMs) return "Combat deadline elapsed.";
            // Blind mode owns its own no-progress handling in Combat(); the generic evidence
            // deadline would otherwise stop the whole patrol before a disengage can happen.
            if (phase == Phase.Combat && !Blind && now - lastEvidence > 10000) return "Combat has no new casting or target-health evidence for ten seconds.";
            if (phase == Phase.Loot && now - stateStarted >= profile.LootTimeoutMs) return "Loot confirmation deadline elapsed.";
            if (phase == Phase.Skin && now - stateStarted >= profile.SkinTimeoutMs) return "Skin confirmation deadline elapsed.";
            if (phase == Phase.Rest && now - stateStarted >= profile.RestTimeoutMs) return "Recovery deadline elapsed; supplies or recovery evidence are missing.";
            if (phase == Phase.Scan && now - stateStarted > (long)(profile.ScanLimit + 2) * profile.ScanIntervalMs + 2000) return "Target scan stalled without distinct observations.";
            return null;
        }
        private double RouteDistance(double x, double y)
        { return DistanceFromRoute(route, profile.Calibration.AxisScale, x, y); }
        internal static double DistanceFromRoute(RouteDocument route, double scale, double x, double y)
        {
            double best = Double.MaxValue;
            for (int i = 1; i < route.Points.Count; i++)
            {
                Waypoint a = route.Points[i - 1], b = route.Points[i];
                double dx = (b.X - a.X) * scale, dy = b.Y - a.Y, px = (x - a.X) * scale, py = y - a.Y;
                double length = dx * dx + dy * dy;
                double t = length == 0 ? 0 : Math.Max(0, Math.Min(1, (px * dx + py * dy) / length));
                best = Math.Min(best, Math.Sqrt((px - t * dx) * (px - t * dx) + (py - t * dy) * (py - t * dy)));
            }
            return best;
        }
        private void Enter(Phase next, long now) { phase = next; stateStarted = now; }
        private void ResetApproachProgress() { approachingAnchor = false; pendingForwardMs = 0; approachForwardMs = 0; }
        private FarmCommand Fail(string reason) { Stop(reason); return Command(FarmAction.Stop, 0, LastReason, false, false); }
        private FarmCommand None(string reason) { return Command(FarmAction.None, 0, reason, false, false); }
        private FarmCommand Command(FarmAction action, int hold, string reason, bool alive, bool dead)
        {
            LastReason = reason;
            return new FarmCommand { Action = action, HoldMilliseconds = hold, ExpectedTargetToken = alive || dead ? LockedTargetToken : 0,
                RequireAliveTarget = alive, RequireDeadTarget = dead, Reason = reason };
        }
    }
}
