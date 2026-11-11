using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using WoWProbe;

// Pure profile/catalog tests. No native input or real game APIs are referenced.
internal static class BattleProfileTests
{
    private static int passed, failed;
    private static string directory;
    private const string Json = "{\"schema_version\":1,\"name\":\"Simulation\",\"targets\":[{\"name\":\"Wolf\",\"name_hash\":2089724669,\"kind\":\"monster\",\"avatar_file\":\"avatars/wolf.png\",\"saved_at_utc\":\"\"}],\"skill_sequence\":\"112\",\"skill_interval_ms\":1500,\"scan_limit\":10,\"scan_interval_ms\":2000,\"approach_timeout_ms\":12000,\"combat_timeout_ms\":60000,\"loot_timeout_ms\":4000,\"skin_wait_ms\":1500,\"skin_timeout_ms\":6000,\"rest_timeout_ms\":25000,\"heal_below\":40,\"mana_below\":35,\"recover_to\":85,\"critical_health\":15,\"range_slot\":1,\"skinning\":true,\"allow_approach\":true,\"approach_position_confirmed\":false,\"bindings_confirmed\":false,\"calibration\":{\"axis_scale\":1,\"facing_sign\":-1,\"facing_offset\":0,\"turn_radians_per_second\":2,\"forward_units_per_second\":0.01,\"confirmed\":false,\"map_id\":900001,\"map_mode\":2,\"zone\":\"Simulation only\"},\"movement\":{\"forward\":87,\"backward\":83,\"turn_left\":81,\"turn_right\":69,\"strafe_left\":65,\"strafe_right\":68,\"jump\":32}}";
    private static int Main()
    {
        directory = Path.Combine(Path.GetTempPath(), "WoWProbe-profile-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            Run("name hashing uses full UTF8 djb2 with unsigned overflow", Hashes);
            Run("targets require exact name hash and supported kind", TargetValidation);
            Run("avatar paths are confined to relative avatars PNG files", AvatarPaths);
            Run("avatar paths reject Windows device aliases", AvatarDevices);
            Run("target cloning isolates saved metadata", TargetClone);
            Run("catalog selects exact full-name hashes despite truncated display names", MatchFullName);
            Run("catalog rejects unknown identity, attackability and alive state", MatchUnknown);
            Run("catalog excludes players, dead targets and NPC entries", MatchExcluded);
            Run("profile validates simulation without live confirmation flags", SimulationFlags);
            Run("profile rejects empty or excessive target lists and duplicate identities", CatalogValidation);
            Run("profile validates names, schema and skill sequence", ProfileIdentity);
            Run("profile enforces timing boundaries and skin wait ordering", TimeBounds);
            Run("profile enforces ordered health and recovery thresholds", Thresholds);
            Run("profile delegates calibration and movement validation", NestedValidation);
            Run("profile cloning isolates all nested mutable settings", DeepClone);
            Run("JSON roundtrip preserves every profile setting", Roundtrip);
            Run("Unicode paths and decimal-comma cultures preserve numbers and names", UnicodeCulture);
            Run("JSON never fills missing fields from defaults", MissingFields);
            Run("JSON rejects scalar coercions and incorrect containers", JsonTypes);
            Run("JSON rejects unsupported schema and inconsistent target hashes", JsonValues);
            Run("JSON rejects duplicate keys, malformed syntax and unknown fields", StrictJson);
            Run("JSON rejects nonfinite values, invalid Unicode and excessive depth", JsonBounds);
            Run("profile file input is bounded to one megabyte", FileBound);
            Run("invalid and oversized saves preserve the last good file", AtomicValidation);
            Run("failed filesystem replacement preserves old data and cleans temp files", AtomicFailure);
            Run("successful saves atomically replace existing files", AtomicReplacement);
            Run("shipped example is an unconfirmed synthetic-map battle profile", ShippedExample);
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine("Battle profile tests: {0} passed, {1} failed. No game or OS input was used.", passed, failed); return failed == 0 ? 0 : 1;
    }
    private static void Run(string name, Action action)
    { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.GetType().Name + ": " + ex.Message); } }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual, string message)
    { Check(EqualityComparer<T>.Default.Equals(expected, actual), message + ": expected " + expected + ", got " + actual); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static string FileName(string name) { return Path.Combine(directory, name); }
    private static BattleTarget Target(string name)
    { return new BattleTarget { Name = name, NameHash = TargetCatalog.HashName(name), AvatarFile = "avatars/wolf.png" }; }
    private static BattleProfile Example()
    {
        return new BattleProfile { Name = "Simulation", Skinning = true, AllowApproach = true, Targets = new List<BattleTarget> { Target("Wolf") },
            Calibration = new NavigationCalibration { MapId = 900001, MapMode = 2, Zone = "Simulation only" }, Movement = new RouteKeyBindings() };
    }
    private static FarmObservation Observation()
    {
        return new FarmObservation { Fresh = true, KnownCore = true, TargetExists = true, TargetDeadKnown = true,
            TargetIdentityKnown = true, TargetToken = 123, NameHashKnown = true, TargetNameHash = 2089724669,
            AttackabilityKnown = true, TargetCanAttack = true, TargetName = "Wolf", TargetHealth = 100 };
    }
    private static void BadProfile(Action<BattleProfile> change)
    { BattleProfile p = Example(); change(p); Throws<FormatException>(delegate { p.Validate(); }); }
    private static BattleProfile Read(string json)
    { string path = FileName("input.json"); File.WriteAllText(path, json, new UTF8Encoding(false, true)); return BattleProfileStore.Load(path); }
    private static void BadJson(string json) { Throws<FormatException>(delegate { Read(json); }); }
    private static void Hashes()
    {
        Equal((uint)5381, TargetCatalog.HashName(""), "empty djb2 seed"); Equal((uint)177638, TargetCatalog.HashName("A"), "single ASCII");
        Equal((uint)2089724669, TargetCatalog.HashName("Wolf"), "ASCII hash"); Equal((uint)656330021, TargetCatalog.HashName("\u6f14\u793a\u91ce\u517d"), "full UTF8 hash");
        Check(TargetCatalog.HashName("Wolf") != TargetCatalog.HashName("wolf"), "name case is significant");
        Throws<ArgumentNullException>(delegate { TargetCatalog.HashName(null); }); Throws<FormatException>(delegate { TargetCatalog.HashName("\ud800"); });
    }
    private static void TargetValidation()
    {
        Target("Wolf").Validate(); BattleTarget npc = Target("Vendor"); npc.Kind = "npc"; npc.Validate();
        foreach (string name in new string[] { null, "", " ", new string('x', 129) })
        { BattleTarget target = Target("Wolf"); target.Name = name; Throws<FormatException>(delegate { target.Validate(); }); }
        BattleTarget bad = Target("Wolf"); bad.NameHash++; Throws<FormatException>(delegate { bad.Validate(); });
        bad = Target("Wolf"); bad.NameHash = 0; Throws<FormatException>(delegate { bad.Validate(); });
        bad = Target("Wolf"); bad.Kind = "player"; Throws<FormatException>(delegate { bad.Validate(); });
    }
    private static void AvatarPaths()
    {
        foreach (string value in new string[] { "", "avatars/wolf.png", "avatars/\u91ce\u517d.png", "avatars/wolf.PNG" })
        { BattleTarget t = Target("Wolf"); t.AvatarFile = value; t.Validate(); }
        foreach (string value in new string[] { null, "../wolf.png", "avatars/../wolf.png", "avatars/wolf..png", "C:/avatars/wolf.png", "/avatars/wolf.png", "\\\\server\\wolf.png", "avatars/wolf.jpg", "avatars/folder/wolf.png", "avatars/.png", "avatars/a:bad.png", "avatars/bad?.png" })
        { BattleTarget t = Target("Wolf"); t.AvatarFile = value; Throws<FormatException>(delegate { t.Validate(); }); }
    }
    private static void TargetClone()
    {
        BattleTarget target = Target("Wolf"), copy = target.Clone(); copy.Name = "Other"; copy.AvatarFile = ""; copy.SavedAtUtc = "changed";
        Equal("Wolf", target.Name, "name isolation"); Equal("avatars/wolf.png", target.AvatarFile, "avatar isolation"); Equal("", target.SavedAtUtc, "timestamp isolation");
    }
    private static void AvatarDevices()
    {
        foreach (string path in new string[] { "avatars/CON.png", "avatars/nul.png", "avatars/AUX.png", "avatars/prn.png", "avatars/COM1.png", "avatars/LPT9.png", "avatars/con.other.png", "avatars/con .png" })
        { BattleTarget t = Target("Wolf"); t.AvatarFile = path; Throws<FormatException>(delegate { t.Validate(); }); }
    }
    private static void MatchFullName()
    {
        BattleProfile p = Example(); FarmObservation o = Observation(); Equal("Wolf", TargetCatalog.Match(p, o).Name, "exact match");
        string prefix = new string('W', 32); p.Targets[0] = Target(prefix + " alpha"); o.TargetName = prefix; o.TargetNameHash = p.Targets[0].NameHash;
        Equal(prefix + " alpha", TargetCatalog.Match(p, o).Name, "truncated display does not prevent full-hash match");
        o.TargetNameHash = TargetCatalog.HashName(prefix + " beta"); Equal(null, TargetCatalog.Match(p, o), "same display prefix cannot select another name");
    }
    private static void MatchUnknown()
    {
        Action<FarmObservation>[] changes = { delegate(FarmObservation o) { o.Fresh = false; }, delegate(FarmObservation o) { o.KnownCore = false; },
            delegate(FarmObservation o) { o.ReadFailures = 1; }, delegate(FarmObservation o) { o.TargetExists = false; },
            delegate(FarmObservation o) { o.TargetIdentityKnown = false; }, delegate(FarmObservation o) { o.TargetToken = 0; },
            delegate(FarmObservation o) { o.NameHashKnown = false; }, delegate(FarmObservation o) { o.TargetNameHash = 0; },
            delegate(FarmObservation o) { o.AttackabilityKnown = false; }, delegate(FarmObservation o) { o.TargetDeadKnown = false; } };
        foreach (Action<FarmObservation> change in changes) { FarmObservation o = Observation(); change(o); Equal(null, TargetCatalog.Match(Example(), o), "unknown target state must not match"); }
        Equal(null, TargetCatalog.Match(null, Observation()), "missing profile"); Equal(null, TargetCatalog.Match(Example(), null), "missing observation");
    }
    private static void MatchExcluded()
    {
        FarmObservation o = Observation(); o.TargetIsPlayer = true; Equal(null, TargetCatalog.Match(Example(), o), "players excluded");
        o = Observation(); o.TargetCanAttack = false; Equal(null, TargetCatalog.Match(Example(), o), "friendly excluded");
        o = Observation(); o.TargetDead = true; Equal(null, TargetCatalog.Match(Example(), o), "dead excluded");
        BattleProfile p = Example(); p.Targets[0].Kind = "npc"; Equal(null, TargetCatalog.Match(p, Observation()), "NPC excluded");
        p = Example(); p.Targets[0].Name = "different full name"; Equal(null, TargetCatalog.Match(p, Observation()), "forged saved hash excluded");
    }
    private static void SimulationFlags()
    {
        BattleProfile p = Example(); p.Validate(); Check(!p.BindingsConfirmed && !p.Calibration.Confirmed && !p.ApproachPositionConfirmed && p.AllowApproach, "simulation permits unconfirmed approach");
        p.BindingsConfirmed = p.Calibration.Confirmed = p.ApproachPositionConfirmed = true; p.Validate();
    }
    private static void CatalogValidation()
    {
        BadProfile(delegate(BattleProfile p) { p.Targets = null; }); BadProfile(delegate(BattleProfile p) { p.Targets.Clear(); });
        BadProfile(delegate(BattleProfile p) { p.Targets.Add(null); }); BadProfile(delegate(BattleProfile p) { p.Targets.Add(p.Targets[0].Clone()); });
        BadProfile(delegate(BattleProfile p) { BattleTarget t = p.Targets[0].Clone(); t.Kind = "npc"; p.Targets.Add(t); });
        BattleProfile profile = Example(); for (int i = 1; i < 200; i++) profile.Targets.Add(Target("Wolf " + i.ToString(CultureInfo.InvariantCulture))); profile.Validate();
        profile.Targets.Add(Target("Last wolf")); Throws<FormatException>(delegate { profile.Validate(); });
    }
    private static void ProfileIdentity()
    {
        BadProfile(delegate(BattleProfile p) { p.SchemaVersion = 2; });
        foreach (string name in new string[] { null, "", " ", new string('x', 129) }) BadProfile(delegate(BattleProfile p) { p.Name = name; });
        foreach (string sequence in new string[] { null, "", "0", "5", "1 2", new string('1', 65) }) BadProfile(delegate(BattleProfile p) { p.SkillSequence = sequence; });
        BattleProfile valid = Example(); valid.SkillSequence = new string('4', 64); valid.Validate();
        BadProfile(delegate(BattleProfile p) { p.RangeSlot = 0; }); BadProfile(delegate(BattleProfile p) { p.RangeSlot = 5; });
    }
    private static void TimeBounds()
    {
        Action<BattleProfile, int>[] set = { delegate(BattleProfile p, int v) { p.SkillIntervalMs = v; }, delegate(BattleProfile p, int v) { p.ScanLimit = v; },
            delegate(BattleProfile p, int v) { p.ScanIntervalMs = v; }, delegate(BattleProfile p, int v) { p.ApproachTimeoutMs = v; },
            delegate(BattleProfile p, int v) { p.CombatTimeoutMs = v; }, delegate(BattleProfile p, int v) { p.LootTimeoutMs = v; },
            delegate(BattleProfile p, int v) { p.SkinWaitMs = v; }, delegate(BattleProfile p, int v) { p.SkinTimeoutMs = v; }, delegate(BattleProfile p, int v) { p.RestTimeoutMs = v; } };
        int[,] bounds = { { 1000, 5000 }, { 1, 20 }, { 500, 10000 }, { 1000, 60000 }, { 5000, 180000 }, { 1000, 15000 }, { 1500, 10000 }, { 1500, 30000 }, { 5000, 120000 } };
        for (int i = 0; i < set.Length; i++)
        {
            int k = i; BadProfile(delegate(BattleProfile p) { set[k](p, bounds[k, 0] - 1); }); BadProfile(delegate(BattleProfile p) { set[k](p, bounds[k, 1] + 1); });
            BattleProfile low = Example(), high = Example(); low.SkinTimeoutMs = high.SkinTimeoutMs = 30000; set[k](low, bounds[k, 0]); set[k](high, bounds[k, 1]); low.Validate(); high.Validate();
        }
        BadProfile(delegate(BattleProfile p) { p.SkinWaitMs = 6000; p.SkinTimeoutMs = 5999; });
    }
    private static void Thresholds()
    {
        BadProfile(delegate(BattleProfile p) { p.CriticalHealth = 0; }); BadProfile(delegate(BattleProfile p) { p.CriticalHealth = 41; });
        BadProfile(delegate(BattleProfile p) { p.HealBelow = p.CriticalHealth; }); BadProfile(delegate(BattleProfile p) { p.HealBelow = 81; });
        BadProfile(delegate(BattleProfile p) { p.ManaBelow = 0; }); BadProfile(delegate(BattleProfile p) { p.ManaBelow = 81; });
        BadProfile(delegate(BattleProfile p) { p.RecoverTo = p.HealBelow; }); BadProfile(delegate(BattleProfile p) { p.ManaBelow = 70; p.RecoverTo = 70; });
        BadProfile(delegate(BattleProfile p) { p.RecoverTo = 101; });
        BattleProfile low = Example(); low.CriticalHealth = 1; low.HealBelow = 2; low.ManaBelow = 1; low.RecoverTo = 3; low.Validate();
        BattleProfile high = Example(); high.CriticalHealth = 40; high.HealBelow = high.ManaBelow = 80; high.RecoverTo = 100; high.Validate();
    }
    private static void NestedValidation()
    {
        BadProfile(delegate(BattleProfile p) { p.Calibration = null; }); BadProfile(delegate(BattleProfile p) { p.Movement = null; });
        BadProfile(delegate(BattleProfile p) { p.Calibration.AxisScale = Double.NaN; }); BadProfile(delegate(BattleProfile p) { p.Calibration.MapId = 0; });
        BadProfile(delegate(BattleProfile p) { p.Movement.TurnLeft = p.Movement.TurnRight; }); BadProfile(delegate(BattleProfile p) { p.Movement.Forward = 0x31; });
    }
    private static void DeepClone()
    {
        BattleProfile p = Example(), clone = p.Clone(); clone.Targets[0].Name = "Other"; clone.Targets.Add(Target("Other"));
        clone.Calibration.Zone = "Elsewhere"; clone.Movement.TurnLeft = 0; clone.Name = "Other profile";
        Equal("Wolf", p.Targets[0].Name, "target clone"); Equal(1, p.Targets.Count, "list clone"); Equal("Simulation only", p.Calibration.Zone, "calibration clone");
        Equal((ushort)81, p.Movement.TurnLeft, "movement clone"); Equal("Simulation", p.Name, "name clone");
    }
    private static void Roundtrip()
    {
        BattleProfile p = Example(); p.SkillSequence = "43211234"; p.SkillIntervalMs = 2400; p.ScanLimit = 7; p.ScanIntervalMs = 2300;
        p.ApproachTimeoutMs = 13000; p.CombatTimeoutMs = 65000; p.LootTimeoutMs = 5000; p.SkinWaitMs = 1700; p.SkinTimeoutMs = 7000; p.RestTimeoutMs = 30000;
        p.HealBelow = 45; p.ManaBelow = 50; p.RecoverTo = 90; p.CriticalHealth = 20; p.RangeSlot = 4; p.BindingsConfirmed = p.ApproachPositionConfirmed = p.Calibration.Confirmed = true;
        p.Calibration.AxisScale = 1.7; p.Calibration.FacingSign = 1; p.Calibration.FacingOffset = -0.3; p.Calibration.TurnRadiansPerSecond = 3.4; p.Calibration.ForwardUnitsPerSecond = 0.034;
        p.Calibration.MapId = UInt32.MaxValue; p.Calibration.MapMode = 3; p.Targets[0].SavedAtUtc = "2026-11-11T00:00:00Z";
        ushort left = p.Movement.TurnLeft; p.Movement.TurnLeft = p.Movement.StrafeLeft; p.Movement.StrafeLeft = left;
        string path = FileName("roundtrip.json"); BattleProfileStore.Save(path, p); BattleProfile loaded = BattleProfileStore.Load(path);
        foreach (System.Reflection.FieldInfo field in typeof(BattleProfile).GetFields())
            if (field.FieldType.IsPrimitive || field.FieldType == typeof(string)) Equal(field.GetValue(p), field.GetValue(loaded), "profile field " + field.Name);
        foreach (System.Reflection.FieldInfo field in typeof(NavigationCalibration).GetFields()) Equal(field.GetValue(p.Calibration), field.GetValue(loaded.Calibration), "calibration " + field.Name);
        foreach (System.Reflection.FieldInfo field in typeof(RouteKeyBindings).GetFields()) Equal(field.GetValue(p.Movement), field.GetValue(loaded.Movement), "movement " + field.Name);
        foreach (System.Reflection.FieldInfo field in typeof(BattleTarget).GetFields()) Equal(field.GetValue(p.Targets[0]), field.GetValue(loaded.Targets[0]), "target " + field.Name);
        Equal(1, loaded.Targets.Count, "target count"); Check(File.ReadAllText(path).Contains("\"skill_interval_ms\""), "snake_case field names");
    }
    private static void UnicodeCulture()
    {
        CultureInfo culture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR"); BattleProfile p = Example(); p.Name = "\u6a21\u62df\u6218\u6597 \ud83c\udf0d";
            p.Targets[0] = Target("\u6f14\u793a\u91ce\u517d"); p.Calibration.AxisScale = 1.1234567890123457;
            string path = FileName("\u6218\u6597\u914d\u7f6e.json"); BattleProfileStore.Save(path, p); BattleProfile q = BattleProfileStore.Load(path);
            Equal(p.Name, q.Name, "Unicode profile"); Equal((uint)656330021, q.Targets[0].NameHash, "UTF8 hash"); Equal(p.Calibration.AxisScale, q.Calibration.AxisScale, "invariant double roundtrip");
        }
        finally { Thread.CurrentThread.CurrentCulture = culture; }
    }
    private static void MissingFields()
    {
        string[] fields = { "\"schema_version\":1,", "\"name\":\"Simulation\",", "\"skill_sequence\":\"112\",", "\"skill_interval_ms\":1500,", "\"scan_limit\":10,", "\"scan_interval_ms\":2000,", "\"approach_timeout_ms\":12000,", "\"combat_timeout_ms\":60000,", "\"loot_timeout_ms\":4000,", "\"skin_wait_ms\":1500,", "\"skin_timeout_ms\":6000,", "\"rest_timeout_ms\":25000,", "\"heal_below\":40,", "\"mana_below\":35,", "\"recover_to\":85,", "\"critical_health\":15,", "\"range_slot\":1,", "\"skinning\":true,", "\"allow_approach\":true,", "\"approach_position_confirmed\":false,", "\"bindings_confirmed\":false,", "\"name_hash\":2089724669,", "\"kind\":\"monster\",", "\"avatar_file\":\"avatars/wolf.png\",", "\"axis_scale\":1,", "\"map_id\":900001,", "\"forward\":87," };
        foreach (string field in fields) BadJson(Json.Replace(field, ""));
    }
    private static void JsonTypes()
    {
        string[,] cases = { { "\"schema_version\":1", "\"schema_version\":\"1\"" }, { "\"name_hash\":2089724669", "\"name_hash\":true" },
            { "\"skinning\":true", "\"skinning\":1" }, { "\"scan_limit\":10", "\"scan_limit\":10.5" },
            { "\"axis_scale\":1", "\"axis_scale\":\"1\"" }, { "\"forward\":87", "\"forward\":null" },
            { "\"name\":\"Wolf\"", "\"name\":null" }, { "\"calibration\":{", "\"calibration\":null,\"bad\":{" } };
        for (int i = 0; i < cases.GetLength(0); i++) BadJson(Json.Replace(cases[i, 0], cases[i, 1]));
        BadJson(Json.Replace("\"targets\":[", "\"targets\":{\"bad\":[")); BadJson("null"); BadJson("[]");
    }
    private static void JsonValues()
    {
        string[,] cases = { { "\"schema_version\":1", "\"schema_version\":2" }, { "\"name_hash\":2089724669", "\"name_hash\":2089724670" },
            { "\"avatar_file\":\"avatars/wolf.png\"", "\"avatar_file\":\"../wolf.png\"" }, { "\"kind\":\"monster\"", "\"kind\":\"player\"" },
            { "\"heal_below\":40", "\"heal_below\":15" }, { "\"jump\":32", "\"jump\":65536" }, { "\"range_slot\":1", "\"range_slot\":0" } };
        for (int i = 0; i < cases.GetLength(0); i++) BadJson(Json.Replace(cases[i, 0], cases[i, 1]));
    }
    private static void StrictJson()
    {
        BadJson(Json.Replace("\"scan_limit\":10", "\"scan_limit\":10,\"scan_limit\":20")); BadJson(Json.Replace("\"scan_limit\":10", "scan_limit:10"));
        BadJson(Json.Replace("\"scan_limit\":10", "\"scan_limit\":01")); BadJson(Json + "false"); BadJson("/* comment */" + Json);
        BadJson(Json.Replace("\"schema_version\":1", "\"schema_version\":1,\"unknown\":false"));
    }
    private static void JsonBounds()
    {
        foreach (string number in new string[] { "NaN", "Infinity", "1e9999" }) BadJson(Json.Replace("\"axis_scale\":1", "\"axis_scale\":" + number));
        BadJson(Json.Replace("Wolf", "\\uD800")); BadJson(new string('[', 100) + "0" + new string(']', 100));
        string path = FileName("bad-utf8.json"); File.WriteAllBytes(path, new byte[] { 0xff, 0xfe, 0x7b, 0x7d }); Throws<FormatException>(delegate { BattleProfileStore.Load(path); });
    }
    private static void FileBound()
    { BadJson(new string(' ', 1024 * 1024 + 1) + Json); }
    private static void AtomicValidation()
    {
        string path = FileName("atomic.json"); BattleProfile p = Example(); BattleProfileStore.Save(path, p); string before = File.ReadAllText(path);
        p.SkillSequence = "5"; Throws<FormatException>(delegate { BattleProfileStore.Save(path, p); }); Equal(before, File.ReadAllText(path), "invalid profile preserves old file");
        p = Example(); p.Targets[0].SavedAtUtc = new string('x', 1024 * 1024); Throws<FormatException>(delegate { BattleProfileStore.Save(path, p); }); Equal(before, File.ReadAllText(path), "oversized profile preserves old file");
        Equal(0, Directory.GetFiles(directory, "*.tmp").Length, "validation temp cleanup");
    }
    private static void AtomicFailure()
    {
        string path = FileName("locked.json"); BattleProfileStore.Save(path, Example()); string before = File.ReadAllText(path);
        using (FileStream held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        { Throws<IOException>(delegate { BattleProfileStore.Save(path, Example()); }); }
        Equal(before, File.ReadAllText(path), "failed replacement leaves original data intact"); Equal(0, Directory.GetFiles(directory, "*.tmp").Length, "I/O failure temp cleanup");
    }
    private static void AtomicReplacement()
    {
        string path = FileName("replace.json"); BattleProfile p = Example(); BattleProfileStore.Save(path, p); p.Name = "Updated"; BattleProfileStore.Save(path, p);
        Equal("Updated", BattleProfileStore.Load(path).Name, "replacement stored"); Equal(0, Directory.GetFiles(directory, "*.tmp").Length, "successful temp cleanup");
    }
    private static void ShippedExample()
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "profiles", "examples", "simulation-battle.json");
        BattleProfile p = BattleProfileStore.Load(path); Equal((uint)900001, p.Calibration.MapId, "synthetic map"); Equal(2, p.Calibration.MapMode, "synthetic map mode");
        Equal("Simulation only", p.Calibration.Zone, "synthetic zone"); Equal(1, p.Targets.Count, "one demonstration monster"); Equal("\u6f14\u793a\u91ce\u517d", p.Targets[0].Name, "full example name");
        Check(p.AllowApproach && !p.ApproachPositionConfirmed && !p.BindingsConfirmed && !p.Calibration.Confirmed, "simulation confirmations remain false");
    }
}
