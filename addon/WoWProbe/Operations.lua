-- Passive WO4 observations only. No protected action, item use/sale, cursor change or UI click is called here.
local P=WoWProbe
local floor=math.floor
local U32=4294967296
P.operationsState={revision=0,bagFull=0,merchantOpened=0,merchantClosed=0,page=0,cycle=0}
local function finite(x) return type(x)=="number" and x==x and x>-math.huge and x<math.huge end
local function integer(x,max) return finite(x) and x>=0 and x<=max and floor(x)==x end
local function attempt(fn,...)
    if type(fn)~="function" then return nil end
    local args={...};local ok,result=pcall(function() return {fn(unpack(args))} end)
    if ok then return result end
    if WoWProbeDB and WoWProbeDB.errors then WoWProbeDB.errors.operations={message=string.sub(tostring(result),1,300)} end
end
local function truth(v)
    if v==true or v==1 then return true,true end
    if v==false or v==0 or v==nil then return false,true end
    return false,false
end
local function shown(frame)
    if not frame then return nil end
    local a=attempt(frame.IsVisible or frame.IsShown,frame)
    if a then local v,k=truth(a[1]);if k then return v end end
end
local function rectangle(frame,r)
    if not frame or not r or not r.valid or shown(frame)~=true then return nil end
    local a=attempt(frame.GetRect,frame);local scale=attempt(frame.GetEffectiveScale,frame)
    if not a or not scale or not finite(scale[1]) or scale[1]<=0 then return nil end
    for i=1,4 do if not finite(a[i]) then return nil end end
    local k=scale[1]/(768/r.physicalHeight)
    local x=floor(a[1]*k+.5);local y=floor(r.physicalHeight-(a[2]+a[4])*k+.5)
    local w=floor(a[3]*k+.5);local h=floor(a[4]*k+.5)
    if x<0 or y<0 or w<1 or h<1 or x+w>r.physicalWidth or y+h>r.physicalHeight then return nil end
    return {x,y,w,h}
end
local function popupState(r)
    local result={known=false}
    if type(StaticPopupDialogs)~="table" or not integer(STATICPOPUP_NUMDIALOGS,10) or STATICPOPUP_NUMDIALOGS<1 then return result end
    result.releaseKnown=type(StaticPopupDialogs.DEATH)=="table"
    result.resurrectKnown=type(StaticPopupDialogs.RECOVER_CORPSE)=="table"
    result.releaseVisible,result.releaseEnabled,result.resurrectVisible,result.resurrectEnabled=false,false,false,false
    result.confirmation,result.incoming=false,false
    for i=1,STATICPOPUP_NUMDIALOGS do
        local frame=_G["StaticPopup"..i];local visible=shown(frame)
        if visible==nil then return {known=false} end
        if visible then
            local which=frame.which
            if type(which)~="string" then return {known=false} end
            if which=="DEATH" or which=="RECOVER_CORPSE" then
                local button=frame.button1 or _G["StaticPopup"..i.."Button1"]
                local on=shown(button);local e=button and attempt(button.IsEnabled,button);local enabled,enabledKnown=false,false
                if e then enabled,enabledKnown=truth(e[1]) end
                local rect=on and rectangle(button,r)
                if which=="DEATH" then
                    if on~=true or not enabledKnown or not rect then result.releaseKnown=false
                    else result.releaseVisible=true;result.releaseEnabled=enabled;result.releaseRegion=rect end
                else
                    if on~=true or not enabledKnown or not rect then result.resurrectKnown=false
                    else result.resurrectVisible=true;result.resurrectEnabled=enabled;result.resurrectRegion=rect end
                end
            elseif which=="RESURRECT" or which=="RESURRECT_NO_SICKNESS" or which=="RESURRECT_NO_TIMER" then result.incoming=true
            else result.confirmation=true end
        end
    end
    result.known=true;return result
end
local function inventoryFunctions()
    local c=type(C_Container)=="table" and C_Container or nil
    return c and c.GetContainerNumSlots or GetContainerNumSlots,c and c.GetContainerItemInfo or GetContainerItemInfo,c and c.GetContainerItemQuestInfo or GetContainerItemQuestInfo
