using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace WoWProbe
{
    public sealed class Waypoint
    {
        public double X, Y, Tolerance = 0.003;
        public int WaitMilliseconds;
        public string Action = "wait";
        public Waypoint Clone() { return (Waypoint)MemberwiseClone(); }
    }
    public sealed class RouteDocument
    {
        public int SchemaVersion = 1;
        public string RouteId, Name, RouteType = "farm", Zone = "";
        public uint MapId;
        public int MapMode;
        public bool AllowReverse = true, Loop;
        public List<Waypoint> Points = new List<Waypoint>();
        public void Validate()
        {
            if (SchemaVersion != 1) throw new FormatException("Unsupported route schema version.");
            RouteChecks.Label(RouteId, "route_id"); RouteChecks.Label(Name, "name");
            RouteChecks.Type(RouteType); RouteChecks.Binding(MapId, MapMode, Zone);
            if (Points == null || Points.Count < 2 || Points.Count > 5000)
                throw new FormatException("A route must have 2 through 5000 waypoints.");
            bool different = false;
            for (int i = 0; i < Points.Count; i++)
            {
                Waypoint p = Points[i];
                if (p == null) throw new FormatException("A waypoint cannot be null.");
                if (!RouteChecks.Range(p.X, 0, 1) || !RouteChecks.Range(p.Y, 0, 1))
                    throw new FormatException("Waypoint coordinates must be finite normalized values from 0 through 1.");
                if (!RouteChecks.Range(p.Tolerance, 0.0001, 0.02)) throw new FormatException("Invalid waypoint tolerance.");
                if (p.WaitMilliseconds < 0 || p.WaitMilliseconds > 60000) throw new FormatException("Invalid waypoint wait.");
                if (p.Action != "scan" && p.Action != "wait" && p.Action != "interact" && p.Action != "turn_point")
                    throw new FormatException("Unsupported waypoint action.");
                if (p.X != Points[0].X || p.Y != Points[0].Y) different = true;
            }
            if (!different) throw new FormatException("A route must contain at least two different positions.");
        }
        public RouteDocument Clone()
        {
            RouteDocument copy = (RouteDocument)MemberwiseClone();
            if (Points != null)
            {
                copy.Points = new List<Waypoint>(Points.Count);
                foreach (Waypoint point in Points) copy.Points.Add(point == null ? null : point.Clone());
            }
            return copy;
        }
        public RouteDocument Reversed()
        {
            Validate();
            if (!AllowReverse) throw new InvalidOperationException("This route forbids reverse navigation.");
            RouteDocument copy = Clone(); copy.Points.Reverse();
            foreach (Waypoint point in copy.Points)
                if (point.Action == "interact") { point.Action = "wait"; point.WaitMilliseconds = 0; }
            return copy;
        }
        public string Fingerprint()
        {
            Validate();
            // Fixed field order and roundtrip invariant numbers make identity independent of JSON whitespace/culture.
            byte[] bytes = RouteFiles.Utf8.GetBytes(RouteJson.WriteRoute(this));
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes); StringBuilder result = new StringBuilder(64);
                foreach (byte value in hash) result.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }
    }
    public static class RouteStore
    {
        public static void Save(string path, RouteDocument route)
        {
            if (route == null) throw new ArgumentNullException("route");
            RouteDocument copy = route.Clone(); copy.Validate();
            RouteFiles.Save(path, RouteJson.WriteRoute(copy));
        }
        public static RouteDocument Load(string path)
        {
            Dictionary<string, object> obj = RouteJson.Object(RouteFiles.Load(path));
            RouteJson.Keys(obj, "schema_version,route_id,name,route_type,map_id,map_mode,zone,reverse_policy,loop,points", "name");
            string id = RouteJson.String(obj, "route_id");
            string policy = RouteJson.String(obj, "reverse_policy");
            if (policy != "allowed" && policy != "forbidden") throw new FormatException("Unsupported reverse policy.");
            RouteDocument route = new RouteDocument {
                SchemaVersion = RouteJson.Integer(obj, "schema_version", Int32.MinValue, Int32.MaxValue),
                RouteId = id, Name = obj.ContainsKey("name") ? RouteJson.String(obj, "name") : id,
                RouteType = RouteJson.String(obj, "route_type"), MapId = RouteJson.Unsigned(obj, "map_id"),
                MapMode = RouteJson.Integer(obj, "map_mode", Int32.MinValue, Int32.MaxValue), Zone = RouteJson.String(obj, "zone"),
                AllowReverse = policy == "allowed", Loop = RouteJson.Boolean(obj, "loop") };
            List<object> points = obj["points"] as List<object>;
            if (points == null || points.Count < 2 || points.Count > 5000) throw new FormatException("Invalid route points array.");
            foreach (object value in points)
            {
                Dictionary<string, object> p = RouteJson.Object(value);
                RouteJson.Keys(p, "x,y,tol,wait_ms,action", null);
                route.Points.Add(new Waypoint { X = RouteJson.Number(p, "x"), Y = RouteJson.Number(p, "y"),
                    Tolerance = RouteJson.Number(p, "tol"), WaitMilliseconds = RouteJson.Integer(p, "wait_ms", Int32.MinValue, Int32.MaxValue),
                    Action = RouteJson.String(p, "action") });
            }
            route.Validate(); return route;
        }
    }
    public sealed class RouteRecorder
    {
        private readonly object sync = new object();
        private RouteDocument recording;
        public bool IsRecording { get { lock (sync) return recording != null; } }
        public int Count { get { lock (sync) return recording == null ? 0 : recording.Points.Count; } }
        public RouteDocument Snapshot { get { lock (sync) return recording == null ? null : recording.Clone(); } }
        public void Start(ProbePacket first, bool fresh, string name, string routeType)
        {
            lock (sync)
            {
                if (recording != null) throw new InvalidOperationException("A route is already being recorded.");
                CheckPacket(first, fresh); RouteChecks.Label(name, "name"); RouteChecks.Type(routeType);
                RouteDocument route = new RouteDocument { RouteId = Guid.NewGuid().ToString("N"), Name = name,
                    RouteType = routeType, MapId = first.MapId, MapMode = first.MapMode, Zone = first.Zone };
                route.Points.Add(new Waypoint { X = first.X, Y = first.Y }); recording = route;
            }
        }
        public bool Add(ProbePacket packet, bool fresh)
        {
            lock (sync)
            {
                if (recording == null) throw new InvalidOperationException("No route is being recorded.");
                CheckPacket(packet, fresh);
                if (packet.MapId != recording.MapId || packet.MapMode != recording.MapMode ||
                    !String.Equals(packet.Zone, recording.Zone, StringComparison.Ordinal))
                    throw new InvalidOperationException("Recording map, mode or zone changed.");
                Waypoint previous = recording.Points[recording.Points.Count - 1];
                double dx = packet.X - previous.X, dy = packet.Y - previous.Y;
                if (dx * dx + dy * dy < 0.00002 * 0.00002) return false;
                if (recording.Points.Count >= 5000) throw new InvalidOperationException("The recording has reached 5000 waypoints.");
                recording.Points.Add(new Waypoint { X = packet.X, Y = packet.Y }); return true;
            }
        }
        public RouteDocument Stop()
        {
            lock (sync)
            {
                if (recording == null) throw new InvalidOperationException("No route is being recorded.");
                recording.Validate(); RouteDocument result = recording.Clone(); recording = null; return result;
            }
        }
        public void Cancel() { lock (sync) recording = null; }
        private static void CheckPacket(ProbePacket packet, bool fresh)
        {
            if (packet == null || !fresh || packet.ReadFailures != 0 || (packet.Capabilities & 65) != 65 ||
                !RouteChecks.Range(packet.X, 0, 1) || !RouteChecks.Range(packet.Y, 0, 1) || packet.MapId == 0 ||
                packet.MapMode < 1 || packet.MapMode > 3 || String.IsNullOrWhiteSpace(packet.Zone) || packet.Zone.Length > 128)
                throw new InvalidOperationException("Recording requires fresh coordinates and a known map/zone without read failures.");
        }
    }
    public sealed class RouteCheckpoint
    {
        public string RouteFingerprint;
        public int NextIndex;
        public bool Reverse, Loop, Completed;
        public uint MapId;
        public int MapMode;
        public string Zone;
    }
    public static class CheckpointStore
    {
        public static void Save(string path, RouteCheckpoint checkpoint)
        {
            if (checkpoint == null) throw new ArgumentNullException("checkpoint");
            // Serialize a snapshot before the atomic write, so validation never touches an existing destination.
            RouteCheckpoint cp = new RouteCheckpoint { RouteFingerprint = checkpoint.RouteFingerprint, NextIndex = checkpoint.NextIndex,
                Reverse = checkpoint.Reverse, Loop = checkpoint.Loop, Completed = checkpoint.Completed,
                MapId = checkpoint.MapId, MapMode = checkpoint.MapMode, Zone = checkpoint.Zone };
            ValidateShape(cp); RouteFiles.Save(path, RouteJson.WriteCheckpoint(cp));
        }
        public static RouteCheckpoint Load(string path, RouteDocument original, bool reverse, bool loop)
        {
            if (original == null) throw new ArgumentNullException("original");
            RouteDocument route = original.Clone(); route.Validate();
            Dictionary<string, object> obj = RouteJson.Object(RouteFiles.Load(path));
            RouteJson.Keys(obj, "schema_version,route_fingerprint,next_index,reverse,loop,completed,map_id,map_mode,zone", null);
            if (RouteJson.Integer(obj, "schema_version", Int32.MinValue, Int32.MaxValue) != 1) throw new FormatException("Unsupported checkpoint schema version.");
            RouteCheckpoint cp = new RouteCheckpoint { RouteFingerprint = RouteJson.String(obj, "route_fingerprint"),
                NextIndex = RouteJson.Integer(obj, "next_index", Int32.MinValue, Int32.MaxValue), Reverse = RouteJson.Boolean(obj, "reverse"),
                Loop = RouteJson.Boolean(obj, "loop"), Completed = RouteJson.Boolean(obj, "completed"), MapId = RouteJson.Unsigned(obj, "map_id"),
                MapMode = RouteJson.Integer(obj, "map_mode", Int32.MinValue, Int32.MaxValue), Zone = RouteJson.String(obj, "zone") };
            ValidateShape(cp);
            if (!String.Equals(cp.RouteFingerprint, route.Fingerprint(), StringComparison.Ordinal) || cp.MapId != route.MapId ||
                cp.MapMode != route.MapMode || !String.Equals(cp.Zone, route.Zone, StringComparison.Ordinal) || cp.Reverse != reverse || cp.Loop != loop)
                throw new FormatException("Checkpoint route identity, direction or loop mode does not match.");
            if (reverse && !route.AllowReverse) throw new FormatException("This route forbids reverse navigation.");
            if (cp.NextIndex > route.Points.Count || cp.Completed != (cp.NextIndex == route.Points.Count))
                throw new FormatException("Checkpoint index and completion state are inconsistent.");
            // Return completion explicitly; loading data never starts a navigation session.
            return cp;
        }
        private static void ValidateShape(RouteCheckpoint cp)
        {
            if (cp.RouteFingerprint == null || cp.RouteFingerprint.Length != 64) throw new FormatException("Invalid route fingerprint.");
            foreach (char c in cp.RouteFingerprint)
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) throw new FormatException("Invalid route fingerprint.");
            if (cp.NextIndex < 0 || cp.NextIndex > 5000) throw new FormatException("Invalid checkpoint index.");
            RouteChecks.Binding(cp.MapId, cp.MapMode, cp.Zone);
        }
    }

    internal static class RouteChecks
    {
        internal static bool Range(double value, double min, double max)
        { return !Double.IsNaN(value) && !Double.IsInfinity(value) && value >= min && value <= max; }
        internal static void Label(string value, string name)
        {
            if (String.IsNullOrWhiteSpace(value) || value.Length > 128) throw new FormatException("Invalid " + name + ": expected 1 through 128 characters.");
            Unicode(value);
        }
        internal static void Unicode(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (Char.IsHighSurrogate(value[i]))
                {
                    if (i + 1 >= value.Length || !Char.IsLowSurrogate(value[++i])) throw new FormatException("Unpaired Unicode surrogate.");
                }
                else if (Char.IsLowSurrogate(value[i])) throw new FormatException("Unpaired Unicode surrogate.");
            }
        }
        internal static void Type(string type)
        { if (type != "farm" && type != "corpse_run" && type != "vendor_return") throw new FormatException("Unsupported route type."); }
        internal static void Binding(uint mapId, int mapMode, string zone)
        {
            if (mapId == 0 || mapMode < 1 || mapMode > 3) throw new FormatException("A route needs a known map id and mode from 1 through 3.");
            Label(zone, "zone");
        }
    }

    internal static class RouteFiles
    {
        internal const int MaxBytes = 2 * 1024 * 1024;
        internal static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        internal static object Load(string path)
        {
            byte[] bytes;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > MaxBytes) throw new FormatException("JSON file exceeds the 2 MB limit.");
                bytes = new byte[(int)stream.Length]; int count = 0;
                while (count < bytes.Length)
                {
                    int read = stream.Read(bytes, count, bytes.Length - count);
                    if (read == 0) throw new FormatException("JSON file was truncated."); count += read;
                }
            }
            try
            {
                int start = bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191 ? 3 : 0;
                return new RouteJson.Parser(Utf8.GetString(bytes, start, bytes.Length - start)).Parse();
            }
            catch (DecoderFallbackException ex) { throw new FormatException("Route/checkpoint file must contain valid UTF8.", ex); }
        }
        internal static void Save(string path, string json)
        {
            byte[] bytes = Utf8.GetBytes(json);
            if (bytes.Length > MaxBytes) throw new FormatException("JSON file exceeds the 2 MB limit.");
            string full = Path.GetFullPath(path), parent = Path.GetDirectoryName(full);
            Directory.CreateDirectory(parent);
            string temporary = Path.Combine(parent, "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                // Both operations stay on the destination volume. Do not degrade to delete/copy on failure.
                if (File.Exists(full)) File.Replace(temporary, full, null); else File.Move(temporary, full);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    // Small strict JSON codec. Framework coercion/deserialization is deliberately avoided at this trust boundary.
    internal static class RouteJson
    {
        private sealed class JsonNumber { internal string Text; internal JsonNumber(string text) { Text = text; } }
        internal static Dictionary<string, object> Object(object value)
        {
            Dictionary<string, object> result = value as Dictionary<string, object>;
            if (result == null) throw new FormatException("Expected a JSON object."); return result;
        }
        internal static void Keys(Dictionary<string, object> obj, string fields, string optional)
        {
            string[] names = fields.Split(','); HashSet<string> allowed = new HashSet<string>(names, StringComparer.Ordinal);
            foreach (string key in obj.Keys) if (!allowed.Contains(key)) throw new FormatException("Unknown JSON field: " + key);
            foreach (string key in names) if (key != optional && !obj.ContainsKey(key)) throw new FormatException("Missing JSON field: " + key);
        }
        internal static string String(Dictionary<string, object> obj, string key)
        { string value = obj[key] as string; if (value == null) throw new FormatException("Expected a string: " + key); return value; }
        internal static bool Boolean(Dictionary<string, object> obj, string key)
        { if (!(obj[key] is bool)) throw new FormatException("Expected a boolean: " + key); return (bool)obj[key]; }
        private static long Whole(Dictionary<string, object> obj, string key, long min, long max)
        {
            JsonNumber number = obj[key] as JsonNumber; long value;
            if (number == null || !Int64.TryParse(number.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value) || value < min || value > max)
                throw new FormatException("Expected a bounded integer: " + key);
            return value;
        }
        internal static int Integer(Dictionary<string, object> obj, string key, int min, int max) { return (int)Whole(obj, key, min, max); }
        internal static uint Unsigned(Dictionary<string, object> obj, string key) { return (uint)Whole(obj, key, 0, UInt32.MaxValue); }
        internal static double Number(Dictionary<string, object> obj, string key)
        {
            JsonNumber number = obj[key] as JsonNumber; double value;
            if (number == null || !Double.TryParse(number.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || Double.IsInfinity(value) || Double.IsNaN(value))
                throw new FormatException("Expected a finite number: " + key); return value;
        }
        private static string Q(string value)
        {
            StringBuilder result = new StringBuilder("\"");
            foreach (char c in value)
            {
                if (c == '\"' || c == '\\') result.Append('\\').Append(c);
                else if (c < 32) result.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else result.Append(c);
            }
            return result.Append('\"').ToString();
        }
        private static string N(double value) { return value == 0 ? "0" : value.ToString("R", CultureInfo.InvariantCulture); }
        private static string B(bool value) { return value ? "true" : "false"; }
        internal static string WriteRoute(RouteDocument r)
        {
            StringBuilder json = new StringBuilder("{\"schema_version\":1,\"route_id\":");
            json.Append(Q(r.RouteId)).Append(",\"name\":").Append(Q(r.Name)).Append(",\"route_type\":").Append(Q(r.RouteType));
            json.Append(",\"map_id\":").Append(r.MapId.ToString(CultureInfo.InvariantCulture)).Append(",\"map_mode\":").Append(r.MapMode.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"zone\":").Append(Q(r.Zone)).Append(",\"reverse_policy\":").Append(Q(r.AllowReverse ? "allowed" : "forbidden"));
            json.Append(",\"loop\":").Append(B(r.Loop)).Append(",\"points\":[");
            for (int i = 0; i < r.Points.Count; i++)
            {
                if (i > 0) json.Append(','); Waypoint p = r.Points[i];
                json.Append("{\"x\":").Append(N(p.X)).Append(",\"y\":").Append(N(p.Y)).Append(",\"tol\":").Append(N(p.Tolerance));
                json.Append(",\"wait_ms\":").Append(p.WaitMilliseconds.ToString(CultureInfo.InvariantCulture)).Append(",\"action\":").Append(Q(p.Action)).Append('}');
            }
            return json.Append("]}").ToString();
        }
        internal static string WriteCheckpoint(RouteCheckpoint c)
        {
            return "{\"schema_version\":1,\"route_fingerprint\":" + Q(c.RouteFingerprint) + ",\"next_index\":" + c.NextIndex.ToString(CultureInfo.InvariantCulture) +
                ",\"reverse\":" + B(c.Reverse) + ",\"loop\":" + B(c.Loop) + ",\"completed\":" + B(c.Completed) +
                ",\"map_id\":" + c.MapId.ToString(CultureInfo.InvariantCulture) + ",\"map_mode\":" + c.MapMode.ToString(CultureInfo.InvariantCulture) + ",\"zone\":" + Q(c.Zone) + "}";
        }

        internal sealed class Parser
        {
            private readonly string text;
            private int index;
            internal Parser(string text) { this.text = text; }
            internal object Parse()
            { object result = Value(0); Space(); if (index != text.Length) throw Bad(); return result; }
            private FormatException Bad() { return new FormatException("Invalid or overly nested JSON near character " + index.ToString(CultureInfo.InvariantCulture) + "."); }
            private void Space() { while (index < text.Length && (text[index] == ' ' || text[index] == '\t' || text[index] == '\r' || text[index] == '\n')) index++; }
            private bool Take(char c) { Space(); if (index < text.Length && text[index] == c) { index++; return true; } return false; }
            private void Need(char c) { if (!Take(c)) throw Bad(); }
            private object Value(int depth)
            {
                if (depth > 16) throw Bad(); Space(); if (index >= text.Length) throw Bad(); char c = text[index];
                if (c == '{')
                {
                    index++; Dictionary<string, object> obj = new Dictionary<string, object>(StringComparer.Ordinal); if (Take('}')) return obj;
                    do
                    {
                        Space(); if (index >= text.Length || text[index] != '\"') throw Bad(); string key = Text(); Need(':');
                        if (obj.ContainsKey(key) || obj.Count >= 32) throw Bad(); obj.Add(key, Value(depth + 1));
                        if (Take('}')) return obj;
                    } while (Take(','));
                    throw Bad();
                }
                if (c == '[')
                {
                    index++; List<object> list = new List<object>(); if (Take(']')) return list;
                    do { if (list.Count >= 5000) throw Bad(); list.Add(Value(depth + 1)); if (Take(']')) return list; } while (Take(','));
                    throw Bad();
                }
                if (c == '\"') return Text();
                if (c == 't') { Literal("true"); return true; }
                if (c == 'f') { Literal("false"); return false; }
                if (c == 'n') { Literal("null"); return null; }
                if (c == '-' || Digit(c)) return Numeric();
                throw Bad();
            }
            private void Literal(string value)
            { if (index + value.Length > text.Length || System.String.CompareOrdinal(text, index, value, 0, value.Length) != 0) throw Bad(); index += value.Length; }
            private static bool Digit(char c) { return c >= '0' && c <= '9'; }
            private object Numeric()
            {
                int start = index; if (text[index] == '-') index++;
                if (index >= text.Length) throw Bad();
                if (text[index] == '0') index++;
                else { if (!Digit(text[index])) throw Bad(); while (index < text.Length && Digit(text[index])) index++; }
                if (index < text.Length && text[index] == '.')
                { index++; int fraction = index; while (index < text.Length && Digit(text[index])) index++; if (fraction == index) throw Bad(); }
                if (index < text.Length && (text[index] == 'e' || text[index] == 'E'))
                {
                    index++; if (index < text.Length && (text[index] == '+' || text[index] == '-')) index++;
                    int exponent = index; while (index < text.Length && Digit(text[index])) index++; if (exponent == index) throw Bad();
                }
                return new JsonNumber(text.Substring(start, index - start));
            }
            private string Text()
            {
                index++; StringBuilder value = new StringBuilder();
                while (index < text.Length)
                {
                    char c = text[index++];
                    if (c == '\"') { string result = value.ToString(); RouteChecks.Unicode(result); return result; }
                    if (c < 32) throw Bad();
                    if (c == '\\')
                    {
                        if (index >= text.Length) throw Bad(); c = text[index++];
                        switch (c)
                        {
                            case '\"': case '\\': case '/': break;
                            case 'b': c = '\b'; break; case 'f': c = '\f'; break; case 'n': c = '\n'; break;
                            case 'r': c = '\r'; break; case 't': c = '\t'; break;
                            case 'u':
                                int code = 0;
                                for (int i = 0; i < 4; i++)
                                {
                                    if (index >= text.Length) throw Bad(); char h = text[index++];
                                    int hex = h >= '0' && h <= '9' ? h - '0' : h >= 'a' && h <= 'f' ? h - 'a' + 10 : h >= 'A' && h <= 'F' ? h - 'A' + 10 : -1;
                                    if (hex < 0) throw Bad(); code = code * 16 + hex;
                                }
                                c = (char)code; break;
                            default: throw Bad();
                        }
                    }
                    value.Append(c);
                }
                throw Bad();
            }
        }
    }
}
