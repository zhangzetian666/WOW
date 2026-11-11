using System;
using System.Drawing;
using System.Text;

namespace WoWProbe
{
    /// <summary>An immutable, fully validated WP1 frame. No game or native API is used here.</summary>
    public sealed class ProbePacket
    {
        public int Sequence { get; private set; }
        public int Flags { get; private set; }
        public int KnownFlags { get; private set; }
        public int Capabilities { get; private set; }
        public int Health { get; private set; }
        public int Power { get; private set; }
        public int TargetHealth { get; private set; }
        public int PowerType { get; private set; }
        public double X { get; private set; }
        public double Y { get; private set; }
        public double Facing { get; private set; }
        public int FreeSlots { get; private set; }
        public uint MapId { get; private set; }
        public double CorpseX { get; private set; }
        public double CorpseY { get; private set; }
        public uint Build { get; private set; }
        public uint InterfaceVersion { get; private set; }
        public uint PluginMilliseconds { get; private set; }
        public string TargetName { get; private set; }
        public string Zone { get; private set; }
        public string ClientVersion { get; private set; }
        public int MapMode { get; private set; }
        public int TargetReaction { get; private set; }
        public int ReadFailures { get; private set; }

        private ProbePacket() { }

        public static ProbePacket Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 128) throw Invalid("Packet must contain exactly 128 bytes.");
            // Decode a private snapshot, so callers cannot mutate a frame during validation.
            byte[] b = (byte[])bytes.Clone();
            if (b[0] != 87 || b[1] != 80 || b[2] != 49 || b[3] != 1)
                throw Invalid("Unsupported packet signature or version.");
            int a = 0, sum = 0;
            for (int i = 0; i < 126; i++) { a = (a + b[i]) % 255; sum = (sum + a) % 255; }
            if (b[126] != a || b[127] != sum) throw Invalid("Fletcher16 checksum mismatch.");
            if (b[40] > 32 || b[41] > 32) throw Invalid("UTF8 field length exceeds 32 bytes.");
            if (!Percent(b[8]) || !Percent(b[9]) || !Percent(b[10])) throw Invalid("Invalid percentage.");
            if (b[122] > 3 || (b[123] > 8 && b[123] != 255) || b[124] > 15)
                throw Invalid("Invalid enum or read failure flags.");
            if ((b[6] & ~b[125]) != 0) throw Invalid("Unknown boolean flag cannot be true.");

            int caps = b[7];
            int x = U16(b, 12), y = U16(b, 14), facing = U16(b, 16), bags = U16(b, 18);
            int corpseX = U16(b, 24), corpseY = U16(b, 26);
            PairCapability(caps, 1, x, y, "player position");
            PairCapability(caps, 128, corpseX, corpseY, "corpse position");
            Capability(caps, 2, facing != 65535, "facing");
            Capability(caps, 4, b[8] != 255, "health");
            Capability(caps, 8, b[9] != 255, "power");
            Capability(caps, 16, bags != 65535, "bags");
            Capability(caps, 32, b[40] != 0, "target name");
            uint mapId = U32(b, 20);
            bool hasMap = (caps & 64) != 0;
            if (hasMap ? (mapId == 0 || b[122] == 0) : (mapId != 0 || b[122] != 0))
                throw Invalid("Map capability does not match map identity and mode.");

            string target = Text(b, 42, b[40], 32);
            string zone = Text(b, 74, b[41], 32);
            int versionLength = 0;
            while (versionLength < 16 && b[106 + versionLength] != 0) versionLength++;
            string version = Text(b, 106, versionLength, 16);

