-- WP1 read-only diagnostics. Lua 5.1: no bit libraries or protected action calls.
WoWProbe = {}
local P = WoWProbe
local floor, pi = math.floor, math.pi
local U16, U32 = 65535, 4294967296
P.sequence, P.elapsed = 0, 0
P.merchantOpen, P.lootOpen = false, false
P.merchantKnown, P.lootKnown = false, false

local function finite(n)
    return type(n) == "number" and n == n and n > -math.huge and n < math.huge
end
local function integer(n, max)
    return finite(n) and n >= 0 and n <= max and floor(n) == n
end
local function rawTime()
    if type(GetTime) == "function" then
        local ok, n = pcall(GetTime)
        if ok and finite(n) then return n, true end
    end
    return 0, false
end
local function errorRecord(name, message)
    if type(WoWProbeDB) ~= "table" then return end
    local errors = WoWProbeDB.errors
    local item = errors[name] or { count = 0 }
    item.count = item.count + 1
    item.message = string.sub(tostring(message), 1, 300)
    item.lastTime = rawTime()
    errors[name] = item
end
local function call(name, fn, ...)
    if type(fn) ~= "function" then errorRecord(name, "API unavailable"); return false end
    local ok, a, b, c, d = pcall(fn, ...)
    if not ok then errorRecord(name, a); return false end
    return true, a, b, c, d
end
local function invalid(name, message)
    errorRecord(name, message or "API returned an unavailable or invalid value")
end
local function binding(name, value) WoWProbeDB.bindings[name] = value end
local function percent(current, maximum)
    if not finite(current) or not finite(maximum) or maximum <= 0 or current < 0 or current > maximum then return nil end
    return floor(current / maximum * 100 + 0.5)
end
local function readPercent(name, currentAPI, maxAPI, unit)
    local a, current = call(name, currentAPI, unit)
    local b, maximum = call(name .. "Max", maxAPI, unit)
    if a and b then
        local result = percent(current, maximum)
        if result ~= nil then return result end
        invalid(name)
    end
    return nil
end
local function boolean(name, fn, ...)
    local ok, result = call(name, fn, ...)
    if not ok then return false, false end
    -- WoW 3.x uses 1/nil in some boolean APIs; nil means false, not failure.
    return result and true or false, true
end
local function windowState(name, frame, eventKnown, eventValue)
    if eventKnown then return eventValue, true end
    if frame then return boolean(name .. ".IsShown", frame.IsShown, frame) end
    invalid(name, "State not observed: frame unavailable and no open/close event received")
    return false, false
end
local function coordinates(x, y, legacy)
    return finite(x) and finite(y) and x >= 0 and x <= 1 and y >= 0 and y <= 1
        and not (legacy and x == 0 and y == 0)
end
local function vectorXY(label, v)
    if type(v) ~= "table" and type(v) ~= "userdata" then invalid(label); return nil end
    if type(v.GetXY) == "function" then
        local ok, x, y = call(label .. ".GetXY", v.GetXY, v)
        if ok then return x, y end
    else return v.x, v.y end
end

function P.UTF8Truncate(s, maximum)
    if type(s) ~= "string" then return "" end
    local i, finish = 1, 0
    while i <= #s and i <= maximum do
        local b, count = string.byte(s, i), nil
        if b == 0 then break
        elseif b < 128 then count = 1
        elseif b >= 194 and b <= 223 then count = 2
        elseif b >= 224 and b <= 239 then count = 3
        elseif b >= 240 and b <= 244 then count = 4
        else break end
        if i + count - 1 > #s or i + count - 1 > maximum then break end
        local valid = true
        for j = 1, count - 1 do
            local c = string.byte(s, i + j)
            if c < 128 or c > 191 then valid = false end
        end
        local second = string.byte(s, i + 1)
        if count == 3 and ((b == 224 and second < 160) or (b == 237 and second > 159)) then valid = false end
        if count == 4 and ((b == 240 and second < 144) or (b == 244 and second > 143)) then valid = false end
        if not valid then break end
        finish = i + count - 1; i = finish + 1
    end
    return string.sub(s, 1, finish)
end

