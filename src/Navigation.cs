using System;

namespace WoWProbe
{
    public sealed class NavigationCalibration
    {
        public double AxisScale = 1, FacingSign = -1, FacingOffset, TurnRadiansPerSecond = 2, ForwardUnitsPerSecond = 0.01;
        public bool Confirmed;
        public uint MapId;
        public int MapMode;
        public string Zone = "";
        public void Validate()
        {
            if (!NavigationMath.InRange(AxisScale, 0.1, 10) || (FacingSign != -1 && FacingSign != 1) ||
                !NavigationMath.Finite(FacingOffset) || !NavigationMath.InRange(TurnRadiansPerSecond, 0.1, 10) ||
                !NavigationMath.InRange(ForwardUnitsPerSecond, 0.0001, 0.2))
                throw new ArgumentException("Invalid navigation scale, orientation or movement calibration.");
            if (MapId == 0 || MapMode < 1 || MapMode > 3 || String.IsNullOrWhiteSpace(Zone) || Zone.Length > 128)
                throw new ArgumentException("Navigation calibration requires a valid map ID, mode and zone.");
        }
        public NavigationCalibration Clone() { return (NavigationCalibration)MemberwiseClone(); }
    }
    public sealed class NavigationSample
    {
        public double X, Y, Facing;
        public uint MapId;
        public int MapMode, Sequence;
        public string Zone;
        public bool Fresh, KnownCore, Dead, Ghost, Combat, BagFull;
        public int ReadFailures;
        public static NavigationSample FromPacket(ProbePacket packet, bool fresh)
        {
            if (packet == null) return new NavigationSample { X = Double.NaN, Y = Double.NaN, Facing = Double.NaN };
            return new NavigationSample {
                X = packet.X, Y = packet.Y, Facing = packet.Facing, MapId = packet.MapId, MapMode = packet.MapMode,
                Zone = packet.Zone, Sequence = packet.Sequence, ReadFailures = packet.ReadFailures,
                Fresh = fresh && (packet.Capabilities & 67) == 67 && NavigationMath.InRange(packet.X, 0, 1) &&
                    NavigationMath.InRange(packet.Y, 0, 1) && NavigationMath.Finite(packet.Facing) && packet.MapId != 0 && packet.MapMode >= 1 && packet.MapMode <= 3,
                KnownCore = (packet.KnownFlags & 7) == 7,
                Dead = (packet.Flags & 1) != 0, Ghost = (packet.Flags & 2) != 0, Combat = (packet.Flags & 4) != 0,
                BagFull = (packet.Flags & 128) != 0 || packet.FreeSlots == 0
            };
        }
    }
    public enum NavigationAction { None, Forward, TurnLeft, TurnRight, StrafeLeft, StrafeRight, Jump, Stop, Completed }
    public enum NavigationPurpose { Patrol, CorpseRun, VendorReturn }
    public sealed class NavigationCommand
    {
        public NavigationAction Action;
        public int HoldMilliseconds, TargetIndex;
        public string Reason;
    }
    public sealed class Navigator
    {
        private readonly RouteDocument route;
        private readonly NavigationCalibration calibration;
        private readonly bool loop;
        private readonly NavigationPurpose purpose;
        private bool stopped, hasTime, hasSequence, hasForwardAnchor, retreating;
        private int sequence, arrivalCount, lastReached = -1, pendingForwardMs;
        private long lastTime, targetStarted, arrivalTime, pendingIssued, forwardMs;
        private double anchorX, anchorY;

