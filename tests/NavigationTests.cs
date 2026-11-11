using System;
using System.Collections.Generic;
using WoWProbe;

// Pure deterministic tests. Neither native capture nor an input backend is compiled here.
internal static class NavigationTests
{
    private static int passed, failed;
    private static int Main()
    {
        Run("calibration rejects invalid numeric bounds and route identity", CalibrationValidation);
        Run("sample conversion retains unavailable and dangerous packet states", PacketConversion);
        Run("cardinal bearings and shortest wrap choose the correct turn", CompassGeometry);
        Run("aspect ratio and facing sign and offset affect planning", CalibratedGeometry);
        Run("approach pulses shrink and remain within their bounds", PulseBounds);
        Run("arrival needs two consecutive distinct packets", ArrivalConfirmation);
        Run("wait requires elapsed time and another healthy new packet", WaypointWait);
        Run("movement away invalidates pending arrival and wait", ArrivalLost);
        Run("unsafe samples latch stop even on duplicate sequence", DangerousStates);
        Run("bad map coordinate facing clock and timeout stop", InvalidSamplesAndTime);
        Run("duplicates cannot plan or confirm arrival but still time out", DuplicateFrames);
        Run("stuck recovery escalates then stops without skipping a target", RecoveryExhaustion);
        Run("recovery cannot retreat without a previously verified waypoint", RecoveryNoPrevious);
        Run("turning and elapsed idle time cannot trigger stuck recovery", RecoveryUsesForwardTime);
        Run("retreat returns to the previous known waypoint then retries target", RecoveryRetreat);
        Run("retreat already at the previous waypoint never emits a movement pulse", RecoveryRetreatAlreadyThere);
        Run("stuck detection starts only after two seconds of actual forward pulses", RecoveryThreshold);
        Run("observable progress resets the stuck window", RecoveryProgress);
        Run("reverse and loop preserve original route and explicit stop", ReverseAndLoop);
        Run("navigator rejects looping across an unrecorded closing segment", OpenLoopNavigator);
        Run("simulator rejects looping across an unrecorded closing segment", OpenLoopSimulator);
        Run("loop closure uses calibrated distance and larger endpoint tolerance", CalibratedLoopClosure);
        Run("simulator produces independently expected movement and clock", SimulatorGeometry);
        Run("straight corner reverse and aspect simulations finish deterministically", SimulatedRoutes);
        Run("simulation safety faults stop before any virtual movement", SimulatorFaults);
        Console.WriteLine("Navigation tests: {0} passed, {1} failed. Virtual simulation only; no OS input.", passed, failed);
        return failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Equal<T>(T want, T got, string message)
    { if (!EqualityComparer<T>.Default.Equals(want, got)) throw new Exception(message + ": expected " + want + ", actual " + got); }
    private static void Near(double want, double got, double tolerance, string message)
    { Check(!Double.IsNaN(got) && Math.Abs(want - got) <= tolerance, message + ": expected " + want + ", actual " + got); }
    private static void Throws(Action operation)
    {
        try { operation(); } catch (ArgumentException) { return; } catch (InvalidOperationException) { return; }
        throw new Exception("expected validation rejection");
    }
    private static RouteDocument Route(params double[] coordinates)
    {
        RouteDocument r = new RouteDocument { RouteId = "nav-fixture", Name = "Navigation fixture", MapId = 12, MapMode = 1, Zone = "Test zone", Points = new List<Waypoint>() };
        for (int i = 0; i < coordinates.Length; i += 2)
            r.Points.Add(new Waypoint { X = coordinates[i], Y = coordinates[i + 1], Tolerance = 0.0005 });
        return r;
    }
    private static NavigationCalibration Calibration()
    { return new NavigationCalibration { MapId = 12, MapMode = 1, Zone = "Test zone" }; }
    private static NavigationSample Sample(int seq, double x, double y, double rawFacing)
    { return new NavigationSample { X = x, Y = y, Facing = rawFacing, MapId = 12, MapMode = 1, Zone = "Test zone", Sequence = seq, Fresh = true, KnownCore = true }; }
    private static Navigator New(RouteDocument r, int start)
    { return new Navigator(r, Calibration(), false, false, start); }
    private static void CalibrationValidation()
    {
        Calibration().Validate();
        Action<NavigationCalibration>[] corrupt = {
            c => c.AxisScale = Double.NaN, c => c.AxisScale = 0.09, c => c.AxisScale = 10.1,
            c => c.FacingSign = 0, c => c.FacingSign = 0.5, c => c.FacingOffset = Double.PositiveInfinity,
            c => c.TurnRadiansPerSecond = 0.09, c => c.TurnRadiansPerSecond = 10.1,
            c => c.ForwardUnitsPerSecond = 0.00009, c => c.ForwardUnitsPerSecond = 0.21,
            c => c.MapId = 0, c => c.MapMode = 4, c => c.Zone = ""
        };
        foreach (Action<NavigationCalibration> change in corrupt)
        { NavigationCalibration c = Calibration(); change(c); Throws(delegate { c.Validate(); }); }
        NavigationCalibration wrong = Calibration(); wrong.MapId++;
        Throws(delegate { new Navigator(Route(0.5, 0.5, 0.6, 0.5), wrong, false, false, 0); });
        Throws(delegate { New(Route(0.5, 0.5, 0.6, 0.5), -1); });
        Throws(delegate { New(Route(0.5, 0.5, 0.6, 0.5), 3); });
        Navigator complete = new Navigator(Route(0.5, 0.5, 0.6, 0.5, 0.5, 0.5), Calibration(), false, true, 3);
        Check(complete.Completed, "completed checkpoint never loops itself");
        NavigationCalibration copy = Calibration(); Navigator immutable = new Navigator(Route(0.5, 0.5, 0.6, 0.5), copy, false, false, 1);
        copy.FacingSign = 1; copy.AxisScale = 10;
        Equal(NavigationAction.TurnRight, immutable.Step(Sample(1, 0.5, 0.5, 0), 0).Action, "private calibration snapshot");
    }
    private static ProbePacket Packet(bool available, int flags, int known, int failures, int bags)
    {
        byte[] b = new byte[128]; b[0] = 87; b[1] = 80; b[2] = 49; b[3] = 1; b[4] = 9;
        b[6] = (byte)flags; b[7] = (byte)(available ? 83 : 16); b[8] = b[9] = b[10] = 255;
        U16(b, 12, available ? 0 : 65535); U16(b, 14, available ? 32767 : 65535); U16(b, 16, available ? 0 : 65535); U16(b, 18, bags);
        b[20] = (byte)(available ? 12 : 0); U16(b, 24, 65535); U16(b, 26, 65535);
        byte[] zone = System.Text.Encoding.UTF8.GetBytes("Test zone"); b[41] = (byte)zone.Length; Buffer.BlockCopy(zone, 0, b, 74, zone.Length);
        b[122] = (byte)(available ? 1 : 0); b[123] = 255; b[124] = (byte)failures; b[125] = (byte)known;
        int a = 0, sum = 0; for (int i = 0; i < 126; i++) { a = (a + b[i]) % 255; sum = (sum + a) % 255; }
        b[126] = (byte)a; b[127] = (byte)sum; return ProbePacket.Decode(b);
    }
    private static void U16(byte[] b, int i, int value) { b[i] = (byte)value; b[i + 1] = (byte)(value >> 8); }
    private static void PacketConversion()
    {
        NavigationSample s = NavigationSample.FromPacket(Packet(true, 0, 255, 0, 3), true);
        Check(s.Fresh && s.KnownCore && !s.BagFull, "healthy conversion"); Near(0, s.X, 0, "zero is legal");
        Equal(9, s.Sequence, "sequence copied");
        s = NavigationSample.FromPacket(Packet(false, 0, 255, 0, 3), true); Check(!s.Fresh && Double.IsNaN(s.X), "missing position fails closed");
        s = NavigationSample.FromPacket(Packet(true, 0, 3, 0, 3), true); Check(!s.KnownCore, "unknown combat not healthy");
        s = NavigationSample.FromPacket(Packet(true, 135, 255, 2, 3), true); Check(s.Dead && s.Ghost && s.Combat && s.BagFull && s.ReadFailures == 2, "all danger flags copied");
        Check(NavigationSample.FromPacket(Packet(true, 0, 255, 0, 0), true).BagFull, "zero free slots stop patrol");
        Check(!NavigationSample.FromPacket(null, true).Fresh, "missing packet invalid");
        Check(!NavigationSample.FromPacket(Packet(true, 0, 255, 0, 3), false).Fresh, "external freshness preserved");
    }
    private static NavigationAction Direction(double targetX, double targetY, double rawFacing)
    { return New(Route(targetX, targetY, 0.1, 0.1), 0).Step(Sample(1, 0.5, 0.5, rawFacing), 0).Action; }
    private static void CompassGeometry()
    {
        Equal(NavigationAction.Forward, Direction(0.5, 0.4, 0), "north");
        Equal(NavigationAction.TurnRight, Direction(0.6, 0.5, 0), "east clockwise");
        Equal(NavigationAction.TurnLeft, Direction(0.4, 0.5, 0), "west anticlockwise");
        Equal(NavigationAction.Forward, Direction(0.5, 0.6, Math.PI), "south facing south");
        Equal(NavigationAction.TurnLeft, Direction(0.5, 0.4, 6.0), "wrap through north anticlockwise");
        Equal(NavigationAction.TurnRight, Direction(0.5, 0.4, 0.28), "wrap through north clockwise");
    }
    private static void CalibratedGeometry()
    {
        RouteDocument r = Route(0.51, 0.49, 0.2, 0.2);
        NavigationCalibration c = Calibration(); c.AxisScale = 2; c.FacingSign = 1; c.FacingOffset = 0.5;
        Navigator n = new Navigator(r, c, false, false, 0);
        Equal(NavigationAction.Forward, n.Step(Sample(1, 0.5, 0.5, 0.60714871779409), 0).Action, "scaled diagonal and raw offset");
        c.AxisScale = 1; n = new Navigator(r, c, false, false, 0);
        Equal(NavigationAction.TurnLeft, n.Step(Sample(1, 0.5, 0.5, 0.60714871779409), 0).Action, "unscaled diagonal is farther left");
        r = Route(0.501, 0.5, 0.2, 0.2); r.Points[0].Tolerance = 0.0015; c.AxisScale = 2;
        n = new Navigator(r, c, false, false, 0);
        Check(n.Step(Sample(1, 0.5, 0.5, Math.PI / 2 - 0.5), 0).Action != NavigationAction.None, "aspect scales arrival distance too");
    }
    private static void PulseBounds()
    {
        NavigationCommand far = New(Route(0.5, 0.1, 0.1, 0.1), 0).Step(Sample(1, 0.5, 0.5, 0), 0);
        NavigationCommand near = New(Route(0.5, 0.4985, 0.1, 0.1), 0).Step(Sample(1, 0.5, 0.5, 0), 0);
        Check(far.HoldMilliseconds <= 200 && far.HoldMilliseconds >= 60, "forward bound");
        Check(near.HoldMilliseconds >= 60 && near.HoldMilliseconds < far.HoldMilliseconds, "near target shortens pulse");
        NavigationCommand turn = New(Route(0.6, 0.5, 0.1, 0.1), 0).Step(Sample(1, 0.5, 0.5, 0), 0);
        Check(turn.HoldMilliseconds >= 60 && turn.HoldMilliseconds <= 250, "turn bound");
    }
    private static void ArrivalConfirmation()
    {
        Navigator n = New(Route(0.5, 0.5, 0.6, 0.5), 0); NavigationSample s = Sample(65535, 0.5, 0.5, 0);
        Equal(NavigationAction.None, n.Step(s, 0).Action, "first arrival"); Equal(0, n.TargetIndex, "not advanced");
        Equal(NavigationAction.None, n.Step(s, 200).Action, "duplicate"); Equal(0, n.TargetIndex, "duplicate not confirm");
        s.Sequence = 0; n.Step(s, 400); Equal(1, n.TargetIndex, "wrap is distinct arrival packet");
        s.X = 0.6; s.Sequence = 1; n.Step(s, 600); Check(!n.Completed, "last requires confirmation");
        s.Sequence = 2; Equal(NavigationAction.Completed, n.Step(s, 800).Action, "final completion");
        Equal(2, n.TargetIndex, "completion index"); Check(n.Completed, "complete flag");
    }
    private static void WaypointWait()
    {
        RouteDocument r = Route(0.5, 0.5, 0.6, 0.5); r.Points[0].WaitMilliseconds = 1000; r.Points[0].Action = "interact";
        Navigator n = New(r, 0); n.Step(Sample(1, 0.5, 0.5, 0), 0); n.Step(Sample(2, 0.5, 0.5, 0), 100);
        n.Step(Sample(3, 0.5, 0.5, 0), 1099); Equal(0, n.TargetIndex, "full wait after confirmation");
        Equal(NavigationAction.None, n.Step(Sample(3, 0.5, 0.5, 0), 1100).Action, "duplicate cannot finish wait"); Equal(0, n.TargetIndex, "still waiting");
        n.Step(Sample(4, 0.5, 0.5, 0), 1101); Equal(1, n.TargetIndex, "new healthy observation finishes wait");
        Equal(0, n.RecoveryAttempts, "wait no recovery");
    }
    private static void ArrivalLost()
    {
        RouteDocument r = Route(0.5, 0.5, 0.6, 0.5); r.Points[0].WaitMilliseconds = 1000;
        Navigator n = New(r, 0); n.Step(Sample(1, 0.5, 0.5, 0), 0); n.Step(Sample(2, 0.5, 0.5, 0), 100);
        n.Step(Sample(3, 0.5, 0.49, Math.PI), 1100); Equal(0, n.TargetIndex, "leave waypoint resets wait");
        n.Step(Sample(4, 0.5, 0.5, 0), 1200); n.Step(Sample(5, 0.5, 0.5, 0), 1300);
        n.Step(Sample(6, 0.5, 0.5, 0), 2299); Equal(0, n.TargetIndex, "second arrival restarts wait");
        n.Step(Sample(7, 0.5, 0.5, 0), 2300); Equal(1, n.TargetIndex, "reconfirmed wait ends");
    }
    private static void DangerousStates()
    {
        Action<NavigationSample>[] hazards = { s => s.Dead = true, s => s.Ghost = true, s => s.Combat = true, s => s.BagFull = true, s => s.KnownCore = false, s => s.Fresh = false, s => s.ReadFailures = 1 };
        foreach (Action<NavigationSample> hazard in hazards)
        {
            Navigator n = New(Route(0.5, 0.2, 0.6, 0.5), 0); NavigationSample s = Sample(1, 0.5, 0.5, 0); n.Step(s, 0); hazard(s);
            Equal(NavigationAction.Stop, n.Step(s, 1).Action, "duplicate still safety checks");
            Equal(NavigationAction.Stop, n.Step(Sample(2, 0.5, 0.5, 0), 200).Action, "danger latched");
            Check(!String.IsNullOrEmpty(n.LastReason), "stop has explanation");
        }
    }
    private static void InvalidSamplesAndTime()
    {
        Action<NavigationSample>[] faults = { s => s.X = Double.NaN, s => s.Y = 1.01, s => s.X = -0.1, s => s.Facing = Double.PositiveInfinity, s => s.MapId++, s => s.MapMode = 2, s => s.Zone = "Other zone", s => s.Zone = null };
        foreach (Action<NavigationSample> fault in faults)
        { Navigator n = New(Route(0.5, 0.2, 0.6, 0.5), 0); NavigationSample s = Sample(1, 0.5, 0.5, 0); fault(s); Equal(NavigationAction.Stop, n.Step(s, 0).Action, "invalid state stops"); }
        Navigator a = New(Route(0.5, 0.2, 0.6, 0.5), 0); a.Step(Sample(1, 0.5, 0.5, 0), 100); Equal(NavigationAction.Stop, a.Step(Sample(1, 0.5, 0.5, 0), 99).Action, "clock reversal on duplicate");
        Navigator b = New(Route(0.5, 0.2, 0.6, 0.5), 0); Equal(NavigationAction.Stop, b.Step(null, 0).Action, "null sample");
        Navigator c = New(Route(0.5, 0.2, 0.6, 0.5), 0); Equal(NavigationAction.Stop, c.Step(Sample(1, 0.5, 0.5, 0), -1).Action, "negative monotonic clock");
    }
    private static void DuplicateFrames()
    {
        Navigator n = New(Route(0.5, 0.1, 0.1, 0.1), 0); NavigationSample s = Sample(1, 0.5, 0.5, 0);
        n.Step(s, 0); for (int i = 1; i < 100; i++) Equal(NavigationAction.None, n.Step(s, i * 100).Action, "no duplicate action");
        Equal(0, n.RecoveryAttempts, "duplicate no recovery");
        Equal(NavigationAction.Stop, n.Step(s, 120001).Action, "duplicate still node timeout");
    }
    private static void ReachFirst(Navigator n, ref int seq, ref long now)
    { n.Step(Sample(++seq, 0.5, 0.5, 0), now); now += 200; n.Step(Sample(++seq, 0.5, 0.5, 0), now); now += 200; }
    private static NavigationCommand FrozenStep(Navigator n, ref int seq, ref long now, double x, double y)
    {
        NavigationCommand c = n.Step(Sample(++seq, x, y, 0), now); now += Math.Max(60, c.HoldMilliseconds); return c;
    }
    private static void RecoveryExhaustion()
    {
        Navigator n = New(Route(0.5, 0.5, 0.5, 0.2, 0.7, 0.2), 0); int seq = 0; long now = 0; ReachFirst(n, ref seq, ref now);
        bool jump = false, strafe = false; NavigationCommand last = null;
        for (int i = 0; i < 1000; i++)
        {
            last = FrozenStep(n, ref seq, ref now, 0.5, 0.5);
            if (last.Action == NavigationAction.Jump) { jump = true; Equal(120, last.HoldMilliseconds, "jump bound"); }
            if (last.Action == NavigationAction.StrafeLeft || last.Action == NavigationAction.StrafeRight) { strafe = true; Equal(200, last.HoldMilliseconds, "strafe bound"); }
            Check(n.TargetIndex == 1, "never skip blocked target");
            if (last.Action == NavigationAction.Stop) break;
        }
        Check(jump && strafe, "graded recovery attempted"); Equal(3, n.RecoveryAttempts, "recovery cap"); Equal(NavigationAction.Stop, last.Action, "exhaustion stops");
        Equal(NavigationAction.Stop, n.Step(Sample(++seq, 0.5, 0.2, 0), now).Action, "never auto resume");
    }
    private static void RecoveryNoPrevious()
    {
        Navigator n = New(Route(0.5, 0.2, 0.7, 0.2), 0); int seq = 0; long now = 0; NavigationCommand c = null;
        for (int i = 0; i < 300; i++) { c = FrozenStep(n, ref seq, ref now, 0.5, 0.5); if (c.Action == NavigationAction.Stop) break; }
        Equal(NavigationAction.Stop, c.Action, "no verified retreat point stops"); Equal(0, n.TargetIndex, "target not skipped"); Check(n.RecoveryAttempts <= 3, "bounded attempts");
    }
    private static void RecoveryUsesForwardTime()
    {
        Navigator n = New(Route(0.6, 0.5, 0.7, 0.5), 0);
        for (int i = 0; i < 30; i++) Equal(NavigationAction.TurnRight, n.Step(Sample(i, 0.5, 0.5, 0), i * 200).Action, "turn only");
        Equal(0, n.RecoveryAttempts, "turning is not forward stuck");
        n = New(Route(0.5, 0.2, 0.7, 0.2), 0); n.Step(Sample(1, 0.5, 0.5, 0), 0); n.Step(Sample(2, 0.5, 0.5, 0), 10000);
        Equal(0, n.RecoveryAttempts, "idle time credits one bounded forward pulse");
    }
    private static void RecoveryRetreat()
    {
        Navigator n = New(Route(0.5, 0.5, 0.5, 0.2, 0.7, 0.2), 0); int seq = 0; long now = 0; ReachFirst(n, ref seq, ref now);
        NavigationCommand c = null;
        for (int i = 0; i < 100; i++) { c = FrozenStep(n, ref seq, ref now, 0.5, 0.45); if (n.RecoveryAttempts == 3) break; }
        Equal(3, n.RecoveryAttempts, "third recovery retreats");
        Check(c.Action == NavigationAction.TurnLeft || c.Action == NavigationAction.TurnRight, "retreat turns toward verified previous point");
        n.Step(Sample(++seq, 0.5, 0.5, Math.PI), now); now += 250;
        n.Step(Sample(++seq, 0.5, 0.5, Math.PI), now); now += 250;
        Equal(1, n.TargetIndex, "retreat never advances original target");
        c = n.Step(Sample(++seq, 0.5, 0.5, 0), now);
        Equal(NavigationAction.Forward, c.Action, "original target retried after verified retreat");
    }
    private static void RecoveryRetreatAlreadyThere()
    {
        Navigator n = New(Route(0.5, 0.5, 0.5, 0.2), 0); int seq = 0; long now = 0; ReachFirst(n, ref seq, ref now);
        NavigationCommand c = null;
        for (int i = 0; i < 100; i++) { c = FrozenStep(n, ref seq, ref now, 0.5, 0.5); if (n.RecoveryAttempts == 3) break; }
        Equal(3, n.RecoveryAttempts, "entered retreat stage");
        Equal(NavigationAction.None, c.Action, "already at retreat point only confirms, never moves away");
    }
    private static void RecoveryThreshold()
    {
        Navigator n = New(Route(0.5, 0.2, 0.7, 0.2), 0);
        for (int i = 0; i < 10; i++)
        { Equal(NavigationAction.Forward, n.Step(Sample(i, 0.5, 0.5, 0), i * 200).Action, "less than two seconds remains forward"); Equal(0, n.RecoveryAttempts, "recovery not early"); }
        Equal(NavigationAction.Jump, n.Step(Sample(10, 0.5, 0.5, 0), 2000).Action, "two-second stationary threshold");
    }
    private static void RecoveryProgress()
    {
        Navigator n = New(Route(0.5, 0.2, 0.7, 0.2), 0);
        for (int i = 0; i < 40; i++)
            Equal(NavigationAction.Forward, n.Step(Sample(i, 0.5, 0.5 - i * 0.0002, 0), i * 200).Action, "actual forward progress continues normally");
        Equal(0, n.RecoveryAttempts, "moving along route is never classified as stuck");
    }
    private static void ReverseAndLoop()
    {
        RouteDocument r = Route(0.5, 0.5, 0.6, 0.5); r.Points[1].Action = "interact"; r.Points[1].WaitMilliseconds = 1000;
        Navigator reverse = new Navigator(r, Calibration(), true, false, 0);
        Near(0.6, reverse.WorkingRoute.Points[0].X, 0, "reversed point"); Equal("wait", reverse.WorkingRoute.Points[0].Action, "reverse strips interaction");
        RouteDocument copy = reverse.WorkingRoute; copy.Points[0].X = 0.1; Near(0.6, reverse.WorkingRoute.Points[0].X, 0, "private route snapshot"); Near(0.5, r.Points[0].X, 0, "original retained");
        r.AllowReverse = false; Throws(delegate { new Navigator(r, Calibration(), true, false, 0); }); r.AllowReverse = true;
        RouteDocument closed = r.Clone(); closed.Points.Add(r.Points[0].Clone());
        Navigator n = new Navigator(closed, Calibration(), false, true, 0);
        n.Step(Sample(1, 0.5, 0.5, 0), 0); n.Step(Sample(2, 0.5, 0.5, 0), 200);
        n.Step(Sample(3, 0.6, 0.5, 0), 400); n.Step(Sample(4, 0.6, 0.5, 0), 600); n.Step(Sample(5, 0.6, 0.5, 0), 1600);
        Equal(2, n.TargetIndex, "loop follows the recorded closing segment after wait");
        n.Step(Sample(6, 0.5, 0.5, 0), 1800); n.Step(Sample(7, 0.5, 0.5, 0), 2000);
        Equal(0, n.TargetIndex, "closed loop wraps after final arrival"); Check(!n.Completed, "loop active");
        n.Stop("user stop"); Equal(NavigationAction.Stop, n.Step(Sample(8, 0.5, 0.5, 0), 2200).Action, "explicit stop latched");
    }
    private static void OpenLoopNavigator()
    {
        RouteDocument open = Route(0.5, 0.5, 0.6, 0.5, 0.6, 0.4);
        Throws(delegate { new Navigator(open, Calibration(), false, true, 0); });
        Throws(delegate { new Navigator(open, Calibration(), true, true, 0); });
        open.Loop = true;
        Navigator once = new Navigator(open, Calibration(), false, false, 0);
        Check(!once.Completed, "explicit loop=false permits an open single traversal regardless of stored loop setting");
    }
    private static void OpenLoopSimulator()
    {
        RouteDocument open = Route(0.5, 0.5, 0.6, 0.5, 0.6, 0.4);
        Throws(delegate { new NavigationSimulator(open, Calibration(), false, true, 0); });
        Throws(delegate { new NavigationSimulator(open, Calibration(), true, true, 0); });
        open.Loop = true;
        NavigationSimulator once = new NavigationSimulator(open, Calibration(), false, false, 0);
        Equal(0L, once.ElapsedMilliseconds, "explicit loop=false permits a single virtual traversal");
    }
    private static void CalibratedLoopClosure()
    {
        RouteDocument r = Route(0.5, 0.5, 0.6, 0.5, 0.501953125, 0.5);
        r.Points[0].Tolerance = 0.001953125; r.Points[2].Tolerance = 0.0001;
        NavigationCalibration c = Calibration();
        Navigator accepted = new Navigator(r, c, false, true, 0);
        Check(!accepted.Completed, "exact maximum endpoint tolerance accepts closure");
        new NavigationSimulator(r, c, true, true, 0);
        c.AxisScale = 1.00001;
        Throws(delegate { new Navigator(r, c, false, true, 0); });
        Throws(delegate { new NavigationSimulator(r, c, false, true, 0); });
        r.Points[0].Tolerance = 0.0001; r.Points[2].Tolerance = 0.001953125; c.AxisScale = 1;
        new Navigator(r, c, true, true, 0);
        new NavigationSimulator(r, c, false, true, 0);
    }
    private static void SimulatorGeometry()
    {
        RouteDocument r = Route(0.5, 0.5, 0.6, 0.5); NavigationCalibration c = Calibration(); c.AxisScale = 2; c.FacingSign = 1; c.FacingOffset = Math.PI / 2; c.TurnRadiansPerSecond = 2;
        NavigationSimulator sim = new NavigationSimulator(r, c, false, false, 0);
        NavigationSample s1 = sim.Sample(), s2 = sim.Sample(); Check(s1.Sequence != s2.Sequence, "each sample advances virtual sequence");
        sim.Apply(new NavigationCommand { Action = NavigationAction.Forward, HoldMilliseconds = 200 });
        Near(0.501, sim.X, 1e-12, "aspect scaled east motion"); Near(0.5, sim.Y, 1e-12, "east leaves y"); Equal(200L, sim.ElapsedMilliseconds, "apply advances clock");
        sim.Apply(new NavigationCommand { Action = NavigationAction.TurnRight, HoldMilliseconds = 250 }); Near(0.5, sim.Facing, 1e-12, "clockwise raw sign conversion");
        sim.Apply(new NavigationCommand { Action = NavigationAction.Jump, HoldMilliseconds = 120 }); sim.Advance(200); Equal(770L, sim.ElapsedMilliseconds, "virtual clock only");
        Equal(0, sim.InputEvents, "no native input events");
        Throws(delegate { sim.Advance(-1); }); Throws(delegate { sim.Apply(new NavigationCommand { Action = NavigationAction.Forward, HoldMilliseconds = 10000 }); });
    }
    private static long Simulate(RouteDocument route, NavigationCalibration c, bool reverse)
    {
        Navigator n = new Navigator(route, c, reverse, false, 0); NavigationSimulator sim = new NavigationSimulator(route, c, reverse, false, 0);
        for (int i = 0; i < 3000 && !n.Completed; i++)
        {
            NavigationCommand command = n.Step(sim.Sample(), sim.ElapsedMilliseconds);
            Check(command.Action != NavigationAction.Stop, "simulation stopped: " + command.Reason);
            sim.Apply(command); if (command.HoldMilliseconds == 0) sim.Advance(200);
        }
        Check(n.Completed, "simulation did not finish"); Equal(0, sim.InputEvents, "virtual only");
        Equal(0, n.RecoveryAttempts, "clear route never stuck");
        RouteDocument working = reverse ? route.Reversed() : route; Waypoint end = working.Points[working.Points.Count - 1];
        Check(Math.Abs(sim.X - end.X) <= 0.002 && Math.Abs(sim.Y - end.Y) <= 0.002, "ends near final coordinate");
        return sim.ElapsedMilliseconds;
    }
    private static void SimulatedRoutes()
    {
        RouteDocument straight = Route(0.5, 0.5, 0.5, 0.45); NavigationCalibration c = Calibration();
        long first = Simulate(straight, c, false); Equal(first, Simulate(straight, c, false), "deterministic elapsed clock");
        RouteDocument corner = Route(0.5, 0.5, 0.5, 0.46, 0.55, 0.46, 0.55, 0.49);
        Simulate(corner, c, false); Simulate(corner, c, true);
        c.AxisScale = 2.4; c.FacingSign = 1; c.FacingOffset = 0.7; Simulate(corner, c, false);
    }
    private static void SimulatorFaults()
    {
        RouteDocument r = Route(0.5, 0.5, 0.6, 0.5); NavigationSimulator sim = new NavigationSimulator(r, Calibration(), false, false, 0); Navigator n = New(r, 0);
        NavigationSample sample = sim.Sample(); sample.Fresh = false; NavigationCommand command = n.Step(sample, sim.ElapsedMilliseconds); sim.Apply(command);
        Equal(NavigationAction.Stop, command.Action, "simulation fault latches"); Near(0.5, sim.X, 0, "no virtual movement on stop"); Equal(0, sim.InputEvents, "no OS side effects");
    }
}