local function bags()
    local container = type(C_Container) == "table" and C_Container or nil
    local free = container and container.GetContainerNumFreeSlots or GetContainerNumFreeSlots
    local slots = container and container.GetContainerNumSlots or GetContainerNumSlots
    local item = container and container.GetContainerItemInfo or GetContainerItemInfo
    local prefix = container and "C_Container." or ""
    local last = integer(NUM_TOTAL_EQUIPPED_BAG_SLOTS, 10) and NUM_TOTAL_EQUIPPED_BAG_SLOTS
        or (integer(NUM_BAG_SLOTS, 10) and NUM_BAG_SLOTS or 4)
    if type(Enum) == "table" and type(Enum.BagIndex) == "table" and integer(Enum.BagIndex.ReagentBag, 10) then
        last = math.max(last, Enum.BagIndex.ReagentBag)
    end
    local total = 0
    if type(free) == "function" then
        binding("bags", prefix .. "GetContainerNumFreeSlots")
        for bag = 0, last do
            local ok, n = call(prefix .. "GetContainerNumFreeSlots", free, bag)
            if not ok or not integer(n, 65534) then if ok then invalid("bags") end; return nil end
            total = total + n
        end
    else
        binding("bags", prefix .. "GetContainerNumSlots/GetContainerItemInfo scan")
        if type(slots) ~= "function" or type(item) ~= "function" then invalid("bags", "No free-slot API or safe scan APIs"); return nil end
        for bag = 0, last do
            local ok, n = call(prefix .. "GetContainerNumSlots", slots, bag)
            if not ok or not integer(n, 1000) then if ok then invalid("bags") end; return nil end
            for slot = 1, n do
                local success, info = call(prefix .. "GetContainerItemInfo", item, bag, slot)
                if not success then return nil end
                -- Legacy returns an item texture; modern returns a table. nil is an empty slot.
                if info == nil then total = total + 1 end
            end
        end
    end
    if not integer(total, 65534) then invalid("bags"); return nil end
    return total
end

local function mapState(ghost)
    local m = { mode = 0, id = 0, failed = false }
    local modern = type(C_Map) == "table" and type(C_Map.GetBestMapForUnit) == "function"
        and type(C_Map.GetPlayerMapPosition) == "function"
    if modern then
        binding("map", "C_Map.GetBestMapForUnit/GetPlayerMapPosition")
        local ok, id = call("C_Map.GetBestMapForUnit", C_Map.GetBestMapForUnit, "player")
        if not ok or not integer(id, U32 - 1) or id == 0 then
            if ok then invalid("map", "No current player map identity") end; m.failed = true; return m
        end
        m.id, m.mode = id, 1
        local success, v = call("C_Map.GetPlayerMapPosition", C_Map.GetPlayerMapPosition, id, "player")
        if success then m.x, m.y = vectorXY("C_Map.GetPlayerMapPosition", v) end
        if not coordinates(m.x, m.y) then m.x, m.y, m.failed = nil, nil, true; invalid("map.position") end
        if ghost then
            local death = type(C_DeathInfo) == "table" and C_DeathInfo.GetCorpseMapPosition
            local corpseOK, corpse = call("C_DeathInfo.GetCorpseMapPosition", death, id)
            if corpseOK then m.cx, m.cy = vectorXY("C_DeathInfo.GetCorpseMapPosition", corpse) end
            if not coordinates(m.cx, m.cy, true) then m.cx, m.cy, m.failed = nil, nil, true end
        end
        return m
    end
    binding("map", "legacy current-zone map (only while WorldMapFrame is hidden)")
    local frame = WorldMapFrame
    local visibleOK, visible = call("WorldMapFrame.IsShown", frame and frame.IsShown, frame)
    if not visibleOK or visible then
        invalid("map", "Legacy map unavailable while world map is open or visibility is unknown")
        m.failed = true; return m
    end
    local refreshed = call("SetMapToCurrentZone", SetMapToCurrentZone)
    if not refreshed then m.failed = true; return m end
    if type(GetCurrentMapAreaID) == "function" then
        local ok, id = call("GetCurrentMapAreaID", GetCurrentMapAreaID)
        if ok and integer(id, U32 - 1) and id > 0 then m.id, m.mode = id, 2
        else if ok then invalid("GetCurrentMapAreaID") end; m.failed = true; return m end
    else
        local a, continent = call("GetCurrentMapContinent", GetCurrentMapContinent)
        local b, zone = call("GetCurrentMapZone", GetCurrentMapZone)
        if a and b and integer(continent, 429495) and continent > 0 and integer(zone, 9999) and zone > 0 then
            m.id, m.mode = continent * 10000 + zone, 3
        else invalid("map", "No legacy map identity"); m.failed = true; return m end
    end
    local ok, x, y = call("GetPlayerMapPosition", GetPlayerMapPosition, "player")
    if ok and coordinates(x, y, true) then m.x, m.y = x, y
    else m.failed = true; if ok then invalid("map.position", "Zero/invalid legacy map position") end end
    if ghost then
        local corpseOK, cx, cy = call("GetCorpseMapPosition", GetCorpseMapPosition)
        if corpseOK and coordinates(cx, cy, true) then m.cx, m.cy = cx, cy
        else m.failed = true; if corpseOK then invalid("map.corpse") end end
    end
    return m
