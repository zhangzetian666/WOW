-- Run from phase1 with Lua 5.1 (or lupa.lua51). WoW itself is the external boundary.
local tests, passed = {}, 0
local function test(name, fn) tests[#tests + 1] = { name, fn } end
local function eq(actual, expected, label)
    assert(actual == expected, (label or "value") .. ": expected " .. tostring(expected) .. ", got " .. tostring(actual))
end
local function has(value, mask) return math.floor(value / mask) % 2 == 1 end
local function word(p, offset) return p[offset + 1] + p[offset + 2] * 256 end
local function dword(p, offset) return word(p, offset) + word(p, offset + 2) * 65536 end
local function object()
    local o = { scripts = {}, shown = true, textures = {}, labels = {} }
    function o:SetScript(n, f) self.scripts[n] = f end
    function o:RegisterEvent(n) self.events = self.events or {}; self.events[n] = true end
    function o:SetSize(w, h) self.width, self.height = w, h end
    function o:SetWidth(w) self.width = w end
    function o:SetHeight(h) self.height = h end
    function o:SetPoint(...) self.point = {...} end
    function o:ClearAllPoints() end
    function o:SetScale(s) self.scale = s end
    function o:GetEffectiveScale() return self.scale or 0.75 end
    function o:SetFrameStrata(s) self.strata = s end
    function o:SetFrameLevel(n) self.level = n end
    function o:EnableMouse(v) self.mouse = v end
    function o:Show() self.shown = true end
    function o:Hide() self.shown = false end
    function o:IsShown() return self.shown end
    function o:SetText(s) self.text = s end
    function o:SetTexture(...) self.color = {...} end
    function o:SetColorTexture(...) self.color = {...} end
    function o:CreateTexture() local t = object(); self.textures[#self.textures+1] = t; return t end
    function o:CreateFontString() local t = object(); self.labels[#self.labels+1] = t; return t end
    return o
end
local function environment()
    local env = setmetatable({}, {__index = _G}); env._G = env
    env.timeNow = 12.345
    env.UIParent, env.WorldMapFrame = object(), object(); env.WorldMapFrame:Hide()
    env.MerchantFrame, env.LootFrame = object(), object(); env.MerchantFrame:Hide(); env.LootFrame:Hide()
    env.CreateFrame = function() return object() end
    env.DEFAULT_CHAT_FRAME = {AddMessage = function() end}
    env.SlashCmdList = {}
    env.GetTime = function() return env.timeNow end
    env.GetPhysicalScreenSize = function() return 1920,1080 end
    env.GetBuildInfo = function() return "3.3.5", "12340", "Jun 24 2010", 30300 end
    env.UnitHealth = function(unit) return unit == "player" and 0 or 25 end
    env.UnitHealthMax = function() return 100 end
    env.UnitPower = function() return 30 end
    env.UnitPowerMax = function() return 100 end
    env.UnitPowerType = function() return 1, "RAGE" end
    env.UnitExists = function() return true end
    env.UnitName = function() return "Target" end
    env.UnitIsDead = function() return false end
    env.UnitIsGhost = function() return false end
    env.UnitAffectingCombat = function() return false end
    env.UnitReaction = function() return 3 end
    env.GetPlayerFacing = function() return math.pi end
    env.GetRealZoneText = function() return "Zone" end
    env.GetContainerNumFreeSlots = function() return 0, 0 end
    env.GetBindingKey = function(action) return action == "MOVEFORWARD" and "W" or nil end
    env.GetCurrentMapAreaID = function() return 42 end
    env.SetMapToCurrentZone = function() env.mapRefreshes = (env.mapRefreshes or 0) + 1 end
    env.GetPlayerMapPosition = function() return 0.25, 0.75 end
    env.GetCorpseMapPosition = function() return 0, 0 end
    env.ERR_INV_FULL = "Inventory is full."
    return env
end
local function load(env)
    local chunk, reason = loadfile("addon/WoWProbe/WoWProbe.lua")
    assert(chunk, "WoWProbe implementation must exist: " .. tostring(reason))
    setfenv(chunk, env); chunk()
    assert(type(env.WoWProbe) == "table", "WoWProbe module missing")
    env.WoWProbe.Initialize()
    return env.WoWProbe
end

test("128-byte packet uses agreed offsets, real zero values and Fletcher checksum", function()
    local env = environment(); local wp = load(env); local p = wp.BuildPacket()
    eq(#p, 128); eq(p[1], 87); eq(p[2], 80); eq(p[3], 49); eq(p[4], 1)
    eq(p[9], 0, "dead/zero health"); eq(p[10], 30); eq(p[11], 25); eq(p[12], 1, "rage type")
    eq(word(p, 12), 16384); eq(word(p, 14), 49151); eq(word(p, 16), 32767)
    eq(word(p, 18), 0, "full bags are known zero"); eq(dword(p, 20), 42)
    eq(dword(p, 28), 12340); eq(dword(p, 32), 30300); eq(dword(p, 36), 12345)
    eq(p[123], 2); eq(p[124], 3); eq(p[125], 0); eq(p[126], 255, "all flag values known")
    local a, b = 0, 0
    for i = 1, 126 do a = (a + p[i]) % 255; b = (b + a) % 255 end
    eq(p[127], a); eq(p[128], b)
    WoWProbeTestFixture = p -- Shared fixture for the independent C# decoder check.
end)

test("missing health, resource, bags and map APIs produce unknown sentinels and failures", function()
    local env = environment()
    env.UnitHealth, env.UnitHealthMax, env.UnitPower, env.UnitPowerMax = nil, nil, nil, nil
    env.GetContainerNumFreeSlots, env.GetPlayerMapPosition, env.GetCurrentMapAreaID = nil, nil, nil
    local wp = load(env); local p = wp.BuildPacket()
    eq(p[9], 255); eq(p[10], 255); eq(p[11], 255)
    eq(word(p, 12), 65535); eq(word(p, 18), 65535); eq(dword(p, 20), 0)
    eq(has(p[8], 4), false); eq(has(p[8], 8), false); eq(has(p[8], 16), false)
    eq(has(p[8], 64), false); eq(p[125], 15)
    assert(next(env.WoWProbeDB.errors), "missing APIs must be recorded")
end)

test("legacy resource API keeps zero energy and type rather than fabricating mana", function()
    local env = environment(); env.UnitPower, env.UnitPowerMax = nil, nil
    env.UnitMana = function() return 0 end; env.UnitManaMax = function() return 100 end
    env.UnitPowerType = function() return 3, "ENERGY" end
    local wp = load(env); local p = wp.BuildPacket()
    eq(p[10], 0); eq(p[12], 3); eq(has(p[8], 8), true)
    eq(env.WoWProbeDB.bindings.power, "UnitMana/UnitManaMax")
end)

test("API exception invalidates its category while panel and other fields survive", function()
    local env = environment(); env.UnitHealth = function() error("restricted health") end
    local wp = load(env); local p = wp.BuildPacket()
    eq(p[9], 255); eq(p[11], 255); eq(p[10], 30)
    eq(has(p[125], 1), true); eq(has(p[125], 2), true)
    wp.RenderPacket(p); eq(#wp.panel.textures, 384)
    assert(env.WoWProbeDB.errors.UnitHealth, "API error must be diagnostic")
end)

test("unknown map never invents zero coordinate or a continent identity", function()
    local env = environment(); env.GetPlayerMapPosition = function() return 0, 0 end
    local wp = load(env); local p = wp.BuildPacket()
    eq(word(p, 12), 65535); eq(word(p, 14), 65535); eq(has(p[8], 1), false)
    eq(has(p[125], 8), true)
end)

test("legacy map stays untouched while the user's world map is open", function()
    local env = environment(); env.WorldMapFrame:Show()
    local wp = load(env); local p = wp.BuildPacket()
    eq(env.mapRefreshes, nil); eq(has(p[8], 1), false); eq(has(p[8], 64), false)
    eq(p[123], 0); eq(has(p[125], 8), true)
end)

test("modern map obtains identity without touching the user's world map", function()
    local env = environment(); env.WorldMapFrame:Show()
    env.C_Map = {
        GetBestMapForUnit = function(unit) eq(unit, "player"); return 2023 end,
        GetPlayerMapPosition = function(id, unit)
            eq(id, 2023); eq(unit, "player"); return {GetXY = function() return 0, 0.5 end}
        end
    }
    local wp = load(env); local p = wp.BuildPacket()
    eq(p[123], 1); eq(dword(p, 20), 2023); eq(word(p, 12), 0); eq(word(p, 14), 32767)
    eq(env.mapRefreshes, nil); eq(has(p[8], 1), true)
end)

test("legacy continent and zone identity is detected when area IDs are absent", function()
    local env = environment(); env.GetCurrentMapAreaID = nil
    env.GetCurrentMapContinent = function() return 2 end; env.GetCurrentMapZone = function() return 7 end
    local wp = load(env); local p = wp.BuildPacket()
    eq(p[123], 3); eq(dword(p, 20), 20007); eq(has(p[8], 64), true)
end)

test("bag scan distinguishes empty slots from inaccessible item information", function()
    local env = environment(); env.GetContainerNumFreeSlots = nil
    env.GetContainerNumSlots = function(bag) return bag == 0 and 2 or 0 end
    env.GetContainerItemInfo = function(bag, slot) return slot == 1 and "texture" or nil end
    local wp = load(env); local p = wp.BuildPacket(); eq(word(p, 18), 1)
    env.GetContainerItemInfo = function() error("bag inaccessible") end
    p = wp.BuildPacket(); eq(word(p, 18), 65535); eq(has(p[125], 4), true)
end)

test("UTF8 fields truncate on a character boundary and are zero padded", function()
    local env = environment(); env.UnitName = function() return string.rep("a", 30) .. "界X" end
    env.GetRealZoneText = function() return string.rep("区", 11) end
    local wp = load(env); local p = wp.BuildPacket()
    eq(p[41], 30); eq(p[42], 30)
    eq(p[72], 97); eq(p[73], 0); eq(p[74], 0)
    eq(p[105], 0); eq(p[106], 0)
    eq(wp.UTF8Truncate("a界", 3), "a"); eq(wp.UTF8Truncate("a界", 4), "a界")
end)

test("merchant, loot and bag-full flags are event driven and expire by time", function()
    local env = environment(); local wp = load(env)
    wp.OnEvent(nil, "MERCHANT_SHOW"); wp.OnEvent(nil, "LOOT_OPENED")
    wp.OnEvent(nil, "UI_ERROR_MESSAGE", 123, env.ERR_INV_FULL)
    local p = wp.BuildPacket(); eq(has(p[7], 32), true); eq(has(p[7], 64), true); eq(has(p[7], 128), true)
    env.timeNow = env.timeNow + 5.1; wp.OnEvent(nil, "MERCHANT_CLOSED"); wp.OnEvent(nil, "LOOT_CLOSED")
    p = wp.BuildPacket(); eq(has(p[7], 32), false); eq(has(p[7], 64), false); eq(has(p[7], 128), false)
    wp.OnEvent(nil, "UI_ERROR_MESSAGE", env.ERR_INV_FULL)
    eq(has(wp.BuildPacket()[7], 128), true, "legacy UI_ERROR_MESSAGE signature")
end)

test("corpse coordinates require ghost state and a valid pair", function()
    local env = environment(); env.GetCorpseMapPosition = function() return 0.6, 0.4 end
    local wp = load(env); local p = wp.BuildPacket(); eq(word(p, 24), 65535)
    env.UnitIsGhost = function() return true end
    p = wp.BuildPacket(); eq(has(p[7], 2), true); eq(has(p[8], 128), true)
    eq(word(p, 24), 39320); eq(word(p, 26), 26214)
end)

test("200ms heartbeat advances when the panel is hidden and wraps sequence", function()
    local env = environment(); local wp = load(env)
    wp.sequence = 65535; wp.Command("hide"); wp.Tick(nil, 0.199); eq(wp.sequence, 65535)
    wp.Tick(nil, 0.0011); eq(wp.sequence, 0)
    wp.Tick(nil, 0.6); eq(wp.sequence, 3)
    wp.Command("show"); eq(wp.panel.shown, true)
end)

test("binary RGB renderer places 384 exact physical cells with labels below", function()
    local env = environment(); local wp = load(env)
    local p = {}; for i=1,128 do p[i] = 0 end; p[1], p[2] = 129, 255
    wp.RenderPacket(p)
    eq(wp.panel.scale, (768 / 1080) / 0.75); eq(wp.panel.width, 256); eq(wp.panel.height, 96)
    eq(#wp.panel.textures, 384)
    local c = wp.panel.textures[1].color; eq(c[1], 1); eq(c[2], 0); eq(c[3], 0); eq(c[4], 1)
    c = wp.panel.textures[3].color; eq(c[1], 0); eq(c[2], 1); eq(c[3], 0)
    c = wp.panel.textures[6].color; eq(c[1], 1); eq(c[2], 1); eq(c[3], 0)
    local last = wp.panel.textures[384]; eq(last.width, 8); eq(last.height, 8)
    eq(last.point[4], 248); eq(last.point[5], -88)
    assert(wp.panel.labels[1].point[5] <= -100, "label must not overlap the grid")
end)

test("diagnostics save build, capabilities, bindings and API errors; reset preserves visibility", function()
    local env = environment(); local wp = load(env); wp.BuildPacket()
    eq(env.WoWProbeDB.client.build, 12340); eq(env.WoWProbeDB.client.interface, 30300)
    eq(env.WoWProbeDB.bindings.power, "UnitPower/UnitPowerMax")
    assert(type(env.WoWProbeDB.capabilities) == "table")
    wp.Command("hide"); wp.Command("reset")
    eq(wp.panel.shown, false); eq(env.WoWProbeDB.visible, false)
    wp.Command("info"); assert(env.WoWProbeDB.lastPacket, "diagnostic snapshot must be saved")
end)

test("zero maximum resources are unknown and never masquerade as zero percent", function()
    local env = environment(); env.UnitHealthMax = function() return 0 end
    env.UnitPowerMax = function() return 0 end
    local wp = load(env); local p = wp.BuildPacket()
    eq(p[9], 255); eq(p[10], 255); eq(p[11], 255)
    eq(has(p[8], 4), false); eq(has(p[8], 8), false); eq(has(p[125], 1), true)
end)

test("modern bag API includes reagent bag and reports a zero free-slot result", function()
    local env = environment(); env.Enum = {BagIndex = {ReagentBag = 5}}
    env.C_Container = {GetContainerNumFreeSlots = function(bag) return bag == 5 and 3 or 0, 0 end}
    local wp = load(env); local p = wp.BuildPacket(); eq(word(p, 18), 3)
    eq(env.WoWProbeDB.bindings.bags, "C_Container.GetContainerNumFreeSlots")
end)

test("failed rendering cannot be shown as a live data panel by the show command", function()
    local env = environment(); local wp = load(env)
    wp.cells[2].SetColorTexture = function() error("render failed") end
    wp.Command("show"); eq(wp.panel.shown, false)
    assert(env.WoWProbeDB.errors.renderer, "render failure must be saved")
end)

test("diagnostics inventory unavailable APIs and retain previous session errors", function()
    local env = environment()
    env.WoWProbeDB = { visible = true, errors = { OldFailure = {count = 1, message = "previous session"} } }
    local wp = load(env)
    eq(env.WoWProbeDB.apiAvailability.UnitPower, true)
    eq(env.WoWProbeDB.apiAvailability["C_Map.GetBestMapForUnit"], false)
    assert(env.WoWProbeDB.previousSession.errors.OldFailure)
    assert(type(env.WoWProbeDB.eventRegistration) == "table")
end)

test("text fields stop before NUL and malformed UTF8 so the strict reader can decode them", function()
    local env = environment(); env.UnitName = function() return "Alpha\000Beta" end
    env.GetRealZoneText = function() return "Zone" .. string.char(255) .. "bad" end
    local wp = load(env); local p = wp.BuildPacket()
    eq(p[41], 5); eq(p[42], 4); eq(p[48], 0)
    eq(wp.UTF8Truncate(string.char(237,160,128),32), "", "UTF8 surrogate is rejected")
end)

test("partial target read failure keeps name capability consistent with encoded name", function()
    local env = environment()
    -- Ordinary Lua cannot represent WoW's restricted numbers. Fail the numeric
    -- operation on this reaction value to exercise that boundary after name read.
    env.math = setmetatable({floor = function(n)
        if n == 7 then error("restricted numeric operation") end
        return math.floor(n)
    end}, {__index = math})
    env.UnitReaction = function() return 7 end
    local wp = load(env); local p = wp.BuildPacket()
    eq(has(p[8], 32), false); eq(p[41], 0); eq(p[124], 255); eq(has(p[125], 2), true)
end)

test("missing state APIs encode unknown flags rather than a healthy false", function()
    local env = environment()
    env.UnitIsDead, env.UnitIsGhost, env.UnitAffectingCombat, env.UnitExists = nil, nil, nil, nil
    local wp = load(env); local p = wp.BuildPacket()
    for _, mask in ipairs({1,2,4,8,16}) do
        eq(has(p[7], mask), false, "unknown state value " .. mask)
        eq(has(p[126], mask), false, "unknown validity bit " .. mask)
    end
    eq(has(p[125],1), true); eq(has(p[125],2), true)
    eq(env.WoWProbeDB.lastPacket.knownFlags, p[126])
end)

test("throwing combat and target-dead APIs invalidate only their boolean state", function()
    local env = environment()
    env.UnitAffectingCombat = function() error("combat restricted") end
    env.UnitIsDead = function(unit) if unit == "target" then error("target death restricted") end; return false end
    local wp = load(env); local p = wp.BuildPacket()
    eq(has(p[126],4), false); eq(has(p[126],16), false)
    eq(has(p[126],1), true); eq(has(p[126],8), true)
    eq(has(p[125],1), true); eq(has(p[125],2), true)
end)

test("known absent target also makes target-dead known false", function()
    local env = environment(); env.UnitExists = function() return false end
    local wp = load(env); local p = wp.BuildPacket()
    eq(has(p[126],8), true); eq(has(p[126],16), true)
    eq(has(p[7],8), false); eq(has(p[7],16), false); eq(has(p[125],2), false)
end)

test("merchant and loot state remain unknown until a frame or an event establishes them", function()
    local env = environment(); env.MerchantFrame, env.LootFrame = nil, nil
    local wp = load(env); local p = wp.BuildPacket()
    eq(has(p[126],32), false); eq(has(p[126],64), false)
    eq(has(p[7],32), false); eq(has(p[7],64), false)
    wp.OnEvent(nil,"MERCHANT_SHOW"); wp.OnEvent(nil,"LOOT_OPENED")
    p=wp.BuildPacket()
    eq(has(p[126],32), true); eq(has(p[126],64), true)
    eq(has(p[7],32), true); eq(has(p[7],64), true)
    wp.OnEvent(nil,"MERCHANT_CLOSED"); wp.OnEvent(nil,"LOOT_CLOSED")
    p=wp.BuildPacket()
    eq(has(p[126],32), true); eq(has(p[126],64), true)
    eq(has(p[7],32), false); eq(has(p[7],64), false)
    wp.OnEvent(nil,"PLAYER_ENTERING_WORLD")
    p=wp.BuildPacket(); eq(has(p[126],32),false); eq(has(p[126],64),false)
end)

test("bag-full validity requires working error observation and follows time-based expiration", function()
    local env=environment(); local wp=load(env)
    local p=wp.BuildPacket(); eq(has(p[126],128),true); eq(has(p[7],128),false)
    wp.OnEvent(nil,"UI_ERROR_MESSAGE",env.ERR_INV_FULL)
    p=wp.BuildPacket(); eq(has(p[126],128),true); eq(has(p[7],128),true)
    env.timeNow=env.timeNow+5.1; p=wp.BuildPacket()
    eq(has(p[126],128),true); eq(has(p[7],128),false)
    wp.eventRegistration.UI_ERROR_MESSAGE=false
    p=wp.BuildPacket(); eq(has(p[126],128),false); eq(has(p[7],128),false); eq(has(p[125],4),true)
    wp.eventRegistration.UI_ERROR_MESSAGE=true; env.GetTime=nil
    p=wp.BuildPacket(); eq(has(p[126],128),false,"recent error requires a usable clock")
end)

test("renderer accounts for physical height at 1080p and 1440p across UI scales", function()
    for _,height in ipairs({1080,1440}) do
        for _,uiScale in ipairs({1,0.75}) do
            local env=environment()
            env.GetPhysicalScreenSize=function() return 2560,height end
            env.UIParent.GetEffectiveScale=function() return uiScale end
            local wp=load(env)
            local pixels=8*wp.panel.scale*uiScale/(768/height)
            assert(math.abs(pixels-8)<0.0000001,"cell must occupy eight physical pixels")
            eq(wp.rendererReady,true); eq(env.WoWProbeDB.renderer.physicalHeight,height)
        end
    end
end)

test("legacy resolution is detected through gxResolution when physical screen API is missing", function()
    local env=environment(); env.GetPhysicalScreenSize=nil
    env.GetCVar=function(name) eq(name,"gxResolution"); return "1920x1080" end
    local wp=load(env)
    eq(wp.rendererReady,true); eq(wp.panel.scale,(768/1080)/0.75)
    eq(env.WoWProbeDB.renderer.resolutionSource,"GetCVar(gxResolution)")
end)

test("missing or invalid dimensions hide the panel until valid physical geometry is obtained", function()
    local env=environment(); env.GetPhysicalScreenSize=function() return 1920,0 end
    local wp=load(env)
    eq(wp.rendererReady,false); eq(wp.panel.shown,false)
    eq(env.WoWProbeDB.renderer.valid,false)
    wp.Command("show"); eq(wp.panel.shown,false)
    assert(env.WoWProbeDB.errors.renderer)
    env.GetPhysicalScreenSize=function() return 1920,1080 end
    wp.Command("show"); eq(wp.rendererReady,true); eq(wp.panel.shown,true)
    env.GetPhysicalScreenSize=nil; env.GetCVar=nil
    wp.ScalePanel(); eq(wp.rendererReady,false); eq(wp.panel.shown,false)
end)

for _, t in ipairs(tests) do
    local ok, message = pcall(t[2])
    if ok then passed = passed + 1; print("PASS " .. t[1])
    else print("FAIL " .. t[1] .. "\n  " .. tostring(message)) end
end
print(string.format("AddOn tests: %d/%d passed (%s)", passed, #tests, _VERSION))
assert(passed == #tests, "AddOn test failures: " .. (#tests - passed))