end
local function slotInfo(fn,bag,slot)
    local data=attempt(fn,bag,slot);if not data then return nil end
    if data[1]==nil then return {empty=true} end
    local info={}
    if type(data[1])=="table" then
        local t=data[1];info.count=t.stackCount;info.locked=t.isLocked;info.quality=t.quality;info.link=t.hyperlink;info.id=t.itemID
        if t.iconFileID==nil and t.itemID==nil and t.hyperlink==nil then return nil end
    else info.count=data[2];info.locked=data[3];info.quality=data[4];info.link=data[7] end
    info.empty=false;return info
end
local function inventorySnapshot()
    if not P.eventRegistration or P.eventRegistration.BAG_UPDATE~=true then return nil end
    local slots,items=inventoryFunctions();if type(slots)~="function" or type(items)~="function" then return nil end
    local state=P.operationsState;local revision=state.revision
    local result={sizes={},occupancy={},items={},total=0}
    for i=1,25 do result.occupancy[i]=0 end
    for bag=0,4 do
        local count=attempt(slots,bag)
        if not count or not integer(count[1],40) then return nil end
        result.sizes[bag+1]=count[1]
        for slot=1,count[1] do
            local info=slotInfo(items,bag,slot);if not info then return nil end
            info.bag,info.slot=bag,slot;result.total=result.total+1;result.items[result.total]=info
            if not info.empty then local byte=bag*5+floor((slot-1)/8)+1;result.occupancy[byte]=result.occupancy[byte]+2^((slot-1)%8) end
        end
    end
    if result.total==0 or state.revision~=revision then return nil end
    result.revision=revision;return result
end
local function metadata(item)
    if item.empty then
        return {known=true,empty=true,count=0,id=0,quality=255,price=0,questKnown=true,typeKnown=true,quest=false,consumable=false,locked=false,
            fingerprint=P.HashUTF8(item.bag..":"..item.slot..":empty")}
    end
    local out={known=false,empty=false,count=integer(item.count,65535) and item.count or 0,id=0,quality=255,price=U32-1}
    local lock,lk=truth(item.locked);out.locked=lock
    local link=item.link
    local linkedId=type(link)=="string" and tonumber(string.match(link,"item:(%d+)")) or nil
    local id=item.id
    local consistent=not integer(id,U32-1) or id==0 or id==linkedId
    if not integer(id,U32-1) or id==0 then id=linkedId end
    if integer(id,U32-1) then out.id=id end
    local _,_,questFn=inventoryFunctions();local q=attempt(questFn,item.bag,item.slot)
    if q then
        local quest,qid
        if type(q[1])=="table" then quest=q[1].isQuestItem;qid=q[1].questID else quest=q[1];qid=q[2] end
        local value,known=truth(quest)
        local modern=type(C_Container)=="table" and type(C_Container.GetContainerItemQuestInfo)=="function"
        if modern and (type(q[1])~="table" or quest==nil) then known=false end
        if known and (qid==nil or integer(qid,U32-1)) then out.questKnown=true;out.quest=value or (qid~=nil and qid>0) end
    end
    local c=type(C_Item)=="table" and C_Item or nil
    local info=attempt(c and c.GetItemInfo or GetItemInfo,link or id)
    if info and type(info[1])=="string" and #info[1]>0 and integer(info[3],7) and integer(info[11],U32-2) and lk and out.id>0 and out.count>0 and consistent and linkedId==out.id
        and (not integer(item.quality,7) or item.quality==info[3]) then
        out.known=true;out.quality=info[3];out.price=info[11]
        local classId=info[12]
        if not integer(classId,255) then
            local instant=attempt(c and c.GetItemInfoInstant or GetItemInfoInstant,link or id)
            if instant and integer(instant[6],255) then classId=instant[6] end
        end
        if integer(classId,255) then out.typeKnown=true;out.consumable=classId==0
        else
            local class=attempt(c and c.GetItemClassInfo or GetItemClassInfo,0)
            if class and type(class[1])=="string" and #class[1]>0 and type(info[6])=="string" and #info[6]>0 then out.typeKnown=true;out.consumable=class[1]==info[6] end
        end
        if type(link)~="string" or #link==0 then out.known=false;out.quality=255;out.price=U32-1
        else out.fingerprint=P.HashUTF8(table.concat({item.bag,item.slot,link,out.id,out.count,out.quality},":")) end
    end
    return out
