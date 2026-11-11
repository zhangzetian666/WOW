# WoWProbe AddOn reference

This is the read-only AddOn for the WP1 phase 1 diagnostic tool. It reads public UI APIs and renders a 128-byte packet as a 32-by-12 RGB grid. It does not move, target, cast, buy, loot, resurrect, set bindings, or invoke protected action APIs.

`WoWProbe.toc` uses Interface 30300 as a candidate baseline. This is not confirmation that the user's client is 3.3.5. The client must allow loading out-of-date AddOns when its interface number differs. `GetBuildInfo()` and available API functions determine the recorded client information and selected readers. Actual game compatibility remains unverified.

Commands: `/wowprobe show`, `/wowprobe hide`, `/wowprobe info`, `/wowprobe reset`. Hiding the panel preserves its internal 200ms heartbeat; the external reader cannot see its packet until it is shown. `reset` clears the current diagnostic report and retains the chosen visibility. It does not reset the sequence number.

The grid starts 8 physical pixels from the client top-left. Each cell is 8 pixels; the grid is 256 by 96 pixels. Following Blizzard's PixelUtil conversion, its frame scale is `(768 / physicalScreenHeight) / UIParent:GetEffectiveScale()`. Physical dimensions come from `GetPhysicalScreenSize()`, or `GetCVar("gxResolution")` when that API is absent on a legacy client. Missing or invalid dimensions hide the panel and block packet rendering until a valid size is obtained. Text sits below the grid. Real-client screenshot calibration must confirm these dimensions, especially with display scaling, render scaling, or other UI modifications. See the [shipped PixelUtil source](https://raw.githubusercontent.com/Gethe/wow-ui-source/live/Interface/AddOns/Blizzard_SharedXML/PixelUtil.lua).

`WoWProbeDB` is saved by WoW at logout or `/reload`, under the account's normal `WTF/Account/.../SavedVariables/WoWProbe.lua` path. It contains:

- `client`: actual version, build, interface and build date; `gameVerified` stays false.
- `apiAvailability` and `eventRegistration`: detected functions and registration outcomes.
- `bindings`: selected reader routes; `keyBindings`: existing gameplay key bindings, read without modification.
- `capabilities` and `lastPacket`: the latest successfully read capability and state snapshot; `lastPacket.knownFlags` records which boolean states were established.
- `errors`: API/category errors, count, last message and plugin time; one bounded `previousSession` preserves the preceding report across reloads.
- `renderer`: intended effective scale and pixel geometry; `visible`: saved panel preference.

Missing, invalid and throwing APIs produce unknown sentinels and diagnostic errors. Packet offset 125 is the `KnownFlags` mask, with the same bit positions as flags at offset 6: dead, ghost, combat, target exists, target dead, merchant, loot, and recent bag-full error. A flag is meaningful only when its validity bit is set. Missing player state APIs set the health read-failure bit; missing target state APIs set the target read-failure bit. Merchant/loot state is established by a readable frame or an observed open/close event; unknown values clear their validity bits and are recorded in diagnostics. Recent bag-full observation requires successful error-event registration. A failure in one category leaves other readable fields available. A fatal packet or renderer failure hides the panel; showing it again requires a successful render. Unknown values are not substitutes for healthy values. A legitimate zero health, zero resource, zero free slots, or modern map coordinate zero is retained.

Readers use `UnitPower`/`UnitPowerMax` when available, then the legacy `UnitMana` pair, preserving the reported resource type. Bags use the modern or legacy free-slot API, otherwise scan empty slots only if both slot-count and item-information APIs exist. Modern reagent bags are included when detected. Free slots are the sum across equipped bags, including specialized bag slots; this is not an item-specific capacity calculation.

Modern maps use `C_Map.GetBestMapForUnit` and `C_Map.GetPlayerMapPosition`; corpse coordinates use `C_DeathInfo.GetCorpseMapPosition` only while ghosted. Legacy maps refresh with `SetMapToCurrentZone` only when `WorldMapFrame` is explicitly known hidden. Opening the user's legacy world map makes map data unavailable rather than changing their selected map. A legacy `(0,0)` coordinate pair means unavailable. Map modes distinguish modern IDs, area IDs, and the legacy `continent * 10000 + zone` composite, which is not an interchangeable modern map ID.

The source signatures were checked against the shipped UI source mirrors: [legacy WorldMapFrame](https://github.com/wowgaming/3.3.5-interface-files/blob/main/WorldMapFrame.lua), [modern generated map API](https://raw.githubusercontent.com/Gethe/wow-ui-source/live/Interface/AddOns/Blizzard_APIDocumentationGenerated/MapDocumentation.lua), and [modern generated corpse API](https://raw.githubusercontent.com/Gethe/wow-ui-source/live/Interface/AddOns/Blizzard_APIDocumentationGenerated/DeathInfoDocumentation.lua). Runtime checks remain necessary because other clients may omit or restrict these APIs.

Tests in `tests/addon_tests.lua` execute under real Lua 5.1 with a WoW API/rendering boundary stub. They verify packet/checksum, renderer layout, API failures, resource types, map behavior, UTF8 boundaries, heartbeat, events and diagnostics. These tests do not establish actual in-game color output or client behavior.
