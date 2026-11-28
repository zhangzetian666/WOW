using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace WoWProbe
{
    public sealed class FishingSettings
    {
        public int CastDelayMinMs,CastDelayMaxMs,ReelDelayMinMs,ReelDelayMaxMs;
        public ushort CastKey=0x31,InteractKey=0x46,SelectKey=9;
        public int TapMs=80,CastTimeoutMs=30000,PostCatchDelayMs=1000,LootWaitMs=3000,MinimumBiteDelayMs=700,BiteDebounceMs=1000,BagFusionMs=1000,BagCandidateMs=2000,VendorWaitMs=15000,ScanIntervalMs=1000,ScanLimit=12,MinimumFreeSlots=2;
        public bool EnableVendor,RequireAudio=true,BindingsConfirmed,AutoLootConfirmed,AudioConfirmed,AutoSellConfirmed;
        public FishingSettings Clone(){return (FishingSettings)MemberwiseClone();}
        public void Validate()
        {
            if(CastKey<49||CastKey>57||InteractKey!=70||(SelectKey!=9&&SelectKey!=0x75))throw new FormatException("Unsupported fishing key bindings.");
            Range(CastDelayMinMs,0,60000);Range(CastDelayMaxMs,CastDelayMinMs,60000);Range(ReelDelayMinMs,0,1000);Range(ReelDelayMaxMs,ReelDelayMinMs,1000);Range(TapMs,60,250);Range(CastTimeoutMs,5000,60000);Range(PostCatchDelayMs,0,5000);Range(LootWaitMs,500,10000);Range(MinimumBiteDelayMs,200,5000);Range(BiteDebounceMs,500,3000);Range(BagFusionMs,200,3000);Range(BagCandidateMs,BagFusionMs,5000);Range(VendorWaitMs,1000,60000);Range(ScanIntervalMs,500,5000);Range(ScanLimit,1,30);Range(MinimumFreeSlots,1,40);
            if(MinimumBiteDelayMs>=CastTimeoutMs||LootWaitMs>=CastTimeoutMs)throw new FormatException("Fishing time windows overlap invalidly.");
        }
        static void Range(int n,int lo,int hi){if(n<lo||n>hi)throw new FormatException("Fishing setting is outside supported bounds.");}
    }
    public sealed class FishingPlan
    {
        public int SchemaVersion=1;public string Name="Fishing";public FishingSettings Settings=new FishingSettings();public NavigationCalibration Calibration=new NavigationCalibration();public RouteKeyBindings Movement=new RouteKeyBindings();public Waypoint Spot=new Waypoint();public double Facing;public RouteDocument VendorRoute;public BattleTarget Vendor;
        public FishingPlan Clone(){var p=(FishingPlan)MemberwiseClone();p.Settings=Settings==null?null:Settings.Clone();p.Calibration=Calibration==null?null:Calibration.Clone();p.Movement=Movement==null?null:Movement.Clone();p.Spot=Spot==null?null:Spot.Clone();p.VendorRoute=VendorRoute==null?null:VendorRoute.Clone();p.Vendor=Vendor==null?null:Vendor.Clone();return p;}
        public void Validate()
        {
            if(SchemaVersion!=1||Settings==null||Calibration==null||Movement==null||Spot==null)throw new FormatException("Fishing plan is incomplete or unsupported.");RouteChecks.Label(Name,"fishing plan");Settings.Validate();Calibration.Validate();Movement.Validate();
            if(!NavigationMath.InRange(Spot.X,0,1)||!NavigationMath.InRange(Spot.Y,0,1)||!NavigationMath.InRange(Spot.Tolerance,.0001,.02)||!NavigationMath.InRange(Facing,0,Math.PI*2))throw new FormatException("Invalid fishing spot or facing.");
            if(Settings.EnableVendor)
            {
                if(VendorRoute==null||Vendor==null)throw new FormatException("Vendor travel needs a recorded route and saved NPC.");VendorRoute.Validate();Vendor.Validate();
                if(Vendor.Kind!="npc"||VendorRoute.RouteType!="vendor_return"||!VendorRoute.AllowReverse||VendorRoute.Loop||VendorRoute.MapId!=Calibration.MapId||VendorRoute.MapMode!=Calibration.MapMode||!String.Equals(VendorRoute.Zone,Calibration.Zone,StringComparison.Ordinal))throw new FormatException("Fishing vendor route or NPC does not match this plan.");
                if(NavigationMath.Distance(VendorRoute.Points[0].X-Spot.X,VendorRoute.Points[0].Y-Spot.Y,Calibration.AxisScale)>Spot.Tolerance)throw new FormatException("Vendor route must start at the saved fishing spot.");
                foreach(var point in VendorRoute.Points)if(point.Action=="interact")throw new FormatException("Fishing routes cannot embed interaction actions.");
            }
        }
        public string Fingerprint(){Validate();using(var sha=SHA256.Create()){var hash=sha.ComputeHash(Encoding.UTF8.GetBytes(FishingPlanStore.Write(this,true)));var b=new StringBuilder(64);foreach(byte v in hash)b.Append(v.ToString("x2",CultureInfo.InvariantCulture));return b.ToString();}}
    }
    public sealed class FishingObservation
    {
        public OperationObservation Game=new OperationObservation();public long CapturedAtMs,LastBiteAtMs=-1,LastBagVoiceAtMs=-1,LastBagTextAtMs=-1;public bool AudioReady,VisualReady;public bool SignalReady {get{return AudioReady||VisualReady;}}public double BiteConfidence,BagVoiceConfidence,BagTextConfidence;
        public FishingObservation Clone(){var o=(FishingObservation)MemberwiseClone();o.Game=Game==null?null:Game.Clone();return o;}
    }
    public enum FishingAction{None,Key,Stop,Completed}
    public enum FishingPurpose{None,Cast,Reel,Travel,SelectNpc,OpenMerchant,CloseMerchant,RestoreFacing}
    public sealed class FishingCommand
    {public FishingAction Action;public FishingPurpose Purpose;public ushort Key;public int HoldMilliseconds;public uint ExpectedTargetToken,ExpectedNameHash;public long ExpectedBiteAtMs=-1;public string Reason="";}
    public sealed class FishingCheckpoint
    {public int SchemaVersion=1,RouteIndex,VendorSlotsBefore=-1;public string PlanFingerprint,Phase;public uint VendorInventoryRevision,VendorTargetToken,VendorNameHash;public bool MerchantObserved;}
    public static class FishingPlanStore
    {
        public static void Save(string path,FishingPlan plan){RouteFiles.Save(path,Write(plan,true));}
        public static FishingPlan Load(string path){var p=(FishingPlan)ReadValue(RouteFiles.Load(path),typeof(FishingPlan));p.Validate();Clear(p);return p;}
        internal static string Write(FishingPlan plan,bool clearConfirmations)
        {if(plan==null)throw new ArgumentNullException("plan");var p=plan.Clone();p.Validate();if(clearConfirmations)Clear(p);return new JavaScriptSerializer{MaxJsonLength=RouteFiles.MaxBytes}.Serialize(Normalize(p));}
        static void Clear(FishingPlan p){p.Settings.BindingsConfirmed=p.Settings.AutoLootConfirmed=p.Settings.AudioConfirmed=p.Settings.AutoSellConfirmed=p.Calibration.Confirmed=false;}
        static object Normalize(object value)
        {
            if(value==null||value is string||value.GetType().IsPrimitive||value is decimal)return value;
            var list=value as System.Collections.IList;if(list!=null){var a=new List<object>();foreach(var item in list)a.Add(Normalize(item));return a;}
            var result=new SortedDictionary<string,object>(StringComparer.Ordinal);foreach(var f in value.GetType().GetFields(BindingFlags.Public|BindingFlags.Instance))result[f.Name]=Normalize(f.GetValue(value));return result;
        }
        internal static object ReadValue(object node,Type type)
        {
            if(node==null){if(type.IsValueType)throw new FormatException("Null numeric or boolean fishing field.");return null;}
            if(type==typeof(string)){if(!(node is string))throw new FormatException("Expected a fishing string.");return node;}
            var box=new Dictionary<string,object>{{"value",node}};
            if(type==typeof(bool))return RouteJson.Boolean(box,"value");
            if(type==typeof(int))return RouteJson.Integer(box,"value",Int32.MinValue,Int32.MaxValue);
            if(type==typeof(ushort))return (ushort)RouteJson.Integer(box,"value",0,UInt16.MaxValue);
            if(type==typeof(uint))return RouteJson.Unsigned(box,"value");
            if(type==typeof(double))return RouteJson.Number(box,"value");
            if(type==typeof(List<Waypoint>)){var source=node as List<object>;if(source==null||source.Count>5000)throw new FormatException("Invalid fishing route point array.");var list=new List<Waypoint>();foreach(var item in source)list.Add((Waypoint)ReadValue(item,typeof(Waypoint)));return list;}
            var fields=type.GetFields(BindingFlags.Public|BindingFlags.Instance);var names=new List<string>();foreach(var f in fields)names.Add(f.Name);var o=RouteJson.Object(node);string optional=type==typeof(FishingSettings)?"CastDelayMinMs,CastDelayMaxMs,ReelDelayMinMs,ReelDelayMaxMs":null;RouteJson.Keys(o,String.Join(",",names.ToArray()),optional);var result=Activator.CreateInstance(type);foreach(var f in fields)if(o.ContainsKey(f.Name))f.SetValue(result,ReadValue(o[f.Name],f.FieldType));return result;
        }
    }
    public static class FishingCheckpointStore
    {
        public static void Save(string path,FishingCheckpoint checkpoint){Validate(checkpoint,null);RouteFiles.Save(path,new JavaScriptSerializer().Serialize(checkpoint));}
        public static FishingCheckpoint Load(string path,FishingPlan plan){var cp=(FishingCheckpoint)FishingPlanStore.ReadValue(RouteFiles.Load(path),typeof(FishingCheckpoint));Validate(cp,plan);return cp;}
        internal static void Validate(FishingCheckpoint c,FishingPlan plan)
        {
            if(c==null||c.SchemaVersion!=1||c.RouteIndex<0||c.RouteIndex>5000||c.PlanFingerprint==null||c.PlanFingerprint.Length!=64)throw new FormatException("Invalid fishing checkpoint.");foreach(char x in c.PlanFingerprint)if(!(x>='a'&&x<='f')&&!(x>='0'&&x<='9'))throw new FormatException("Invalid fishing checkpoint fingerprint.");
            if(c.Phase!="Ready"&&c.Phase!="PreCast"&&c.Phase!="ReelDelay"&&c.Phase!="WaitBite"&&c.Phase!="LootWait"&&c.Phase!="RecastDelay"&&c.Phase!="GoSpot"&&c.Phase!="ReturnNpc"&&c.Phase!="NpcScan"&&c.Phase!="VendorWait"&&c.Phase!="ReturnSpot"&&c.Phase!="RestoreFacing"&&c.Phase!="Stopped")throw new FormatException("Invalid fishing checkpoint phase.");
            bool travel=c.Phase=="GoSpot"||c.Phase=="ReturnNpc"||c.Phase=="NpcScan"||c.Phase=="VendorWait"||c.Phase=="ReturnSpot";
            if(!travel&&c.RouteIndex!=0)throw new FormatException("Stationary checkpoint cannot contain a travel index.");
            if(c.Phase=="VendorWait")
            {if(c.VendorSlotsBefore<0||c.VendorSlotsBefore>200||c.VendorTargetToken==0||c.VendorNameHash==0)throw new FormatException("Vendor-wait checkpoint lacks a known original inventory baseline or saved merchant identity.");}
            else if(c.VendorSlotsBefore!=-1||c.VendorInventoryRevision!=0||c.VendorTargetToken!=0||c.VendorNameHash!=0||c.MerchantObserved)throw new FormatException("A non-selling checkpoint cannot claim merchant or inventory evidence.");
            if(plan!=null){plan.Validate();if(c.PlanFingerprint!=plan.Fingerprint())throw new FormatException("Fishing checkpoint belongs to another plan.");if(travel&&(!plan.Settings.EnableVendor||c.RouteIndex>plan.VendorRoute.Points.Count))throw new FormatException("Fishing checkpoint route does not match the plan.");}
            if(plan!=null&&(c.Phase=="NpcScan"||c.Phase=="VendorWait")&&c.RouteIndex!=plan.VendorRoute.Points.Count)throw new FormatException("Merchant checkpoints must be at the recorded route endpoint.");
            if(plan!=null&&c.Phase=="VendorWait"&&c.VendorNameHash!=plan.Vendor.NameHash)throw new FormatException("Vendor-wait checkpoint names a different merchant.");
        }
    }
    public sealed class FishingEngine
    {
        bool channelSeen,lootWindowSeen;long quietSince=-1;
        readonly FishingPlan plan;readonly string fingerprint;readonly Func<int,int> random;long pendingBite=-1,reelDue;Navigator navigator;bool hasTime,hasSequence,reelUsed,merchantSeen,returningToSpot;int sequence,scans,merchantAttempts;long lastTime,castAt,castDeadline,reelAt,delayUntil,nextScan,stateAt,lastBite=-1,lastBag=-1,eventFloor=-1,rejectedBite=-1,rejectedVoice=-1,rejectedText=-1;uint lootOpened,lootClosed,inventoryRevision,npcToken;int slotsBefore;string checkpointPhase="Ready";
        public FishingEngine(FishingPlan plan,Func<int,int> random=null){this.random=random??QuickRandom.Next;if(plan==null)throw new ArgumentNullException("plan");this.plan=plan.Clone();this.plan.Validate();fingerprint=this.plan.Fingerprint();Status="Ready";LastReason="Awaiting a fresh fishing observation.";}
        public string Status{get;private set;}public string LastReason{get;private set;}public int RouteIndex{get;private set;}public int Casts{get;private set;}public int Reels{get;private set;}public int ConfirmedCatches{get;private set;}public int VendorVisits{get;private set;}
        public FishingCheckpoint CurrentCheckpoint{get{string phase=Status=="Stopped"?checkpointPhase:Status;var cp=new FishingCheckpoint{PlanFingerprint=fingerprint,Phase=phase,RouteIndex=phase=="GoSpot"||phase=="ReturnNpc"||phase=="ReturnSpot"||phase=="NpcScan"||phase=="VendorWait"?RouteIndex:0};if(phase=="VendorWait"){cp.VendorSlotsBefore=slotsBefore;cp.VendorInventoryRevision=inventoryRevision;cp.VendorTargetToken=npcToken;cp.VendorNameHash=plan.Vendor.NameHash;cp.MerchantObserved=merchantSeen;}return cp;}}
        public void Stop(string reason){if(Status=="Stopped")return;checkpointPhase=Status;Status="Stopped";LastReason=String.IsNullOrEmpty(reason)?"Fishing stopped by request.":reason;if(navigator!=null)navigator.Stop(LastReason);}
        public void Restore(FishingCheckpoint checkpoint,FishingObservation observation,long nowMs,bool confirmed)
        {
            if(!confirmed)throw new InvalidOperationException("Fishing checkpoint recovery requires explicit manual confirmation.");
            if(hasTime||Status!="Ready")throw new InvalidOperationException("Only a new inactive fishing engine can load a checkpoint.");
            FishingCheckpointStore.Validate(checkpoint,plan);string problem=ValidateLiveObservation(plan,observation);if(problem!=null)throw new InvalidOperationException(problem);
            if(nowMs<0||observation.CapturedAtMs<0||observation.CapturedAtMs>nowMs||nowMs-observation.CapturedAtMs>=900)throw new InvalidOperationException("Checkpoint recovery needs a recent capture.");
            var f=observation.Game.Farm;if(f.Casting||f.Channeling)throw new InvalidOperationException("Finish the existing cast manually before resuming a checkpoint.");
            bool travel=checkpoint.Phase=="GoSpot"||checkpoint.Phase=="ReturnNpc"||checkpoint.Phase=="ReturnSpot"||checkpoint.Phase=="NpcScan"||checkpoint.Phase=="VendorWait";
            if(travel)
            {
                bool reverse=checkpoint.Phase=="ReturnSpot"||checkpoint.Phase=="GoSpot";var route=reverse?plan.VendorRoute.Reversed():plan.VendorRoute.Clone();int previous=Math.Max(0,Math.Min(route.Points.Count-1,checkpoint.RouteIndex-1));var point=route.Points[previous];
                if(NavigationMath.Distance(f.X-point.X,f.Y-point.Y,plan.Calibration.AxisScale)>Math.Min(.006,Math.Max(point.Tolerance,plan.Spot.Tolerance)))throw new InvalidOperationException("Current position is not at the checkpoint's previous recorded waypoint.");
                RouteIndex=checkpoint.RouteIndex;
                if(checkpoint.Phase=="VendorWait")
                {
                    var g=observation.Game;
                    if(!MatchesNpc(f)||f.TargetToken!=checkpoint.VendorTargetToken||!g.MerchantKnown)throw new InvalidOperationException("A selling checkpoint requires the same verified NPC and a known current merchant-window state.");
                    if(g.MerchantOpen&&(g.MerchantTargetToken!=checkpoint.VendorTargetToken||g.MerchantNameHash!=checkpoint.VendorNameHash))throw new InvalidOperationException("The current merchant window does not match the saved selling checkpoint.");
                    if(!g.MerchantOpen&&!checkpoint.MerchantObserved)throw new InvalidOperationException("A closed merchant without prior observed-open evidence cannot prove selling while paused.");
                    Status="VendorWait";stateAt=nowMs;merchantSeen=checkpoint.MerchantObserved||g.MerchantOpen;merchantAttempts=2;npcToken=checkpoint.VendorTargetToken;slotsBefore=checkpoint.VendorSlotsBefore;inventoryRevision=checkpoint.VendorInventoryRevision;
                }
                else if(checkpoint.Phase=="NpcScan"){Status="NpcScan";scans=0;nextScan=stateAt=nowMs;}
                else{if(checkpoint.Phase=="GoSpot"&&(!observation.Game.MerchantKnown||observation.Game.MerchantOpen))throw new InvalidOperationException("Close the merchant window manually before resuming initial-position travel.");Status=checkpoint.Phase=="GoSpot"?"GoSpot":reverse?"ReturnSpot":"ReturnNpc";navigator=new Navigator(plan.VendorRoute,plan.Calibration,reverse,false,RouteIndex,NavigationPurpose.VendorReturn);}
            }
            else{if(!AtSpot(f))throw new InvalidOperationException("A fishing checkpoint may restart only at the saved spot.");Status="Ready";RouteIndex=0;}
            hasTime=true;lastTime=nowMs;hasSequence=false;eventFloor=lastBite=lastBag=nowMs;reelUsed=false;LastReason="Checkpoint validated; pending input, sounds and cast timing were discarded.";
        }
        FishingCommand Result(FishingAction action,string reason){LastReason=reason;return new FishingCommand{Action=action,Reason=reason};}
        FishingCommand None(string reason){return Result(FishingAction.None,reason);}
        FishingCommand Fail(string reason){Stop(reason);return Result(FishingAction.Stop,LastReason);}
        FishingCommand Key(FishingPurpose purpose,ushort key,int hold,string reason,uint token=0,uint name=0,long bite=-1){LastReason=reason;return new FishingCommand{Action=FishingAction.Key,Purpose=purpose,Key=key,HoldMilliseconds=hold,Reason=reason,ExpectedTargetToken=token,ExpectedNameHash=name,ExpectedBiteAtMs=bite};}
        bool AtSpot(FarmObservation f){return NavigationMath.Distance(f.X-plan.Spot.X,f.Y-plan.Spot.Y,plan.Calibration.AxisScale)<=plan.Spot.Tolerance;}
        bool Event(long at,double confidence,long now,int age){return at>=0&&at<=now&&now-at<=age&&NavigationMath.InRange(confidence,.7,1);}
        bool IsBagFull(FishingObservation o,long now,out long acceptedAt)
        {
            bool voice=o.AudioReady&&o.LastBagVoiceAtMs>eventFloor&&o.LastBagVoiceAtMs!=rejectedVoice&&Event(o.LastBagVoiceAtMs,o.BagVoiceConfidence,now,plan.Settings.BagCandidateMs),text=o.LastBagTextAtMs>eventFloor&&o.LastBagTextAtMs!=rejectedText&&Event(o.LastBagTextAtMs,o.BagTextConfidence,now,plan.Settings.BagCandidateMs);
            bool paired=voice&&text&&Math.Abs(o.LastBagVoiceAtMs-o.LastBagTextAtMs)<=plan.Settings.BagFusionMs;
            bool slots=o.Game.FreeSlotsKnown&&o.Game.FreeSlots==0;
            acceptedAt=voice&&text?Math.Max(o.LastBagVoiceAtMs,o.LastBagTextAtMs):voice?o.LastBagVoiceAtMs:o.LastBagTextAtMs;
            return (paired||(slots&&(voice||text)))&&acceptedAt>lastBag;
        }
        FishingCommand Cast(long now)
        {if(now>Int64.MaxValue-plan.Settings.CastTimeoutMs)return Fail("Fishing deadline overflow.");Casts++;castAt=now;castDeadline=now+plan.Settings.CastTimeoutMs;reelUsed=channelSeen=lootWindowSeen=false;quietSince=-1;Status="WaitBite";return Key(FishingPurpose.Cast,plan.Settings.CastKey,plan.Settings.TapMs,"One fishing cast begins a new bounded window.");}
        int Draw(int min,int max){return min+(max>min?random(max-min+1):0);}
        FishingCommand QueueCast(long now)
        {int delay=Draw(plan.Settings.CastDelayMinMs,plan.Settings.CastDelayMaxMs);if(delay==0)return Cast(now);Status="PreCast";delayUntil=checked(now+delay);return None("本杆抛竿前随机等待 "+delay+" 毫秒。");}
        FishingCommand Reel(FishingObservation o,long now)
        {var f=o.Game.Farm;reelAt=now;Reels++;lootWindowSeen=o.Game.LootWindowKnown&&o.Game.LootWindowOpen;quietSince=-1;lootOpened=f.LootOpened;lootClosed=f.LootClosed;inventoryRevision=o.Game.InventoryRevision;slotsBefore=o.Game.FreeSlots;Status="LootWait";return Key(FishingPurpose.Reel,plan.Settings.InteractKey,plan.Settings.TapMs,"已确认本杆新鱼讯，随机延迟结束，按F一次收杆。",0,0,pendingBite);}
        bool CastEnded(OperationObservation o,long now,bool distinct)
        {return distinct&&channelSeen&&quietSince>=0&&now-quietSince>=500&&now-castAt>=1000&&o.LootWindowKnown&&!o.LootWindowOpen&&!o.Farm.Casting&&!o.Farm.Channeling;}
        FishingCommand RecastAfterCompletion(long now)
        {int delay=Draw(plan.Settings.CastDelayMinMs,plan.Settings.CastDelayMaxMs);Status="PreCast";delayUntil=checked(quietSince+delay);return None("本杆已结束但未确认收获；从结束时计抛竿前随机等待 "+delay+" 毫秒，再次抛竿。");}
        FishingCommand TimeoutCast(FarmObservation current,long now,bool distinct)
        {
            if(now-castDeadline>2000)return Fail("旧钓鱼读条到期后2秒仍未确认结束，保留自动重试；不额外按F或ESC。");
            if(current.Casting||current.Channeling)return None("等待旧杆读条结束（最多2秒缓冲），期间不重复收杆或按ESC。");
            if(!distinct)return None("The cast deadline was reached; awaiting a distinct completed-cast frame inside the bounded grace.");
            return QueueCast(now);
        }
        FishingCommand BeginInitialReturn(OperationObservation current,long now)
        {
            if(!plan.Settings.EnableVendor||plan.VendorRoute==null)return Fail("Move to the saved fishing spot; no enabled recorded return route is available.");
            var f=current.Farm;if(f.Casting||f.Channeling)return Fail("Finish the active cast before initial recorded travel.");
            if(!current.MerchantKnown)return Fail("The merchant-window state is unknown; close it manually before initial recorded travel.");
            bool close=current.MerchantOpen;
            if(close&&(!MatchesNpc(f)||current.MerchantTargetToken!=f.TargetToken||current.MerchantNameHash!=plan.Vendor.NameHash))return Fail("The open merchant is not the currently verified saved NPC; initial travel is blocked.");
            int joined=-1;for(int i=plan.VendorRoute.Points.Count-1;i>=0;i--){var point=plan.VendorRoute.Points[i];if(NavigationMath.Distance(f.X-point.X,f.Y-point.Y,plan.Calibration.AxisScale)<=Math.Min(.006,point.Tolerance)){joined=i;break;}}
            if(joined<0)return Fail("Initial fishing travel must start at an explicitly recorded node, not an arbitrary nearest point or corridor position.");
            RouteIndex=Math.Min(plan.VendorRoute.Points.Count-1,plan.VendorRoute.Points.Count-joined);navigator=new Navigator(plan.VendorRoute,plan.Calibration,true,false,RouteIndex,NavigationPurpose.VendorReturn);Status="GoSpot";stateAt=now;returningToSpot=false;
            if(close)return Key(FishingPurpose.CloseMerchant,0x1B,plan.Settings.TapMs,"Close the currently verified saved merchant before initial recorded travel.",f.TargetToken,plan.Vendor.NameHash);
            return None("Join the reverse remainder at the current recorded node, then restore the fishing facing before the first cast.");
        }
        public FishingCommand Step(FishingObservation observation,long nowMs)
        {
            if(Status=="Stopped")return Result(FishingAction.Stop,LastReason);
            if(nowMs<0||(hasTime&&nowMs<lastTime))return Fail("Fishing monotonic clock moved backwards or is invalid.");hasTime=true;lastTime=nowMs;
            string problem=ValidateLiveObservation(plan,observation);if(problem!=null)return Fail(problem);
            var o=observation.Clone();var g=o.Game;var f=g.Farm;var s=plan.Settings;
            if(o.CapturedAtMs<0||o.CapturedAtMs>nowMs||nowMs-o.CapturedAtMs>=900)return Fail("Fishing observation capture is stale or from the future.");
            if(o.LastBiteAtMs>nowMs)rejectedBite=o.LastBiteAtMs;if(o.LastBagVoiceAtMs>nowMs)rejectedVoice=o.LastBagVoiceAtMs;if(o.LastBagTextAtMs>nowMs)rejectedText=o.LastBagTextAtMs;
            bool distinct=!hasSequence||g.Sequence!=sequence;if(distinct){hasSequence=true;sequence=g.Sequence;}
            if(distinct&&(Status=="WaitBite"||Status=="ReelDelay"||Status=="LootWait")){
                if(f.Casting||f.Channeling){channelSeen=true;quietSince=-1;}
                else if(quietSince<0)quietSince=nowMs;
                if(g.LootWindowKnown&&g.LootWindowOpen){lootWindowSeen=true;quietSince=-1;}
            }
            bool travelling=Status=="GoSpot"||Status=="ReturnNpc"||Status=="NpcScan"||Status=="VendorWait"||Status=="ReturnSpot";
            long fullAt;if(!travelling&&IsBagFull(o,nowMs,out fullAt))
            {
                lastBag=fullAt;
                if(!s.EnableVendor)return Fail("Bag full was corroborated; stationary fishing stops without vendor travel.");
                if(!AtSpot(f))return Fail("Bag-full return cannot join an unrecorded position.");
                navigator=new Navigator(plan.VendorRoute,plan.Calibration,false,false,0,NavigationPurpose.VendorReturn);Status="ReturnNpc";RouteIndex=0;stateAt=nowMs;return None("Bag full corroborated; fishing window cancelled for the recorded vendor route.");
            }
            if(travelling&&navigator!=null&&(Status=="GoSpot"||Status=="ReturnNpc"||Status=="ReturnSpot")&&FarmEngine.DistanceFromRoute(navigator.WorkingRoute,plan.Calibration.AxisScale,f.X,f.Y)>.006)return Fail("Fishing travel left its recorded route corridor.");
            if(Status=="GoSpot"||Status=="ReturnNpc"||Status=="ReturnSpot")
            {
                var n=navigator.Step(new NavigationSample{Fresh=g.Fresh&&f.Fresh,KnownCore=f.KnownCore,ReadFailures=f.ReadFailures,Sequence=g.Sequence,MapId=f.MapId,MapMode=f.MapMode,Zone=f.Zone,X=f.X,Y=f.Y,Facing=f.Facing,Dead=f.PlayerDead,Ghost=f.PlayerGhost,Combat=f.InCombat,BagFull=g.FreeSlotsKnown&&g.FreeSlots==0},nowMs);RouteIndex=navigator.TargetIndex;
                if(n.Action==NavigationAction.Stop)return Fail(n.Reason);
                if(n.Action==NavigationAction.Completed){if(Status=="ReturnNpc"){Status="NpcScan";nextScan=nowMs;stateAt=nowMs;scans=0;}else{bool vendorReturn=Status=="ReturnSpot";Status="RestoreFacing";returningToSpot=vendorReturn;stateAt=nowMs;RouteIndex=0;}return None("Recorded fishing travel completed; checking the next guarded phase.");}
                if(n.Action==NavigationAction.None)return None(n.Reason);
                return Key(FishingPurpose.Travel,plan.Movement.For(n.Action),n.HoldMilliseconds,n.Reason);
            }
            if(Status=="NpcScan")
            {
                if(nowMs-stateAt>(long)s.ScanIntervalMs*s.ScanLimit+5000)return Fail("Saved fishing NPC scan exceeded its deadline.");
                if(!distinct)return None("Awaiting a distinct NPC observation.");
                if(MatchesNpc(f)){if(!g.FreeSlotsKnown||!g.InventoryEventsKnown)return Fail("Known inventory revision and free-slot baseline are required before opening the fishing merchant.");if(!g.MerchantKnown||g.MerchantOpen)return Fail("The merchant must be known closed before a new fishing interaction; use a validated selling checkpoint for an already-open merchant.");npcToken=f.TargetToken;merchantSeen=false;merchantAttempts=1;inventoryRevision=g.InventoryRevision;slotsBefore=g.FreeSlots;stateAt=nowMs;Status="VendorWait";return Key(FishingPurpose.OpenMerchant,s.InteractKey,s.TapMs,"Interact once with the verified saved fishing NPC.",npcToken,plan.Vendor.NameHash);}
                if(nowMs<nextScan)return None("Waiting between bounded NPC scans.");if(scans>=s.ScanLimit)return Fail("Saved fishing NPC was not found within its scan limit.");scans++;nextScan=nowMs+s.ScanIntervalMs;return Key(FishingPurpose.SelectNpc,s.SelectKey,s.TapMs,"Bounded fishing NPC target scan.");
            }
            if(Status=="VendorWait")
            {
                if(nowMs-stateAt>=s.VendorWaitMs)return Fail("External selling did not restore observed free slots before its deadline.");
                if(!MatchesNpc(f)||f.TargetToken!=npcToken)return Fail("Fishing merchant target identity changed during interaction.");
                if(g.MerchantKnown&&g.MerchantOpen){if(g.MerchantTargetToken!=npcToken||g.MerchantNameHash!=plan.Vendor.NameHash)return Fail("The opened merchant is not the saved fishing NPC.");merchantSeen=true;}
                if(distinct&&merchantSeen&&g.FreeSlotsKnown&&slotsBefore>=0&&g.FreeSlots>=s.MinimumFreeSlots&&g.FreeSlots>slotsBefore&&g.InventoryEventsKnown&&g.InventoryRevision!=inventoryRevision)
                {navigator=new Navigator(plan.VendorRoute,plan.Calibration,true,false,0,NavigationPurpose.VendorReturn);Status="ReturnSpot";RouteIndex=0;if(!g.MerchantOpen)return None("Observed slots recovered and merchant already closed; return without an unnecessary Escape key.");return Key(FishingPurpose.CloseMerchant,0x1B,s.TapMs,"Observed slots recovered after the verified merchant; close UI and return by recorded points.",npcToken,plan.Vendor.NameHash);}
                if(distinct&&!merchantSeen&&merchantAttempts<2&&nowMs-stateAt>=1500){merchantAttempts++;return Key(FishingPurpose.OpenMerchant,s.InteractKey,s.TapMs,"One bounded retry opening the same verified fishing merchant.",npcToken,plan.Vendor.NameHash);}
                return None("Waiting for the external selling plugin and observed inventory recovery.");
            }
            if(Status=="Ready"&&Casts==0&&!AtSpot(f))return BeginInitialReturn(g,nowMs);
            if(!AtSpot(f))return Fail("Fishing casts require the current position to remain at the saved spot.");
            if(Status=="RestoreFacing")
            {
                if(nowMs-stateAt>=15000)return Fail("Fishing facing restoration exceeded its deadline.");if(!distinct)return None("Awaiting a new facing observation.");
                double target=NavigationMath.Normalize(plan.Calibration.FacingSign*plan.Facing+plan.Calibration.FacingOffset),current=NavigationMath.Normalize(plan.Calibration.FacingSign*f.Facing+plan.Calibration.FacingOffset),delta=NavigationMath.Normalize(target-current);
                if(Math.Abs(delta)>.12)return Key(FishingPurpose.RestoreFacing,delta>0?plan.Movement.TurnRight:plan.Movement.TurnLeft,FishingTravelPolicy.FacingHold(delta,plan.Calibration.TurnRadiansPerSecond),"Restore the saved fishing facing before recasting.");
                if(returningToSpot)VendorVisits++;returningToSpot=false;Status="Ready";return None("At the saved fishing spot and facing.");
            }
            if(Status=="Ready")
            {if(!distinct)return None("Awaiting a distinct frame before casting.");if(f.Casting||f.Channeling)return Fail("An existing cast must finish before starting fishing.");if(Math.Abs(NavigationMath.Normalize(plan.Facing-f.Facing))>.12){Status="RestoreFacing";returningToSpot=false;stateAt=nowMs;return None("Restore the recorded fishing facing before the first cast.");}return QueueCast(nowMs);}
            if(Status=="PreCast")
            {if(nowMs<delayUntil||!distinct)return None("等待本杆随机抛竿延迟结束。");if(f.Casting||f.Channeling)return None("等待已有施法结束。");if(Math.Abs(NavigationMath.Normalize(plan.Facing-f.Facing))>.12){Status="RestoreFacing";stateAt=nowMs;return None("抛竿前重新确认面水朝向。");}return Cast(nowMs);}
            if(Status=="ReelDelay")
            {if(nowMs>=castDeadline)return TimeoutCast(f,nowMs,distinct);if(!o.SignalReady||o.LastBiteAtMs!=pendingBite||!Event(pendingBite,o.BiteConfidence,nowMs,900+s.ReelDelayMaxMs)){Status="WaitBite";return None("延迟期间鱼讯失效，本杆不补按F。");}if(nowMs<reelDue||!distinct)return None("等待本杆随机收杆延迟结束。");return Reel(o,nowMs);}
            if(Status=="RecastDelay")
            {if(nowMs<delayUntil)return None("Waiting one second after observed loot completion.");if(f.Casting||f.Channeling)return Fail("An active cast remains after the post-catch delay; fishing will not cast over it.");if(!distinct)return None("Awaiting a fresh frame before recast.");return QueueCast(nowMs);}
            if(Status=="LootWait")
            {
                bool loot=f.LootEventsKnown&&f.LootOpened!=lootOpened&&f.LootClosed!=lootClosed||lootWindowSeen&&g.LootWindowKnown&&!g.LootWindowOpen;
                bool inventory=g.InventoryEventsKnown&&g.InventoryRevision!=inventoryRevision&&g.FreeSlotsKnown&&slotsBefore>=0&&g.FreeSlots<slotsBefore;
                if(distinct&&(loot||inventory)&&!(g.LootWindowKnown&&g.LootWindowOpen)){ConfirmedCatches++;Status="RecastDelay";delayUntil=nowMs+s.PostCatchDelayMs;return None("Loot completion was observed; wait before the next cast.");}
                if(g.LootWindowKnown&&g.LootWindowOpen)return None("拾取窗口仍打开，等拾取完成后再抛竿。");
                if(CastEnded(g,nowMs,distinct)&&nowMs-reelAt>=700)return RecastAfterCompletion(nowMs);
                if(nowMs>=castDeadline)return TimeoutCast(f,nowMs,distinct);
                if(nowMs-reelAt>=s.LootWaitMs)return None("收竿暂无拾取反馈；继续核对读条和拾取窗口，不重复按F。");
                return None("Waiting for observed loot after the single reel.");
            }
            if(Status=="WaitBite")
            {
                if(CastEnded(g,nowMs,distinct))return RecastAfterCompletion(nowMs);
                if(nowMs>=castDeadline)return TimeoutCast(f,nowMs,distinct);
                if(distinct&&!reelUsed&&o.SignalReady&&o.LastBiteAtMs>eventFloor&&o.LastBiteAtMs!=rejectedBite&&Event(o.LastBiteAtMs,o.BiteConfidence,nowMs,900)&&o.LastBiteAtMs>=castAt+s.MinimumBiteDelayMs&&o.LastBiteAtMs>lastBite&&(lastBite<0||o.LastBiteAtMs-lastBite>=s.BiteDebounceMs))
                {reelUsed=true;lastBite=pendingBite=o.LastBiteAtMs;int delay=Draw(s.ReelDelayMinMs,s.ReelDelayMaxMs);long remaining=castDeadline-nowMs-s.TapMs-100;if(remaining<0)return None("鱼讯距离本杆截止过近，不发送过期收杆。");delay=(int)Math.Min(delay,remaining);if(delay==0)return Reel(o,nowMs);reelDue=nowMs+delay;Status="ReelDelay";return None("本杆收杆随机等待 "+delay+" 毫秒。");}
                return None("等待本杆鱼讯（水花或已启用的声音识别），正常运行中。");
            }
            return Fail("Unsupported fishing state.");
        }
        bool MatchesNpc(FarmObservation f){return f.TargetExists&&f.TargetIdentityKnown&&f.TargetToken!=0&&f.NameHashKnown&&f.TargetNameHash==plan.Vendor.NameHash&&f.AttackabilityKnown&&!f.TargetCanAttack&&!f.TargetIsPlayer&&f.TargetDeadKnown&&!f.TargetDead;}
        public static string ValidateLiveObservation(FishingPlan plan,FishingObservation o)
        {
            if(plan==null||o==null||o.Game==null||o.Game.Farm==null)return "Fishing observations or plan are missing.";var g=o.Game;var f=g.Farm;
            if(!g.Fresh||!f.Fresh||!f.KnownCore||f.ReadFailures!=0||g.Sequence!=f.Sequence)return "Fishing requires fresh sequence-matched known game observations without API failures.";
            if(f.PlayerDead||f.PlayerGhost||f.InCombat)return "Death, ghost or combat stopped fishing.";
            if(f.MapId!=plan.Calibration.MapId||g.MapId!=plan.Calibration.MapId||f.MapMode!=plan.Calibration.MapMode||!String.Equals(f.Zone,plan.Calibration.Zone,StringComparison.Ordinal))return "Fishing map identity or zone changed.";
            if(!NavigationMath.InRange(f.X,0,1)||!NavigationMath.InRange(f.Y,0,1)||!NavigationMath.InRange(f.Facing,0,Math.PI*2))return "Fishing player position or facing is invalid.";
            if(!g.CursorKnown||!g.CursorEmpty||!g.ConfirmationKnown||g.ConfirmationVisible||!g.IncomingResurrectionKnown||g.IncomingResurrection)return "Fishing cursor, confirmation or resurrection state is unsafe or unknown.";
            if(!f.CastingKnown)return "Fishing casting state is unknown.";
            if(plan.Settings.RequireAudio&&!o.SignalReady)return "声音和水花图像均不可用，请核对观察区域。";
            if(g.FreeSlotsKnown&&(g.FreeSlots<0||g.FreeSlots>200))return "Fishing free-slot evidence is invalid.";
            return null;
        }
    }
}
