using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using WoWProbe;

// This runner never constructs a native backend. Every input event stays in FakeInput.
internal static class CoreTests
{
    private static int passed;
    private static int failed;
    private static long now;
    private static bool fresh;

    private static int Main()
    {
        Run("packet decodes legal zero positions and unsigned widths", PacketFields);
        Run("packet decodes unavailable sentinels", PacketMissing);
        Run("packet rejects wrong length, magic and version", PacketHeader);
        Run("packet rejects checksum damage and noncanonical Fletcher bytes", PacketChecksum);
        Run("packet rejects percent and enum ranges", PacketRanges);
        Run("packet tracks known boolean flags and rejects unknown true bits", PacketKnownFlags);
        Run("packet rejects capability sentinel mismatches", PacketCapabilities);
        Run("packet rejects invalid UTF8, lengths and padding", PacketStrings);
        Run("pixel layout has independent expected bit positions", PixelLayout);
        Run("pixel roundtrip preserves all bytes and offset calibration", PixelRoundtrip);
        Run("pixel rejects uncertain colors and unused blue bits", PixelNoise);
        Run("pixel rejects outside coordinates and wrong arguments", PixelBounds);
        Run("heartbeat requires sequence advancement", HeartbeatAdvance);
        Run("heartbeat repeats do not extend the 900ms deadline", HeartbeatExpiry);
        Run("heartbeat handles sequence wrap and large plugin timestamps", HeartbeatWrap);
        Run("heartbeat invalidation and reset require new baseline", HeartbeatReset);
        Run("heartbeat rejects a backwards local clock", HeartbeatClock);
        Run("input eligibility requires fresh data and known core flags", EligibilityKnownFlags);
        Run("input eligibility rejects read errors despite advancing sequence", EligibilityReadErrors);
        Run("guard defaults to no input", GuardDefault);
        Run("guard waits five seconds and releases at hold deadline", GuardPulse);
        Run("guard permits each documented key", GuardKeys);
        Run("guard rejects unsupported keys and unsafe timings", GuardArguments);
        Run("guard countdown allows focus to move to target", GuardFocusCountdown);
        Run("guard cancels unfocused due pulse without retry", GuardMissingFocus);
        Run("guard cancels stale due pulse without retry", GuardStale);
        Run("guard stale data cancels countdown before deadline", GuardStalePending);
        Run("guard F12 cancels pending pulse", GuardEmergencyPending);
        Run("guard Stop cancels before deadline", GuardStopPending);
        Run("guard Stop releases active key once", GuardStopActive);
        Run("guard focus loss releases active key", GuardFocusLost);
        Run("guard stale data releases active key", GuardStaleActive);
        Run("guard F12 releases active key", GuardEmergencyActive);
        Run("guard never releases an already held user key", GuardAlreadyDown);
        Run("guard checks physical key state only at due time", GuardAlreadyDownLate);
        Run("guard backend read errors latch without rearming", GuardReadException);
        Run("guard partial KeyDown error attempts emergency release", GuardDownException);
        Run("guard KeyUp error latches and does not loop", GuardUpException);
        Run("guard data callback and clock errors stop safely", GuardDelegateException);
        Run("guard maximum hold is bounded", GuardMaxHold);
        Run("guard rejects replacing pending or active pulse", GuardSinglePulse);
        Run("guard Stop and Tick serialize without duplicate input", GuardConcurrency);
        Run("invalid packet immediately closes the heartbeat gate", InvalidDecodeStopsInput);
        Console.WriteLine("Core tests: {0} passed, {1} failed. No OS input backend was used.", passed, failed);
        return failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }

    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(message + ": expected " + expected + ", actual " + actual);
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private static byte[] Packet(int sequence)
    {
        byte[] b = new byte[128];
        b[0] = 87; b[1] = 80; b[2] = 49; b[3] = 1;
        U16(b, 4, sequence);
        b[8] = b[9] = b[10] = 255;
        for (int i = 12; i <= 18; i += 2) U16(b, i, 65535);
        U16(b, 24, 65535); U16(b, 26, 65535);
        b[123] = 255;
        b[125] = 255;
        byte[] version = Encoding.UTF8.GetBytes("3.3.5");
        Buffer.BlockCopy(version, 0, b, 106, version.Length);
        Seal(b); return b;
    }
    private static void U16(byte[] b, int offset, int value)
    { b[offset] = (byte)value; b[offset + 1] = (byte)(value >> 8); }
    private static void U32(byte[] b, int offset, uint value)
    { for (int i = 0; i < 4; i++) b[offset + i] = (byte)(value >> (8 * i)); }
    private static void Seal(byte[] b)
    {
        int a = 0, c = 0;
        for (int i = 0; i < 126; i++) { a = (a + b[i]) % 255; c = (c + a) % 255; }
        b[126] = (byte)a; b[127] = (byte)c;
    }
    private static ProbePacket Decoded(int sequence) { return ProbePacket.Decode(Packet(sequence)); }
    private static void Invalid(byte[] bytes) { Seal(bytes); Throws<FormatException>(delegate { ProbePacket.Decode(bytes); }); }

