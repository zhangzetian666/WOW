using System;
using System.Collections.Generic;
using System.Drawing;

namespace WoWProbe
{
    public static class SalePolicy
    {
        public static bool CanSell(InventoryItem item,CampaignSettings settings)
        {
            if(item==null||settings==null||settings.MaxSaleQuality<0||settings.MaxSaleQuality>1)return false;
            if(!item.Known||item.Empty||item.Locked||!item.QuestKnown||item.Quest||!item.TypeKnown||item.Consumable||item.ItemId==0||item.Count<=0||item.Count>65535||item.Quality<0||item.Quality>settings.MaxSaleQuality||item.UnitPrice==0||item.UnitPrice==UInt32.MaxValue)return false;
            if(settings.ProtectedItemIds==null||settings.ProtectedItemIds.Contains(item.ItemId))return false;
            return true;
        }
    }

    /// <summary>Pure evidence-driven merchant workflow. It never sends native input or changes game inventory.</summary>
    public sealed class VendorEngine
    {
        enum Phase { WaitCombat, Outbound, SelectNpc, OpenMerchant, OpenBags, InspectInventory, AwaitSale, AutoClean, CloseMerchant, ReturnFarm, Completed, Stopped }
        readonly CampaignPlan plan;readonly CampaignSettings settings;readonly HashSet<uint> names=new HashSet<uint>();readonly HashSet<int> inspected=new HashSet<int>();
        Phase phase=Phase.WaitCombat;Navigator navigator;bool hasTime,hasSequence;int sequence,scans,openAttempts;long lastNow,started,stateStarted,nextScan,lastOpen;
        uint vendorToken,vendorHash,openedBefore,closedBefore,inspectionRevision,saleRevision,saleFingerprint,saleItem,saleLayout,initialRevision;int saleBag,saleSlot,saleCount,saleFree,initialFree;byte[] saleSizes;
        public string Status{get{return phase.ToString();}}
        public string LastReason{get;private set;}
        public bool Completed{get;private set;}
        public int SoldItems{get;private set;}
        public int SavedFarmIndex{get;private set;}
        public VendorEngine(CampaignPlan plan,int savedFarmIndex)
        {
            if(plan==null)throw new ArgumentNullException("plan");this.plan=plan.Clone();this.plan.Validate();settings=this.plan.Settings;
            if(!settings.EnableVendor||savedFarmIndex<0||savedFarmIndex>=this.plan.FarmRoute.Points.Count)throw new ArgumentException("A vendor workflow needs an enabled route and an unfinished farm index.");
            SavedFarmIndex=savedFarmIndex;foreach(var t in this.plan.Battle.Targets)if(t.Kind=="npc")names.Add(t.NameHash);
            if(names.Count==0)throw new ArgumentException("A saved NPC is required.");LastReason="Awaiting safe vendor interruption observations.";
        }
        public OperationCommand Step(OperationObservation observation,long nowMs)
        {
            if(phase==Phase.Stopped)return Command(OperationAction.Stop,OperationPurpose.CloseUi,LastReason);
            if(Completed)return Command(OperationAction.ResumeFarm,OperationPurpose.Farm,LastReason);
            if(nowMs<0||(hasTime&&nowMs<lastNow))return Fail("Vendor clock is invalid or moved backwards.");
            if(!hasTime){started=stateStarted=nowMs;if(observation!=null){initialRevision=observation.InventoryRevision;initialFree=observation.FreeSlots;}}hasTime=true;lastNow=nowMs;
            string danger=Unsafe(observation);if(danger!=null)return Fail(danger);
            if(nowMs-started>=settings.VendorTimeoutMs)return Fail("Vendor workflow exceeded its total deadline.");
            if(phase==Phase.AwaitSale&&nowMs-stateStarted>=settings.SaleTimeoutMs)return Fail("The selected slot did not produce bounded sale evidence; no retry was sent.");
            if((phase==Phase.OpenBags||phase==Phase.InspectInventory)&&nowMs-stateStarted>=settings.InventoryTimeoutMs)return Fail("Inventory scan did not reach verified capacity before its deadline.");
            if(phase==Phase.AutoClean&&nowMs-stateStarted>=settings.AutoCleanTimeoutMs)return Fail("Server auto-clean did not produce changed-inventory and capacity evidence.");
            if(phase==Phase.OpenMerchant&&nowMs-stateStarted>=Math.Min(settings.VendorTimeoutMs,15000))return Fail("Merchant did not open with the selected saved NPC.");
            if(phase==Phase.CloseMerchant&&nowMs-stateStarted>=settings.SaleTimeoutMs)return Fail("Merchant close was not confirmed; return travel is blocked.");
            OperationObservation o=observation;
            bool distinct=!hasSequence||sequence!=o.Sequence;if(!distinct)return None("Awaiting a distinct vendor observation.");hasSequence=true;sequence=o.Sequence;
            switch(phase)
            {
                case Phase.WaitCombat:
                    if(o.Farm.InCombat)return None("Waiting for combat to end before recorded vendor travel.");
                    if(o.Farm.Casting||o.Farm.Channeling)return None("Waiting for the current cast before vendor travel.");
                    RouteDocument outbound;
                    try{outbound=TravelPlanner.ToVendor(plan,o,SavedFarmIndex);}catch(Exception ex){return Fail("Vendor route cannot be joined safely: "+ex.Message);}
                    navigator=outbound==null?null:new Navigator(outbound,plan.Battle.Calibration,false,false,0,NavigationPurpose.VendorReturn);
                    Enter(Phase.Outbound,nowMs);return Key(0x1B,OperationPurpose.CloseUi,"Closing any old loot window before vendor travel.",false);
                case Phase.Outbound:return Travel(o,nowMs,false);
                case Phase.SelectNpc:
                    if(IsSavedNpc(o.Farm))
                    {vendorToken=o.Farm.TargetToken;vendorHash=o.Farm.TargetNameHash;openedBefore=o.MerchantOpened;openAttempts=1;lastOpen=nowMs;Enter(Phase.OpenMerchant,nowMs);return Key(0x46,OperationPurpose.MerchantOpen,"Opening the selected saved non-player merchant.",true);}
                    if(o.Farm.TargetExists&&o.Farm.AttackabilityKnown&&o.Farm.TargetIsPlayer)return Fail("A player target cannot be used as the saved merchant.");
                    if(nowMs<nextScan)return None("Waiting between configured friendly NPC selections.");
                    if(scans>=settings.NpcScanLimit)return Fail("Configured NPC selection limit reached without the saved NPC.");
                    scans++;nextScan=nowMs+settings.NpcScanIntervalMs;return Key(settings.NpcSelectKey,OperationPurpose.NpcSelect,"Selecting a saved friendly NPC through the confirmed user binding.",false);
                case Phase.OpenMerchant:
                    if(o.MerchantOpen)
                    {
                        string merchant=MerchantProblem(o);if(merchant!=null)return Fail(merchant);
                        if(o.MerchantOpened==openedBefore)return None("Waiting for a new merchant-open event for this NPC.");
                        Enter(settings.AutoCleanOnly?Phase.AutoClean:Phase.OpenBags,nowMs);
                        if(settings.AutoCleanOnly)return None("Waiting for verified server auto-clean; item clicks are disabled.");
                        return Key(0x42,OperationPurpose.BagsOpen,"Opening all bags once using the confirmed binding.",true);
                    }
                    if(!SameNpc(o.Farm))return Fail("Saved NPC identity changed before merchant open.");
                    if(nowMs-lastOpen<Math.Max(1000,settings.NpcScanIntervalMs))return None("Waiting for merchant-open evidence.");
                    if(openAttempts>=3)return Fail("Three bounded merchant interactions failed.");
                    openAttempts++;lastOpen=nowMs;return Key(0x46,OperationPurpose.MerchantOpen,"Retrying the same saved NPC interaction within its bound.",true);
                case Phase.OpenBags:
                    Enter(Phase.InspectInventory,nowMs);inspectionRevision=o.InventoryRevision;inspected.Clear();return Inspect(o,nowMs);
                case Phase.InspectInventory:return Inspect(o,nowMs);
                case Phase.AwaitSale:
                    if(!o.InventoryKnown||!o.InventoryEventsKnown)return Fail("Inventory or update evidence became unknown while confirming a sale.");
                    if(saleLayout!=o.LayoutToken||!SameSizes(saleSizes,o.BagSizes))return Fail("Inventory or client layout changed during the sale.");
                    if(o.Item!=null&&o.Item.Bag==saleBag&&o.Item.Slot==saleSlot&&!o.Item.Empty&&(o.Item.ItemId!=saleItem||o.Item.Fingerprint!=saleFingerprint||o.Item.Count!=saleCount))return Fail("The selected sale slot was replaced or changed.");
                    bool occupied;try{occupied=o.SlotOccupied(saleBag,saleSlot);}catch(ArgumentOutOfRangeException){return Fail("The selected sale slot no longer exists.");}
                    if(o.InventoryRevision!=saleRevision&&!occupied&&o.FreeSlots>saleFree)
                    {SoldItems++;Enter(Phase.InspectInventory,nowMs);inspectionRevision=o.InventoryRevision;inspected.Clear();return None("The exact selected slot emptied after an inventory revision and free capacity increased.");}
                    return None("Awaiting the selected slot's empty bitmap and increased free slots on a new inventory revision.");
                case Phase.AutoClean:
                    if(!o.InventoryKnown||!o.InventoryEventsKnown)return Fail("Auto-clean requires known inventory updates.");
                    if(o.InventoryRevision!=initialRevision&&o.FreeSlots>initialFree&&Capacity(o))return BeginClose(o,nowMs);
                    return None("Auto-clean has no changed-inventory and recovered-capacity proof yet.");
                case Phase.CloseMerchant:
                    if(o.MerchantOpen)return None("Waiting for merchant-close evidence before return travel.");
                    if(o.MerchantClosed==closedBefore)return None("Waiting for a new merchant-close event.");
                    RouteDocument back;
                    try{back=TravelPlanner.FromVendor(plan,SavedFarmIndex);}catch(Exception ex){return Fail("Recorded return route cannot be constructed: "+ex.Message);}
                    navigator=back==null?null:new Navigator(back,plan.Battle.Calibration,false,false,0,NavigationPurpose.VendorReturn);Enter(Phase.ReturnFarm,nowMs);return None("Merchant closed; returning through the recorded reverse route.");
                case Phase.ReturnFarm:return Travel(o,nowMs,true);
                default:return Fail("Unknown vendor workflow phase.");
            }
        }
        OperationCommand Inspect(OperationObservation o,long now)
        {
            if(Capacity(o))return BeginClose(o,now);
            if(o.FreeSlots>=settings.MinimumFreeSlots)return None("Capacity recovered; waiting for the full-inventory signal to clear.");
            if(!o.InventoryKnown||!o.InventoryEventsKnown||!o.InventoryCycleKnown)return Fail("Inventory metadata, update events or page-cycle support is unknown.");
            if(o.Item==null||o.TotalSlots<=0||o.TotalSlots>200||o.PageIndex<0||o.PageIndex>=o.TotalSlots)return Fail("Inventory page identity is unavailable or invalid.");
            InventoryItem item=o.Item;
            if(!item.Known)return Fail("The current inventory item metadata is unknown; no item will be sold.");
            if(o.InventoryRevision!=inspectionRevision){inspectionRevision=o.InventoryRevision;inspected.Clear();}
            inspected.Add(o.PageIndex);
            if(SalePolicy.CanSell(item,settings))
            {
                if(SoldItems>=settings.SaleLimit)return Fail("The configured sale limit was reached before capacity recovered.");
                if(!item.RectangleKnown||!Region(o,item.Region)||item.Fingerprint==0)return Fail("A verified visible item rectangle and fingerprint are required for selling.");
                bool occupied;try{occupied=o.SlotOccupied(item.Bag,item.Slot);}catch(ArgumentOutOfRangeException){return Fail("The candidate sale slot is outside the inventory.");}
                if(!occupied)return Fail("The candidate sale item disagrees with the full inventory occupancy bitmap.");
                saleBag=item.Bag;saleSlot=item.Slot;saleItem=item.ItemId;saleCount=item.Count;saleFingerprint=item.Fingerprint;saleRevision=o.InventoryRevision;saleFree=o.FreeSlots;saleLayout=o.LayoutToken;saleSizes=(byte[])o.BagSizes.Clone();
                Enter(Phase.AwaitSale,now);
                return new OperationCommand{Action=OperationAction.RightClick,Purpose=OperationPurpose.Sell,HoldMilliseconds=80,Region=item.Region,ExpectedTargetToken=vendorToken,ExpectedNameHash=vendorHash,ExpectedItemId=saleItem,ExpectedItemFingerprint=saleFingerprint,ExpectedInventoryRevision=saleRevision,ExpectedItemCount=saleCount,Bag=saleBag,Slot=saleSlot,Reason="Selling one explicitly eligible visible slot; waiting for exact slot and capacity evidence."};
            }
            if(inspected.Count>=o.TotalSlots)return Fail("Every inventory slot was inspected without enough eligible items to restore capacity.");
            return None("Keeping this protected, noneligible or unsellable item and scanning the next inventory page.");
        }
        OperationCommand BeginClose(OperationObservation o,long now){closedBefore=o.MerchantClosed;Enter(Phase.CloseMerchant,now);return Key(0x1B,OperationPurpose.CloseUi,"Closing the verified merchant before recorded return travel.",true);}
        OperationCommand Travel(OperationObservation o,long now,bool returning)
        {
            if(navigator==null){if(returning)return Finish();scans=0;nextScan=now;Enter(Phase.SelectNpc,now);return None("Recorded vendor destination reached; preparing bounded friendly selection.");}
            var f=o.Farm;var c=navigator.Step(new NavigationSample{X=f.X,Y=f.Y,Facing=f.Facing,MapId=f.MapId,MapMode=f.MapMode,Zone=f.Zone,Sequence=o.Sequence,Fresh=o.Fresh,KnownCore=f.KnownCore,ReadFailures=f.ReadFailures,Dead=f.PlayerDead,Ghost=f.PlayerGhost,Combat=f.InCombat,BagFull=f.BagFull},now);
            if(c.Action==NavigationAction.Stop)return Fail(c.Reason);
            if(c.Action==NavigationAction.Completed){navigator=null;if(returning)return Finish();scans=0;nextScan=now;Enter(Phase.SelectNpc,now);return None("Recorded vendor destination reached; preparing bounded friendly selection.");}
            if(c.Action==NavigationAction.None)return None(c.Reason);
            return new OperationCommand{Action=OperationAction.Key,Purpose=OperationPurpose.Travel,Key=plan.Battle.Movement.For(c.Action),HoldMilliseconds=c.HoldMilliseconds,Reason=c.Reason};
        }
        bool IsSavedNpc(FarmObservation f){return f.TargetExists&&f.TargetIdentityKnown&&f.TargetToken!=0&&f.NameHashKnown&&names.Contains(f.TargetNameHash)&&f.AttackabilityKnown&&!f.TargetCanAttack&&!f.TargetIsPlayer&&f.TargetDeadKnown&&!f.TargetDead&&f.TargetHealth>0&&f.TargetHealth<=100;}
        bool SameNpc(FarmObservation f){return IsSavedNpc(f)&&f.TargetToken==vendorToken&&f.TargetNameHash==vendorHash;}
        string MerchantProblem(OperationObservation o)
        {
            if(!o.MerchantKnown||!o.MerchantOpen)return "The verified merchant window is unavailable.";
            if(!SameNpc(o.Farm)||o.MerchantTargetToken!=vendorToken||o.MerchantNameHash!=vendorHash)return "The open merchant belongs to a different or unknown NPC.";
            if(!o.SellTabKnown||!o.SellTab)return "Merchant sale tab is unknown or the buyback tab is selected.";
            if(!o.CursorKnown||!o.CursorEmpty)return "An item is held by the cursor or cursor state is unknown.";
            if(!o.ConfirmationKnown||o.ConfirmationVisible)return "A confirmation popup is visible or its state is unknown.";
            return null;
        }
        internal string SafetyProblem(OperationObservation o){return Unsafe(o);}
        string Unsafe(OperationObservation o)
        {
            if(o==null||!o.Fresh||o.Farm==null||!o.Farm.Fresh||!o.Farm.KnownCore||o.Farm.ReadFailures!=0)return "Fresh matching observations with known core state are required for vendor travel.";
            var f=o.Farm;if(f.PlayerDead||f.PlayerGhost)return "Death or ghost state interrupted the vendor workflow.";
            if(f.InCombat&&phase!=Phase.WaitCombat)return "Combat interrupted the vendor workflow.";
            if(f.Health<=plan.Battle.CriticalHealth||f.Health>100)return "Unknown or critical health stopped vendor travel.";
            if(!f.CastingKnown)return "Casting state is unknown during vendor travel.";
            if(phase!=Phase.WaitCombat&&(f.Casting||f.Channeling))return "Unexpected casting interrupted the vendor workflow.";
            if(f.MapId!=plan.FarmRoute.MapId||f.MapMode!=plan.FarmRoute.MapMode||!String.Equals(f.Zone,plan.FarmRoute.Zone,StringComparison.Ordinal))return "Vendor map identity or zone changed.";
            if(!NavigationMath.InRange(f.X,0,1)||!NavigationMath.InRange(f.Y,0,1)||!NavigationMath.InRange(f.Facing,0,Math.PI*2))return "Vendor position or facing is invalid.";
            if(!o.FreeSlotsKnown||o.FreeSlots<0||o.FreeSlots>200)return "Inventory free capacity is unknown or invalid.";
            if(!o.MerchantKnown)return "Merchant window state is unknown.";
            if(!o.CursorKnown||!o.CursorEmpty)return "Cursor contents are unknown or an item is being held; vendor input stopped.";
            if(!o.ConfirmationKnown||o.ConfirmationVisible)return "A confirmation dialog is visible or its state is unknown; vendor input stopped.";
            if(!o.GeometryKnown||o.LayoutToken==0||o.ClientWidth!=settings.ClientWidth||o.ClientHeight!=settings.ClientHeight)return "Client geometry is unknown or differs from the confirmed layout.";
            if((phase==Phase.Outbound||phase==Phase.ReturnFarm)&&navigator!=null&&FarmEngine.DistanceFromRoute(navigator.WorkingRoute,plan.Battle.Calibration.AxisScale,f.X,f.Y)>settings.JoinTolerance)return "Player left the recorded vendor travel corridor.";
            if((phase==Phase.OpenBags||phase==Phase.InspectInventory||phase==Phase.AwaitSale||phase==Phase.AutoClean)||(phase==Phase.OpenMerchant&&o.MerchantOpen))return MerchantProblem(o);
            if(phase==Phase.CloseMerchant&&o.MerchantOpen)return MerchantProblem(o);
            return null;
        }
        static bool Region(OperationObservation o,Rectangle r){return r.Width>0&&r.Height>0&&r.Width<=512&&r.Height<=512&&r.X>=0&&r.Y>=0&&(long)r.Right<=o.ClientWidth&&(long)r.Bottom<=o.ClientHeight;}
        static bool SameSizes(byte[] a,byte[] b){if(a==null||b==null||a.Length!=5||b.Length!=5)return false;for(int i=0;i<5;i++)if(a[i]!=b[i])return false;return true;}
        bool Capacity(OperationObservation o){return o.FreeSlots>=settings.MinimumFreeSlots&&!o.Farm.BagFull;}
        void Enter(Phase value,long now){phase=value;stateStarted=now;}
        OperationCommand Key(ushort key,OperationPurpose purpose,string reason,bool npc){var c=Command(OperationAction.Key,purpose,reason);c.Key=key;c.HoldMilliseconds=120;if(npc){c.ExpectedTargetToken=vendorToken;c.ExpectedNameHash=vendorHash;}return c;}
        static OperationCommand Command(OperationAction action,OperationPurpose purpose,string reason){return new OperationCommand{Action=action,Purpose=purpose,Reason=reason};}
        static OperationCommand None(string reason){return Command(OperationAction.None,OperationPurpose.CloseUi,reason);}
        OperationCommand Finish(){Completed=true;phase=Phase.Completed;LastReason="Vendor capacity recovered, merchant closed and recorded reverse return completed; resuming the exact saved farm index.";return Command(OperationAction.ResumeFarm,OperationPurpose.Farm,LastReason);}
        OperationCommand Fail(string why){Stop(why);return Command(OperationAction.Stop,OperationPurpose.CloseUi,LastReason);}
        public void Stop(string why){if(phase==Phase.Stopped)return;phase=Phase.Stopped;LastReason=String.IsNullOrWhiteSpace(why)?"Vendor workflow stopped by request.":why;if(navigator!=null)navigator.Stop(LastReason);}
    }
}
