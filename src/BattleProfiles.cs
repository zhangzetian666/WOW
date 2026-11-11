using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace WoWProbe
{
    public sealed class BattleTarget
    {
        public string Name, Kind = "monster", AvatarFile = "", SavedAtUtc = "";
        public uint NameHash;
        public BattleTarget Clone() { return (BattleTarget)MemberwiseClone(); }
        public void Validate()
        {
            RouteChecks.Label(Name, "target name");
            if (NameHash == 0 || NameHash != TargetCatalog.HashName(Name)) throw new FormatException("Target hash must match the complete UTF8 name.");
            if (Kind != "monster" && Kind != "npc") throw new FormatException("Target kind must be monster or npc.");
            if (AvatarFile == null || SavedAtUtc == null) throw new FormatException("Target avatar and timestamp must be strings.");
            RouteChecks.Unicode(AvatarFile); RouteChecks.Unicode(SavedAtUtc);
            if (AvatarFile.Length == 0) return;
            // A single canonical relative directory keeps imported profiles out of arbitrary filesystem paths.
            if (!AvatarFile.StartsWith("avatars/", StringComparison.Ordinal) || AvatarFile.Contains("..") || AvatarFile.IndexOf('\\') >= 0)
                throw new FormatException("Avatar must be an empty string or a relative avatars/*.png path.");
            string file = AvatarFile.Substring(8);
            if (file.Length <= 4 || !file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) throw new FormatException("Avatar must be a PNG filename.");
            foreach (char c in file)
                if (c < 32 || "<>:\"/\\|?*".IndexOf(c) >= 0) throw new FormatException("Invalid avatar filename.");
            string stem = file.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" || stem == "CONIN$" || stem == "CONOUT$" ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                    ((stem[3] >= '1' && stem[3] <= '9') || stem[3] == '\u00b9' || stem[3] == '\u00b2' || stem[3] == '\u00b3')))
                throw new FormatException("Avatar filename cannot name a Windows device.");
        }
    }
    public static class TargetCatalog
    {
        public static uint HashName(string name)
        {
            if (name == null) throw new ArgumentNullException("name"); RouteChecks.Unicode(name);
            uint hash = 5381;
            foreach (byte b in RouteFiles.Utf8.GetBytes(name)) hash = unchecked(hash * 33 + b);
            return hash == 0 ? 1 : hash;
        }
        public static BattleTarget Match(BattleProfile profile, FarmObservation observation)
        {
            FarmObservation o = observation;
            if (profile == null || profile.Targets == null || o == null || !o.Fresh || !o.KnownCore || o.ReadFailures != 0 ||
                !o.TargetExists || !o.TargetDeadKnown || o.TargetDead || !o.TargetIdentityKnown || o.TargetToken == 0 ||
                !o.NameHashKnown || o.TargetNameHash == 0 || !o.AttackabilityKnown || !o.TargetCanAttack || o.TargetIsPlayer)
                return null;
            foreach (BattleTarget target in profile.Targets)
            {
                if (target == null || target.Kind != "monster" || target.NameHash != o.TargetNameHash) continue;
                // Recheck mutable catalog entries. A forged/stale hash must never make a different full name eligible.
                try { target.Validate(); } catch (FormatException) { continue; }
                return target.Clone();
            }
            return null;
        }
    }
    public sealed class BattleProfile
    {
        public int SchemaVersion = 1;
        public string Name = "\u9ed8\u8ba4";
        public List<BattleTarget> Targets = new List<BattleTarget>();
        public string SkillSequence = "112";
        public int SkillIntervalMs = 1500, ScanLimit = 10, ScanIntervalMs = 2000, ApproachTimeoutMs = 12000,
            CombatTimeoutMs = 60000, LootTimeoutMs = 4000, SkinWaitMs = 1500, SkinTimeoutMs = 6000, RestTimeoutMs = 25000,
            HealBelow = 40, ManaBelow = 35, RecoverTo = 85, CriticalHealth = 15, RangeSlot = 1;
        public bool Skinning, AllowApproach, ApproachPositionConfirmed, BindingsConfirmed;
        public NavigationCalibration Calibration = new NavigationCalibration();
        public RouteKeyBindings Movement = new RouteKeyBindings();
        public void Validate()
        {
            if (SchemaVersion != 1) throw new FormatException("Unsupported battle profile schema version.");
            RouteChecks.Label(Name, "profile name");
            if (Targets == null || Targets.Count < 1 || Targets.Count > 200) throw new FormatException("A battle profile needs 1 through 200 targets.");
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal); HashSet<uint> hashes = new HashSet<uint>();
            foreach (BattleTarget target in Targets)
            {
                if (target == null) throw new FormatException("A battle target cannot be null."); target.Validate();
                if (!names.Add(target.Name) || !hashes.Add(target.NameHash)) throw new FormatException("Duplicate target name or hash.");
            }
            if (String.IsNullOrEmpty(SkillSequence) || SkillSequence.Length > 64) throw new FormatException("Skill sequence must have 1 through 64 digits from 1 through 4.");
            foreach (char c in SkillSequence) if (c < '1' || c > '4') throw new FormatException("Skill sequence must contain only 1 through 4.");
            Bound(SkillIntervalMs, 1000, 5000, "skill interval"); Bound(ScanLimit, 1, 20, "scan limit"); Bound(ScanIntervalMs, 500, 10000, "scan interval");
            Bound(ApproachTimeoutMs, 1000, 60000, "approach timeout"); Bound(CombatTimeoutMs, 5000, 180000, "combat timeout");
            Bound(LootTimeoutMs, 1000, 15000, "loot timeout"); Bound(SkinWaitMs, 1500, 10000, "skin wait");
            Bound(SkinTimeoutMs, SkinWaitMs, 30000, "skin timeout"); Bound(RestTimeoutMs, 5000, 120000, "rest timeout");
            Bound(CriticalHealth, 1, 40, "critical health"); Bound(HealBelow, CriticalHealth + 1, 80, "heal threshold");
            Bound(ManaBelow, 1, 80, "mana threshold"); Bound(RecoverTo, Math.Max(HealBelow, ManaBelow) + 1, 100, "recovery threshold");
            Bound(RangeSlot, 1, 4, "range slot");
            if (Calibration == null || Movement == null) throw new FormatException("Calibration and movement settings are required.");
            try { Calibration.Validate(); Movement.Validate(); }
            catch (ArgumentException ex) { throw new FormatException("Invalid battle movement or map calibration.", ex); }
            RouteChecks.Label(Calibration.Zone, "calibration zone");
            // Live confirmations are enforced by the input session. Unconfirmed settings remain usable by the simulator.
        }
        private static void Bound(int value, int min, int max, string name)
        { if (value < min || value > max) throw new FormatException("Invalid " + name + "."); }
        public BattleProfile Clone()
        {
            BattleProfile copy = (BattleProfile)MemberwiseClone();
            if (Targets != null)
            {
                copy.Targets = new List<BattleTarget>(Targets.Count);
                foreach (BattleTarget target in Targets) copy.Targets.Add(target == null ? null : target.Clone());
            }
            copy.Calibration = Calibration == null ? null : Calibration.Clone(); copy.Movement = Movement == null ? null : Movement.Clone();
            return copy;
        }
    }
    public static class BattleProfileStore
    {
        private const int MaxBytes = 1024 * 1024;
        private const string ProfileFields = "schema_version,name,targets,skill_sequence,skill_interval_ms,scan_limit,scan_interval_ms,approach_timeout_ms,combat_timeout_ms,loot_timeout_ms,skin_wait_ms,skin_timeout_ms,rest_timeout_ms,heal_below,mana_below,recover_to,critical_health,range_slot,skinning,allow_approach,approach_position_confirmed,bindings_confirmed,calibration,movement";
        public static void Save(string path, BattleProfile profile)
        {
            if (profile == null) throw new ArgumentNullException("profile"); BattleProfile copy = profile.Clone(); copy.Validate();
            string json = Write(copy);
            if (RouteFiles.Utf8.GetByteCount(json) > MaxBytes) throw new FormatException("Battle profile exceeds the 1 MB limit.");
            RouteFiles.Save(path, json);
        }
        public static BattleProfile Load(string path)
        {
            byte[] bytes;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > MaxBytes) throw new FormatException("Battle profile exceeds the 1 MB limit.");
                bytes = new byte[(int)stream.Length]; int count = 0;
                while (count < bytes.Length)
                { int read = stream.Read(bytes, count, bytes.Length - count); if (read == 0) throw new FormatException("Battle profile file was truncated."); count += read; }
            }
            object data;
            try
            {
                int start = bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191 ? 3 : 0;
                data = new RouteJson.Parser(RouteFiles.Utf8.GetString(bytes, start, bytes.Length - start)).Parse();
            }
            catch (DecoderFallbackException ex) { throw new FormatException("Battle profile must contain valid UTF8.", ex); }
            Dictionary<string, object> root = RouteJson.Object(data); RouteJson.Keys(root, ProfileFields, null);
            BattleProfile p = new BattleProfile {
                SchemaVersion = Int(root, "schema_version"), Name = RouteJson.String(root, "name"), SkillSequence = RouteJson.String(root, "skill_sequence"),
                SkillIntervalMs = Int(root, "skill_interval_ms"), ScanLimit = Int(root, "scan_limit"), ScanIntervalMs = Int(root, "scan_interval_ms"),
                ApproachTimeoutMs = Int(root, "approach_timeout_ms"), CombatTimeoutMs = Int(root, "combat_timeout_ms"), LootTimeoutMs = Int(root, "loot_timeout_ms"),
                SkinWaitMs = Int(root, "skin_wait_ms"), SkinTimeoutMs = Int(root, "skin_timeout_ms"), RestTimeoutMs = Int(root, "rest_timeout_ms"),
                HealBelow = Int(root, "heal_below"), ManaBelow = Int(root, "mana_below"), RecoverTo = Int(root, "recover_to"), CriticalHealth = Int(root, "critical_health"),
                RangeSlot = Int(root, "range_slot"), Skinning = RouteJson.Boolean(root, "skinning"), AllowApproach = RouteJson.Boolean(root, "allow_approach"),
                ApproachPositionConfirmed = RouteJson.Boolean(root, "approach_position_confirmed"), BindingsConfirmed = RouteJson.Boolean(root, "bindings_confirmed") };
            List<object> targets = root["targets"] as List<object>;
            if (targets == null || targets.Count < 1 || targets.Count > 200) throw new FormatException("Expected 1 through 200 battle targets.");
            foreach (object value in targets)
            {
                Dictionary<string, object> t = RouteJson.Object(value); RouteJson.Keys(t, "name,name_hash,kind,avatar_file,saved_at_utc", null);
                p.Targets.Add(new BattleTarget { Name = RouteJson.String(t, "name"), NameHash = RouteJson.Unsigned(t, "name_hash"), Kind = RouteJson.String(t, "kind"),
                    AvatarFile = RouteJson.String(t, "avatar_file"), SavedAtUtc = RouteJson.String(t, "saved_at_utc") });
            }
            Dictionary<string, object> c = RouteJson.Object(root["calibration"]);
            RouteJson.Keys(c, "axis_scale,facing_sign,facing_offset,turn_radians_per_second,forward_units_per_second,confirmed,map_id,map_mode,zone", null);
            p.Calibration = new NavigationCalibration { AxisScale = RouteJson.Number(c, "axis_scale"), FacingSign = RouteJson.Number(c, "facing_sign"),
                FacingOffset = RouteJson.Number(c, "facing_offset"), TurnRadiansPerSecond = RouteJson.Number(c, "turn_radians_per_second"),
                ForwardUnitsPerSecond = RouteJson.Number(c, "forward_units_per_second"), Confirmed = RouteJson.Boolean(c, "confirmed"),
                MapId = RouteJson.Unsigned(c, "map_id"), MapMode = Int(c, "map_mode"), Zone = RouteJson.String(c, "zone") };
            Dictionary<string, object> m = RouteJson.Object(root["movement"]);
            RouteJson.Keys(m, "forward,backward,turn_left,turn_right,strafe_left,strafe_right,jump", null);
            p.Movement = new RouteKeyBindings { Forward = Key(m, "forward"), Backward = Key(m, "backward"), TurnLeft = Key(m, "turn_left"),
                TurnRight = Key(m, "turn_right"), StrafeLeft = Key(m, "strafe_left"), StrafeRight = Key(m, "strafe_right"), Jump = Key(m, "jump") };
            p.Validate(); return p;
        }
        private static int Int(Dictionary<string, object> obj, string key) { return RouteJson.Integer(obj, key, Int32.MinValue, Int32.MaxValue); }
        private static ushort Key(Dictionary<string, object> obj, string key) { return (ushort)RouteJson.Integer(obj, key, 0, UInt16.MaxValue); }
        private static string Write(BattleProfile p)
        {
            JsonWriter w = new JsonWriter(); w.Int("schema_version", p.SchemaVersion); w.String("name", p.Name);
            StringBuilder targets = new StringBuilder("[");
            for (int i = 0; i < p.Targets.Count; i++)
            {
                if (i > 0) targets.Append(','); BattleTarget t = p.Targets[i]; JsonWriter tw = new JsonWriter();
                tw.String("name", t.Name); tw.Int("name_hash", t.NameHash); tw.String("kind", t.Kind); tw.String("avatar_file", t.AvatarFile); tw.String("saved_at_utc", t.SavedAtUtc);
                targets.Append(tw.End());
            }
            w.Raw("targets", targets.Append(']').ToString()); w.String("skill_sequence", p.SkillSequence);
            w.Int("skill_interval_ms", p.SkillIntervalMs); w.Int("scan_limit", p.ScanLimit); w.Int("scan_interval_ms", p.ScanIntervalMs);
            w.Int("approach_timeout_ms", p.ApproachTimeoutMs); w.Int("combat_timeout_ms", p.CombatTimeoutMs); w.Int("loot_timeout_ms", p.LootTimeoutMs);
            w.Int("skin_wait_ms", p.SkinWaitMs); w.Int("skin_timeout_ms", p.SkinTimeoutMs); w.Int("rest_timeout_ms", p.RestTimeoutMs);
            w.Int("heal_below", p.HealBelow); w.Int("mana_below", p.ManaBelow); w.Int("recover_to", p.RecoverTo); w.Int("critical_health", p.CriticalHealth); w.Int("range_slot", p.RangeSlot);
            w.Bool("skinning", p.Skinning); w.Bool("allow_approach", p.AllowApproach); w.Bool("approach_position_confirmed", p.ApproachPositionConfirmed); w.Bool("bindings_confirmed", p.BindingsConfirmed);
            NavigationCalibration c = p.Calibration; JsonWriter cw = new JsonWriter();
            cw.Number("axis_scale", c.AxisScale); cw.Number("facing_sign", c.FacingSign); cw.Number("facing_offset", c.FacingOffset);
            cw.Number("turn_radians_per_second", c.TurnRadiansPerSecond); cw.Number("forward_units_per_second", c.ForwardUnitsPerSecond);
            cw.Bool("confirmed", c.Confirmed); cw.Int("map_id", c.MapId); cw.Int("map_mode", c.MapMode); cw.String("zone", c.Zone); w.Raw("calibration", cw.End());
            RouteKeyBindings m = p.Movement; JsonWriter mw = new JsonWriter(); mw.Int("forward", m.Forward); mw.Int("backward", m.Backward);
            mw.Int("turn_left", m.TurnLeft); mw.Int("turn_right", m.TurnRight); mw.Int("strafe_left", m.StrafeLeft); mw.Int("strafe_right", m.StrafeRight); mw.Int("jump", m.Jump);
            w.Raw("movement", mw.End()); return w.End();
        }
        private sealed class JsonWriter
        {
            private readonly StringBuilder text = new StringBuilder("{"); private bool hasFields;
            internal void Raw(string name, string value)
            { if (hasFields) text.Append(','); hasFields = true; Quote(name); text.Append(':').Append(value); }
            internal void String(string name, string value)
            { if (hasFields) text.Append(','); hasFields = true; Quote(name); text.Append(':'); Quote(value); }
            internal void Int(string name, long value) { Raw(name, value.ToString(CultureInfo.InvariantCulture)); }
            internal void Number(string name, double value) { Raw(name, value == 0 ? "0" : value.ToString("R", CultureInfo.InvariantCulture)); }
            internal void Bool(string name, bool value) { Raw(name, value ? "true" : "false"); }
            internal string End() { return text.ToString() + "}"; }
            private void Quote(string value)
            {
                text.Append('\"');
                foreach (char c in value)
                {
                    if (c == '\"' || c == '\\') text.Append('\\').Append(c);
                    else if (c < 32) text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else text.Append(c);
                }
                text.Append('\"');
            }
        }
    }
}