    private static void PacketFields()
    {
        byte[] b = Packet(65535); b[6] = 255; b[7] = 255;
        b[8] = 0; b[9] = 100; b[10] = 50; b[11] = 9;
        U16(b, 12, 0); U16(b, 14, 65534); U16(b, 16, 32767); U16(b, 18, 0);
        U32(b, 20, 0xF1234567); U16(b, 24, 0); U16(b, 26, 32767);
        U32(b, 28, 0xFEDCBA98); U32(b, 32, 0x87654321); U32(b, 36, UInt32.MaxValue);
        byte[] name = Encoding.UTF8.GetBytes("目标"); b[40] = (byte)name.Length;
        Buffer.BlockCopy(name, 0, b, 42, name.Length);
        byte[] zone = Encoding.UTF8.GetBytes("区域"); b[41] = (byte)zone.Length;
        Buffer.BlockCopy(zone, 0, b, 74, zone.Length);
        b[122] = 3; b[123] = 8; b[124] = 15; Seal(b);
        ProbePacket p = ProbePacket.Decode(b);
        Equal(65535, p.Sequence, "sequence"); Equal(255, p.Flags, "flags"); Equal(255, p.Capabilities, "caps");
        Equal(255, p.KnownFlags, "known boolean flags");
        Equal(0, p.Health, "health"); Equal(100, p.Power, "power"); Equal(50, p.TargetHealth, "target health");
        Equal(9, p.PowerType, "resource type"); Equal(0.0, p.X, "zero X"); Equal(1.0, p.Y, "Y");
        Check(Math.Abs(p.Facing - Math.PI) < 0.000001, "facing radians"); Equal(0, p.FreeSlots, "full bags");
        Equal(0xF1234567u, p.MapId, "unsigned map"); Equal(0.0, p.CorpseX, "zero corpse X");
        Equal(0.5, p.CorpseY, "corpse Y"); Equal(0xFEDCBA98u, p.Build, "build width");
        Equal(0x87654321u, p.InterfaceVersion, "interface width"); Equal(UInt32.MaxValue, p.PluginMilliseconds, "plugin width");
        Equal("目标", p.TargetName, "UTF8 target"); Equal("区域", p.Zone, "UTF8 zone");
        Equal("3.3.5", p.ClientVersion, "version"); Equal(3, p.MapMode, "map mode");
        Equal(8, p.TargetReaction, "reaction"); Equal(15, p.ReadFailures, "read failures");
    }
    private static void PacketMissing()
    {
        byte[] b = Packet(0); b[125] = 0; Seal(b); ProbePacket p = ProbePacket.Decode(b);
        Equal(0, p.KnownFlags, "missing booleans remain unknown"); Equal(0, p.Flags, "unknown flags cannot be true");
        Check(Double.IsNaN(p.X) && Double.IsNaN(p.Y) && Double.IsNaN(p.Facing), "missing position/facing must be NaN");
        Check(Double.IsNaN(p.CorpseX) && Double.IsNaN(p.CorpseY), "missing corpse must be NaN");
        Equal(-1, p.FreeSlots, "missing bags"); Equal(255, p.Health, "unknown health");
        Equal(255, p.Power, "unknown power"); Equal(255, p.TargetHealth, "unknown target health");
        Equal(255, p.TargetReaction, "unknown reaction");
    }
    private static void PacketHeader()
    {
        Throws<FormatException>(delegate { ProbePacket.Decode(null); });
        Throws<FormatException>(delegate { ProbePacket.Decode(new byte[127]); });
        Throws<FormatException>(delegate { ProbePacket.Decode(new byte[129]); });
        byte[] b = Packet(1); b[0] = 0; Invalid(b);
        b = Packet(1); b[3] = 2; Invalid(b);
    }
    private static void PacketChecksum()
    {
        byte[] b = Packet(1); b[4] ^= 1; Throws<FormatException>(delegate { ProbePacket.Decode(b); });
        b = Packet(1); b[126] ^= 1; Throws<FormatException>(delegate { ProbePacket.Decode(b); });
        b = Packet(1); b[127] ^= 1; Throws<FormatException>(delegate { ProbePacket.Decode(b); });
        b = Packet(46); Equal((byte)0, b[126], "hand-derived zero Fletcher accumulator");
        b[126] = 255; Throws<FormatException>(delegate { ProbePacket.Decode(b); });
    }
    private static void PacketRanges()
    {
        foreach (int offset in new int[] { 8, 9, 10 }) { byte[] b = Packet(0); b[offset] = 101; Invalid(b); }
        byte[] c = Packet(0); c[122] = 4; Invalid(c);
        c = Packet(0); c[123] = 9; Invalid(c);
        c = Packet(0); c[124] = 16; Invalid(c);
    }
    private static void PacketKnownFlags()
    {
        for (int bit = 1; bit <= 128; bit <<= 1)
        {
            byte[] b = Packet(0); b[6] = (byte)bit; b[125] = (byte)(255 ^ bit); Invalid(b);
            b = Packet(0); b[6] = (byte)bit; b[125] = (byte)bit; Seal(b);
            ProbePacket p = ProbePacket.Decode(b); Equal(bit, p.Flags, "known true flag"); Equal(bit, p.KnownFlags, "validity bit");
        }
        byte[] empty = Packet(0); empty[125] = 0; Seal(empty);
        Equal(0, ProbePacket.Decode(empty).KnownFlags, "zero validity mask is legal for diagnostic packet");
    }
    private static void PacketCapabilities()
    {
        foreach (int cap in new int[] { 1, 2, 4, 8, 16, 32, 64, 128 })
        { byte[] b = Packet(0); b[7] = (byte)cap; Invalid(b); }
        foreach (int offset in new int[] { 12, 14, 16, 18, 24, 26 })
        { byte[] b = Packet(0); U16(b, offset, 0); Invalid(b); }
        byte[] c = Packet(0); c[8] = 0; Invalid(c);
        c = Packet(0); c[9] = 100; Invalid(c);
        c = Packet(0); c[40] = 1; c[42] = 65; Invalid(c);
        c = Packet(0); U32(c, 20, 1); Invalid(c);
        c = Packet(0); c[122] = 1; Invalid(c);
        c = Packet(0); c[7] = 1; U16(c, 12, 0); Invalid(c);
        c = Packet(0); c[7] = 128; U16(c, 24, 0); Invalid(c);
    }
    private static void PacketStrings()
    {
        byte[] b = Packet(0); b[40] = 33; Invalid(b);
        b = Packet(0); b[41] = 33; Invalid(b);
        b = Packet(0); b[7] = 32; b[40] = 1; b[42] = 0xE4; Invalid(b);
        b = Packet(0); b[41] = 2; b[74] = 0xC0; b[75] = 0xAF; Invalid(b);
        b = Packet(0); b[106] = 0xED; b[107] = 0xA0; b[108] = 0x80; Invalid(b);
        b = Packet(0); b[43] = 65; Invalid(b);
        b = Packet(0); b[75] = 65; Invalid(b);
        b = Packet(0); b[114] = 65; Invalid(b);
        b = Packet(0); b[41] = 1; b[74] = 0; Invalid(b);
        b = Packet(0); for (int i = 0; i < 16; i++) b[106 + i] = 65; Seal(b);
        Equal("AAAAAAAAAAAAAAAA", ProbePacket.Decode(b).ClientVersion, "16 byte full version");
    }
    private static void PixelLayout()
    {
        byte[] b = new byte[128]; b[0] = 0xA5;
        using (Bitmap img = PixelCodec.Encode(b, 8))
        {
            Equal(256, img.Width, "width"); Equal(96, img.Height, "height");
            Equal(Color.Magenta.ToArgb(), img.GetPixel(4, 4).ToArgb(), "bits 0/1/2");
            Equal(Color.Blue.ToArgb(), img.GetPixel(12, 4).ToArgb(), "bits 3/4/5");
            Equal(Color.Lime.ToArgb(), img.GetPixel(20, 4).ToArgb(), "bits 6/7");
        }
    }
    private static void PixelRoundtrip()
    {
        byte[] b = new byte[128]; for (int i = 0; i < b.Length; i++) b[i] = (byte)(i * 197);
        using (Bitmap img = PixelCodec.Encode(b, 8))
        using (Bitmap offset = new Bitmap(300, 130))
        {
            using (Graphics g = Graphics.FromImage(offset)) g.DrawImageUnscaled(img, 11, 7);
            byte[] decoded = PixelCodec.Decode(offset, 15, 11, 8);
            for (int i = 0; i < 128; i++) Equal(b[i], decoded[i], "byte " + i);
        }
    }
    private static void PixelNoise()
    {
        foreach (int channel in new int[] { 65, 128, 190 })
        using (Bitmap img = PixelCodec.Encode(new byte[128], 8))
        { img.SetPixel(4, 4, Color.FromArgb(channel, 0, 0)); Throws<FormatException>(delegate { PixelCodec.Decode(img, 4, 4, 8); }); }
        using (Bitmap img = PixelCodec.Encode(new byte[128], 8))
        {
            img.SetPixel(20, 4, Color.Blue); Throws<FormatException>(delegate { PixelCodec.Decode(img, 4, 4, 8); });
            img.SetPixel(20, 4, Color.Black); img.SetPixel(4, 4, Color.FromArgb(64, 191, 64));
            Equal((byte)2, PixelCodec.Decode(img, 4, 4, 8)[0], "safe binary margins");
        }
    }
    private static void PixelBounds()
    {
        Throws<ArgumentException>(delegate { PixelCodec.Encode(new byte[127], 8); });
        Throws<ArgumentException>(delegate { PixelCodec.Encode(null, 8); });
        Throws<ArgumentException>(delegate { PixelCodec.Encode(new byte[128], 0); });
        Throws<ArgumentException>(delegate { PixelCodec.Decode(null, 0, 0, 8); });
        using (Bitmap img = PixelCodec.Encode(new byte[128], 8))
        {
            Throws<ArgumentException>(delegate { PixelCodec.Decode(img, -1, 4, 8); });
            Throws<ArgumentException>(delegate { PixelCodec.Decode(img, 4, -1, 8); });
            Throws<ArgumentException>(delegate { PixelCodec.Decode(img, 12, 4, 8); });
            Throws<ArgumentException>(delegate { PixelCodec.Decode(img, 4, 12, 8); });
            Throws<ArgumentException>(delegate { PixelCodec.Decode(img, 4, 4, 0); });
            Throws<ArgumentException>(delegate { PixelCodec.Decode(img, Int32.MaxValue, 4, Int32.MaxValue); });
        }
    }
    private static void HeartbeatAdvance()
    {
        Heartbeat h = new Heartbeat(); Check(!h.IsFresh(0), "empty heartbeat");
        h.Observe(Decoded(5), 0); Check(!h.IsFresh(0), "one frame is insufficient");
        h.Observe(Decoded(5), 200); Check(!h.IsFresh(200), "same sequence insufficient");
        h.Observe(Decoded(6), 400); Check(h.IsFresh(400), "advancing sequence enables data");
    }
    private static void HeartbeatExpiry()
    {
        Heartbeat h = new Heartbeat(); h.Observe(Decoded(1), 0); h.Observe(Decoded(2), 200);
        h.Observe(Decoded(2), 1000); Check(h.IsFresh(1099), "fresh at +899ms");
        Check(!h.IsFresh(1100), "stale at +900ms"); h.Observe(Decoded(2), 1101);
        Check(!h.IsFresh(1101), "repeated stale frame stays stale");
        h.Observe(Decoded(3), 1200); Check(h.IsFresh(1200), "new advance recovers freshness");
    }
    private static void HeartbeatWrap()
    {
        Heartbeat h = new Heartbeat(); h.Observe(Decoded(65535), 10); h.Observe(Decoded(0), 210);
        Check(h.IsFresh(210), "ushort wrap advances");
        byte[] b = Packet(1); U32(b, 36, UInt32.MaxValue); Seal(b);
        h.Observe(ProbePacket.Decode(b), 410); Check(h.IsFresh(410), "plugin timestamp independent");
    }
    private static void HeartbeatReset()
    {
        Heartbeat h = new Heartbeat(); h.Observe(Decoded(1), 0); h.Observe(Decoded(2), 200);
        h.Invalidate(); Check(!h.IsFresh(201), "invalidated"); h.Observe(Decoded(2), 202);
        Check(!h.IsFresh(202), "invalid requires new baseline"); h.Observe(Decoded(3), 400);
        Check(h.IsFresh(400), "advance after invalid"); h.Reset(); h.Observe(Decoded(10), 500);
        Check(!h.IsFresh(500), "calibration reset needs advance");
    }
    private static void HeartbeatClock()
    {
        Heartbeat h = new Heartbeat(); h.Observe(Decoded(1), 1000); h.Observe(Decoded(2), 1200);
        Check(!h.IsFresh(1199), "clock reversal invalidates gate"); h.Observe(Decoded(3), 1201);
        Check(!h.IsFresh(1201), "reversal requires baseline"); h.Observe(Decoded(4), 1401);
        Check(h.IsFresh(1401), "fresh after subsequent advance");
    }
    private static void EligibilityKnownFlags()
    {
        Check(!InputEligibility.Allows(null, true), "null packet blocked");
        Check(!InputEligibility.Allows(Decoded(1), false), "stale stream blocked");
        foreach (int mask in new int[] { 0, 1, 2, 3, 4, 5, 6, 248, 249, 250, 251, 252, 253, 254 })
        {
            byte[] b = Packet(1); b[125] = (byte)mask; Seal(b);
            Check(!InputEligibility.Allows(ProbePacket.Decode(b), true), "unknown dead/ghost/combat blocked: " + mask);
        }
        foreach (int mask in new int[] { 7, 15, 127, 255 })
        {
            byte[] b = Packet(1); b[125] = (byte)mask; Seal(b);
            Check(InputEligibility.Allows(ProbePacket.Decode(b), true), "known core states with clean reads allowed");
        }
    }
    private static void EligibilityReadErrors()
    {
        Heartbeat h = new Heartbeat(); h.Observe(Decoded(1), 0); h.Observe(Decoded(2), 200);
        Check(InputEligibility.Allows(Decoded(2), h.IsFresh(200)), "clean advancing stream allowed");
        int sequence = 3; long time = 400;
        foreach (int failure in new int[] { 1, 2, 4, 8, 15 })
        {
            byte[] b = Packet(sequence++); b[124] = (byte)failure; Seal(b); ProbePacket packet = ProbePacket.Decode(b);
            h.Observe(packet, time); Check(h.IsFresh(time), "damaged API reads still advance heartbeat");
            Check(!InputEligibility.Allows(packet, h.IsFresh(time)), "read failure blocks eligibility: " + failure);
            time += 200;
        }
    }