        public Navigator(RouteDocument original, NavigationCalibration calibration, bool reverse, bool loop, int startIndex)
            :this(original,calibration,reverse,loop,startIndex,NavigationPurpose.Patrol) { }
        public Navigator(RouteDocument original, NavigationCalibration calibration, bool reverse, bool loop, int startIndex,NavigationPurpose purpose)
        {
            if(purpose!=NavigationPurpose.Patrol&&purpose!=NavigationPurpose.CorpseRun&&purpose!=NavigationPurpose.VendorReturn)throw new ArgumentOutOfRangeException("purpose");
            route = NavigationMath.Prepare(original, calibration, reverse, loop, startIndex);
            this.calibration = calibration.Clone();
            this.loop = loop;this.purpose=purpose;
            TargetIndex = startIndex;
            Completed = startIndex == route.Points.Count;
            Status = Completed ? "Completed" : "Ready";
            LastReason = Completed ? "The supplied checkpoint is complete; it will not restart automatically." : "Awaiting a healthy new frame.";
        }
        public int TargetIndex { get; private set; }
        public string Status { get; private set; }
        public int RecoveryAttempts { get; private set; }
        public bool Completed { get; private set; }
        public RouteDocument WorkingRoute { get { return route.Clone(); } }
        public string LastReason { get; private set; }
        public NavigationCommand Step(NavigationSample sample, long nowMs)
        {
            if (stopped) return Command(NavigationAction.Stop, 0, LastReason);
            if (Completed) return Command(NavigationAction.Completed, 0, LastReason);
            if (nowMs < 0 || (hasTime && nowMs < lastTime)) return Fail("Monotonic clock moved backwards or is invalid.");
            if (!hasTime) { hasTime = true; targetStarted = nowMs; }
            lastTime = nowMs;
            string invalid = UnsafeReason(sample);
            if (invalid != null) return Fail(invalid);
            if (nowMs - targetStarted > 120000) return Fail("Waypoint exceeded its 120-second deadline.");
            // Duplicate frames never plan movement or change arrival/recovery counters. The safety gate above still runs.
            if (hasSequence && sequence == sample.Sequence)
                return Command(NavigationAction.None, 0, "Awaiting a distinct frame.");
            hasSequence = true; sequence = sample.Sequence;

            if (pendingForwardMs != 0)
            {
                forwardMs += Math.Min((long)pendingForwardMs, nowMs - pendingIssued);
                pendingForwardMs = 0;
            }
            if (hasForwardAnchor && NavigationMath.Distance(sample.X - anchorX, sample.Y - anchorY, calibration.AxisScale) >= 0.0001)
                ResetForwardWindow();

            Waypoint target = route.Points[retreating ? lastReached : TargetIndex];
            double distance = NavigationMath.Distance(target.X - sample.X, target.Y - sample.Y, calibration.AxisScale);
            if (distance <= target.Tolerance)
            {
                ResetForwardWindow();
                if (arrivalCount < 2) arrivalCount++;
                if (arrivalCount < 2) return Command(NavigationAction.None, 0, "Confirming waypoint with another distinct frame.");
                if (retreating)
                {
                    retreating = false; arrivalCount = 0;
                    return Command(NavigationAction.None, 0, "Previous verified waypoint reached; retrying the blocked target.");
                }
                if (arrivalCount == 2) { arrivalTime = nowMs; arrivalCount = 3; }
                if (nowMs - arrivalTime < target.WaitMilliseconds)
                {
                    Status = "Waiting";
                    return Command(NavigationAction.None, 0, "Waiting at waypoint; action '" + target.Action + "' is metadata only.");
                }
                lastReached = TargetIndex;
                TargetIndex++;
                if (TargetIndex == route.Points.Count)
                {
                    if (!loop)
                    {
                        Completed = true; Status = "Completed";
                        return Command(NavigationAction.Completed, 0, "Route completed; automatic restart is disabled.");
                    }
                    TargetIndex = 0;
                }
                targetStarted = nowMs; arrivalCount = 0; Status = "Navigating";
                return Command(NavigationAction.None, 0, "Waypoint verified; awaiting a new frame for the next target.");
            }
            arrivalCount = 0;
            if (forwardMs >= 2000)
            {
                if (retreating || RecoveryAttempts >= 3) return Fail("Stuck recovery exhausted; route stopped without skipping its target.");
                RecoveryAttempts++; ResetForwardWindow(); Status = "Recovering";
                if (RecoveryAttempts == 1) return Command(NavigationAction.Jump, 120, "No movement after two seconds of forward pulses: bounded jump recovery.");
                if (RecoveryAttempts == 2) return Command(NavigationAction.StrafeLeft, 200, "Still stuck: bounded lateral recovery.");
                if (lastReached < 0 || lastReached == TargetIndex) return Fail("No previously verified waypoint is available for safe retreat.");
                retreating = true; target = route.Points[lastReached];
                distance = NavigationMath.Distance(target.X - sample.X, target.Y - sample.Y, calibration.AxisScale);
                if (distance <= target.Tolerance)
                {
                    arrivalCount = 1;
                    return Command(NavigationAction.None, 0, "Already at the previous verified waypoint; confirming retreat with a new frame.");
                }
                // Retreat is planned through the same calibrated steering; it cannot skip the blocked target.
            }
            if (!retreating) Status = "Navigating";
            double bearing = Math.Atan2((target.X - sample.X) * calibration.AxisScale, -(target.Y - sample.Y));
            double facing = NavigationMath.Normalize(NavigationMath.Normalize(calibration.FacingSign * sample.Facing) + NavigationMath.Normalize(calibration.FacingOffset));
            double delta = NavigationMath.Normalize(bearing - facing);
            if (Math.Abs(delta) > 0.12)
                return Command(delta > 0 ? NavigationAction.TurnRight : NavigationAction.TurnLeft,
                    NavigationMath.Pulse(Math.Abs(delta) / calibration.TurnRadiansPerSecond * 1000, 60, 250),
                    retreating ? "Turning toward the previous verified waypoint." : "Correcting calibrated heading.");
            int hold = NavigationMath.Pulse((distance - target.Tolerance * 0.5) / calibration.ForwardUnitsPerSecond * 1000, 60, 200);
            if (!hasForwardAnchor) { hasForwardAnchor = true; anchorX = sample.X; anchorY = sample.Y; }
            pendingForwardMs = hold; pendingIssued = nowMs;
            return Command(NavigationAction.Forward, hold, retreating ? "Returning to the previous verified waypoint." : "Approaching waypoint with a bounded forward pulse.");
        }

