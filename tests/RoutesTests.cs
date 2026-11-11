using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using WoWProbe;

// Pure route/file tests: this runner never constructs a native input backend.
internal static class RoutesTests
{
    private static int passed, failed;
    private static string directory;
    private const string Json = "{\"schema_version\":1,\"route_id\":\"test-route\",\"name\":\"Example\",\"route_type\":\"farm\",\"map_id\":123,\"map_mode\":1,\"zone\":\"Zone\",\"reverse_policy\":\"allowed\",\"loop\":false,\"points\":[{\"x\":0,\"y\":0.2,\"tol\":0.003,\"wait_ms\":0,\"action\":\"scan\"},{\"x\":1,\"y\":0.9,\"tol\":0.02,\"wait_ms\":60000,\"action\":\"interact\"}]}";

    private static int Main()
    {
        directory = Path.Combine(Path.GetTempPath(), "WoWProbe-routes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Run("route roundtrip preserves schema, bindings, actions and zero coordinates", Roundtrip);
            Run("Unicode paths and decimal-comma culture roundtrip invariant numbers", UnicodeCulture);
            Run("missing name alone defaults to route id", NameDefault);
            Run("required fields cannot be guessed or coerced", RequiredFields);
            Run("strict JSON rejects duplicate keys and non-JSON syntax", StrictJson);
            Run("JSON rejects incorrect scalar and container types", JsonTypes);
            Run("JSON rejects unknown schema, enum and point values", JsonValues);
            Run("JSON input is bounded in bytes, depth and UTF8 validity", JsonBounds);
            Run("route validation rejects invalid identity and binding", RouteIdentity);
            Run("route validation rejects nonfinite or out of range coordinates", Coordinates);
            Run("route validation checks waypoint tolerance, wait and action", WaypointValues);
            Run("route validation enforces count and distinct positions", PointCounts);
            Run("clones do not share mutable waypoints", CloneIsolation);
            Run("reverse filters interactions and preserves other waypoint data", Reverse);
            Run("forbidden reverse is rejected", ReverseForbidden);
            Run("fingerprint changes for every persisted identity and behavior field", Fingerprints);
            Run("invalid replacement preserves the last good route and leaves no temp files", AtomicRoute);
            Run("recorder starts with one point and suppresses near duplicates", Recorder);
            Run("recorder requires fresh available position and map without read errors", RecorderValidity);
            Run("recorder rejects changed map, mode or zone without mutating", RecorderBindings);
            Run("recorder snapshots are isolated and failed stop remains recordable", RecorderLifecycle);
            Run("recorder bounds point count before appending", RecorderBound);
            Run("checkpoint roundtrip binds original route, direction and loop", Checkpoint);
            Run("checkpoint rejects changed route content and map identity", CheckpointIdentity);
            Run("checkpoint rejects out of bounds index and inconsistent completion", CheckpointProgress);
            Run("checkpoint rejects malformed types, fields and schema", CheckpointMalformed);
            Run("checkpoint replacement is atomic on invalid data", AtomicCheckpoint);
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine("Route tests: {0} passed, {1} failed. No game or OS input was used.", passed, failed);
        return failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action action)
    {
        try { action(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.GetType().Name + ": " + ex.Message); }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual, string message)
    { Check(EqualityComparer<T>.Default.Equals(expected, actual), message + ": expected " + expected + ", got " + actual); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static string FileName(string name) { return Path.Combine(directory, name); }
    private static RouteDocument Example()
    {
        return new RouteDocument { RouteId = "test-route", Name = "Example", MapId = 123, MapMode = 1, Zone = "Zone",
            Points = new List<Waypoint> { new Waypoint { X = 0, Y = 0.2, Action = "scan" },
                new Waypoint { X = 1, Y = 0.9, Tolerance = 0.02, WaitMilliseconds = 60000, Action = "interact" } } };
    }
    private static RouteDocument Read(string json)
    { string path = FileName("input.json"); File.WriteAllText(path, json, new UTF8Encoding(false, true)); return RouteStore.Load(path); }
    private static void InvalidJson(string json) { Throws<FormatException>(delegate { Read(json); }); }
    private static void InvalidRoute(Action<RouteDocument> mutation)
    { RouteDocument route = Example(); mutation(route); Throws<FormatException>(delegate { route.Validate(); }); }

    private static void Roundtrip()
    {
        RouteDocument a = Example(); string path = FileName("roundtrip.json");
        RouteStore.Save(path, a); RouteDocument b = RouteStore.Load(path);
        Equal(1, b.SchemaVersion, "version"); Equal("test-route", b.RouteId, "id"); Equal("Example", b.Name, "name");
        Equal("farm", b.RouteType, "type"); Equal((uint)123, b.MapId, "map"); Equal(1, b.MapMode, "mode");
        Equal("Zone", b.Zone, "zone"); Check(b.AllowReverse && !b.Loop, "policies");
        Equal(2, b.Points.Count, "point count"); Equal(0.0, b.Points[0].X, "legal zero");
        Equal(0.9, b.Points[1].Y, "Y"); Equal(0.02, b.Points[1].Tolerance, "tolerance");
        Equal(60000, b.Points[1].WaitMilliseconds, "wait"); Equal("interact", b.Points[1].Action, "action");
        Equal(a.Fingerprint(), b.Fingerprint(), "fingerprint roundtrip");
        string text = File.ReadAllText(path); Check(text.Contains("\"schema_version\"") && !text.Contains("SchemaVersion"), "snake_case");
        b.MapId = UInt32.MaxValue; RouteStore.Save(path, b); Equal(UInt32.MaxValue, RouteStore.Load(path).MapId, "uint map");
    }
    private static void UnicodeCulture()
    {
        CultureInfo saved = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            RouteDocument route = Example(); route.Name = "\u6d4b\u8bd5\u8def\u7ebf \ud83c\udf0d"; route.Zone = "\u827e\u5c14\u6587\u68ee\u6797";
            route.Points[0].X = 0.12345678901234566;
            string path = FileName("\u8def\u7ebf-\u6d4b\u8bd5.json"); RouteStore.Save(path, route);
            RouteDocument loaded = RouteStore.Load(path); Equal(route.Name, loaded.Name, "Unicode name");
            Equal(route.Zone, loaded.Zone, "Unicode zone"); Equal(route.Points[0].X, loaded.Points[0].X, "roundtrip double");
            Equal(route.Fingerprint(), loaded.Fingerprint(), "culture-independent fingerprint");
        }
        finally { Thread.CurrentThread.CurrentCulture = saved; }
    }
    private static void NameDefault()
    { Equal("test-route", Read(Json.Replace("\"name\":\"Example\",", "")).Name, "missing name default"); InvalidJson(Json.Replace("\"name\":\"Example\"", "\"name\":null")); }
    private static void RequiredFields()
    {
        string[] fields = { "\"schema_version\":1,", "\"route_id\":\"test-route\",", "\"route_type\":\"farm\",", "\"map_id\":123,", "\"map_mode\":1,", "\"zone\":\"Zone\",", "\"reverse_policy\":\"allowed\",", "\"loop\":false," };
        foreach (string field in fields) InvalidJson(Json.Replace(field, ""));
        foreach (string field in new string[] { "\"x\":0,", "\"y\":0.2,", "\"tol\":0.003,", "\"wait_ms\":0,", ",\"action\":\"scan\"" }) InvalidJson(Json.Replace(field, ""));
        InvalidJson(Json.Replace("\"points\":", "\"misspelled_points\":"));
    }
    private static void StrictJson()
    {
        InvalidJson(Json.Replace("\"map_id\":123", "\"map_id\":123,\"map_id\":124"));
        InvalidJson(Json.Replace("\"name\":\"Example\"", "name:'Example'"));
        InvalidJson(Json.Replace("\"x\":0", "\"x\":00")); InvalidJson(Json.Replace("\"x\":0", "\"x\":+0"));
        InvalidJson(Json.Replace("\"x\":0", "\"x\":.0")); InvalidJson(Json.Replace("\"x\":0", "\"x\":0."));
        InvalidJson(Json.Substring(0, Json.Length - 1) + ",}"); InvalidJson(Json + " false");
        InvalidJson("/*comment*/" + Json); InvalidJson(Json.Replace("Example", "bad\nname"));
        InvalidJson(Json.Replace("Example", "\\uD800")); InvalidJson(Json.Replace("Example", "\\uDC00"));
    }
    private static void JsonTypes()
    {
        string[,] cases = { { "\"schema_version\":1", "\"schema_version\":\"1\"" }, { "\"map_id\":123", "\"map_id\":true" },
            { "\"map_mode\":1", "\"map_mode\":1.5" }, { "\"loop\":false", "\"loop\":0" },
            { "\"name\":\"Example\"", "\"name\":[]" }, { "\"reverse_policy\":\"allowed\"", "\"reverse_policy\":true" },
            { "\"x\":0", "\"x\":\"0\"" }, { "\"tol\":0.003", "\"tol\":null" },
            { "\"wait_ms\":0", "\"wait_ms\":0.1" }, { "\"action\":\"scan\"", "\"action\":false" } };
        for (int i = 0; i < cases.GetLength(0); i++) InvalidJson(Json.Replace(cases[i, 0], cases[i, 1]));
        InvalidJson("[]"); InvalidJson("null"); InvalidJson(Json.Replace("\"points\":[", "\"points\":{\"a\":["));
        InvalidJson(Json.Replace("{\"x\":0,\"y\":0.2,\"tol\":0.003,\"wait_ms\":0,\"action\":\"scan\"}", "null"));
    }
    private static void JsonValues()
    {
        string[,] cases = { { "\"schema_version\":1", "\"schema_version\":2" }, { "\"route_type\":\"farm\"", "\"route_type\":\"combat\"" },
            { "\"map_id\":123", "\"map_id\":0" }, { "\"map_id\":123", "\"map_id\":4294967296" },
            { "\"map_id\":123", "\"map_id\":-1" }, { "\"map_mode\":1", "\"map_mode\":0" },
            { "\"reverse_policy\":\"allowed\"", "\"reverse_policy\":\"auto\"" }, { "\"zone\":\"Zone\"", "\"zone\":\"\"" },
            { "\"x\":0", "\"x\":NaN" }, { "\"x\":0", "\"x\":Infinity" },
            { "\"x\":0", "\"x\":1e9999" }, { "\"x\":0", "\"x\":-0.01" } };
        for (int i = 0; i < cases.GetLength(0); i++) InvalidJson(Json.Replace(cases[i, 0], cases[i, 1]));
    }
    private static void JsonBounds()
    {
        InvalidJson(new string(' ', 2 * 1024 * 1024 + 1) + Json);
        InvalidJson(new string('[', 100) + "0" + new string(']', 100));
        string path = FileName("bad-utf8.json"); File.WriteAllBytes(path, new byte[] { 0xff, 0xfe, 0x7b, 0x7d });
        Throws<FormatException>(delegate { RouteStore.Load(path); });
    }
    private static void RouteIdentity()
    {
        InvalidRoute(delegate(RouteDocument r) { r.SchemaVersion = 2; });
        InvalidRoute(delegate(RouteDocument r) { r.RouteId = " "; }); InvalidRoute(delegate(RouteDocument r) { r.Name = null; });
        InvalidRoute(delegate(RouteDocument r) { r.RouteId = new string('x', 129); }); InvalidRoute(delegate(RouteDocument r) { r.Name = new string('x', 129); });
        InvalidRoute(delegate(RouteDocument r) { r.Zone = ""; }); InvalidRoute(delegate(RouteDocument r) { r.Zone = new string('x', 129); });
        InvalidRoute(delegate(RouteDocument r) { r.MapId = 0; }); InvalidRoute(delegate(RouteDocument r) { r.MapMode = 4; });
        InvalidRoute(delegate(RouteDocument r) { r.RouteType = "Farm"; });
        foreach (string type in new string[] { "farm", "corpse_run", "vendor_return" }) { RouteDocument r = Example(); r.RouteType = type; r.Validate(); }
    }
    private static void Coordinates()
    {
        foreach (double value in new double[] { Double.NaN, Double.PositiveInfinity, Double.NegativeInfinity, -0.0001, 1.0001 })
        {
            InvalidRoute(delegate(RouteDocument r) { r.Points[0].X = value; });
            InvalidRoute(delegate(RouteDocument r) { r.Points[0].Y = value; });
        }
    }
    private static void WaypointValues()
    {
        foreach (double value in new double[] { Double.NaN, Double.PositiveInfinity, 0, 0.00009, 0.020001 }) InvalidRoute(delegate(RouteDocument r) { r.Points[0].Tolerance = value; });
        InvalidRoute(delegate(RouteDocument r) { r.Points[0].WaitMilliseconds = -1; }); InvalidRoute(delegate(RouteDocument r) { r.Points[0].WaitMilliseconds = 60001; });
        InvalidRoute(delegate(RouteDocument r) { r.Points[0].Action = null; }); InvalidRoute(delegate(RouteDocument r) { r.Points[0].Action = "fight"; });
        InvalidRoute(delegate(RouteDocument r) { r.Points[0] = null; });
        foreach (string action in new string[] { "scan", "wait", "interact", "turn_point" }) { RouteDocument r = Example(); r.Points[0].Action = action; r.Points[0].Tolerance = 0.0001; r.Validate(); }
    }
    private static void PointCounts()
    {
        InvalidRoute(delegate(RouteDocument r) { r.Points = null; }); InvalidRoute(delegate(RouteDocument r) { r.Points.Clear(); });
        InvalidRoute(delegate(RouteDocument r) { r.Points.RemoveAt(1); });
        InvalidRoute(delegate(RouteDocument r) { r.Points[1].X = 0; r.Points[1].Y = 0.2; });
        RouteDocument route = Example(); route.Points.Insert(1, route.Points[0].Clone()); route.Validate();
        while (route.Points.Count < 5000) route.Points.Add(route.Points[0].Clone()); route.Validate();
        route.Points.Add(route.Points[0].Clone()); Throws<FormatException>(delegate { route.Validate(); });
    }
    private static void CloneIsolation()
    {
        RouteDocument a = Example(), b = a.Clone(); b.Points[0].X = 0.4; b.Points.Add(new Waypoint()); b.Zone = "Other";
        Equal(0.0, a.Points[0].X, "point isolation"); Equal(2, a.Points.Count, "list isolation"); Equal("Zone", a.Zone, "metadata isolation");
    }
    private static void Reverse()
    {
        RouteDocument a = Example(); a.Points[0].WaitMilliseconds = 900; a.Points.Insert(1, new Waypoint { X = 0.5, Y = 0.3, Action = "turn_point", WaitMilliseconds = 123 });
        string before = a.Fingerprint(); RouteDocument b = a.Reversed();
        Equal(1.0, b.Points[0].X, "reversed first point"); Equal("wait", b.Points[0].Action, "interaction filter");
        Equal(0, b.Points[0].WaitMilliseconds, "interaction wait removed"); Equal(0.02, b.Points[0].Tolerance, "tolerance retained");
        Equal("turn_point", b.Points[1].Action, "turn marker retained"); Equal(123, b.Points[1].WaitMilliseconds, "turn wait retained");
        Equal("scan", b.Points[2].Action, "scan retained"); Equal(900, b.Points[2].WaitMilliseconds, "scan wait retained");
        Equal(before, a.Fingerprint(), "source unchanged"); b.Points[2].X = 0.4; Equal(0.0, a.Points[0].X, "reverse clone isolated");
    }
    private static void ReverseForbidden()
    { RouteDocument r = Example(); r.AllowReverse = false; Throws<InvalidOperationException>(delegate { r.Reversed(); }); }
    private static void Fingerprints()
    {
        RouteDocument route = Example(); string first = route.Fingerprint(); Equal(64, first.Length, "SHA256 hex length"); Equal(first, route.Clone().Fingerprint(), "clone identity");
        Action<RouteDocument>[] mutations = {
            delegate(RouteDocument r) { r.RouteId += "x"; }, delegate(RouteDocument r) { r.Name += "x"; },
            delegate(RouteDocument r) { r.RouteType = "corpse_run"; }, delegate(RouteDocument r) { r.MapId++; },
            delegate(RouteDocument r) { r.MapMode = 2; }, delegate(RouteDocument r) { r.Zone += "x"; },
            delegate(RouteDocument r) { r.AllowReverse = false; }, delegate(RouteDocument r) { r.Loop = true; },
            delegate(RouteDocument r) { r.Points[0].X = 0.1; }, delegate(RouteDocument r) { r.Points[0].Y = 0.1; },
            delegate(RouteDocument r) { r.Points[0].Tolerance = 0.004; }, delegate(RouteDocument r) { r.Points[0].WaitMilliseconds++; },
            delegate(RouteDocument r) { r.Points[0].Action = "wait"; }, delegate(RouteDocument r) { r.Points.Reverse(); },
            delegate(RouteDocument r) { r.Points.Add(new Waypoint { X = 0.4, Y = 0.3 }); } };
        foreach (Action<RouteDocument> mutate in mutations) { RouteDocument copy = route.Clone(); mutate(copy); Check(first != copy.Fingerprint(), "changed field must change fingerprint"); }
    }
    private static void AtomicRoute()
    {
        string path = FileName("atomic-route.json"); RouteDocument route = Example(); RouteStore.Save(path, route); string before = File.ReadAllText(path);
        route.Points[0].X = Double.NaN; Throws<FormatException>(delegate { RouteStore.Save(path, route); }); Equal(before, File.ReadAllText(path), "old file preserved");
        route = Example(); route.Name = "Replacement"; RouteStore.Save(path, route); Equal("Replacement", RouteStore.Load(path).Name, "existing file replaced");
        Equal(0, Directory.GetFiles(directory, "*.tmp").Length, "temporary files removed");
    }

    private static ProbePacket Packet(int sequence, double x, double y, uint map, int mode, string zone, int readFailures, bool position, bool mapping)
    {
        byte[] bytes = new byte[128]; bytes[0] = 87; bytes[1] = 80; bytes[2] = 49; bytes[3] = 1;
        U16(bytes, 4, sequence); bytes[7] = (byte)((position ? 1 : 0) | (mapping ? 64 : 0)); bytes[8] = bytes[9] = bytes[10] = 255;
        U16(bytes, 12, position ? (int)Math.Round(x * 65534) : 65535); U16(bytes, 14, position ? (int)Math.Round(y * 65534) : 65535);
        U16(bytes, 16, 65535); U16(bytes, 18, 65535); U16(bytes, 24, 65535); U16(bytes, 26, 65535);
        if (mapping) { for (int i = 0; i < 4; i++) bytes[20 + i] = (byte)(map >> (8 * i)); bytes[122] = (byte)mode; }
        byte[] text = Encoding.UTF8.GetBytes(zone); bytes[41] = (byte)text.Length; Buffer.BlockCopy(text, 0, bytes, 74, text.Length);
        bytes[123] = 255; bytes[124] = (byte)readFailures; bytes[125] = 255;
        int a = 0, b = 0; for (int i = 0; i < 126; i++) { a = (a + bytes[i]) % 255; b = (b + a) % 255; }
        bytes[126] = (byte)a; bytes[127] = (byte)b; return ProbePacket.Decode(bytes);
    }
    private static void U16(byte[] b, int offset, int value) { b[offset] = (byte)value; b[offset + 1] = (byte)(value >> 8); }
    private static ProbePacket At(int sequence, double x) { return Packet(sequence, x, 0.2, 123, 1, "Zone", 0, true, true); }
    private static void Recorder()
    {
        RouteRecorder r = new RouteRecorder(); r.Start(At(1, 0), true, "Recorded", "farm"); Equal(1, r.Count, "first point automatic");
        Check(!r.Add(At(2, 0.000015), true), "near duplicate ignored"); Check(r.Add(At(3, 0.1), true), "new position recorded");
        RouteDocument route = r.Stop(); route.Validate(); Equal(2, route.Points.Count, "recorded count"); Equal((uint)123, route.MapId, "map binding");
        Equal(0.0, route.Points[0].X, "zero not unknown"); Equal("Recorded", route.Name, "recording name"); Check(!r.IsRecording, "stopped");
    }
    private static void RecorderValidity()
    {
        RouteRecorder r = new RouteRecorder(); Throws<InvalidOperationException>(delegate { r.Start(At(1, 0), false, "Name", "farm"); });
        Check(!r.IsRecording, "invalid start leaves idle"); r.Start(At(1, 0), true, "Name", "farm");
        Throws<InvalidOperationException>(delegate { r.Add(At(2, 0.1), false); });
        Throws<InvalidOperationException>(delegate { r.Add(null, true); });
        Throws<InvalidOperationException>(delegate { r.Add(Packet(2, 0.1, 0.2, 123, 1, "Zone", 1, true, true), true); });
        Throws<InvalidOperationException>(delegate { r.Add(Packet(2, 0.1, 0.2, 123, 1, "Zone", 0, false, true), true); });
        Throws<InvalidOperationException>(delegate { r.Add(Packet(2, 0.1, 0.2, 123, 1, "Zone", 0, true, false), true); });
        Equal(1, r.Count, "invalid packets never appended");
    }
    private static void RecorderBindings()
    {
        RouteRecorder r = new RouteRecorder(); r.Start(At(1, 0), true, "Name", "farm");
        Throws<InvalidOperationException>(delegate { r.Add(Packet(2, 0.1, 0.2, 124, 1, "Zone", 0, true, true), true); });
        Throws<InvalidOperationException>(delegate { r.Add(Packet(2, 0.1, 0.2, 123, 2, "Zone", 0, true, true), true); });
        Throws<InvalidOperationException>(delegate { r.Add(Packet(2, 0, 0.2, 123, 1, "Other", 0, true, true), true); });
        Equal(1, r.Count, "binding errors leave route unchanged"); Check(r.IsRecording, "binding error retains recording for explicit stop/cancel");
    }
    private static void RecorderLifecycle()
    {
        RouteRecorder r = new RouteRecorder(); Equal(null, r.Snapshot, "idle snapshot"); Throws<InvalidOperationException>(delegate { r.Stop(); });
        r.Start(At(1, 0), true, "Name", "farm"); Throws<InvalidOperationException>(delegate { r.Start(At(2, 0.1), true, "Other", "farm"); });
        Throws<FormatException>(delegate { r.Stop(); }); Check(r.IsRecording, "too few points retain recording");
        RouteDocument snapshot = r.Snapshot; snapshot.Points[0].X = 1; snapshot.Name = "Other"; Equal(0.0, r.Snapshot.Points[0].X, "snapshot point isolation"); Equal("Name", r.Snapshot.Name, "snapshot metadata isolation");
        r.Add(At(2, 0.1), true); r.Stop(); Equal(0, r.Count, "stop clears recording"); Equal(null, r.Snapshot, "stop snapshot");
        r.Start(At(3, 0.2), true, "Name", "vendor_return"); r.Cancel(); r.Cancel(); Check(!r.IsRecording, "cancel idle"); Equal(0, r.Count, "cancel count");
    }
    private static void RecorderBound()
    {
        RouteRecorder r = new RouteRecorder(); r.Start(At(0, 0), true, "Name", "farm");
        for (int i = 1; i < 5000; i++) Check(r.Add(At(i, (i % 2) * 0.1), true), "alternating points append");
        Throws<InvalidOperationException>(delegate { r.Add(At(5000, 0), true); }); Equal(5000, r.Count, "count bounded"); r.Stop().Validate();
    }
    private static RouteCheckpoint Progress(RouteDocument route)
    { return new RouteCheckpoint { RouteFingerprint = route.Fingerprint(), NextIndex = 1, Reverse = true, Loop = false, MapId = route.MapId, MapMode = route.MapMode, Zone = route.Zone }; }
    private static void Checkpoint()
    {
        RouteDocument route = Example(); string path = FileName("checkpoint.json"); RouteCheckpoint cp = Progress(route); CheckpointStore.Save(path, cp);
        RouteCheckpoint loaded = CheckpointStore.Load(path, route, true, false); Equal(1, loaded.NextIndex, "next index"); Check(loaded.Reverse && !loaded.Loop && !loaded.Completed, "checkpoint flags");
        Throws<FormatException>(delegate { CheckpointStore.Load(path, route, false, false); }); Throws<FormatException>(delegate { CheckpointStore.Load(path, route, true, true); });
        cp.NextIndex = 2; cp.Completed = true; CheckpointStore.Save(path, cp); loaded = CheckpointStore.Load(path, route, true, false);
        Check(loaded.Completed && loaded.NextIndex == 2, "completed checkpoint remains completed for caller to prevent resume");
        cp.NextIndex = 0; cp.Completed = false; CheckpointStore.Save(path, cp); Equal(0, CheckpointStore.Load(path, route, true, false).NextIndex, "zero next index valid");
    }
    private static void CheckpointIdentity()
    {
        RouteDocument route = Example(); string path = FileName("cp-identity.json"); CheckpointStore.Save(path, Progress(route));
        RouteDocument changed = route.Clone(); changed.Points[0].Tolerance = 0.005; Throws<FormatException>(delegate { CheckpointStore.Load(path, changed, true, false); });
        foreach (int which in new int[] { 0, 1, 2, 3 })
        {
            RouteCheckpoint cp = Progress(route); if (which == 0) cp.MapId++; if (which == 1) cp.MapMode++; if (which == 2) cp.Zone += "x"; if (which == 3) cp.RouteFingerprint = new string('0', 64);
            CheckpointStore.Save(path, cp); Throws<FormatException>(delegate { CheckpointStore.Load(path, route, true, false); });
        }
        route.AllowReverse = false; RouteCheckpoint forbidden = Progress(route); CheckpointStore.Save(path, forbidden); Throws<FormatException>(delegate { CheckpointStore.Load(path, route, true, false); });
    }
    private static void CheckpointProgress()
    {
        RouteDocument route = Example(); string path = FileName("cp-progress.json");
        foreach (int index in new int[] { -1, 3 })
        {
            RouteCheckpoint cp = Progress(route); CheckpointStore.Save(path, cp); string text = File.ReadAllText(path).Replace("\"next_index\":1", "\"next_index\":" + index.ToString(CultureInfo.InvariantCulture));
            File.WriteAllText(path, text); Throws<FormatException>(delegate { CheckpointStore.Load(path, route, true, false); });
        }
        RouteCheckpoint end = Progress(route); end.NextIndex = 2; end.Completed = false; CheckpointStore.Save(path, end); Throws<FormatException>(delegate { CheckpointStore.Load(path, route, true, false); });
        end.NextIndex = 1; end.Completed = true; CheckpointStore.Save(path, end); Throws<FormatException>(delegate { CheckpointStore.Load(path, route, true, false); });
    }
    private static void CheckpointMalformed()
    {
        RouteDocument route = Example(); string path = FileName("cp-malformed.json"); CheckpointStore.Save(path, Progress(route)); string text = File.ReadAllText(path);
        string[,] cases = { { "\"schema_version\":1", "\"schema_version\":2" }, { "\"next_index\":1", "\"next_index\":\"1\"" }, { "\"reverse\":true", "\"reverse\":1" }, { "\"completed\":false,", "" }, { "\"map_mode\":1", "\"map_mode\":null" } };
        for (int i = 0; i < cases.GetLength(0); i++) { File.WriteAllText(path, text.Replace(cases[i, 0], cases[i, 1])); Throws<FormatException>(delegate { CheckpointStore.Load(path, route, true, false); }); }
    }
    private static void AtomicCheckpoint()
    {
        RouteDocument route = Example(); string path = FileName("cp-atomic.json"); RouteCheckpoint cp = Progress(route); CheckpointStore.Save(path, cp); string before = File.ReadAllText(path);
        cp.MapId = 0; Throws<FormatException>(delegate { CheckpointStore.Save(path, cp); }); Equal(before, File.ReadAllText(path), "invalid checkpoint preserves file");
        cp = Progress(route); cp.NextIndex = 0; CheckpointStore.Save(path, cp); Equal(0, CheckpointStore.Load(path, route, true, false).NextIndex, "checkpoint replaced");
        Equal(0, Directory.GetFiles(directory, "*.tmp").Length, "checkpoint temp cleanup");
    }
}