end

local function readClient()
    local ok, version, build, date, interface = call("GetBuildInfo", GetBuildInfo)
    local b, i = tonumber(build), tonumber(interface)
    WoWProbeDB.client = {
        version = ok and type(version) == "string" and version or "",
        build = integer(b, U32 - 1) and b or 0,
        date = ok and type(date) == "string" and date or "",
        interface = integer(i, U32 - 1) and i or 0,
        candidate = "3.3.5 / Interface 30300; actual client is capability detected",
        gameVerified = false,
    }
end
local function captureBindings()
    local keys = {}
    for _, action in ipairs({"MOVEFORWARD", "MOVEBACKWARD", "TURNLEFT", "TURNRIGHT", "STRAFELEFT", "STRAFERIGHT", "TARGETNEARESTENEMY", "JUMP", "INTERACTTARGET", "ACTIONBUTTON1", "ACTIONBUTTON2", "ACTIONBUTTON3", "ACTIONBUTTON4", "ACTIONBUTTON5", "ACTIONBUTTON8", "ACTIONBUTTON9"}) do
        local ok, first, second = call("GetBindingKey", GetBindingKey, action)
        keys[action] = ok and { first = first or "", second = second or "" } or { unavailable = true }
    end
    WoWProbeDB.keyBindings = keys
end
local function captureAPIs()
    local inventory = {}
    for _, path in ipairs({"GetBuildInfo", "GetTime", "UnitHealth", "UnitHealthMax", "UnitPower", "UnitPowerMax", "UnitMana", "UnitManaMax", "UnitPowerType", "UnitExists", "UnitName", "UnitReaction", "UnitIsDead", "UnitIsGhost", "UnitAffectingCombat", "GetPlayerFacing", "GetRealZoneText", "GetZoneText", "GetBindingKey", "GetContainerNumFreeSlots", "GetContainerNumSlots", "GetContainerItemInfo", "C_Container.GetContainerNumFreeSlots", "C_Container.GetContainerNumSlots", "C_Container.GetContainerItemInfo", "SetMapToCurrentZone", "GetCurrentMapAreaID", "GetCurrentMapContinent", "GetCurrentMapZone", "GetPlayerMapPosition", "GetCorpseMapPosition", "C_Map.GetBestMapForUnit", "C_Map.GetPlayerMapPosition", "C_DeathInfo.GetCorpseMapPosition"}) do
        local value = _G
        for part in string.gmatch(path, "[^.]+") do value = type(value) == "table" and value[part] or nil end
        inventory[path] = type(value) == "function"
    end
    WoWProbeDB.apiAvailability = inventory
    WoWProbeDB.eventRegistration = P.eventRegistration or {}
    for event, reason in pairs(P.registrationErrors or {}) do errorRecord("event." .. event, reason) end
    binding("health", "UnitHealth/UnitHealthMax")
    binding("target", "UnitExists/UnitName/UnitHealth/UnitHealthMax/UnitReaction")
    binding("facing", "GetPlayerFacing")
end

