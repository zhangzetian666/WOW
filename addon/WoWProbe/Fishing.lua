-- WF1: passive fishing social/UI observations only. Never sends chat or input.
local P=WoWProbe
local floor=math.floor
local count,sender,lastAuthor=0,0,nil
local registered=false
local frame=CreateFrame("Frame")
local ok=pcall(frame.RegisterEvent,frame,"CHAT_MSG_WHISPER");registered=ok
frame:SetScript("OnEvent",function(_,event,_,author)
    if event=="CHAT_MSG_WHISPER" and type(author)=="string" and #author>0 then
        local valid,hash=pcall(P.HashUTF8,string.lower(author))
        if valid and hash then count=(count+1)%4294967296;sender=hash;lastAuthor=author end
    end
end)
function P.CreateFishingPanel()
    local panel=CreateFrame("Frame","WoWProbeFishingPanel",UIParent);P.fishingPanel=panel
    panel:SetWidth(256);panel:SetHeight(96);panel:SetFrameStrata("TOOLTIP");panel:EnableMouse(false);P.fishingCells={}
    for i=0,383 do local t=panel:CreateTexture(nil,"OVERLAY");t:SetWidth(8);t:SetHeight(8);t:SetPoint("TOPLEFT",panel,"TOPLEFT",(i%32)*8,-floor(i/32)*8);P.fishingCells[i+1]=t end
end
function P.BuildFishingPacket(core)
    local p={};for i=1,128 do p[i]=0 end
    local function put(i,n)p[i+1]=n end
    local function u32(i,n)for j=0,3 do put(i+j,floor(n/256^j)%256)end end
    put(0,87);put(1,70);put(2,49);put(3,1);put(4,core[5]);put(5,core[6]);put(6,registered and 1 or 0);u32(8,count);u32(12,sender)
    local good,edit=pcall(function()return ChatEdit_GetActiveWindow and ChatEdit_GetActiveWindow()end)
    if good then
        local known,chatType,tellTarget=pcall(function()if edit and edit:IsShown() then return edit:GetAttribute("chatType"),edit:GetAttribute("tellTarget") end end)
        if known then
            put(7,1)
            if chatType=="SAY" then put(7,3)
            elseif chatType=="WHISPER" and type(tellTarget)=="string" then put(7,5);u32(16,P.HashUTF8(string.lower(tellTarget))) end
        end
    end
    local bagsKnown,bagsOpen=pcall(function()
        if type(IsBagOpen)=="function" then local v=IsBagOpen(0);return v==true or v==1 end
        if ContainerFrame1 and ContainerFrame1.IsShown then return ContainerFrame1:IsShown() end
        error("Bag visibility unavailable")
    end)
    if bagsKnown then put(20,bagsOpen and 3 or 1) end
    local a,b=0,0;for i=1,126 do a=(a+p[i])%255;b=(b+a)%255 end;put(126,a);put(127,b)
    return p
end