end
local function itemRectangle(bag,slot,r)
    -- Legacy container names enumerate visual buttons in reverse order: only their actual GetID identifies a slot.
    for i=1,13 do
        local container=_G["ContainerFrame"..i]
        if shown(container)==true then
            local id=attempt(container.GetID,container);local name=attempt(container.GetName,container)
            if id and id[1]==bag and name and type(name[1])=="string" then
                for j=1,40 do
                    local button=_G[name[1].."Item"..j]
                    if button then local sid=attempt(button.GetID,button);if sid and sid[1]==slot then return rectangle(button,r) end end
                end
            end
        end
    end
end
function P.OperationsEvent(event,...)
    local s=P.operationsState
    if event=="BAG_UPDATE" or event=="ITEM_LOCK_CHANGED" then s.revision=(s.revision+1)%U32
    elseif event=="MERCHANT_SHOW" then s.merchantOpened=(s.merchantOpened+1)%U32
    elseif event=="MERCHANT_CLOSED" then s.merchantClosed=(s.merchantClosed+1)%U32
    elseif event=="UI_ERROR_MESSAGE" then
        local a,b=...;local text=type(b)=="string" and b or a
        if type(ERR_INV_FULL)=="string" and text==ERR_INV_FULL then s.bagFull=(s.bagFull+1)%U32 end
    elseif event=="PLAYER_ENTERING_WORLD" then s.page=0;s.pageRevision=nil end
end
function P.CaptureOperationsAPIs()
    WoWProbeDB.apiAvailability=WoWProbeDB.apiAvailability or {}
    for _,path in ipairs({"GetCorpseRecoveryDelay","GetCursorInfo","GetContainerNumSlots","GetContainerItemInfo","GetContainerItemQuestInfo","GetItemInfo","GetItemClassInfo","GetItemInfoInstant","C_Container.GetContainerNumSlots","C_Container.GetContainerItemInfo","C_Container.GetContainerItemQuestInfo","C_Item.GetItemInfo","C_Item.GetItemClassInfo","C_Item.GetItemInfoInstant"}) do
        local namespace,name=string.match(path,"^([^.]+)%.(.+)$")
        local value=namespace and type(_G[namespace])=="table" and _G[namespace][name] or (not namespace and _G[path])
        WoWProbeDB.apiAvailability[path]=type(value)=="function"
    end