            return new ProbePacket
            {
                Sequence = U16(b, 4), Flags = b[6], KnownFlags = b[125], Capabilities = caps,
                Health = b[8], Power = b[9], TargetHealth = b[10], PowerType = b[11],
                X = Position(x), Y = Position(y), Facing = facing == 65535 ? Double.NaN : facing / 65534.0 * 2.0 * Math.PI,
                FreeSlots = bags == 65535 ? -1 : bags, MapId = mapId,
                CorpseX = Position(corpseX), CorpseY = Position(corpseY),
                Build = U32(b, 28), InterfaceVersion = U32(b, 32), PluginMilliseconds = U32(b, 36),
                TargetName = target, Zone = zone, ClientVersion = version,
                MapMode = b[122], TargetReaction = b[123], ReadFailures = b[124]
            };
        }

        private static bool Percent(int p) { return p <= 100 || p == 255; }
        private static FormatException Invalid(string message) { return new FormatException(message); }
        private static int U16(byte[] b, int i) { return b[i] | (b[i + 1] << 8); }
        private static uint U32(byte[] b, int i)
        { return (uint)b[i] | ((uint)b[i + 1] << 8) | ((uint)b[i + 2] << 16) | ((uint)b[i + 3] << 24); }
        private static double Position(int value) { return value == 65535 ? Double.NaN : value / 65534.0; }
        private static void Capability(int caps, int mask, bool valid, string name)
        { if (((caps & mask) != 0) != valid) throw Invalid("Capability does not match " + name + "."); }
        private static void PairCapability(int caps, int mask, int x, int y, string name)
        {
            bool present = (caps & mask) != 0;
            if (present ? (x == 65535 || y == 65535) : (x != 65535 || y != 65535))
                throw Invalid("Capability does not match " + name + ".");
        }
        private static string Text(byte[] b, int start, int length, int width)
        {
            for (int i = 0; i < length; i++)
                if (b[start + i] == 0) throw Invalid("NUL inside a UTF8 field.");
            for (int i = length; i < width; i++)
                if (b[start + i] != 0) throw Invalid("Nonzero UTF8 field padding.");
            try { return new UTF8Encoding(false, true).GetString(b, start, length); }
            catch (DecoderFallbackException ex) { throw new FormatException("Invalid UTF8 field.", ex); }
        }
    }

    /// <summary>A live stream alone is insufficient when required game API reads are unknown.</summary>
    public static class InputEligibility
    {
        public static bool Allows(ProbePacket packet, bool streamFresh)
        {
            return packet != null && streamFresh && packet.ReadFailures == 0 && (packet.KnownFlags & 7) == 7;
        }
    }

    public static class PixelCodec
    {
        public static byte[] Decode(Bitmap image, int centerX, int centerY, int cellSize)
        {
            if (image == null) throw new ArgumentNullException("image");
            if (cellSize <= 0) throw new ArgumentOutOfRangeException("cellSize");
            // Promote before multiplication/addition to make hostile calibration coordinates safe.
            long lastX = (long)centerX + 31L * cellSize;
            long lastY = (long)centerY + 11L * cellSize;
            if (centerX < 0 || centerY < 0 || lastX >= image.Width || lastY >= image.Height)
                throw new ArgumentException("Calibrated packet cells fall outside the captured image.");
            byte[] packet = new byte[128];
            for (int i = 0; i < 384; i++)
            {
                Color c = image.GetPixel((int)((long)centerX + (i % 32) * (long)cellSize),
                    (int)((long)centerY + (i / 32) * (long)cellSize));
                int red = Bit(c.R), green = Bit(c.G), blue = Bit(c.B);
                int group = i % 3;
                if (group == 2 && blue != 0) throw new FormatException("Nonzero unused blue bit in pixel packet.");
                int bits = red | (green << 1) | (blue << 2);
                packet[i / 3] |= (byte)(bits << (group * 3));
            }
            return packet;
        }

        public static Bitmap Encode(byte[] packet, int cellSize)
        {
            if (packet == null) throw new ArgumentNullException("packet");
            if (packet.Length != 128) throw new ArgumentException("Packet must contain exactly 128 bytes.", "packet");
            if (cellSize <= 0 || cellSize > Int32.MaxValue / 32) throw new ArgumentOutOfRangeException("cellSize");
            Bitmap image = new Bitmap(32 * cellSize, 12 * cellSize);
            try
            {
                using (Graphics graphics = Graphics.FromImage(image))
                {
                    for (int i = 0; i < 384; i++)
                    {
                        int bits = packet[i / 3] >> ((i % 3) * 3);
                        Color color = Color.FromArgb((bits & 1) != 0 ? 255 : 0,
                            (bits & 2) != 0 ? 255 : 0, (bits & 4) != 0 ? 255 : 0);
                        using (SolidBrush brush = new SolidBrush(color))
                            graphics.FillRectangle(brush, (i % 32) * cellSize, (i / 32) * cellSize, cellSize, cellSize);
                    }
                }
                return image;
            }
            catch { image.Dispose(); throw; }
        }

        private static int Bit(int channel)
        {
            if (channel >= 65 && channel <= 190) throw new FormatException("Uncertain pixel channel; recalibrate the color grid.");
            return channel >= 128 ? 1 : 0;
        }
    }

    /// <summary>Freshness is measured solely against the injected local monotonic clock.</summary>
    public sealed class Heartbeat
    {
        private readonly object sync = new object();
        private bool observed, advanced, timed;
        private int sequence;
        private long lastAdvance, lastTime;

        public void Observe(ProbePacket packet, long nowMs)
        {
            lock (sync)
            {
                if (packet == null) { ResetCore(); return; }
                if (timed && nowMs < lastTime) ResetCore();
                timed = true; lastTime = nowMs;
                if (!observed) { observed = true; sequence = packet.Sequence; lastAdvance = nowMs; return; }
                if (packet.Sequence != sequence) { sequence = packet.Sequence; lastAdvance = nowMs; advanced = true; }
            }
        }

        public bool IsFresh(long nowMs)
        {
            lock (sync)
            {
                if (timed && nowMs < lastTime) { ResetCore(); return false; }
                timed = true; lastTime = nowMs;
                // Unsigned subtraction cannot wrap a very large elapsed interval into a fresh one.
                return observed && advanced && nowMs >= lastAdvance && unchecked((ulong)nowMs - (ulong)lastAdvance) < 900UL;
            }
        }

        public void Reset() { lock (sync) ResetCore(); }
        public void Invalidate() { lock (sync) ResetCore(); }
        private void ResetCore() { observed = advanced = timed = false; sequence = 0; lastAdvance = lastTime = 0; }
    }

    public interface IInputBackend
    {
        bool IsTargetForeground();
        bool IsEmergencyPressed();
        bool IsKeyAlreadyDown(ushort key);
        void KeyDown(ushort key);
        void KeyUp(ushort key);
    }

    /// <summary>One explicitly scheduled key pulse; the owner must call Tick from its safety monitor.</summary>
    public sealed class InputGuard
    {
        private readonly object sync = new object();
        private readonly IInputBackend backend;
        private readonly Func<long> clock;
        private readonly Func<bool> dataFresh;
        private bool pending, active, ownsKey, faulted, hasTime;
        private ushort key;
        private int holdMs;
        private long due, releaseAt, lastTime;
        private string status = "Idle", lastReason = "";

        public InputGuard(IInputBackend backend, Func<long> clock, Func<bool> dataFresh)
        {
            if (backend == null) throw new ArgumentNullException("backend");
            if (clock == null) throw new ArgumentNullException("clock");
            if (dataFresh == null) throw new ArgumentNullException("dataFresh");
            this.backend = backend; this.clock = clock; this.dataFresh = dataFresh;
        }

        public string Status { get { lock (sync) return status; } }
        public string LastReason { get { lock (sync) return lastReason; } }
        public bool Pending { get { lock (sync) return pending; } }
        public bool Active { get { lock (sync) return active; } }

        public void Schedule(ushort key, int delayMs, int holdMs)
        {
            lock (sync)
            {
                if (!Allowed(key)) throw new ArgumentException("Key is not in the single-pulse allowlist.", "key");
                if (delayMs != 5000) throw new ArgumentOutOfRangeException("delayMs", "Countdown must be 5000 milliseconds.");
                if (holdMs < 60 || holdMs > 250) throw new ArgumentOutOfRangeException("holdMs", "Hold must be 60 through 250 milliseconds.");
                if (faulted) throw new InvalidOperationException("Input backend fault is latched; create a new guard after resolving it.");
                if (pending || active) throw new InvalidOperationException("A single key pulse is already scheduled or active.");
                try
                {
                    long time = clock();
                    due = checked(time + delayMs);
                    this.key = key; this.holdMs = holdMs;
                    lastTime = time; hasTime = true; pending = true; lastReason = ""; status = "Countdown";
                }
                catch (Exception ex) { Fault(ex); }
            }
        }

        public void Tick()
        {
            lock (sync)
            {
                if (faulted || (!pending && !active)) return;
                try
                {
                    long time = clock();
                    if (hasTime && time < lastTime) { StopCore("Local monotonic clock moved backwards."); return; }
                    hasTime = true; lastTime = time;
                    if (backend.IsEmergencyPressed()) { StopCore("F12 emergency stop."); return; }
                    if (!dataFresh()) { StopCore("Screen packet is stale or invalid."); return; }
                    // The countdown deliberately permits the user to switch focus to the target.
                    if (pending && time < due) return;
                    if (!backend.IsTargetForeground()) { StopCore("Target window is not foreground."); return; }
                    if (active)
                    {
                        if (time >= releaseAt) StopCore("Single key pulse completed.");
                        return;
                    }
                    if (backend.IsKeyAlreadyDown(key)) { StopCore("Key is already held; pulse cancelled."); return; }
                    releaseAt = checked(time + holdMs);
                    pending = false; active = true; status = "Holding";
                    // Once KeyDown is attempted it may have succeeded before a backend throws.
                    // Mark ownership first, so every partial-send failure attempts one matching KeyUp.
                    ownsKey = true;
                    backend.KeyDown(key);
                }
                catch (Exception ex) { Fault(ex); }
            }
        }

        public void Stop(string reason)
        {
            lock (sync)
            {
                if (faulted) return;
                StopCore(String.IsNullOrEmpty(reason) ? "Stopped." : reason);
            }
        }

        private void StopCore(string reason)
        {
            pending = false; active = false; status = "Stopped"; lastReason = reason;
            if (!ownsKey) return;
            ownsKey = false;
            try { backend.KeyUp(key); }
            catch (Exception ex) { faulted = true; status = "Faulted"; lastReason = "Key release failed: " + ex.Message; }
        }

        private void Fault(Exception ex)
        {
            faulted = true;
            StopCore("Input stopped after error: " + ex.Message);
            status = "Faulted";
        }

        private static bool Allowed(ushort key)
        {
            switch (key)
            {
                case 0x57: case 0x53: case 0x41: case 0x44: case 0x51: case 0x45:
                case 0x09: case 0x46: case 0x31: case 0x32: case 0x33: case 0x34:
                case 0x35: case 0x38: case 0x39: case 0x20: return true;
                default: return false;
            }
        }
    }
}
