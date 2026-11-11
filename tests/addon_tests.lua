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
    function o:GetTexture() return self.color and self.color[1] end
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
    local operations=loadfile("addon/WoWProbe/Operations.lua")
    if operations then setfenv(operations,env);operations() end
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

-- WC3 fixtures exercise the passive APIs independently of the WP1 compatibility fixture.
local function combatEnvironment()
    local env=environment()
    env.UnitGUID=function() return "Creature-0-1-2-3-456-000001" end
    env.UnitCanAttack=function() return true end
    env.UnitIsPlayer=function() return false end
    env.ActionButton1={action=13}; env.ActionButton2={action=14}
    env.ActionButton3={action=15}; env.ActionButton4={action=16}
    env.HasAction=function() return true end
    env.GetActionCooldown=function(slot) return slot==14 and 12 or 0,slot==14 and 2 or 0,1 end
    env.IsUsableAction=function(slot) return slot~=15,false end
    env.IsActionInRange=function(slot,unit) eq(unit,"target"); return slot==14 and 0 or 1 end
    env.UnitCastingInfo=function() return nil end; env.UnitChannelInfo=function() return nil end
    env.CanLootUnit=function(guid) eq(guid,env.UnitGUID()); return false,false end
    return env
end
test("WC3 keeps unknown APIs unknown and never requests legacy target coordinates",function()
    local env=environment(); env.GetPlayerMapPosition=function(unit) eq(unit,"player"); return 0.2,0.4 end
    local wp=load(env); assert(type(wp.BuildCombatPacket)=="function","WC3 builder missing")
    local p=wp.BuildCombatPacket(wp.BuildPacket())
    eq(#p,128); eq(p[1],87);eq(p[2],67);eq(p[3],51);eq(p[4],1)
    eq(p[7],0);eq(p[8],0);eq(word(p,12),65535);eq(word(p,14),65535)
    eq(p[18],0);eq(p[19],0);eq(p[20],0)
end)
test("WC3 binds selected range and action masks to actual current page slots",function()
    local env=combatEnvironment(); local wp=load(env);wp.Command("range 2")
    local p=wp.BuildCombatPacket(wp.BuildPacket());eq(p[17],2)
    eq(has(p[7],4),true);eq(has(p[8],4),false,"known out of range")
    eq(p[18],15);eq(p[19],13);eq(p[20],11)
    env.GetActionCooldown=nil;p=wp.BuildCombatPacket(wp.BuildPacket())
    eq(has(p[7],128),false);eq(p[18],0);eq(p[19],0)
    env.IsActionInRange=function() return nil end
    p=wp.BuildCombatPacket(wp.BuildPacket());eq(has(p[7],4),false);eq(has(p[8],4),false)
end)
test("WC3 distinguishes known false attackability from missing and throwing APIs",function()
    local env=combatEnvironment();env.UnitCanAttack=function() return false end
    local wp=load(env);local p=wp.BuildCombatPacket(wp.BuildPacket())
    eq(has(p[7],2),true);eq(has(p[8],1),false)
    env.UnitIsPlayer=function() error("restricted") end
    p=wp.BuildCombatPacket(wp.BuildPacket());eq(has(p[7],2),false);eq(has(p[8],1),false)
    env.UnitCanAttack=function() return "invalid boolean" end;env.UnitIsPlayer=function() return false end
    p=wp.BuildCombatPacket(wp.BuildPacket());eq(has(p[7],2),false)
end)
test("WC3 target coordinates require matching modern map identity and actual target API result",function()
    local env=combatEnvironment();local same=true;local supported=true
    env.C_Map={GetBestMapForUnit=function(unit) return unit=="target" and (same and 42 or 43) or 42 end,
        GetPlayerMapPosition=function(id,unit) if unit=="target" and not supported then return nil end;return {x=0.25,y=0.75} end}
    local wp=load(env);local p=wp.BuildCombatPacket(wp.BuildPacket())
    eq(has(p[7],8),true);eq(word(p,12),16384);eq(word(p,14),49151)
    same=false;p=wp.BuildCombatPacket(wp.BuildPacket());eq(has(p[7],8),false)
    same=true;supported=false;p=wp.BuildCombatPacket(wp.BuildPacket());eq(has(p[7],8),false)
end)
test("WC3 hashes complete UTF8 names and GUID bytes without display truncation",function()
    local env=combatEnvironment();env.UnitName=function() return string.rep("兽",20).."甲" end
    local wp=load(env);local core=wp.BuildPacket();local p=wp.BuildCombatPacket(core)
    local function hash(s) local n=5381;for i=1,#s do n=(n*33+string.byte(s,i))%4294967296 end;return n==0 and 1 or n end
    eq(dword(p,8),hash(env.UnitGUID()));eq(dword(p,44),hash(env.UnitName()))
    eq(has(p[57],4),true);assert(core[41]<#env.UnitName())
    env.UnitName=function() return "bad"..string.char(255) end
    p=wp.BuildCombatPacket(wp.BuildPacket());eq(has(p[57],4),false);eq(dword(p,44),0)
end)
test("WC3 event counters capture instant loot and only identified skinning success",function()
    local env=combatEnvironment();env.GetSpellInfo=function(id) if id==8613 then return "剥皮" end end
    local wp=load(env)
    wp.OnEvent(nil,"LOOT_OPENED");wp.OnEvent(nil,"LOOT_CLOSED")
    wp.OnEvent(nil,"UNIT_SPELLCAST_SUCCEEDED","target","Cast-1",8613)
    wp.OnEvent(nil,"UNIT_SPELLCAST_SUCCEEDED","player","Cast-2",133)
    wp.OnEvent(nil,"UNIT_SPELLCAST_SUCCEEDED","player","Cast-3",8613)
    wp.OnEvent(nil,"UNIT_SPELLCAST_SUCCEEDED","player","剥皮","",123)
    wp.OnEvent(nil,"UNIT_SPELLCAST_FAILED","player","Cast-4",133)
    local p=wp.BuildCombatPacket(wp.BuildPacket())
    eq(dword(p,24),1);eq(dword(p,28),1);eq(dword(p,32),3);eq(dword(p,36),1);eq(dword(p,40),2)
    eq(has(p[57],2),true);eq(has(p[57],8),true);eq(has(p[57],16),true)
    wp.eventRegistration.UNIT_SPELLCAST_FAILED=false
    p=wp.BuildCombatPacket(wp.BuildPacket());eq(has(p[57],16),false);eq(dword(p,32),0);eq(dword(p,36),0)
end)
test("WC3 casting supports legacy and modern signatures without inferring completion",function()
    local env=combatEnvironment();env.UnitCastingInfo=function() return "Fire","Fire","icon",12000,14000,false,"id" end
    local wp=load(env);local p=wp.BuildCombatPacket(wp.BuildPacket())
    eq(has(p[7],64),true);eq(has(p[8],32),true);eq(dword(p,20),14000)
    env.UnitCastingInfo=function() return "Fire","Rank 1","Fire","icon",12000,15000,false end
    p=wp.BuildCombatPacket(wp.BuildPacket());eq(dword(p,20),15000)
    env.UnitCastingInfo=function() return nil end
    p=wp.BuildCombatPacket(wp.BuildPacket());eq(dword(p,20),0);eq(has(p[8],32),false)
    env.UnitChannelInfo=nil;p=wp.BuildCombatPacket(wp.BuildPacket());eq(has(p[7],64),false)
end)
test("WC3 tooltip skin signal uses localized marker and exposes unknown when unavailable",function()
    local env=combatEnvironment();env.UNIT_SKINNABLE="可剥皮";env.UnitIsDead=function(unit) return unit=="target" end
    local original=env.CreateFrame;local marker="可剥皮"
    env.CreateFrame=function(kind,name,parent,template)
        local o=original();if kind=="GameTooltip" then
            function o:SetOwner() end;function o:ClearLines() end;function o:SetUnit(unit) eq(unit,"target");return true end
            function o:NumLines() return 1 end;function o:GetName() return name end
            env[name.."TextLeft1"]={GetText=function() return marker end}
        end;return o
    end
    local wp=load(env);local p=wp.BuildCombatPacket(wp.BuildPacket())
    eq(has(p[7],32),true);eq(has(p[8],16),true)
    marker="普通尸体";p=wp.BuildCombatPacket(wp.BuildPacket());eq(has(p[7],32),true);eq(has(p[8],16),false)
    env.UNIT_SKINNABLE=nil;p=wp.BuildCombatPacket(wp.BuildPacket());eq(has(p[7],32),false)
end)
test("WC3 second physical grid shares heartbeat sequence and safe portrait geometry",function()
    local env=combatEnvironment()
    env.SetPortraitTexture=function(texture,unit) eq(unit,"target");texture:SetTexture("portrait-target") end
    env.GetSpellInfo=function(id) if id==8613 then return "Skinning" end end
    local wp=load(env);wp.Tick(nil,0.2)
    wp.OnEvent(nil,"LOOT_OPENED");wp.OnEvent(nil,"LOOT_CLOSED")
    wp.OnEvent(nil,"UNIT_SPELLCAST_SUCCEEDED","player","Cast-1",8613)
    wp.OnEvent(nil,"UNIT_SPELLCAST_FAILED","player","Cast-2",133)
    local a,b=wp.BuildPackets();eq(word(a,4),word(b,4));eq(word(a,4),1)
    eq(wp.combatPanel.width,256);eq(wp.combatPanel.height,96);eq(#wp.combatCells,384)
    eq(wp.combatPanel.point[4],280);eq(wp.panel.point[4],8)
    eq(wp.combatPanel.scale,wp.panel.scale)
    eq(has(b[57],1),true);eq(word(b,48),832);eq(word(b,50),8);eq(word(b,52),64);eq(word(b,54),64)
    assert(word(b,48)>=8+272+256,"portrait reference must not intersect either data grid")
    eq(dword(b,24),1);eq(dword(b,28),1);eq(dword(b,32),1);eq(dword(b,36),1);eq(dword(b,40),1)
    WoWProbeCombatCoreFixture=a;WoWProbeCombatTestFixture=b
end)
test("WC3 never exposes original target portrait beneath the grids or stale reference texture",function()
    local env=combatEnvironment()
    env.TargetFramePortrait={GetRect=function() return 280,720,64,64 end,GetEffectiveScale=function() return 1 end,IsShown=function() return true end}
    local wp=load(env);local p=wp.BuildCombatPacket(wp.BuildPacket());eq(has(p[57],1),false)
    env.SetPortraitTexture=function(texture) texture:SetTexture("portrait-target") end
    p=wp.BuildCombatPacket(wp.BuildPacket());eq(has(p[57],1),true)
    env.SetPortraitTexture=function() end
    p=wp.BuildCombatPacket(wp.BuildPacket());eq(has(p[57],1),false,"missing texture must not reuse old target")
end)
test("WC3 missing loot results remain unknown and do not imply an empty corpse",function()
    local env=combatEnvironment();env.CanLootUnit=function() return nil,nil end
    local wp=load(env);local p=wp.BuildCombatPacket(wp.BuildPacket())
    eq(has(p[7],16),false);eq(has(p[8],8),false)
    env.CanLootUnit=function() return true,true end
    p=wp.BuildCombatPacket(wp.BuildPacket());eq(has(p[7],16),true);eq(has(p[8],8),true)
    env.CanLootUnit=function() return true,false end
    p=wp.BuildCombatPacket(wp.BuildPacket());eq(has(p[7],16),true);eq(has(p[8],8),false)
end)
test("WC3 optional rendering failure preserves live WP1 and hides the extension",function()
    local env=combatEnvironment();local wp=load(env)
    wp.combatCells[1].SetColorTexture=function() error("WC3 draw failed") end
    wp.Tick(nil,0.2);eq(wp.panel.shown,true);eq(wp.combatPanel.shown,false)
    wp.combatPanel:Show()
    wp.cells[1].SetColorTexture=function() error("WP1 draw failed") end
    wp.Command("show");eq(wp.panel.shown,false);eq(wp.combatPanel.shown,false)
end)
test("WC3 physical displacement stays 272 pixels across UI scales and resolutions",function()
    for _,height in ipairs({1080,1440}) do
        for _,scale in ipairs({1,0.75}) do
            local env=combatEnvironment();env.GetPhysicalScreenSize=function() return 2560,height end
            env.UIParent.GetEffectiveScale=function() return scale end
            local wp=load(env)
            eq((wp.combatPanel.point[4]-wp.panel.point[4])*wp.panel.scale*scale/(768/height),272)
            wp.Command("hide");eq(wp.combatPanel.shown,false)
        end
    end
end)
test("WC3 retains range configuration after diagnostic reset and inventories passive APIs",function()
    local env=combatEnvironment();local wp=load(env);wp.Command("range 3");wp.Command("reset")
    eq(env.WoWProbeDB.rangeSlot,3)
    eq(env.WoWProbeDB.apiAvailability.UnitGUID,true);eq(env.WoWProbeDB.apiAvailability.UnitCanAttack,true)
    eq(env.WoWProbeDB.apiAvailability.UnitChannelInfo,true);eq(env.WoWProbeDB.apiAvailability.IsActionInRange,true)
end)

local function operationsEnvironment()
    local env=combatEnvironment();env.GetCorpseRecoveryDelay=function() return 0 end;env.GetCursorInfo=function() return nil end
    env.UnitName=function(unit) return unit=="npc" and "Vendor" or "Target" end
    env.UnitGUID=function(unit) return unit=="npc" and "Creature-NPC-999" or "Creature-0-1-2-3-456-000001" end
    env.UnitHealth=function(unit) return unit=="player" and 50 or 25 end
    env.StaticPopupDialogs={DEATH={},RECOVER_CORPSE={}};env.STATICPOPUP_NUMDIALOGS=4
    local function physical(o,x,y,w,h)
        function o:GetRect() local scale=.75/(768/1080);return x/scale,(1080-y-h)/scale,w/scale,h/scale end
        function o:GetEffectiveScale() return .75 end
        function o:IsVisible() return self.shown end
        function o:IsEnabled() return self.enabled~=false end
        return o
    end
    for i=1,4 do env["StaticPopup"..i]=object();env["StaticPopup"..i]:Hide();env["StaticPopup"..i.."Button1"]=physical(object(),200,300,150,40) end
    env.MerchantFrame:Show();env.MerchantFrame.selectedTab=1
    env.GetContainerNumSlots=function(bag) return bag==0 and 2 or bag==1 and 1 or 0 end
    env.GetContainerItemInfo=function(bag,slot) if bag==0 and slot==2 then return nil end;return 100,2,false,0,false,false,"|Hitem:1001:0:0:0:0:0:0:0|h[Scrap]|h" end
    env.GetContainerItemQuestInfo=function() return false,nil,false end
    env.GetItemInfo=function() return "Scrap","|Hitem:1001:0:0:0:0:0:0:0|h[Scrap]|h",0,1,1,"Miscellaneous","Junk",20,"",100,7 end
    env.GetItemClassInfo=function(id) eq(id,0);return "Consumable" end
    local container=object();function container:GetID() return 0 end;function container:GetName() return "ContainerFrame1" end;env.ContainerFrame1=container
    local b1=physical(object(),1600,800,35,35);function b1:GetID() return 2 end;env.ContainerFrame1Item1=b1
    local b2=physical(object(),1700,800,35,35);function b2:GetID() return 1 end;env.ContainerFrame1Item2=b2
    return env
end
test("WO4 unknown APIs remain unknown without breaking WP1 or WC3",function()
    local env=combatEnvironment();local wp=load(env);local a,b,c=wp.BuildPackets();eq(#a,128);eq(#b,128);eq(#c,128);eq(c[1],87);eq(c[2],79);eq(c[3],52)
    eq(has(word(c,6),16),false);eq(has(word(c,6),1),false);eq(dword(c,26),4294967295)
end)
test("WO4 uses actual merchant npc identity instead of selected target",function()
    local env=operationsEnvironment();local wp=load(env);local p=wp.BuildOperationsPacket(wp.BuildPacket())
    eq(has(word(p,6),8),true);eq(has(word(p,8),16),true);eq(dword(p,88),wp.HashUTF8("Creature-NPC-999"));eq(dword(p,92),wp.HashUTF8("Vendor"))
    env.UnitGUID=function(unit) if unit=="target" then return "Selected-target" end end
    p=wp.BuildOperationsPacket(wp.BuildPacket());eq(has(word(p,6),8),false);eq(dword(p,88),0)
end)
test("WO4 release resurrection cooldown and incoming resurrection never invoke actions",function()
    local env=operationsEnvironment();local calls=0;env.RepopMe=function() calls=calls+1 end;env.RetrieveCorpse=function() calls=calls+1 end
    env.StaticPopup1:Show();env.StaticPopup1.which="DEATH";local wp=load(env);local p=wp.BuildOperationsPacket(wp.BuildPacket())
    eq(has(word(p,6),1),true);eq(has(word(p,8),1),true);eq(has(word(p,8),2),true);eq(word(p,10),200);eq(word(p,12),300)
    env.StaticPopup1.which="RECOVER_CORPSE";env.GetCorpseRecoveryDelay=function() return 1.25 end
    p=wp.BuildOperationsPacket(wp.BuildPacket());eq(has(word(p,8),4),true);eq(dword(p,26),1250)
    env.StaticPopup1.which="RESURRECT_NO_SICKNESS";p=wp.BuildOperationsPacket(wp.BuildPacket());eq(has(word(p,8),128),true);eq(has(word(p,8),4),false);eq(calls,0)
end)
test("WO4 confirmation unknown frames and hidden buttons are conservative",function()
    local env=operationsEnvironment();local wp=load(env);env.StaticPopup1:Show();env.StaticPopup1.which="CONFIRM_SELL_ITEM"
    local p=wp.BuildOperationsPacket(wp.BuildPacket());eq(has(word(p,6),64),true);eq(has(word(p,8),64),true)
    env.StaticPopup2=nil;p=wp.BuildOperationsPacket(wp.BuildPacket());eq(has(word(p,6),64),false);eq(has(word(p,6),1),false)
end)
test("WO4 inventories actual bag capacities and every occupied slot",function()
    local env=operationsEnvironment();local wp=load(env);wp.operationsState.page=0
    local p=wp.BuildOperationsPacket(wp.BuildPacket());eq(has(word(p,6),16),true);eq(p[97],2);eq(p[98],1);eq(p[102],1);eq(p[107],1);eq(word(p,60),3)
    eq(p[63],0);eq(p[64],1);eq(dword(p,64),1001);eq(word(p,68),2);eq(p[71],0);eq(has(p[72],8),true);eq(has(p[72],32),true);eq(has(p[72],64),true);eq(dword(p,72),7)
end)
test("WO4 inventory pagination handles empty slots and cycle completion",function()
    local env=operationsEnvironment();local wp=load(env);wp.operationsState.page=0;wp.operationsState.cycle=0
    local a=wp.BuildOperationsPacket(wp.BuildPacket());local b=wp.BuildOperationsPacket(wp.BuildPacket());local c=wp.BuildOperationsPacket(wp.BuildPacket())
    eq(word(a,58),0);eq(word(b,58),1);eq(has(b[72],16),true);eq(dword(b,64),0);eq(dword(b,72),0);eq(word(c,58),2);eq(dword(c,54),1)
end)
test("WO4 metadata quest and consumable knowledge cannot be inferred from missing APIs",function()
    local env=operationsEnvironment();local wp=load(env);env.GetItemInfo=nil;wp.operationsState.page=0
    local p=wp.BuildOperationsPacket(wp.BuildPacket());eq(has(word(p,6),16),true);eq(has(p[72],8),false);eq(p[71],255);eq(dword(p,72),4294967295)
    env.GetContainerItemQuestInfo=nil;wp.operationsState.page=0;p=wp.BuildOperationsPacket(wp.BuildPacket());eq(has(p[72],32),false)
end)
test("WO4 item rectangle matches actual slot GetID not reversed button suffix",function()
    local env=operationsEnvironment();local wp=load(env);wp.operationsState.page=0
    local p=wp.BuildOperationsPacket(wp.BuildPacket());eq(has(word(p,6),1024),true);eq(word(p,80),1700);eq(word(p,82),800)
    env.ContainerFrame1Item2:Hide();wp.operationsState.page=0;p=wp.BuildOperationsPacket(wp.BuildPacket());eq(has(word(p,6),1024),false)
end)
test("WO4 buyback tab is never advertised as the sell tab",function()
    local env=operationsEnvironment();local wp=load(env);env.MerchantFrame.selectedTab=2
    local p=wp.BuildOperationsPacket(wp.BuildPacket());eq(has(word(p,6),4096),true);eq(has(word(p,8),256),false)
    env.MerchantFrame.selectedTab=nil;p=wp.BuildOperationsPacket(wp.BuildPacket());eq(has(word(p,6),4096),false)
end)
test("WO4 event registration failures withhold inventory and qualified error counters",function()
    local env=operationsEnvironment();local wp=load(env);wp.OnEvent(nil,"BAG_UPDATE",0);wp.OnEvent(nil,"UI_ERROR_MESSAGE",1,env.ERR_INV_FULL)
    wp.operationsState.page=0;local p=wp.BuildOperationsPacket(wp.BuildPacket());eq(dword(p,38),1);eq(dword(p,42),1)
    wp.OnEvent(nil,"UI_ERROR_MESSAGE",1,"special bag full");p=wp.BuildOperationsPacket(wp.BuildPacket());eq(dword(p,42),1)
    wp.eventRegistration.BAG_UPDATE=false;p=wp.BuildOperationsPacket(wp.BuildPacket());eq(has(word(p,6),16),false);eq(dword(p,38),0)
    wp.eventRegistration.UI_ERROR_MESSAGE=false;p=wp.BuildOperationsPacket(wp.BuildPacket());eq(has(word(p,6),256),false);eq(dword(p,42),0)
end)
test("WO4 oversized bags unknown cursor and missing rectangle geometry remain unavailable",function()
    local env=operationsEnvironment();env.GetContainerNumSlots=function() return 41 end;env.GetCursorInfo=nil;local wp=load(env)
    local p=wp.BuildOperationsPacket(wp.BuildPacket());eq(has(word(p,6),16),false);eq(has(word(p,6),32),false)
    env.StaticPopup1:Show();env.StaticPopup1.which="DEATH";env.StaticPopup1Button1.GetRect=nil
    p=wp.BuildOperationsPacket(wp.BuildPacket());eq(has(word(p,6),1),false)
end)
test("WO4 modern quest nil or incomplete metadata cannot declare a safe nonquest item",function()
    local env=operationsEnvironment();env.C_Container={GetContainerItemQuestInfo=function() return nil end};local wp=load(env);wp.operationsState.page=0
    local p=wp.BuildOperationsPacket(wp.BuildPacket());eq(has(p[72],32),false)
    env.C_Container.GetContainerItemQuestInfo=function() return {} end;wp.operationsState.page=0;p=wp.BuildOperationsPacket(wp.BuildPacket());eq(has(p[72],32),false)
    env.C_Container.GetContainerItemQuestInfo=function() return {isQuestItem=true} end;wp.operationsState.page=0;p=wp.BuildOperationsPacket(wp.BuildPacket());eq(has(p[72],32),true);eq(has(p[72],2),true)
end)
test("WO4 conflicting item identity or quality never claims complete metadata",function()
    local env=operationsEnvironment();env.C_Container={GetContainerItemInfo=function() return {iconFileID=100,stackCount=2,isLocked=false,quality=0,hyperlink="|Hitem:1001:0|hTest|h",itemID=2002} end};local wp=load(env);wp.operationsState.page=0
    local p=wp.BuildOperationsPacket(wp.BuildPacket());eq(has(p[72],8),false);eq(dword(p,76),0)
    env.C_Container.GetContainerItemInfo=function() return {iconFileID=100,stackCount=2,isLocked=false,quality=1,hyperlink="|Hitem:1001:0|hTest|h",itemID=1001} end;wp.operationsState.page=0
    p=wp.BuildOperationsPacket(wp.BuildPacket());eq(has(p[72],8),false)
end)
test("WO4 inventory mutation during a metadata callback discards the mixed revision",function()
    local env=operationsEnvironment();local wp=load(env);local original=env.GetItemInfo
    env.GetItemInfo=function(...) wp.OnEvent(nil,"BAG_UPDATE",0);return original(...) end;wp.operationsState.page=0
    local p=wp.BuildOperationsPacket(wp.BuildPacket());eq(has(word(p,6),16),false);eq(word(p,60),0);eq(dword(p,38),0);eq(dword(p,76),0)
end)
test("WO4 optional rendering failure never hides the original two grids",function()
    local env=operationsEnvironment();local wp=load(env);wp.operationsCells[1].SetColorTexture=function() error("WO4 draw failure") end
    wp.Tick(nil,.2);eq(wp.panel.shown,true);eq(wp.combatPanel.shown,true);eq(wp.operationsPanel.shown,false)
    wp.Command("hide");eq(wp.operationsPanel.shown,false)
end)
test("WO4 layout token is stable across dialogs and changes with physical layout",function()
    local env=operationsEnvironment();local wp=load(env);local initial=dword(wp.BuildOperationsPacket(wp.BuildPacket()),34)
    env.StaticPopup1:Show();env.StaticPopup1.which="DEATH"
    eq(dword(wp.BuildOperationsPacket(wp.BuildPacket()),34),initial,"death dialog must not invalidate the whole armed campaign")
    env.StaticPopup1.which="RECOVER_CORPSE"
    eq(dword(wp.BuildOperationsPacket(wp.BuildPacket()),34),initial,"resurrection dialog is normal state transition")
    env.GetPhysicalScreenSize=function() return 2560,1440 end;wp.ScalePanel()
    assert(dword(wp.BuildOperationsPacket(wp.BuildPacket()),34)~=initial,"resolution change must invalidate layout")
    env.GetPhysicalScreenSize=function() return 1920,1080 end;env.UIParent.GetEffectiveScale=function() return .8 end;wp.ScalePanel()
    assert(dword(wp.BuildOperationsPacket(wp.BuildPacket()),34)~=initial,"UI scale change must invalidate layout")
end)
test("WO4 physical rendering shares sequence and never overlaps portrait",function()
    local env=operationsEnvironment();local wp=load(env);wp.Tick(nil,.2);wp.operationsState.page=0
    local a,b,c=wp.BuildPackets();eq(word(a,4),word(b,4));eq(word(a,4),word(c,4));eq(wp.operationsPanel.point[4]-wp.panel.point[4],544);eq(#wp.operationsCells,384)
    eq(wp.portraitPanel.point[4],832);assert(832>=8+544+256)
    WoWProbeOperationsCoreFixture=a;WoWProbeOperationsCombatFixture=b;WoWProbeOperationsTestFixture=c
end)

for _, t in ipairs(tests) do
    local ok, message = pcall(t[2])
    if ok then passed = passed + 1; print("PASS " .. t[1])
    else print("FAIL " .. t[1] .. "\n  " .. tostring(message)) end
end
print(string.format("AddOn tests: %d/%d passed (%s)", passed, #tests, _VERSION))
assert(passed == #tests, "AddOn test failures: " .. (#tests - passed))
