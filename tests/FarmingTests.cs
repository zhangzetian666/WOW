using System;
using System.Collections.Generic;
using WoWProbe;

// All observations and movement are synthetic. No native input backend is compiled into this runner.
internal static class FarmingTests
{
    private static int passed, failed;
    private static int Main()
    {
        Run("patrol scans with bounded TAB attempts and ignores duplicate frames", ScanBoundaries);
        Run("catalog NPCs and player targets cannot be attacked", TargetFiltering);
        Run("combat follows skill sequence interval and locked target guards", CombatSequence);
        Run("unknown cooldown or unusable actions never produce a skill", UnavailableActions);
        Run("casting and channeling wait without issuing another skill", CastingWait);
        Run("locked target identity changes and disappearance stop even on duplicate frames", TargetLockFaults);
        Run("unexpected combat and dangerous or unknown player observations latch stop", SafetyFaults);
        Run("dead target needs two distinct confirmations before loot input", DeathConfirmation);
        Run("instant loot open and close events are not lost while confirming death", InstantLoot);
        Run("loot events observed before target death cannot confirm this corpse", EarlierLootEvents);
        Run("death confirmations in approach must be consecutive distinct frames", ApproachDeathConfirmation);
        Run("range transitions cannot reset the combat evidence deadline", RangeEvidenceDeadline);
        Run("loot permits only one retry and requires event evidence", LootRetry);
        Run("an opened loot frame is not reopened while awaiting close", LootOpenWait);
        Run("skinning requires dedicated success evidence and minimum wait", SkinEvidence);
        Run("unknown skin state blocks enabled skinning but disabled skinning skips it", SkinUnknown);
        Run("failed skin and unknown dedicated event support cannot pretend success", SkinFailure);
        Run("rest uses food and mana once then requires observed recovery", RestEvidence);
        Run("non-mana power types do not trigger drinking", NonManaRest);
        Run("missing supplies stop after the rest deadline", RestTimeout);
        Run("calibrated approach is bounded and enters combat only when in range", ApproachGeometry);
        Run("unknown target positions ranges and excess distance prevent approach", ApproachFaults);
        Run("stationary approach stops after two seconds of actual forward pulses", ApproachNoProgress);
        Run("duplicate frames still enforce approach combat loot and skin deadlines", StateTimeouts);
        Run("clock reversal and critical health stop even during a duplicate", ClockAndCritical);
        Run("encounter preserves the exact patrol route index", ResumePatrol);
        Run("moving outside the recorded route corridor stops safely", RouteCorridor);
        Run("engine owns snapshots and completed checkpoints never restart", SnapshotAndCompletion);
        Run("virtual cycle restores approaches kills loots skins and resumes patrol", FullSimulation);
        Run("virtual missing-position and missing-supplies faults stop without input", SimulatorFaults);
        Console.WriteLine("Farming tests: {0} passed, {1} failed. Synthetic observations only; no OS input.", passed, failed);
        return failed == 0 ? 0 : 1;
    }
    private static void Run(string name, Action test)
    { try { test(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); } }
    private static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    private static void Equal<T>(T want, T got, string why)
    { if (!EqualityComparer<T>.Default.Equals(want, got)) throw new Exception(why + ": expected " + want + ", actual " + got); }
    private static BattleProfile Profile()
    {
        NavigationCalibration c = new NavigationCalibration { MapId = 900001, MapMode = 2, Zone = "Simulation only" };
        return new BattleProfile { Name = "Synthetic farming", Calibration = c, Movement = new RouteKeyBindings(), AllowApproach = true,
            Targets = new List<BattleTarget> { new BattleTarget { Name = "Test beast", NameHash = TargetCatalog.HashName("Test beast"), Kind = "monster" } } };
    }
    private static RouteDocument Route()
    { return new RouteDocument { RouteId = "farm-test", Name = "Synthetic route", MapId = 900001, MapMode = 2, Zone = "Simulation only", Points = new List<Waypoint> {
        new Waypoint { X = 0.5, Y = 0.5, Tolerance = 0.0005 }, new Waypoint { X = 0.5, Y = 0.46, Tolerance = 0.0005 }, new Waypoint { X = 0.55, Y = 0.46, Tolerance = 0.0005 } } }; }
    private static FarmEngine Engine(BattleProfile p) { return new FarmEngine(Route(), p, false, false, 0); }
    private static FarmObservation Observation()
    {
        return new FarmObservation { Sequence = 1, Fresh = true, KnownCore = true, MapId = 900001, MapMode = 2, Zone = "Simulation only", X = 0.5, Y = 0.5, Facing = 0,
            Health = 95, Power = 95, PowerType = 0, TargetHealth = 100, CastingKnown = true, ActionsKnown = true,
            RangeSlot = 1, KnownActionsMask = 15, ReadyActionsMask = 15, UsableActionsMask = 15,
            LootEventsKnown = true, CastEventsKnown = true, SkinSuccessKnown = true, LootKnown = true, SkinKnown = true };
    }
    private static FarmObservation Monster(bool inRange)
    {
        FarmObservation o = Observation(); o.TargetExists = true; o.TargetIdentityKnown = true; o.TargetToken = 77;
        o.NameHashKnown = true; o.TargetNameHash = TargetCatalog.HashName("Test beast"); o.TargetName = "Test beast";
        o.TargetDeadKnown = true; o.AttackabilityKnown = true; o.TargetCanAttack = true; o.RangeKnown = true; o.TargetInRange = inRange;
        o.TargetPositionKnown = true; o.TargetX = 0.5; o.TargetY = 0.492; return o;
    }
    private static FarmCommand Next(FarmEngine e, FarmObservation o, long now) { o.Sequence = (o.Sequence + 1) & 65535; return e.Step(o, now); }
    private static FarmEngine Locked(BattleProfile p, FarmObservation o)
    { FarmEngine e = Engine(p); e.Step(o, 0); Check(e.EncounterActive && e.LockedTargetToken == 77, "saved target locked"); return e; }
    private static void Dead(FarmObservation o) { o.TargetDead = true; o.TargetHealth = 0; o.TargetCanAttack = false; o.InCombat = false; o.Lootable = true; }
    private static FarmEngine LootReady(BattleProfile p, FarmObservation o)
    { FarmEngine e = Locked(p, o); Dead(o); Next(e, o, 200); Next(e, o, 400); Equal("Loot", e.Status, "confirmed corpse enters loot"); return e; }
    private static FarmEngine SkinReady(BattleProfile p, FarmObservation o)
    {
        p.Skinning = true; FarmEngine e = LootReady(p, o); o.LootOpened++; o.LootClosed++; o.Lootable = false; o.Skinnable = true;
        Next(e, o, 600); Equal("Skin", e.Status, "loot events enter skin phase"); return e;
    }
    private static bool Skill(FarmAction a) { return a >= FarmAction.Skill1 && a <= FarmAction.Skill4; }
    private static void ScanBoundaries()
    {
        BattleProfile p = Profile(); p.ScanLimit = 2; FarmEngine e = Engine(p); FarmObservation o = Observation(); e.Step(o, 0); Next(e, o, 200);
        FarmCommand c = Next(e, o, 2000); Equal(FarmAction.SelectTarget, c.Action, "scan starts on interval"); Check(c.HoldMilliseconds >= 60 && c.HoldMilliseconds <= 250, "bounded TAB");
        Equal(FarmAction.None, e.Step(o, 2200).Action, "duplicate TAB not repeated");
        Equal(FarmAction.None, Next(e, o, 3999).Action, "TAB interval respected"); Equal(FarmAction.SelectTarget, Next(e, o, 4000).Action, "second attempt");
        Equal(FarmAction.None, Next(e, o, 6000).Action, "scan budget resumes patrol"); Equal("Patrol", e.Status, "patrol resumes after misses");
    }
    private static void TargetFiltering()
    {
        foreach (bool player in new bool[] { false, true })
        {
            BattleProfile p = Profile(); if (!player) p.Targets[0].Kind = "npc";
            FarmEngine e = Engine(p); FarmObservation o = Monster(true); o.TargetIsPlayer = player;
            Check(!Skill(e.Step(o, 0).Action) && !e.EncounterActive, "NPC or player never accepted");
        }
    }
    private static void CombatSequence()
    {
        BattleProfile p = Profile(); p.SkillSequence = "1124"; FarmObservation o = Monster(true); FarmEngine e = Locked(p, o);
        FarmAction[] expected = { FarmAction.Skill1, FarmAction.Skill1, FarmAction.Skill2, FarmAction.Skill4 };
        for (int i = 0; i < expected.Length; i++)
        {
            long at = 200 + i * 1500; FarmCommand c = Next(e, o, at); Equal(expected[i], c.Action, "ordered skill");
            Equal(77u, c.ExpectedTargetToken, "target identity guard"); Check(c.RequireAliveTarget && !c.RequireDeadTarget, "alive guard");
            Check(c.HoldMilliseconds >= 60 && c.HoldMilliseconds <= 250, "bounded skill"); o.CastSuccess++; o.TargetHealth -= 10;
            Equal(FarmAction.None, Next(e, o, at + 500).Action, "skill rate limit");
        }
    }
    private static void UnavailableActions()
    {
        foreach (int kind in new int[] { 0, 1, 2 })
        {
            FarmObservation o = Monster(true); FarmEngine e = Locked(Profile(), o);
            if (kind == 0) { o.ActionsKnown = false; o.KnownActionsMask = 0; }
            if (kind == 1) o.ReadyActionsMask = 0; if (kind == 2) o.UsableActionsMask = 0;
            Equal(FarmAction.None, Next(e, o, 200).Action, "unknown/unavailable is not usable");
            Equal(FarmAction.None, Next(e, o, 1700).Action, "second unavailable check");
            Equal(FarmAction.Stop, Next(e, o, 3200).Action, "three unavailable checks stop");
        }
    }
    private static void CastingWait()
    {
        FarmObservation o = Monster(true); FarmEngine e = Locked(Profile(), o); o.Casting = true;
        Equal(FarmAction.None, Next(e, o, 200).Action, "casting waits"); o.Casting = false; o.Channeling = true;
        Equal(FarmAction.None, Next(e, o, 2000).Action, "channel waits"); o.Channeling = false;
        Equal(FarmAction.Skill1, Next(e, o, 2200).Action, "cast completion permits skill");
    }
    private static void TargetLockFaults()
    {
        Action<FarmObservation>[] faults = { o => o.TargetToken++, o => o.TargetExists = false, o => o.TargetIdentityKnown = false, o => o.TargetNameHash++, o => o.NameHashKnown = false, o => o.TargetIsPlayer = true, o => o.AttackabilityKnown = false };
        foreach (Action<FarmObservation> fault in faults)
        { FarmObservation o = Monster(true); FarmEngine e = Locked(Profile(), o); fault(o); Equal(FarmAction.Stop, e.Step(o, 100).Action, "duplicate changed identity stops"); Equal(FarmAction.Stop, Next(e, Monster(true), 200).Action, "no automatic resume"); }
    }
    private static void SafetyFaults()
    {
        Action<FarmObservation>[] faults = { o => o.Fresh = false, o => o.KnownCore = false, o => o.PlayerDead = true, o => o.PlayerGhost = true, o => o.BagFull = true,
            o => o.ReadFailures = 1, o => o.MapId++, o => o.MapMode = 1, o => o.Zone = "other", o => o.X = Double.NaN, o => o.Facing = Double.NaN, o => o.Health = 255, o => o.InCombat = true };
        foreach (Action<FarmObservation> fault in faults)
        { FarmEngine e = Engine(Profile()); FarmObservation o = Observation(); fault(o); Equal(FarmAction.Stop, e.Step(o, 0).Action, "unsafe state stops"); Check(!String.IsNullOrEmpty(e.LastReason), "reason retained"); }
    }
    private static void DeathConfirmation()
    {
        FarmObservation o = Monster(true); FarmEngine e = Locked(Profile(), o); Dead(o);
        Equal(FarmAction.None, Next(e, o, 200).Action, "first death observation"); Equal("Combat", e.Status, "still confirming");
        Equal(FarmAction.None, e.Step(o, 400).Action, "duplicate not confirm"); Next(e, o, 600); Equal("Loot", e.Status, "second unique death");
        FarmCommand c = Next(e, o, 800); Equal(FarmAction.Interact, c.Action, "loot input after confirmation"); Check(c.RequireDeadTarget && !c.RequireAliveTarget && c.ExpectedTargetToken == 77, "corpse guard");
    }
    private static void InstantLoot()
    {
        FarmObservation o = Monster(true); FarmEngine e = Locked(Profile(), o); Dead(o); o.LootOpened = 1; o.LootClosed = 1; o.Lootable = false;
        Next(e, o, 200); Next(e, o, 400); FarmCommand c = Next(e, o, 600);
        Check(c.Action != FarmAction.Interact && !e.EncounterActive, "instant open/close evidence survives death confirmation"); Equal("Patrol", e.Status, "resume after confirmed loot");
    }
    private static void EarlierLootEvents()
    {
        FarmObservation o = Monster(true); FarmEngine e = Locked(Profile(), o); o.LootOpened = 1; o.LootClosed = 1;
        Next(e, o, 200); Dead(o); o.Lootable = false; Next(e, o, 400); Next(e, o, 600); Next(e, o, 800);
        Check(e.EncounterActive && e.Status == "Loot", "unrelated earlier loot counters cannot confirm newly killed target");
        o.LootOpened++; o.LootClosed++; Next(e, o, 1000); Check(!e.EncounterActive, "new post-death loot events confirm encounter");
    }
    private static void ApproachDeathConfirmation()
    {
        FarmObservation o = Monster(false); FarmEngine e = Locked(Profile(), o); Dead(o); Next(e, o, 200);
        o.TargetDead = false; o.TargetCanAttack = true; o.TargetHealth = 50; Next(e, o, 400);
        Dead(o); Next(e, o, 600); Equal("Approach", e.Status, "alive observation invalidates first death confirmation");
        Next(e, o, 800); Equal("Loot", e.Status, "two consecutive distinct deaths permit loot");
    }
    private static void RangeEvidenceDeadline()
    {
        FarmObservation o = Monster(true); FarmEngine e = Locked(Profile(), o); o.TargetInRange = false; Next(e, o, 9000);
        o.TargetInRange = true; Next(e, o, 9500);
        Equal(FarmAction.Stop, Next(e, o, 10001).Action, "leaving and reentering range cannot invent combat evidence");
    }
    private static void LootRetry()
    {
        FarmObservation o = Monster(true); BattleProfile p = Profile(); FarmEngine e = LootReady(p, o);
        Equal(FarmAction.Interact, Next(e, o, 600).Action, "first loot attempt");
        Equal(FarmAction.None, Next(e, o, 2399).Action, "no early retry"); Equal(FarmAction.Interact, Next(e, o, 2400).Action, "one bounded retry");
        Equal(FarmAction.None, Next(e, o, 3000).Action, "no third attempt"); Equal(FarmAction.Stop, Next(e, o, 4400).Action, "missing loot events times out");
    }
    private static void LootOpenWait()
    {
        FarmObservation o = Monster(true); FarmEngine e = LootReady(Profile(), o); Next(e, o, 600); o.LootOpened++;
        Equal(FarmAction.None, Next(e, o, 2500).Action, "opened frame waits for close rather than retry"); o.LootClosed++; o.Lootable = false;
        Next(e, o, 2700); Check(!e.EncounterActive, "closed event confirms loot");
    }
    private static void SkinEvidence()
    {
        FarmObservation o = Monster(true); BattleProfile p = Profile(); FarmEngine e = SkinReady(p, o);
        FarmCommand c = Next(e, o, 800); Equal(FarmAction.Skin, c.Action, "skin once"); Check(c.RequireDeadTarget && c.ExpectedTargetToken == 77, "skin locks corpse");
        o.CastSuccess++; Equal(FarmAction.None, Next(e, o, 2400).Action, "generic spell success is not skin proof"); Check(e.EncounterActive, "still waiting dedicated event");
        o.SkinSuccess++; Next(e, o, 2500); Check(!e.EncounterActive, "dedicated skin evidence finishes encounter");
        o = Monster(true); e = SkinReady(Profile(), o); Next(e, o, 800); o.SkinSuccess++;
        Equal(FarmAction.None, Next(e, o, 1000).Action, "minimum skin wait even if event arrives early"); Check(e.EncounterActive, "wait not bypassed");
        Next(e, o, 2300); Check(!e.EncounterActive, "minimum wait elapsed");
    }
    private static void SkinUnknown()
    {
        FarmObservation o = Monster(true); BattleProfile p = Profile(); p.Skinning = true; FarmEngine e = LootReady(p, o); o.LootOpened++; o.LootClosed++; o.Lootable = false; o.SkinKnown = false;
        Equal(FarmAction.Stop, Next(e, o, 600).Action, "enabled unknown skin state stops");
        o = Monster(true); e = LootReady(Profile(), o); o.LootOpened++; o.LootClosed++; o.Lootable = false; o.SkinKnown = false;
        Check(Next(e, o, 600).Action != FarmAction.Stop && !e.EncounterActive, "explicitly disabled skinning does not require skin support");
    }
    private static void SkinFailure()
    {
        FarmObservation o = Monster(true); FarmEngine e = SkinReady(Profile(), o); o.SkinSuccessKnown = false;
        Equal(FarmAction.Stop, Next(e, o, 800).Action, "unknown dedicated event cannot safely skin");
        o = Monster(true); e = SkinReady(Profile(), o); Next(e, o, 800); o.CastFailed++;
        Equal(FarmAction.Stop, Next(e, o, 1200).Action, "failed skin stops without retry");
    }
    private static void RestEvidence()
    {
        FarmEngine e = Engine(Profile()); FarmObservation o = Observation(); o.Health = 30; o.Power = 20;
        e.Step(o, 0); Equal("Rest", e.Status, "low stats enter rest"); Equal(FarmAction.Heal, Next(e, o, 200).Action, "food once"); Equal(FarmAction.Mana, Next(e, o, 400).Action, "drink once");
        Equal(FarmAction.None, Next(e, o, 2000).Action, "do not spam supplies"); o.Health = 85; o.Power = 84; Next(e, o, 4000); Equal("Rest", e.Status, "both recovery thresholds required");
        o.Power = 85; Next(e, o, 4200); Equal("Patrol", e.Status, "observed recovery resumes");
    }
    private static void NonManaRest()
    {
        FarmEngine e = Engine(Profile()); FarmObservation o = Observation(); o.Health = 30; o.Power = 0; o.PowerType = 1; e.Step(o, 0);
        Equal(FarmAction.Heal, Next(e, o, 200).Action, "health recovery"); Check(Next(e, o, 400).Action != FarmAction.Mana, "rage does not drink");
        o.Health = 85; Next(e, o, 1000); Equal("Patrol", e.Status, "nonmana rest ignores mana threshold");
    }
    private static void RestTimeout()
    { FarmEngine e = Engine(Profile()); FarmObservation o = Observation(); o.Health = 30; e.Step(o, 0); Next(e, o, 200); Equal(FarmAction.Stop, e.Step(o, 25000).Action, "duplicate still enforces supply deadline"); }
    private static void ApproachGeometry()
    {
        FarmObservation o = Monster(false); FarmEngine e = Locked(Profile(), o);
        Equal("Approach", e.Status, "out-of-range saved target enters approach"); FarmCommand c = Next(e, o, 200); Equal(FarmAction.Forward, c.Action, "north approach");
        Check(c.HoldMilliseconds >= 60 && c.HoldMilliseconds <= 200 && c.RequireAliveTarget && c.ExpectedTargetToken == 77, "bounded locked movement");
        o.TargetInRange = true; Next(e, o, 400); Equal("Combat", e.Status, "range evidence enters combat"); Equal(FarmAction.Skill1, Next(e, o, 600).Action, "fight only after range known");
        o = Monster(false); o.TargetX = 0.508; o.TargetY = 0.5; e = Locked(Profile(), o);
        Equal(FarmAction.TurnRight, Next(e, o, 200).Action, "east target calibrated clockwise turn");
    }
    private static void ApproachFaults()
    {
        Action<FarmObservation>[] faults = { o => o.TargetPositionKnown = false, o => o.TargetX = Double.NaN, o => o.RangeKnown = false, o => o.RangeSlot = 2, o => o.TargetX = 0.53 };
        foreach (Action<FarmObservation> fault in faults)
        { FarmObservation o = Monster(false); fault(o); FarmEngine e = Engine(Profile()); Equal(FarmAction.Stop, e.Step(o, 0).Action, "unsafe approach rejected before movement"); }
        BattleProfile p = Profile(); p.AllowApproach = false; Equal(FarmAction.Stop, Engine(p).Step(Monster(false), 0).Action, "disabled approach stops out-of-range encounter");
    }
    private static void ApproachNoProgress()
    {
        FarmObservation o = Monster(false); FarmEngine e = Locked(Profile(), o);
        for (int i = 0; i < 10; i++) Equal(FarmAction.Forward, Next(e, o, 200 + i * 200).Action, "bounded forward before threshold");
        Equal(FarmAction.Stop, Next(e, o, 2200).Action, "stationary two seconds stops without blind recovery");
    }
    private static void StateTimeouts()
    {
        FarmObservation o = Monster(false); BattleProfile p = Profile(); FarmEngine e = Locked(p, o); Equal(FarmAction.Stop, e.Step(o, 12000).Action, "approach timeout on duplicate");
        o = Monster(true); e = Locked(Profile(), o); Equal(FarmAction.Stop, e.Step(o, 10001).Action, "ten seconds no cast or health evidence stops on duplicate");
        o = Monster(true); e = LootReady(Profile(), o); Equal(FarmAction.Stop, e.Step(o, 4400).Action, "loot timeout on duplicate");
        o = Monster(true); e = SkinReady(Profile(), o); Equal(FarmAction.Stop, e.Step(o, 6600).Action, "skin timeout on duplicate");
    }
    private static void ClockAndCritical()
    {
        FarmEngine e = Engine(Profile()); FarmObservation o = Observation(); e.Step(o, 100); Equal(FarmAction.Stop, e.Step(o, 99).Action, "backward clock");
        e = Engine(Profile()); e.Step(o, 0); o.Health = 15; Equal(FarmAction.Stop, e.Step(o, 1).Action, "critical on duplicate");
    }
    private static void ResumePatrol()
    {
        FarmEngine e = Engine(Profile()); FarmObservation o = Observation(); e.Step(o, 0); Next(e, o, 200); Equal(1, e.RouteIndex, "first waypoint verified");
        o = Monster(true); o.Sequence = 10; e.Step(o, 400); Dead(o); Next(e, o, 600); Next(e, o, 800); o.LootOpened++; o.LootClosed++; o.Lootable = false; Next(e, o, 1000);
        Check(!e.EncounterActive, "finished encounter"); Equal(1, e.RouteIndex, "same next route index retained");
    }
    private static void RouteCorridor()
    {
        FarmEngine e = Engine(Profile()); FarmObservation o = Observation(); o.Y = 0.48; Check(e.Step(o, 0).Action != FarmAction.Stop, "middle of recorded segment is safe");
        o.X = 0.531; o.Y = 0.481; Equal(FarmAction.Stop, Next(e, o, 200).Action, "more than 0.02 from all recorded segments stops");
    }
    private static void SnapshotAndCompletion()
    {
        BattleProfile p = Profile(); RouteDocument r = Route(); FarmEngine e = new FarmEngine(r, p, false, false, 0); p.Targets[0].NameHash = 1; r.Points[0].X = 0.1;
        FarmObservation o = Monster(true); e.Step(o, 0); Check(e.EncounterActive, "profile snapshot retained"); RouteDocument copy = e.WorkingRoute; copy.Points[0].X = 0.1; Equal(0.5, e.WorkingRoute.Points[0].X, "route clone retained");
        FarmEngine complete = new FarmEngine(Route(), Profile(), false, false, 3); Check(complete.Completed, "completed checkpoint"); Equal(FarmAction.Completed, complete.Step(Observation(), 0).Action, "completed never restarts");
    }
    private static long Simulate(bool skin)
    {
        BattleProfile p = Profile(); p.Skinning = skin; FarmEngine e = Engine(p); FarmSimulator sim = new FarmSimulator(Route(), p, false, false, 0); int skills = 0; bool approached = false, resumed = false;
        for (int i = 0; i < 4000 && !e.Completed; i++)
        {
            FarmCommand c = e.Step(sim.Sample(), sim.ElapsedMilliseconds); Check(c.Action != FarmAction.Stop, "virtual cycle stopped: " + c.Reason);
            if (e.Status == "Approach") approached = true; if (skills > 0 && e.Status == "Patrol") resumed = true;
            if (Skill(c.Action)) skills++; sim.Apply(c); if (c.HoldMilliseconds == 0) sim.Advance(200);
        }
        Check(e.Completed && approached && resumed, "cycle includes approach and patrol resume before completion"); Equal(1, sim.Kills, "one saved monster killed"); Equal(3, sim.SkillCasts, "three observed casts"); Equal(1, sim.Loots, "one confirmed loot"); Equal(skin ? 1 : 0, sim.Skins, "optional skin");
        Check(sim.Heals >= 1 && sim.Drinks >= 1, "initial food and mana restoration demonstrated"); Equal(0, sim.InputEvents, "virtual only"); return sim.ElapsedMilliseconds;
    }
    private static void FullSimulation()
    { long first = Simulate(true); Equal(first, Simulate(true), "deterministic complete cycle"); Simulate(false); }
    private static void SimulatorFaults()
    {
        foreach (bool missingPosition in new bool[] { true, false })
        {
            BattleProfile p = Profile(); FarmEngine e = Engine(p); FarmSimulator sim = new FarmSimulator(Route(), p, false, false, 0);
            sim.MissingTargetPosition = missingPosition; sim.NoSupplies = !missingPosition; FarmCommand c = null;
            for (int i = 0; i < 1000; i++) { c = e.Step(sim.Sample(), sim.ElapsedMilliseconds); if (c.Action == FarmAction.Stop) break; sim.Apply(c); if (c.HoldMilliseconds == 0) sim.Advance(200); }
            Equal(FarmAction.Stop, c.Action, "virtual fault stops"); Equal(0, sim.Kills, "fault cannot complete a kill"); Equal(0, sim.InputEvents, "fault remains virtual");
        }
    }
}
