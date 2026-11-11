# WP1 screen packet protocol version 1

128 bytes, zero-based offsets. Unsigned little endian integers. Missing u16 value=65535; missing percent=255. Colors are binary RGB only (each channel 0 or 255), avoiding dependence on intermediate color accuracy.

32 columns x 12 rows, row major, 8 physical pixels per cell. Each byte occupies three consecutive cells: cell0 RGB=bits0/1/2, cell1 RGB=bits3/4/5, cell2 RG=bits6/7 and B=0. Thus 128*3=384 cells, 256x96 physical pixels. No padding between cells. First cell center is the manually calibrated screen point. Sample threshold 128; intermediate channel values 65..190 are rejected as uncertain. Plugin anchors grid near client top-left and uses frame scale=(768/physicalHeight)/UIParentEffectiveScale, accounting for WoW UI-unit conversion rather than merely forcing effectiveScale=1. Physical height comes from GetPhysicalScreenSize or legacy resolution configuration; unknown resolution disables rendering. Explanatory text is below the grid.

| Offset | Meaning |
|---|---|
|0..2|ASCII WP1 = 87,80,49|
|3|protocol version=1|
|4..5|sequence, increments every 200ms modulo 65536|
|6|flags: dead1, ghost2, combat4, target exists8, target dead16, merchant open32, loot open64, recent bag-full error128|
|7|capabilities: valid position1, facing2, player health4, player power8, bags16, target name32, map identity64, valid corpse position128|
|8|player health percent0..100 or255|
|9|player power percent0..100 or255|
|10|target health percent0..100 or255|
|11|player power type0..255 (0=mana; retain other resource type)|
|12..13,14..15|player x,y normalized0..1 encoded round(value*65534),65535 unavailable|
|16..17|facing angle0..2pi encoded round(angle/(2pi)*65534),65535 unavailable|
|18..19|free bag slots,65535 unavailable|
|20..23|map identifier u32|
|24..25,26..27|corpse x,y same encoding|
|28..31|client build u32|
|32..35|client interface number u32|
|36..39|plugin GetTime()*1000 modulo2^32; NOT compared to Windows wall clock|
|40,41|UTF8 target-name and zone byte lengths, maximum32; truncate on character boundary|
|42..73|target name UTF8, zero padded|
|74..105|zone UTF8, zero padded|
|106..121|version UTF8 max16 bytes, zero padded|
|122|map mode:0 unavailable,1 modern C_Map,2 GetCurrentMapAreaID,3 legacy continent*10000+zone|
|123|target reaction0..8 or255|
|124|read failure flags: health1,target2,bags4,map8|
|125|KnownFlags: bit positions match byte6; set only when that boolean can be determined. Unknown values must not set the corresponding Flags bit.|
|126,127|Fletcher16: a=0,b=0; for bytes0..125 a=(a+byte)%255; b=(b+a)%255; bytes126=a,127=b|

External reader validates magic, version, checksum, lengths, percentages, known/true flag consistency and capability/sentinel consistency. A fresh decoded packet alone is insufficient for input: sequence must advance after calibration/reset; unchanged sequence for >=900ms is stale. Only local monotonic time is used. Invalid decode immediately invalidates the current input gate. Calibration/window changes reset heartbeat. Continuous input eligibility requires fresh valid data, ReadFailures=0, and KnownFlags bits dead/ghost/combat all set; these checks run during countdown as well as the key pulse.

## Core API contract

namespace WoWProbe

ProbePacket.Decode(byte[] bytes) -> ProbePacket; throws FormatException for invalid data.
Fields/properties: Sequence(int), Flags(int), KnownFlags(int), Capabilities(int), Health(int), Power(int), TargetHealth(int), PowerType(int), X(double NaN if unavailable), Y(double), Facing(double radians), FreeSlots(int -1 if unavailable), MapId(uint), CorpseX(double), CorpseY(double), Build(uint), InterfaceVersion(uint), PluginMilliseconds(uint), TargetName(string), Zone(string), ClientVersion(string), MapMode(int), TargetReaction(int), ReadFailures(int).

InputEligibility.Allows(ProbePacket packet,bool streamFresh) -> bool; requires non-null packet, streamFresh, no ReadFailures, KnownFlags&7=7. Diagnostic freshness and input eligibility deliberately remain separate.

PixelCodec.Decode(System.Drawing.Bitmap image,int centerX,int centerY,int cellSize) -> byte[128]; throws FormatException/ArgumentException when invalid/outside.
PixelCodec.Encode(byte[] packet,int cellSize) -> Bitmap, used only for protocol simulator/tests.

Heartbeat.Observe(ProbePacket packet,long nowMs); Heartbeat.IsFresh(long nowMs) -> bool; Heartbeat.Reset(); Heartbeat.Invalidate(). First observation is not fresh; requires a new sequence, and freshness expires at 900ms.

IInputBackend { bool IsTargetForeground(); bool IsEmergencyPressed(); bool IsKeyAlreadyDown(ushort key); void KeyDown(ushort key); void KeyUp(ushort key); }
InputGuard(IInputBackend backend, Func<long> clock, Func<bool> dataFresh).
InputGuard.Schedule(ushort key,int delayMs,int holdMs); InputGuard.Tick(); InputGuard.Stop(string reason); properties Status(string), Pending(bool), Active(bool), LastReason(string).
Schedule allows only W,S,A,D,Q,E,TAB,F,1,2,3,4,5,8,9,SPACE; delay 5000ms; hold 60..250ms. Schedule does not emit input or require current focus (user has countdown to focus game). Tick checks emergency, data freshness and target focus before KeyDown, and keeps checking until KeyUp; at due time failure cancels rather than waiting to regain focus. Stop cancels pending and releases only keys the tool pressed, even if focus moved. Backend exceptions latch stopped reason; no automatic retry/re-arm. Status and state access are thread safe.
