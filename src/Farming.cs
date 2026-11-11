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
    public sealed class FarmEngine
    {
        private enum Phase { Patrol, Scan, Approach, Combat, Loot, Skin, Rest, Stopped, Completed }
        private readonly Navigator navigator;
        private readonly BattleProfile profile;
        private readonly RouteDocument route;
        private Phase phase;
        private bool hasTime, hasSequence, scanScheduled, skillIssued, combatClock, healIssued, manaIssued, skinIssued, approachingAnchor;
        private int sequence, scans, skillIndex, unavailable, deathFrames, lootAttempts, targetHealth;
        private long lastNow, navNow, stateStarted, nextScan, nextSkill, encounterStarted, lastEvidence, skinIssuedAt, pendingForwardAt, approachForwardMs;
        private int pendingForwardMs;
        private uint lockedHash, lootOpenBase, lootCloseBase, skinBase, skinFailedBase, castSuccess;
        private double approachOriginX, approachOriginY, anchorX, anchorY;

        public FarmEngine(RouteDocument route, BattleProfile profile, bool reverse, bool loop, int startIndex)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            this.profile = profile.Clone(); this.profile.Validate();
            navigator = new Navigator(route, this.profile.Calibration, reverse, loop, startIndex);
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
            string unsafeState = UnsafeReason(observation);
            if (unsafeState != null) return Fail(unsafeState);
            FarmObservation o = observation;
            bool distinct = !hasSequence || o.Sequence != sequence;
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
                    (o.CastEventsKnown && o.CastSuccess != castSuccess) || o.Casting || o.Channeling) lastEvidence = nowMs;
                targetHealth = o.TargetHealth; castSuccess = o.CastSuccess;
            }
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
                        Dead = o.PlayerDead, Ghost = o.PlayerGhost, Combat = o.InCombat, BagFull = o.BagFull }, navNow);
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
            if (!o.RangeKnown || o.RangeSlot != profile.RangeSlot) return Fail("The selected range action is unknown or does not match the profile.");
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
            if (o.TargetDead) return ConfirmDeath(o, now);
            deathFrames = 0;
            if (!o.TargetInRange) return EnterApproach(o, now);
            if (o.Casting || o.Channeling) return None("Waiting for casting or channeling to finish.");
            if (skillIssued && now < nextSkill) return None("Waiting for the configured skill interval.");
            int slot = profile.SkillSequence[skillIndex] - '1', mask = 1 << slot;
            skillIndex = (skillIndex + 1) % profile.SkillSequence.Length;
            skillIssued = true; nextSkill = now + profile.SkillIntervalMs;
            if (!o.ActionsKnown || (o.KnownActionsMask & mask) == 0 || (o.ReadyActionsMask & mask) == 0 || (o.UsableActionsMask & mask) == 0)
            {
                unavailable++;
                if (unavailable >= 3) return Fail("Three consecutive skill checks had no known, ready and usable action.");
                return None("Configured skill is unknown, cooling down or unusable; no input sent.");
            }
            unavailable = 0;
            return Command((FarmAction)((int)FarmAction.Skill1 + slot), 120, "Using a known, ready and usable skill against the locked live target.", true, false);
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
            bool healthReady = o.Health >= profile.RecoverTo, manaReady = o.PowerType != 0 || o.Power >= profile.RecoverTo;
            if (healthReady && manaReady)
            { Enter(Phase.Patrol, now); nextScan = now; return None("Observed health and applicable mana recovered; resuming the same route index."); }
            if (o.Casting || o.Channeling) return None("Waiting for the current cast while resting.");
            if (!healthReady && !healIssued) { healIssued = true; return Command(FarmAction.Heal, 120, "Using food once; observed recovery is required.", false, false); }
            if (!manaReady && !manaIssued) { manaIssued = true; return Command(FarmAction.Mana, 120, "Using water once for a mana power type; observed recovery is required.", false, false); }
            return None("Waiting for observed recovery; supplies are not repeated.");
        }
        private FarmCommand FinishEncounter(FarmObservation o, long now)
        {
            if (o.InCombat) return Fail("Combat remains active after the locked corpse interaction finished.");
            LockedTargetToken = 0; lockedHash = 0; Enter(Phase.Patrol, now); nextScan = now + profile.ScanIntervalMs;
            return None("Encounter evidence complete; resuming the saved patrol index.");
        }
        private FarmCommand Select(long now)
        { scans++; nextScan = now + profile.ScanIntervalMs; return Command(FarmAction.SelectTarget, 120, "Bounded target scan " + scans + "/" + profile.ScanLimit + ".", false, false); }
        private bool NeedsRest(FarmObservation o) { return o.Health < profile.HealBelow || (o.PowerType == 0 && o.Power < profile.ManaBelow); }
        private string UnsafeReason(FarmObservation o)
        {
            if (o == null || !o.Fresh || !o.KnownCore || o.ReadFailures != 0) return "Fresh, known core and matching combat observations without read failures are required.";
            if (o.PlayerDead || o.PlayerGhost || o.BagFull) return "Player death, ghost or full bags stopped farming.";
            if (o.Health < 0 || o.Health > 100 || o.PowerType < 0 || o.PowerType == 255 || (o.PowerType == 0 && (o.Power < 0 || o.Power > 100))) return "Required health or mana data is unknown or invalid.";
            if (o.Health <= profile.CriticalHealth) return "Critical player health stopped farming immediately.";
            if (o.MapId != route.MapId || o.MapMode != route.MapMode || !String.Equals(o.Zone, route.Zone, StringComparison.Ordinal)) return "Farming map, mode or zone changed.";
            if (!NavigationMath.InRange(o.X, 0, 1) || !NavigationMath.InRange(o.Y, 0, 1) || !NavigationMath.Finite(o.Facing)) return "Player position or facing is unknown or invalid.";
            if (RouteDistance(o.X, o.Y) > 0.02) return "Player moved outside the calibrated 0.02 safety corridor around the recorded route.";
            if (!o.CastingKnown) return "Casting state is unknown; farming input is paused by stopping the engine.";
            if (o.InCombat && !EncounterActive) return "Unexpected combat occurred before a saved target was locked.";
            if (EncounterActive)
            {
                if (!o.TargetExists || !o.TargetIdentityKnown || o.TargetToken != LockedTargetToken || !o.NameHashKnown || o.TargetNameHash != lockedHash)
                    return "Locked target disappeared or its identity changed.";
                if (!o.TargetDeadKnown || !o.AttackabilityKnown || o.TargetIsPlayer || (!o.TargetDead && !o.TargetCanAttack)) return "Locked target death or attackability is unknown or unsafe.";
                if ((phase == Phase.Combat || phase == Phase.Approach) && !o.TargetDead && (!o.RangeKnown || o.RangeSlot != profile.RangeSlot)) return "Locked target range is unknown or the range slot changed.";
                if (phase == Phase.Approach && !o.TargetDead) return ApproachReason(o);
            }
            return null;
        }
        private string ApproachReason(FarmObservation o)
        {
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
            if (phase == Phase.Combat && now - lastEvidence > 10000) return "Combat has no new casting or target-health evidence for ten seconds.";
            if (phase == Phase.Loot && now - stateStarted >= profile.LootTimeoutMs) return "Loot confirmation deadline elapsed.";
            if (phase == Phase.Skin && now - stateStarted >= profile.SkinTimeoutMs) return "Skin confirmation deadline elapsed.";
            if (phase == Phase.Rest && now - stateStarted >= profile.RestTimeoutMs) return "Recovery deadline elapsed; supplies or recovery evidence are missing.";
            if (phase == Phase.Scan && now - stateStarted > (long)(profile.ScanLimit + 1) * profile.ScanIntervalMs) return "Target scan stalled without distinct observations.";
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