function P.BuildPacket()
    local p = {}; for i = 1, 128 do p[i] = 0 end
    local function put(offset, value) p[offset + 1] = value end
    local function u16(offset, value)
        value = value or U16; put(offset, value % 256); put(offset + 1, floor(value / 256) % 256)
    end
    local function u32(offset, value)
        value = value or 0; u16(offset, value % 65536); u16(offset + 2, floor(value / 65536))
    end
    local function norm(offset, value) u16(offset, value and floor(value * 65534 + 0.5)) end
    local failures, flags, caps, knownFlags = 0, 0, 0, 0
    local function failed(mask) if floor(failures / mask) % 2 == 0 then failures = failures + mask end end
    local function section(name, mask, fn)
        local ok, a, b, c = pcall(fn)
        if not ok then invalid(name, a); if mask then failed(mask) end; return nil end
        return a, b, c
    end
    local dead, deadOK = boolean("UnitIsDead", UnitIsDead, "player")
    local ghost, ghostOK = boolean("UnitIsGhost", UnitIsGhost, "player")
    local combat, combatOK = boolean("UnitAffectingCombat", UnitAffectingCombat, "player")
    if dead then flags = flags + 1 end; if ghost then flags = flags + 2 end; if combat then flags = flags + 4 end
    if deadOK then knownFlags = knownFlags + 1 else failed(1) end
    if ghostOK then knownFlags = knownFlags + 2 else failed(1) end
    if combatOK then knownFlags = knownFlags + 4 else failed(1) end
    local merchant, merchantOK = windowState("MerchantFrame", MerchantFrame, P.merchantKnown, P.merchantOpen)
    local loot, lootOK = windowState("LootFrame", LootFrame, P.lootKnown, P.lootOpen)
    if merchantOK then knownFlags = knownFlags + 32; if merchant then flags = flags + 32 end end
    if lootOK then knownFlags = knownFlags + 64; if loot then flags = flags + 64 end end
    local now, clockKnown = rawTime()
    local bagFullKnown = P.eventRegistration and P.eventRegistration.UI_ERROR_MESSAGE == true and type(ERR_INV_FULL) == "string" and clockKnown
    if bagFullKnown then
        knownFlags = knownFlags + 128
        if P.bagFullUntil and now < P.bagFullUntil then flags = flags + 128 end
    else invalid("bagFull", "UI_ERROR_MESSAGE observation, ERR_INV_FULL or plugin clock unavailable"); failed(4) end

    local health = section("health", 1, function() return readPercent("UnitHealth", UnitHealth, UnitHealthMax, "player") end)
    if health ~= nil then caps = caps + 4 else failed(1) end
    put(8, health or 255)
    local power, powerType = section("power", 1, function()
        local name, current, maximum
        if type(UnitPower) == "function" and type(UnitPowerMax) == "function" then
            name, current, maximum = "UnitPower", UnitPower, UnitPowerMax
            binding("power", "UnitPower/UnitPowerMax")
        else
            name, current, maximum = "UnitMana", UnitMana, UnitManaMax
            binding("power", "UnitMana/UnitManaMax")
        end
        local ok, kind = call("UnitPowerType", UnitPowerType, "player")
        if not ok or not integer(kind, 254) then kind = 255 end
        return readPercent(name, current, maximum, "player"), kind
    end)
    if power ~= nil then caps = caps + 8 else failed(1) end
    put(9, power or 255); put(11, powerType or 255)

    local target = section("target", 2, function()
        local result = { health = 255, reaction = 255, name = "" }
        local exists, known = boolean("UnitExists", UnitExists, "target")
        if not known then failed(2); return result end
        knownFlags = knownFlags + 8
        if not exists then knownFlags = knownFlags + 16; return result end
        flags = flags + 8
        local targetDead, knownDead = boolean("UnitIsDead", UnitIsDead, "target")
        if targetDead then flags = flags + 16 end
        if knownDead then knownFlags = knownFlags + 16 else failed(2) end
        local h = readPercent("UnitHealth", UnitHealth, UnitHealthMax, "target")
        if h ~= nil then result.health = h else failed(2) end
        local ok, name = call("UnitName", UnitName, "target")
        if ok and type(name) == "string" and #name > 0 then
            result.name = P.UTF8Truncate(name, 32)
            if #result.name == 0 then invalid("UnitName", "Name contains no valid UTF8 prefix"); failed(2) end
        else if ok then invalid("UnitName") end; failed(2) end
        local reactionOK, reaction = call("UnitReaction", UnitReaction, "player", "target")
        if reactionOK and integer(reaction, 8) then result.reaction = reaction end
        return result
    end) or { health = 255, reaction = 255, name = "" }
    -- Commit capability only after the whole target section returned safely.
    if #target.name > 0 then caps = caps + 32 end
    put(10, target.health); put(123, target.reaction)

    local facing = section("facing", nil, function()
        local ok, angle = call("GetPlayerFacing", GetPlayerFacing)
        if ok and finite(angle) and angle >= 0 and angle <= 2 * pi then return angle end
        if ok then invalid("GetPlayerFacing") end
    end)
    if facing ~= nil then caps = caps + 2 end
    norm(16, facing and facing / (2 * pi))
    local free = section("bags", 4, bags)
    if free ~= nil then caps = caps + 16 else failed(4) end; u16(18, free)
    local map = section("map", 8, function() return mapState(ghost) end) or { id = 0, mode = 0, failed = true }
    if map.x ~= nil then caps = caps + 1 end; if map.mode ~= 0 then caps = caps + 64 end
    if map.cx ~= nil then caps = caps + 128 end; if map.failed then failed(8) end
    norm(12, map.x); norm(14, map.y); u32(20, map.id); norm(24, map.cx); norm(26, map.cy); put(122, map.mode)

    local zone = section("zone", 8, function()
        local fn, name = GetRealZoneText, "GetRealZoneText"
        if type(fn) ~= "function" then fn, name = GetZoneText, "GetZoneText" end
        local ok, value = call(name, fn)
        if ok and type(value) == "string" then return P.UTF8Truncate(value, 32) end
        return ""
    end) or ""
    local function text(offset, value, max)
        value = P.UTF8Truncate(value, max)
        for i = 1, #value do put(offset + i - 1, string.byte(value, i)) end
        return #value
    end
    put(40, text(42, target.name, 32)); put(41, text(74, zone, 32))
    local client = WoWProbeDB.client
    text(106, client.version, 16); u32(28, client.build); u32(32, client.interface)
    u32(36, floor(rawTime() * 1000) % U32)
    put(0, 87); put(1, 80); put(2, 49); put(3, 1); u16(4, P.sequence)
    put(6, flags); put(7, caps); put(124, failures); put(125, knownFlags)
    local a, b = 0, 0
    for i = 1, 126 do a = (a + p[i]) % 255; b = (b + a) % 255 end
    put(126, a); put(127, b)
    WoWProbeDB.capabilities = { position = map.x ~= nil, facing = facing ~= nil, health = health ~= nil,
        power = power ~= nil, bags = free ~= nil, targetName = #target.name > 0, mapIdentity = map.mode ~= 0, corpse = map.cx ~= nil }
    WoWProbeDB.lastPacket = { sequence = P.sequence, flags = flags, knownFlags = knownFlags, capabilities = caps, readFailures = failures,
        mapId = map.id, mapMode = map.mode, health = health or 255, power = power or 255, powerType = powerType or 255,
        freeSlots = free or U16, targetName = target.name, zone = zone, pluginSeconds = rawTime() }
    return p
end

function P.ScalePanel()
    P.rendererReady = false
    if not P.panel then return false end
    WoWProbeDB.renderer = {valid = false}
    local width, height, source
    if type(GetPhysicalScreenSize) == "function" then
        local success, w, h = call("GetPhysicalScreenSize", GetPhysicalScreenSize)
        if success then width, height, source = w, h, "GetPhysicalScreenSize" end
    else
        local success, resolution = call("GetCVar(gxResolution)", GetCVar, "gxResolution")
        if success and type(resolution) == "string" then
            local w, h = string.match(resolution, "^%s*(%d+)%s*[xX]%s*(%d+)%s*$")
            width, height, source = tonumber(w), tonumber(h), "GetCVar(gxResolution)"
        end
    end
    local ok, parentScale = call("UIParent.GetEffectiveScale", UIParent and UIParent.GetEffectiveScale, UIParent)
    if ok and finite(parentScale) and parentScale > 0 and integer(width, 100000) and width > 0 and integer(height, 100000) and height > 0 then
        -- Blizzard PixelUtil: physicalPixels = uiUnits * effectiveScale / (768 / physicalHeight).
        local pixelScale = 768 / height
        P.panel:SetScale(pixelScale / parentScale)
        P.panel:ClearAllPoints(); P.panel:SetPoint("TOPLEFT", UIParent, "TOPLEFT", 8, -8)
        WoWProbeDB.renderer = {valid = true, effectiveScale = pixelScale, parentScale = parentScale,
            physicalWidth = width, physicalHeight = height, resolutionSource = source,
            cellPixels = 8, widthPixels = 256, heightPixels = 96}
        P.rendererReady = true
        return true
    end
    invalid("renderer", "Cannot establish physical screen dimensions and pixel scale")
    P.panel:Hide()
    return false
end
local function createPanel()
    local panel = CreateFrame("Frame", "WoWProbePanel", UIParent)
    P.panel = panel
    panel:SetWidth(256); panel:SetHeight(96); panel:SetFrameStrata("TOOLTIP"); panel:EnableMouse(false)
    P.cells = {}
    for i = 0, 383 do
        local t = panel:CreateTexture(nil, "OVERLAY")
        t:SetWidth(8); t:SetHeight(8)
        t:SetPoint("TOPLEFT", panel, "TOPLEFT", (i % 32) * 8, -floor(i / 32) * 8)
        P.cells[i + 1] = t
    end
    P.label = panel:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
    P.label:SetPoint("TOPLEFT", panel, "TOPLEFT", 0, -100)
    P.label:SetText("WoWProbe WP1 | read-only | /wowprobe info")
    local ready = P.ScalePanel()
    if WoWProbeDB.visible == false or not ready then panel:Hide() else panel:Show() end
end
function P.RenderPacket(packet)
    for i = 1, 128 do
        local byte = packet[i]
        for n = 0, 2 do
            local r = floor(byte / (2 ^ (n * 3))) % 2
            local g = floor(byte / (2 ^ (n * 3 + 1))) % 2
            local b = n == 2 and 0 or floor(byte / (2 ^ (n * 3 + 2))) % 2
            local cell = P.cells[(i - 1) * 3 + n + 1]
            if type(cell.SetColorTexture) == "function" then cell:SetColorTexture(r, g, b, 1)
            else cell:SetTexture(r, g, b, 1) end
        end
    end
end
local function update()
    if not P.rendererReady and not P.ScalePanel() then return false end
    local ok, packet = pcall(P.BuildPacket)
    if ok then
        local rendered, reason = pcall(P.RenderPacket, packet)
        if not rendered then errorRecord("renderer", reason); if P.panel then P.panel:Hide() end; return false end
        return true
    else
        -- A partially updated grid must never remain a seemingly live packet.
        errorRecord("packet", packet); if P.panel then P.panel:Hide() end
        return false
    end
end
function P.Tick(_, elapsed)
    if not P.initialized or not finite(elapsed) or elapsed < 0 then return end
    P.elapsed = P.elapsed + elapsed
    local steps = floor((P.elapsed + 0.00000001) / 0.2)
    if steps > 0 then
        P.elapsed = P.elapsed - steps * 0.2
        P.sequence = (P.sequence + steps) % 65536
        update()
    end
end
local function printLine(text)
    if DEFAULT_CHAT_FRAME and type(DEFAULT_CHAT_FRAME.AddMessage) == "function" then
        pcall(DEFAULT_CHAT_FRAME.AddMessage, DEFAULT_CHAT_FRAME, "|cffffffffWoWProbe:|r " .. text)
    end
end
function P.ResetDiagnostics()
    local visible = type(WoWProbeDB) == "table" and WoWProbeDB.visible ~= false
    WoWProbeDB = { schema = 1, addonVersion = "0.1.0", visible = visible, errors = {}, bindings = {}, capabilities = {} }
    readClient(); captureBindings(); captureAPIs(); if P.panel then P.ScalePanel() end
end
function P.Command(command)
    command = string.lower(string.match(command or "", "^%s*(%S*)") or "")
    if command == "hide" then WoWProbeDB.visible = false; if P.panel then P.panel:Hide() end
    elseif command == "show" then
        WoWProbeDB.visible = true; P.ScalePanel(); if update() and P.panel then P.panel:Show() end
    elseif command == "reset" then
        P.ResetDiagnostics(); update(); printLine("Diagnostic report reset; saved on logout or /reload.")
    elseif command == "info" then
        captureBindings(); update()
        local c, last = WoWProbeDB.client, WoWProbeDB.lastPacket or {}
        printLine(string.format("client %s build %d interface %d; packet WP1 seq %d; capabilities %d; known flags %d; failures %d; map mode %d.",
            c.version, c.build, c.interface, P.sequence, last.capabilities or 0, last.knownFlags or 0, last.readFailures or 0, last.mapMode or 0))
        printLine("SavedVariables: WoWProbeDB (build, detected APIs, key bindings, errors). Real game verification is still required.")
    else printLine("Commands: /wowprobe show | hide | info | reset. Read-only status; no actions or key bindings are changed.") end
end
function P.Initialize()
    if P.initialized then return end
    local visible = type(WoWProbeDB) ~= "table" or WoWProbeDB.visible ~= false
    local old = type(WoWProbeDB) == "table" and WoWProbeDB or {}
    local previous = { client = old.client, errors = old.errors, lastPacket = old.lastPacket,
        bindings = old.bindings, keyBindings = old.keyBindings, apiAvailability = old.apiAvailability }
    WoWProbeDB = old; WoWProbeDB.visible = visible
    WoWProbeDB.previousSession = previous
    WoWProbeDB.schema, WoWProbeDB.addonVersion = 1, "0.1.0"
    WoWProbeDB.errors, WoWProbeDB.bindings, WoWProbeDB.capabilities = {}, {}, {}
    readClient(); captureBindings(); captureAPIs()
    local ok, reason = pcall(createPanel)
    if not ok then errorRecord("renderer", reason); printLine("Panel could not be created; inspect WoWProbeDB.errors.") end
    P.initialized = true
    update()
end
function P.OnEvent(_, event, ...)
    if event == "ADDON_LOADED" then
        if (...) == "WoWProbe" then P.Initialize() end
        return
    end
    if not P.initialized then return end
    if event == "MERCHANT_SHOW" then P.merchantOpen, P.merchantKnown = true, true
    elseif event == "MERCHANT_CLOSED" then P.merchantOpen, P.merchantKnown = false, true
    elseif event == "LOOT_OPENED" then P.lootOpen, P.lootKnown = true, true
    elseif event == "LOOT_CLOSED" then P.lootOpen, P.lootKnown = false, true
    elseif event == "UI_ERROR_MESSAGE" then
        local first, second = ...
        local message = type(second) == "string" and second or first
        if type(ERR_INV_FULL) == "string" and message == ERR_INV_FULL then P.bagFullUntil = rawTime() + 5 end
    elseif event == "UPDATE_BINDINGS" then captureBindings()
    elseif event == "UI_SCALE_CHANGED" or event == "DISPLAY_SIZE_CHANGED" then P.ScalePanel()
    elseif event == "PLAYER_ENTERING_WORLD" then
        P.merchantOpen, P.lootOpen, P.bagFullUntil = false, false, nil
        P.merchantKnown, P.lootKnown = false, false
        P.ScalePanel()
    elseif event == "PLAYER_LOGOUT" then captureBindings(); update() end
end

SLASH_WOWPROBE1 = "/wowprobe"
SlashCmdList["WOWPROBE"] = P.Command
-- This driver remains visible when the data panel is hidden, preserving its heartbeat.
local driver = CreateFrame("Frame")
P.driver = driver
driver:SetScript("OnEvent", function(...) local ok, reason = pcall(P.OnEvent, ...); if not ok then errorRecord("event", reason) end end)
driver:SetScript("OnUpdate", P.Tick)
for _, event in ipairs({"ADDON_LOADED", "PLAYER_ENTERING_WORLD", "PLAYER_LOGOUT", "UPDATE_BINDINGS", "UI_SCALE_CHANGED", "DISPLAY_SIZE_CHANGED", "MERCHANT_SHOW", "MERCHANT_CLOSED", "LOOT_OPENED", "LOOT_CLOSED", "UI_ERROR_MESSAGE"}) do
    local ok, reason = pcall(driver.RegisterEvent, driver, event)
    P.eventRegistration = P.eventRegistration or {}; P.eventRegistration[event] = ok
    if not ok then P.registrationErrors = P.registrationErrors or {}; P.registrationErrors[event] = tostring(reason) end
end