    private static InputGuard NewGuard(FakeInput backend)
    { now = 0; fresh = true; return new InputGuard(backend, delegate { return Interlocked.Read(ref now); }, delegate { return fresh; }); }
    private static void Due(InputGuard g) { now = 5000; g.Tick(); }
    private static void GuardDefault()
    {
        FakeInput b = new FakeInput(); InputGuard g = NewGuard(b); now = 999999; g.Tick();
        Check(!g.Pending && !g.Active, "idle state"); Equal(0, b.Events.Count, "no automatic input");
        Check(!String.IsNullOrEmpty(g.Status), "status available");
    }
    private static void GuardPulse()
    {
        FakeInput b = new FakeInput(); InputGuard g = NewGuard(b); g.Schedule(0x57, 5000, 120);
        Check(g.Pending && !g.Active, "pending pulse"); Equal(0, b.Events.Count, "Schedule emits nothing");
        now = 4999; g.Tick(); Equal(0, b.Events.Count, "before deadline");
        Due(g); Check(g.Active && !g.Pending, "active pulse"); Equal("D87", b.Events[0], "correct key");
        now = 5119; g.Tick(); Equal(1, b.Events.Count, "still held");
        now = 5120; g.Tick(); Equal("U87", b.Events[1], "release on deadline");
        Check(!g.Pending && !g.Active, "one pulse complete"); now = 10000; g.Tick(); Equal(2, b.Events.Count, "no repeat");
    }
    private static void GuardKeys()
    {
        ushort[] keys = { 0x57, 0x53, 0x41, 0x44, 0x51, 0x45, 0x09, 0x46, 0x31, 0x32, 0x33, 0x34, 0x35, 0x38, 0x39, 0x20 };
        foreach (ushort key in keys)
        {
            FakeInput b = new FakeInput(); InputGuard g = NewGuard(b); g.Schedule(key, 5000, 60); Due(g);
            Equal("D" + key, b.Events[0], "allowed key"); now = 5060; g.Tick(); Equal("U" + key, b.Events[1], "release allowed key");
        }
    }
    private static void GuardArguments()
    {
        FakeInput b = new FakeInput(); InputGuard g = NewGuard(b);
        foreach (ushort key in new ushort[] { 0, 0x36, 0x37, 0x7B, 0x70, 0xA2 })
            Throws<ArgumentException>(delegate { g.Schedule(key, 5000, 120); });
        foreach (int delay in new int[] { -1, 0, 4999, 5001 })
            Throws<ArgumentException>(delegate { g.Schedule(0x57, delay, 120); });
        foreach (int hold in new int[] { -1, 0, 59, 251, Int32.MaxValue })
            Throws<ArgumentException>(delegate { g.Schedule(0x57, 5000, hold); });
        Check(!g.Pending && !g.Active, "invalid arguments leave idle"); Equal(0, b.Events.Count, "no input for bad argument");
    }
    private static void GuardFocusCountdown()
    {
        FakeInput b = new FakeInput(); b.Focus = false; InputGuard g = NewGuard(b);
        g.Schedule(0x57, 5000, 120); now = 4999; g.Tick(); Check(g.Pending, "countdown permits focus switch");
        b.Focus = true; Due(g); Check(g.Active, "target focused by deadline"); g.Stop("test cleanup");
    }
    private static void GuardMissingFocus()
    {
        FakeInput b = new FakeInput(); InputGuard g = NewGuard(b); g.Schedule(0x57, 5000, 120);
        b.Focus = false; Due(g); Check(!g.Pending && !g.Active, "cancel unfocused pulse");
        b.Focus = true; now = 6000; g.Tick(); Equal(0, b.Events.Count, "no delayed retry");
        Check(!String.IsNullOrEmpty(g.LastReason), "reason provided");
    }
    private static void GuardStale()
    {
        FakeInput b = new FakeInput(); InputGuard g = NewGuard(b); g.Schedule(0x57, 5000, 120);
        fresh = false; Due(g); fresh = true; now = 6000; g.Tick();
        Equal(0, b.Events.Count, "stale due pulse never emits"); Check(!g.Pending, "stale cancels");
    }
    private static void GuardStalePending()
    {
        FakeInput b = new FakeInput(); InputGuard g = NewGuard(b); g.Schedule(0x57, 5000, 120);
        fresh = false; now = 100; g.Tick(); Check(!g.Pending, "expired data must cancel countdown immediately");
        fresh = true; Due(g); Equal(0, b.Events.Count, "new data does not re-arm cancelled countdown");
    }
    private static void GuardEmergencyPending()
    {
        FakeInput b = new FakeInput(); InputGuard g = NewGuard(b); g.Schedule(0x57, 5000, 120);
        b.Emergency = true; now = 100; g.Tick(); b.Emergency = false; Due(g);
        Equal(0, b.Events.Count, "F12 stops countdown"); Check(!g.Pending, "cancel pending");
    }
    private static void GuardStopPending()
    {
        FakeInput b = new FakeInput(); InputGuard g = NewGuard(b); g.Schedule(0x57, 5000, 120);
        g.Stop("cancelled by user"); Due(g); Equal(0, b.Events.Count, "no pulse after stop");
        Equal("cancelled by user", g.LastReason, "stop reason");
    }
    private static void GuardStopActive()
    {
        FakeInput b = new FakeInput(); InputGuard g = NewGuard(b); g.Schedule(0x57, 5000, 120); Due(g);
        b.Focus = false; g.Stop("stop active"); g.Stop("stop again"); g.Tick();
        Equal(2, b.Events.Count, "exactly one release"); Equal("U87", b.Events[1], "release even after focus moves");
        Check(!g.Active && !g.Pending, "stopped");
    }
    private static void GuardFocusLost()
    {
        FakeInput b = new FakeInput(); InputGuard g = NewGuard(b); g.Schedule(0x57, 5000, 120); Due(g);
        b.Focus = false; now = 5001; g.Tick(); Equal("U87", b.Events[1], "focus loss releases");
        b.Focus = true; now = 5002; g.Tick(); Equal(2, b.Events.Count, "no focus recovery retry");
    }
    private static void GuardStaleActive()
    {
        FakeInput b = new FakeInput(); InputGuard g = NewGuard(b); g.Schedule(0x57, 5000, 120); Due(g);
        fresh = false; now = 5001; g.Tick(); Equal("U87", b.Events[1], "stale releases"); Check(!g.Active, "inactive");
    }
    private static void GuardEmergencyActive()
    {
        FakeInput b = new FakeInput(); InputGuard g = NewGuard(b); g.Schedule(0x57, 5000, 120); Due(g);
        b.Emergency = true; now = 5001; g.Tick(); Equal("U87", b.Events[1], "F12 releases"); Check(!g.Active, "inactive");
    }
    private static void GuardAlreadyDown()
    {
        FakeInput b = new FakeInput(); b.AlreadyDown = true; InputGuard g = NewGuard(b);
        g.Schedule(0x57, 5000, 120); Due(g); g.Stop("stop");
        Equal(0, b.Events.Count, "must not send down or up for user-held key"); Check(!g.Pending && !g.Active, "blocked");
    }
    private static void GuardAlreadyDownLate()
    {
        FakeInput b = new FakeInput(); InputGuard g = NewGuard(b); g.Schedule(0x57, 5000, 120);
        b.AlreadyDown = true; Due(g); Equal(0, b.Events.Count, "key pressed during countdown blocks pulse");
    }
    private static void GuardReadException()
    {
        foreach (string failure in new string[] { "focus", "emergency", "already" })
        {
            FakeInput b = new FakeInput(); InputGuard g = NewGuard(b); g.Schedule(0x57, 5000, 120); b.ThrowAt = failure;
            Due(g); Check(!g.Pending && !g.Active, "read error cancels"); b.ThrowAt = null; g.Tick();
            Equal(0, b.Events.Count, "read error emits nothing"); Check(!String.IsNullOrEmpty(g.LastReason), "error reason");
            Throws<InvalidOperationException>(delegate { g.Schedule(0x57, 5000, 120); });
        }
        FakeInput active = new FakeInput(); InputGuard guard = NewGuard(active); guard.Schedule(0x57, 5000, 120); Due(guard);
        active.ThrowAt = "focus"; now = 5001; guard.Tick(); Equal("U87", active.Events[1], "read error active release");
    }
    private static void GuardDownException()
    {
        FakeInput b = new FakeInput(); b.ThrowAt = "down"; InputGuard g = NewGuard(b); g.Schedule(0x57, 5000, 120); Due(g);
        Equal(2, b.Events.Count, "partial keydown must be followed by keyup"); Equal("U87", b.Events[1], "release uncertain down");
        Check(!g.Pending && !g.Active, "fault stopped"); b.ThrowAt = null; now = 6000; g.Tick(); Equal(2, b.Events.Count, "fault never retries");
    }
    private static void GuardUpException()
    {
        FakeInput b = new FakeInput(); InputGuard g = NewGuard(b); g.Schedule(0x57, 5000, 120); Due(g);
        b.ThrowAt = "up"; now = 5120; g.Tick(); Check(!g.Pending && !g.Active, "up failure latched");
        now = 5200; g.Tick(); Equal(2, b.Events.Count, "no automatic repeated release");
        Throws<InvalidOperationException>(delegate { g.Schedule(0x57, 5000, 120); });
    }
    private static void GuardDelegateException()
    {
        FakeInput b = new FakeInput(); now = 0;
        InputGuard g = new InputGuard(b, delegate { return now; }, delegate { throw new InvalidOperationException("fresh error"); });
        g.Schedule(0x57, 5000, 120); Due(g); Equal(0, b.Events.Count, "fresh exception cancels"); Check(!g.Pending, "fresh exception stops");
        bool throwClock = false;
        g = new InputGuard(b, delegate { if (throwClock) throw new InvalidOperationException("clock error"); return now; }, delegate { return true; });
        now = 0; g.Schedule(0x57, 5000, 120); now = 5000; g.Tick(); throwClock = true; g.Tick();
        Equal("U87", b.Events[b.Events.Count - 1], "clock exception active release");
    }
    private static void GuardMaxHold()
    {
        FakeInput b = new FakeInput(); InputGuard g = NewGuard(b); g.Schedule(0x57, 5000, 250); Due(g);
        now = 5249; g.Tick(); Check(g.Active, "held until max deadline"); now = 5250; g.Tick();
        Check(!g.Active, "max hold releases"); Equal(2, b.Events.Count, "single bounded pulse");
    }
    private static void GuardSinglePulse()
    {
        FakeInput b = new FakeInput(); InputGuard g = NewGuard(b); g.Schedule(0x57, 5000, 120);
        Throws<InvalidOperationException>(delegate { g.Schedule(0x53, 5000, 120); }); Due(g);
        Throws<InvalidOperationException>(delegate { g.Schedule(0x53, 5000, 120); }); g.Stop("cleanup");
        Equal(2, b.Events.Count, "one pulse only");
    }
    private static void GuardConcurrency()
    {
        FakeInput b = new FakeInput(); InputGuard g = NewGuard(b); g.Schedule(0x57, 5000, 120); now = 5000;
        Exception workerFailure = null; ManualResetEvent start = new ManualResetEvent(false);
        Thread[] workers = new Thread[6];
        for (int i = 0; i < workers.Length; i++)
        {
            int worker = i;
            workers[i] = new Thread(delegate()
            {
                try
                {
                    start.WaitOne();
                    for (int j = 0; j < 200; j++)
                    {
                        if (worker == 0) g.Stop("concurrent stop"); else g.Tick();
                        string status = g.Status; bool pending = g.Pending; bool active = g.Active; string reason = g.LastReason;
                    }
                }
                catch (Exception ex) { Interlocked.CompareExchange(ref workerFailure, ex, null); }
            }); workers[i].Start();
        }
        start.Set(); foreach (Thread t in workers) Check(t.Join(5000), "worker timeout"); start.Dispose();
        Check(workerFailure == null, "concurrent exception");
        Check(b.Events.Count == 0 || b.Events.Count == 2, "race emits either no pulse or one paired pulse");
        if (b.Events.Count == 2) { Equal("D87", b.Events[0], "race down"); Equal("U87", b.Events[1], "race release"); }
        Check(!g.Active && !g.Pending, "final stopped state");
    }
    private static void InvalidDecodeStopsInput()
    {
        Heartbeat h = new Heartbeat(); h.Observe(Decoded(1), 0); h.Observe(Decoded(2), 200);
        FakeInput b = new FakeInput(); now = 200;
        InputGuard g = new InputGuard(b, delegate { return now; }, delegate { return h.IsFresh(now); });
        g.Schedule(0x57, 5000, 120); now = 5200; h.Observe(Decoded(3), now); g.Tick(); Check(g.Active, "active on advancing data");
        byte[] damaged = Packet(4); damaged[50] ^= 1;
        try { h.Observe(ProbePacket.Decode(damaged), now); throw new Exception("bad packet accepted"); }
        catch (FormatException) { h.Invalidate(); }
        g.Tick(); Equal("U87", b.Events[1], "invalid packet closes input gate immediately");
    }

    private sealed class FakeInput : IInputBackend
    {
        public bool Focus = true, Emergency, AlreadyDown;
        public string ThrowAt;
        public readonly List<string> Events = new List<string>();
        private void Fail(string point) { if (ThrowAt == point) throw new InvalidOperationException(point + " failed"); }
        public bool IsTargetForeground() { Fail("focus"); return Focus; }
        public bool IsEmergencyPressed() { Fail("emergency"); return Emergency; }
        public bool IsKeyAlreadyDown(ushort key) { Fail("already"); return AlreadyDown; }
        public void KeyDown(ushort key) { Events.Add("D" + key); Fail("down"); }
        public void KeyUp(ushort key) { Events.Add("U" + key); Fail("up"); }
    }
}