end
function P.BuildOperationsPacket(core)
    local p={};for i=1,128 do p[i]=0 end
    local function put(i,n) p[i+1]=n end
    local function u16(i,n) put(i,n%256);put(i+1,floor(n/256)%256) end
    local function u32(i,n) u16(i,n%65536);u16(i+2,floor(n/65536)) end
    local function rect(i,v) if v then for j=1,4 do u16(i+(j-1)*2,v[j]) end end end
    core=core or P.BuildPacket();put(0,87);put(1,79);put(2,52);put(3,1);u16(4,core[5]+core[6]*256);u32(26,U32-1);put(70,255);u32(72,U32-1)
    local cap,flags=0,0;local state=P.operationsState;local events=P.eventRegistration or {};local r=WoWProbeDB.renderer
    local geometry=r and r.valid and integer(r.physicalWidth,65535) and r.physicalWidth>0 and integer(r.physicalHeight,65535) and r.physicalHeight>0
    local pop=popupState(geometry and r or nil)
    if geometry then
        cap=cap+128;u16(30,r.physicalWidth);u16(32,r.physicalHeight)
        -- Popup visibility changes during normal death/recovery. Only size and scale
        -- belong in the armed client layout identity; command ROIs are checked separately.
        local signature={r.physicalWidth,r.physicalHeight,r.parentScale or 0}
        u32(34,P.HashUTF8(table.concat(signature,":")))
    end
    if pop.known then
        cap=cap+64+512;if pop.confirmation then flags=flags+64 end;if pop.incoming then flags=flags+128 end
        if pop.releaseKnown then cap=cap+1;if pop.releaseVisible then flags=flags+1;rect(10,pop.releaseRegion) end;if pop.releaseEnabled then flags=flags+2 end end
        if pop.resurrectKnown then cap=cap+2;if pop.resurrectVisible then flags=flags+4;rect(18,pop.resurrectRegion) end;if pop.resurrectEnabled then flags=flags+8 end end
    end
    local delay=attempt(GetCorpseRecoveryDelay)
    if delay and finite(delay[1]) and delay[1]>=0 and delay[1]<=3600 then cap=cap+4;u32(26,floor(delay[1]*1000+.5)) end
    local cursor=attempt(GetCursorInfo)
    if cursor then cap=cap+32;if cursor[1]==nil then flags=flags+32 end end
    local merchant=shown(MerchantFrame);local merchantIdentity=false
    if merchant~=nil and events.MERCHANT_SHOW and events.MERCHANT_CLOSED then
        if not merchant then merchantIdentity=true
        else
            local guid=attempt(UnitGUID,"npc");local name=attempt(UnitName,"npc")
            local token=guid and P.HashUTF8(guid[1]);local hash=name and P.HashUTF8(name[1])
            if token and hash then merchantIdentity=true;flags=flags+16;u32(88,token);u32(92,hash) end
        end
        if merchantIdentity then cap=cap+8;u32(46,state.merchantOpened);u32(50,state.merchantClosed) end
    end
    if merchant==true and merchantIdentity then
        local tab=MerchantFrame.selectedTab
        if tab==1 or tab==2 then cap=cap+4096;if tab==1 then flags=flags+256 end end
    end
    if events.UI_ERROR_MESSAGE and type(ERR_INV_FULL)=="string" then cap=cap+256;u32(42,state.bagFull) end
    local inventory=inventorySnapshot()
    if inventory then
        if state.pageRevision~=inventory.revision or not integer(state.page,inventory.total-1) then state.page=0;state.pageRevision=inventory.revision end
        local index=state.page;local item=inventory.items[index+1];local data=metadata(item);local region
        if geometry and merchant and merchantIdentity and MerchantFrame.selectedTab==1 then
            region=itemRectangle(item.bag,item.slot,r)
        end
        if state.revision==inventory.revision then
            if index==inventory.total-1 then state.cycle=(state.cycle+1)%U32 end
            cap=cap+16+2048;u32(38,inventory.revision);u32(54,state.cycle);u16(58,index);u16(60,inventory.total);put(62,item.bag);put(63,item.slot)
            u32(64,data.id or 0);u16(68,data.count or 0);put(70,data.quality);u32(72,data.price);u32(76,data.fingerprint or 0)
            local f=0;if data.locked then f=f+1 end;if data.quest then f=f+2 end;if data.consumable then f=f+4 end;if data.known then f=f+8 end;if data.empty then f=f+16 end;if data.questKnown then f=f+32 end;if data.typeKnown then f=f+64 end;put(71,f)
            for i=1,5 do put(95+i,inventory.sizes[i]) end;for i=1,25 do put(100+i,inventory.occupancy[i]) end
            if region then cap=cap+1024;rect(80,region) end
            state.page=(index+1)%inventory.total
        end
    end
    u16(6,cap);u16(8,flags);local a,b=0,0;for i=1,126 do a=(a+p[i])%255;b=(b+a)%255 end;put(126,a);put(127,b)
    WoWProbeDB.lastOperationsPacket={sequence=core[5]+core[6]*256,capabilities=cap,flags=flags,page=state.page,revision=state.revision}
    return p
end
function P.CreateOperationsPanel()
    local panel=CreateFrame("Frame","WoWProbeOperationsPanel",UIParent);P.operationsPanel=panel
    panel:SetWidth(256);panel:SetHeight(96);panel:SetFrameStrata("TOOLTIP");panel:EnableMouse(false);P.operationsCells={}
    for i=0,383 do local t=panel:CreateTexture(nil,"OVERLAY");t:SetWidth(8);t:SetHeight(8);t:SetPoint("TOPLEFT",panel,"TOPLEFT",(i%32)*8,-floor(i/32)*8);P.operationsCells[i+1]=t end
    local label=panel:CreateFontString(nil,"OVERLAY","GameFontNormalSmall");label:SetPoint("TOPLEFT",panel,"TOPLEFT",0,-100);label:SetText("WoWProbe WO4 | passive operations")
end