        public void Stop(string reason)
        {
            if (stopped) return;
            stopped = true; Status = "Stopped"; LastReason = String.IsNullOrWhiteSpace(reason) ? "Stopped by request." : reason;
            ResetForwardWindow();
        }
        private string UnsafeReason(NavigationSample sample)
        {
            if (sample == null || !sample.Fresh) return "Fresh position, facing and map data are required.";
            if (!sample.KnownCore || sample.ReadFailures != 0) return "Core state is unknown or a game API read failed.";
            if(sample.Combat)return "Combat stopped navigation.";
            if(purpose==NavigationPurpose.CorpseRun)
            {if(!sample.Ghost)return "Corpse navigation requires the player's actual known ghost state.";}
            else if(sample.Dead||sample.Ghost||(purpose==NavigationPurpose.Patrol&&sample.BagFull))return "Dead, ghost or full-bag state stopped navigation.";
            if (sample.MapId != route.MapId || sample.MapMode != route.MapMode || !String.Equals(sample.Zone, route.Zone, StringComparison.Ordinal))
                return "Map identity, map mode or zone changed.";
            if (!NavigationMath.InRange(sample.X, 0, 1) || !NavigationMath.InRange(sample.Y, 0, 1) || !NavigationMath.Finite(sample.Facing))
                return "Position or facing is invalid.";
            return null;
        }
        private void ResetForwardWindow() { hasForwardAnchor = false; forwardMs = 0; pendingForwardMs = 0; }
        private NavigationCommand Fail(string reason) { Stop(reason); return Command(NavigationAction.Stop, 0, LastReason); }
        private NavigationCommand Command(NavigationAction action, int hold, string reason)
        {
            LastReason = reason;
            return new NavigationCommand { Action = action, HoldMilliseconds = hold, TargetIndex = TargetIndex, Reason = reason };
        }
    }
    public sealed class NavigationSimulator
    {
        private readonly RouteDocument route;
        private readonly NavigationCalibration calibration;
        private int sequence;
        public NavigationSimulator(RouteDocument route, NavigationCalibration calibration, bool reverse, bool loop, int startIndex)
        {
            this.route = NavigationMath.Prepare(route, calibration, reverse, loop, startIndex);
            this.calibration = calibration.Clone();
            Waypoint initial = this.route.Points[Math.Max(0, Math.Min(startIndex - 1, this.route.Points.Count - 1))];
            X = initial.X; Y = initial.Y; Facing = 0;
        }
        public double X { get; private set; }
        public double Y { get; private set; }
        public double Facing { get; private set; }
        public long ElapsedMilliseconds { get; private set; }
        public int InputEvents { get { return 0; } }
        public NavigationSample Sample()
        {
            sequence = (sequence + 1) & 65535;
            return new NavigationSample { X = X, Y = Y, Facing = Facing, MapId = route.MapId, MapMode = route.MapMode,
                Zone = route.Zone, Sequence = sequence, Fresh = true, KnownCore = true };
        }
        public void Apply(NavigationCommand command)
        {
            if (command == null) throw new ArgumentNullException("command");
            int hold = command.HoldMilliseconds;
            switch (command.Action)
            {
                case NavigationAction.None:
                case NavigationAction.Stop:
                case NavigationAction.Completed:
                    if (hold != 0) throw new ArgumentException("Nonmovement commands cannot hold an input.");
                    return;
                case NavigationAction.Forward:
                    if (hold < 60 || hold > 200) throw new ArgumentException("Forward pulse must be 60 to 200 milliseconds.");
                    break;
                case NavigationAction.TurnLeft:
                case NavigationAction.TurnRight:
                    if (hold < 60 || hold > 250) throw new ArgumentException("Turn pulse must be 60 to 250 milliseconds.");
                    break;
                case NavigationAction.StrafeLeft:
                case NavigationAction.StrafeRight:
                    if (hold != 200) throw new ArgumentException("Recovery strafe must be 200 milliseconds.");
                    break;
                case NavigationAction.Jump:
                    if (hold != 120) throw new ArgumentException("Recovery jump must be 120 milliseconds.");
                    break;
                default: throw new ArgumentException("Unknown virtual action.");
            }
            double seconds = hold / 1000.0;
            double compass = NavigationMath.Normalize(NavigationMath.Normalize(calibration.FacingSign * Facing) + NavigationMath.Normalize(calibration.FacingOffset));
            if (command.Action == NavigationAction.TurnLeft || command.Action == NavigationAction.TurnRight)
            {
                compass += (command.Action == NavigationAction.TurnRight ? 1 : -1) * calibration.TurnRadiansPerSecond * seconds;
                Facing = NavigationMath.PositiveAngle(calibration.FacingSign * (NavigationMath.Normalize(compass) - NavigationMath.Normalize(calibration.FacingOffset)));
            }
            else if (command.Action != NavigationAction.Jump)
            {
                if (command.Action == NavigationAction.StrafeLeft) compass -= Math.PI / 2;
                if (command.Action == NavigationAction.StrafeRight) compass += Math.PI / 2;
                double travel = calibration.ForwardUnitsPerSecond * seconds;
                X = Math.Max(0, Math.Min(1, X + Math.Sin(compass) * travel / calibration.AxisScale));
                Y = Math.Max(0, Math.Min(1, Y - Math.Cos(compass) * travel));
            }
            Advance(hold);
        }
        public void Advance(int ms)
        {
            if (ms < 0 || ElapsedMilliseconds > Int64.MaxValue - ms) throw new ArgumentOutOfRangeException("ms");
            ElapsedMilliseconds += ms;
        }
    }

    internal static class NavigationMath
    {
        internal static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        internal static bool InRange(double value, double min, double max) { return Finite(value) && value >= min && value <= max; }
        internal static double Normalize(double angle)
        { angle %= 2 * Math.PI; if (angle >= Math.PI) angle -= 2 * Math.PI; if (angle < -Math.PI) angle += 2 * Math.PI; return angle; }
        internal static double PositiveAngle(double angle) { angle %= 2 * Math.PI; return angle < 0 ? angle + 2 * Math.PI : angle; }
        internal static double Distance(double dx, double dy, double axisScale) { return Math.Sqrt(dx * axisScale * dx * axisScale + dy * dy); }
        internal static int Pulse(double requested, int min, int max) { return (int)Math.Max(min, Math.Min(max, Math.Floor(requested))); }
        internal static RouteDocument Prepare(RouteDocument original, NavigationCalibration calibration, bool reverse, bool loop, int startIndex)
        {
            if (original == null) throw new ArgumentNullException("original");
            if (calibration == null) throw new ArgumentNullException("calibration");
            RouteDocument snapshot = original.Clone(); snapshot.Validate(); calibration.Validate();
            if (calibration.MapId != snapshot.MapId || calibration.MapMode != snapshot.MapMode || !String.Equals(calibration.Zone, snapshot.Zone, StringComparison.Ordinal))
                throw new ArgumentException("Navigation calibration does not match the route's map identity.");
            if (startIndex < 0 || startIndex > snapshot.Points.Count) throw new ArgumentOutOfRangeException("startIndex");
            if (loop)
            {
                Waypoint first = snapshot.Points[0], last = snapshot.Points[snapshot.Points.Count - 1];
                if (Distance(first.X - last.X, first.Y - last.Y, calibration.AxisScale) > Math.Max(first.Tolerance, last.Tolerance))
                    throw new ArgumentException("Looping requires a recorded closing segment: the final waypoint must be within calibrated endpoint tolerance of the first waypoint.");
            }
            return reverse ? snapshot.Reversed() : snapshot;
        }
    }
}
