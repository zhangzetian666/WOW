using System;
using System.Drawing;
using System.Collections.Generic;
namespace WoWProbe
{
    public sealed class QuickVendorSettings
    {
        public bool Enabled;public double X,Y;public string Name="";
        public List<Waypoint> Points=new List<Waypoint>();public bool RouteComplete;
        public void Validate(){if(Name==null||Name.Length>128)throw new FormatException("商人名称过长。");foreach(char c in Name)if(Char.IsControl(c)||c=='/')throw new FormatException("商人名称不能含控制字符或斜线。");if(!NavigationMath.InRange(X,0,1)||!NavigationMath.InRange(Y,0,1))throw new FormatException("请录制NPC路线及终点。");if(Points==null||Points.Count>4990)throw new FormatException("NPC路线点数无效。");for(int i=0;i<Points.Count;i++){var p=Points[i];if(p==null||!NavigationMath.InRange(p.X,0,1)||!NavigationMath.InRange(p.Y,0,1)||!NavigationMath.InRange(p.Tolerance,.0001,.002)||p.WaitMilliseconds!=0||p.Action!="wait")throw new FormatException("NPC路线坐标无效。");if(i>0&&p.X==Points[i-1].X&&p.Y==Points[i-1].Y)throw new FormatException("NPC路线相邻点不能重复。");}if(RouteComplete&&(Points.Count<2||Math.Abs(Points[Points.Count-1].X-X)>1e-8||Math.Abs(Points[Points.Count-1].Y-Y)>1e-8))throw new FormatException("NPC路线须至少两点，终点应与NPC位置一致。");}
        public void RequireRoute(){Validate();if(!RouteComplete||Points.Count<2)throw new InvalidOperationException("请先从巡航路线附近开始录制，走到NPC旁后结束录制；旧单坐标不能直接作为NPC路线。");}
        public QuickVendorSettings Clone(){var c=(QuickVendorSettings)MemberwiseClone();c.Points=new List<Waypoint>();foreach(var p in Points)c.Points.Add(p.Clone());return c;}
    }
    public enum QuickVendorAction {None,Stealth,Key,Select,Goods,Repair,Complete,Stop}
    public sealed class QuickVendorCommand
    {public QuickVendorAction Action;public ushort Key;public int Hold=80;public string Text,Reason;public Rectangle Region;public uint Token,Hash,Cost;}
    // Opens the actual merchant and observes the user's existing auto-sell.
    // This driver never clicks an inventory item or invents a sale result.
    public sealed class QuickVendorEngine
    {
        enum Phase {Outbound,Select,Open,WaitSale,Close,Return,Done}
        readonly QuickRoutePlan plan;readonly QuickVendorSettings settings;readonly RouteDocument outbound;Navigator nav;Phase phase;
        readonly RouteKeyBindings movement=new RouteKeyBindings{TurnLeft=37,TurnRight=39};
        readonly System.Collections.Generic.HashSet<uint> rejected=new System.Collections.Generic.HashSet<uint>();
        public void ResumeAfterCombat(OperationObservation o,long now)
        {
            if(o==null||!o.Fresh||o.Farm==null||!plan.MapMatches(o.Farm))throw new InvalidOperationException("NPC路线恢复状态无效。");
            inventoryQuietSince=-1;detourPhase=0;if((phase==Phase.Outbound||phase==Phase.Return)&&nav!=null){nav=QuickServicePaths.Navigate(plan,nav.WorkingRoute,o.Farm,nav.TargetIndex,emergencyRepair);stateAt=now;sequence=-1;return;}
            bool done=(!TwoVisits||completedVisits>=2)&&merchantVerified&&(sharedService?o.Farm.GearKnown&&o.Farm.Durability>=10000&&(!saleRequired||o.FreeSlotsKnown&&o.InventoryEventsKnown&&o.InventoryRevision!=revision&&o.FreeSlots>free&&!o.Farm.BagFull):repair?o.Farm.GearKnown&&o.Farm.Durability>=10000:o.FreeSlotsKnown&&o.InventoryEventsKnown&&o.InventoryRevision!=revision&&o.FreeSlots>free&&!o.Farm.BagFull);
            nav=done?ReturnTravel(o.Farm):QuickServicePaths.Navigate(plan,outbound,o.Farm,outbound.Points.Count-1,emergencyRepair);phase=done?Phase.Return:nav==null?Phase.Select:Phase.Outbound;stateAt=now;sequence=-1;token=hash=0;if(done&&o.MerchantOpen){phase=Phase.Close;closed=o.MerchantClosed;closeAfterCombat=true;}if(!done){repairIssued=false;merchantVerified=false;saleAt=-1;}
        }
        long started=-1,stateAt,nextSelect,saleAt=-1;int sequence=-1,attempts;uint token,hash,revision,opened,closed;int free;
        bool TwoVisits {get{return !repair||sharedService&&saleRequired;}}
        int completedVisits;
        bool merchantVerified;int detourPhase;double detourFacing;
        long inventoryQuietSince=-1;uint quietRevision;bool noSaleObserved;
        public bool NoSaleObserved {get{return noSaleObserved;}}
        bool closeAfterCombat;
        bool stealthRequested,stealthExitRequested;long nextStealth;
        bool emergencyRepair;
        public bool AvoidCombat {get{return emergencyRepair&&phase!=Phase.Return&&phase!=Phase.Done;}}
        public void PrioritizeBrokenGear(FarmObservation f){if(f!=null&&f.GearKnown&&f.GearRed&&f.Durability==0&&!emergencyRepair){emergencyRepair=true;if(nav!=null)nav=QuickServicePaths.Navigate(plan,nav.WorkingRoute,f,nav.TargetIndex,true);}}
        readonly bool repair;bool repairIssued;bool sharedService,saleRequired;
        public QuickVendorEngine(QuickRoutePlan plan,OperationObservation start,bool repair=false,bool fromStart=false,bool emergencyRepair=false)
        {
            if(plan==null)throw new ArgumentNullException("plan");plan.Validate(true);var service=repair?plan.Repair:plan.Vendor;if(!service.Enabled)throw new InvalidOperationException(repair?"请先启用并填写修理NPC坐标。":"请先启用并填写售卖NPC坐标。");if(start==null||!start.Fresh||start.Farm==null||!start.Farm.Fresh)throw new InvalidOperationException("商人及背包状态不可读，请确认配套插件已加载。");
            this.emergencyRepair=emergencyRepair&&start.Farm.GearKnown&&start.Farm.GearRed&&start.Farm.Durability==0;this.plan=plan.Clone();this.repair=repair;settings=service.Clone();settings.RequireRoute();var f=start.Farm;if(!plan.MapMatches(f)||f.InCombat&&!this.emergencyRepair||f.PlayerDead||f.PlayerGhost)throw new InvalidOperationException("商人行程须在当前地图存活脱战时开始。");outbound=QuickServicePaths.Outbound(this.plan,settings,f,!fromStart);nav=QuickServicePaths.Navigate(this.plan,outbound,f,0,this.emergencyRepair);phase=nav==null?Phase.Select:Phase.Outbound;
            sharedService=plan.Vendor.Enabled&&plan.Repair.Enabled&&plan.Vendor.X==plan.Repair.X&&plan.Vendor.Y==plan.Repair.Y;saleRequired=!repair||f.BagFull;
        }
        public QuickVendorCheckpoint Checkpoint
        {get{return phase==Phase.Done?null:new QuickVendorCheckpoint{Plan=plan.Clone(),Outbound=outbound.Clone(),Path=nav==null?null:nav.WorkingRoute,Next=nav==null?0:nav.TargetIndex,Returning=phase==Phase.Return,Repair=repair,Emergency=emergencyRepair,StealthRequested=stealthRequested&&!stealthExitRequested};}}
        public QuickVendorEngine(QuickVendorCheckpoint cp,OperationObservation start)
            :this(cp.Plan,start,cp.Repair,false,cp.Emergency)
        {
            if(cp.Outbound==null||!cp.Plan.MapMatches(start.Farm))throw new InvalidOperationException("NPC断点地图或路线无效，保留断点等待检查。");
            cp.Outbound.Validate();outbound=cp.Outbound.Clone();emergencyRepair=cp.Emergency;stealthRequested=cp.StealthRequested;
            if(cp.Path!=null){cp.Path.Validate();if(cp.Next<0||cp.Next>cp.Path.Points.Count)throw new InvalidOperationException("NPC断点路点编号无效。");nav=new Navigator(cp.Path,plan.Calibration,false,false,cp.Next,NavigationPurpose.VendorReturn,emergencyRepair?long.MaxValue:120000){ForwardPulseLimit=3000};phase=cp.Returning?Phase.Return:Phase.Outbound;}
            else {nav=null;phase=Phase.Select;}
        }
        public void ConfirmStealthCommand(QuickVendorCommand c)
        {if(c.Action==QuickVendorAction.Stealth){if(phase==Phase.Outbound)stealthRequested=true;else stealthExitRequested=true;}}
        Navigator ReturnTravel(FarmObservation f){return QuickServicePaths.Navigate(plan,QuickServicePaths.TripReturn(plan,settings,outbound,f),f,0,emergencyRepair);}
        QuickVendorCommand C(QuickVendorAction a,string reason){return new QuickVendorCommand{Action=a,Reason=reason,Token=token,Hash=hash};}
        bool Npc(FarmObservation f){return f.TargetExists&&f.TargetIdentityKnown&&f.TargetToken!=0&&!rejected.Contains(f.TargetToken)&&f.NameHashKnown&&f.AttackabilityKnown&&!f.TargetCanAttack&&!f.TargetIsPlayer&&f.TargetDeadKnown&&!f.TargetDead&&(settings.Name.Length==0||f.TargetNameHash==TargetCatalog.HashName(settings.Name));}
        public bool Matches(QuickVendorCommand c,OperationObservation o,bool ownedChat)
        {
            if(o==null||!o.Fresh||o.Farm==null||!o.Farm.Fresh||!o.Farm.KnownCore||o.Farm.ReadFailures!=0||!plan.MapMatches(o.Farm)||o.Farm.PlayerDead||o.Farm.PlayerGhost||o.Farm.InCombat&&!AvoidCombat)return false;
            if(!o.CursorKnown||!o.CursorEmpty||!o.ConfirmationKnown||o.ConfirmationVisible)return false;
            var f=o.Farm;if(!f.ChatOpenKnown||!ownedChat&&f.ChatOpen||!f.CastingKnown||f.Casting||f.Channeling)return false;
            if(c.Action==QuickVendorAction.Select)return true;
            if(c.Action==QuickVendorAction.Repair)return o.MerchantOpen&&o.MerchantTargetToken==c.Token&&o.MerchantNameHash==c.Hash&&f.RepairButtonVisible&&f.RepairButtonEnabled&&f.RepairCostKnown&&f.RepairCost==c.Cost&&(!f.MoneyKnown||f.Money>=c.Cost)&&f.RepairRegion==c.Region;
            if(c.Token!=0&&(c.Action==QuickVendorAction.Goods||c.Action==QuickVendorAction.Key&&c.Key==70)&&(!Npc(f)||f.TargetToken!=c.Token||f.TargetNameHash!=c.Hash))return false;
            if(c.Action==QuickVendorAction.Goods)return f.GossipGoodsVisible&&f.GossipGoodsRegion==c.Region;
            return true;
        }
        public QuickVendorCommand Step(OperationObservation o,long now)
        {
            if(started<0){started=stateAt=now;}
            if(now<started)return C(QuickVendorAction.Stop,"NPC行程时钟异常。");
            // Navigation owns per-waypoint progress/stuck checks; merchant phases own their timeouts.
            // Do not impose a wall-clock budget on the entire outbound and return trip.
            if(o==null||!o.Fresh||o.Farm==null||!o.Farm.Fresh||!o.Farm.KnownCore||o.Farm.ReadFailures!=0||!plan.MapMatches(o.Farm)||o.Farm.PlayerDead||o.Farm.PlayerGhost)return C(QuickVendorAction.Stop,"商人行程状态或地图失效。");
            if(!o.CursorKnown||!o.CursorEmpty||!o.ConfirmationKnown||o.ConfirmationVisible)return C(QuickVendorAction.Stop,"鼠标持有物品、确认弹窗或状态未知，暂停商人操作。");
            if(o.Farm.InCombat&&!AvoidCombat)return C(QuickVendorAction.None,"先处理攻击者，暂缓售卖行程。");
            if(sequence==o.Sequence)return C(QuickVendorAction.None,"等待商人状态更新。");sequence=o.Sequence;
            var f=o.Farm;
            if(AvoidCombat&&!stealthExitRequested&&now>=nextStealth)
            {
                if(phase==Phase.Outbound&&(f.StealthKnown?!f.Stealthed:!stealthRequested)&&!f.InCombat)
                {nextStealth=now+3000;var stealth=C(QuickVendorAction.Stealth,"零耐久修理：按Q进入潜行，沿未完成路线前往NPC。");stealth.Key=81;return stealth;}
            }
            if(detourPhase!=0&&(phase==Phase.Outbound||phase==Phase.Return))
            {
                if(detourPhase==1)detourFacing=f.Facing;
                else if(!MouseLookInput.TurnObserved(detourFacing,f.Facing))return C(QuickVendorAction.Stop,"脱困转向后未确认朝向改变，停止盲目前进。");
                var detour=C(QuickVendorAction.Key,detourPhase==1?"NPC路线受阻：转向换方向。":"NPC路线脱困：短距离试走，再接回原路线。");
                detour.Key=(ushort)(detourPhase==1?39:87);detour.Hold=350;detourPhase=detourPhase==1?2:0;return detour;
            }
            if(phase==Phase.Close&&closeAfterCombat){closeAfterCombat=false;var close=C(QuickVendorAction.Key,"反击结束，先关闭仍打开的商店再返回巡航。");close.Key=27;return close;}
            if(phase==Phase.Outbound||phase==Phase.Return)
            {
                if(nav==null){phase=phase==Phase.Outbound?Phase.Select:Phase.Done;stateAt=now;}
                else{var n=nav.Step(new NavigationSample{Fresh=f.Fresh,KnownCore=f.KnownCore,ReadFailures=f.ReadFailures,X=f.X,Y=f.Y,Facing=f.Facing,MapId=f.MapId,MapMode=f.MapMode,Zone=f.Zone,Sequence=f.Sequence,BagFull=f.BagFull},now);if(n.Action==NavigationAction.Jump||n.Action==NavigationAction.StrafeLeft){detourPhase=1;var back=C(QuickVendorAction.Key,"NPC路线前进无位移：先后退，再转向绕开障碍。");back.Key=83;back.Hold=250;return back;}if(n.Action==NavigationAction.Stop)return C(QuickVendorAction.Stop,"商人行程受阻："+n.Reason);if(n.Action==NavigationAction.Completed){nav=null;phase=phase==Phase.Outbound?Phase.Select:Phase.Done;stateAt=now;}else if(n.Action!=NavigationAction.None){var c=C(QuickVendorAction.Key,phase==Phase.Outbound?"沿已录制路线前往NPC，第"+(n.TargetIndex+1)+"个路点。":"沿去程逆序返回，第"+(n.TargetIndex+1)+"个路点。");c.Key=movement.For(n.Action);c.Hold=n.HoldMilliseconds;return c;}else return C(QuickVendorAction.None,n.Reason);}
            }
            if(phase==Phase.Select)
            {
                if(!o.FreeSlotsKnown||!o.InventoryEventsKnown||!o.MerchantKnown)return C(QuickVendorAction.Stop,"商人或背包事件不可读，不能确认售卖。");
                if(Npc(f)&&AvoidCombat&&(f.StealthKnown?f.Stealthed:stealthRequested&&!stealthExitRequested)){if(now<nextStealth)return C(QuickVendorAction.None,"等待退出潜行状态后与NPC交互。");nextStealth=now+3000;var exit=C(QuickVendorAction.Stealth,"已抵达NPC，按Q退出潜行后交互修理。");exit.Key=81;return exit;}
                if(Npc(f)){token=f.TargetToken;hash=f.TargetNameHash;free=o.FreeSlots;revision=o.InventoryRevision;opened=o.MerchantOpened;phase=Phase.Open;stateAt=now;var c=C(QuickVendorAction.Key,"与已核验的非玩家NPC交互。");c.Key=70;return c;}
                if(now<nextSelect)return C(QuickVendorAction.None,"等待选择附近商人。");if(++attempts>8)return C(QuickVendorAction.Stop,"未选中商人；请填写NPC名称或核对坐标。");nextSelect=now+1500;var select=C(QuickVendorAction.Select,"在商人坐标选择附近友方NPC。");select.Text=settings.Name.Length>0?"/targetexact "+settings.Name:"/targetfriend";return select;
            }
            if(phase==Phase.Open)
            {
                if(o.MerchantOpen){if(o.MerchantTargetToken!=token||o.MerchantNameHash!=hash)return C(QuickVendorAction.Stop,"商店NPC身份不匹配，停止售卖。");merchantVerified=true;phase=Phase.WaitSale;stateAt=now;}
                else if(f.GossipGoodsVisible){var c=C(QuickVendorAction.Goods,"选择“我想看看你的货物”。");c.Region=f.GossipGoodsRegion;return c;}
                else if(now-stateAt>8000){rejected.Add(token);phase=Phase.Select;token=hash=0;stateAt=now;var close=C(QuickVendorAction.Key,"未打开货物界面，关闭对话并重新选择NPC。");close.Key=27;return close;}
                else return C(QuickVendorAction.None,"等待商店或货物对话选项。");
            }
            if(phase==Phase.WaitSale)
            {
                if(!o.MerchantOpen||o.MerchantTargetToken!=token||o.MerchantNameHash!=hash)return C(QuickVendorAction.Stop,"售卖过程中商店关闭或身份改变。");
                if(o.InventoryKnown&&o.InventoryEventsKnown&&o.FreeSlotsKnown)
                {if(inventoryQuietSince<0||quietRevision!=o.InventoryRevision){quietRevision=o.InventoryRevision;inventoryQuietSince=now;}}
                else inventoryQuietSince=-1;
                bool idleSale=inventoryQuietSince>=0&&now-inventoryQuietSince>=3000;
                if(idleSale&&o.InventoryRevision==revision)noSaleObserved=true;
                if(TwoVisits&&now-stateAt<3000)return C(QuickVendorAction.None,"NPC窗口已打开，等待满3秒后关闭。");
                if(sharedService)
                {
                    bool sold=!saleRequired||idleSale||o.InventoryEventsKnown&&o.InventoryRevision!=revision&&o.FreeSlotsKnown&&o.FreeSlots>free&&!f.BagFull;
                    bool repaired=f.GearKnown&&f.Durability>=10000;
                    if(sold&&repaired){if(saleAt<0)saleAt=now;if(now-saleAt>=500){phase=Phase.Close;closed=o.MerchantClosed;stateAt=now;var close=C(QuickVendorAction.Key,"装备已修复，售卖已有结果或无继续变化，关闭商店后原路返回。");close.Key=27;return close;}}
                    return now-stateAt>20000?C(QuickVendorAction.Stop,"未确认自动售卖和修理全部完成，请检查客户端设置、可售物品及金币。"):C(QuickVendorAction.None,"商店已打开，等待客户端自动售卖及修理完成。");
                }
                if(repair)
                {
                    if(f.GearKnown&&f.Durability>=10000){phase=Phase.Close;closed=o.MerchantClosed;stateAt=now;var close=C(QuickVendorAction.Key,"已确认装备耐久恢复满，关闭商店并返回巡航。");close.Key=27;return close;}
                    if(!repairIssued&&f.GearKnown&&f.RepairButtonVisible&&f.RepairButtonEnabled&&f.RepairCostKnown){if(f.MoneyKnown&&f.Money<f.RepairCost)return C(QuickVendorAction.Stop,"金币不足，无法完成全部修理。");repairIssued=true;var click=C(QuickVendorAction.Repair,"点击已核验商人的全部修理按钮，等待实际耐久恢复。");click.Region=f.RepairRegion;click.Cost=f.RepairCost;return click;}
                    return now-stateAt>15000?C(QuickVendorAction.Stop,"未确认装备修复，请核对NPC修理功能、金币和配套插件。"):C(QuickVendorAction.None,"等待全部修理按钮或实际装备耐久更新。");
                }
                if(idleSale){phase=Phase.Close;closed=o.MerchantClosed;stateAt=now;var close=C(QuickVendorAction.Key,"商店打开后背包已稳定3秒，无继续售卖变化，关闭商店并原路返回；未把等待计为售卖成功。");close.Key=27;return close;}
                if(o.InventoryEventsKnown&&o.InventoryRevision!=revision&&o.FreeSlotsKnown&&o.FreeSlots>free&&!f.BagFull){if(saleAt<0)saleAt=now;if(now-saleAt>=500){phase=Phase.Close;closed=o.MerchantClosed;stateAt=now;var c=C(QuickVendorAction.Key,"已确认背包空位增加，关闭商店并准备返回。");c.Key=27;return c;}}
                if(now-stateAt>20000)return C(QuickVendorAction.Stop,"打开货物界面后未确认空位增加，请核对客户端自动售卖设置。");
                return C(QuickVendorAction.None,"等待自动售卖产生真实背包空位变化。");
            }
            if(phase==Phase.Close){if(o.MerchantOpen||o.MerchantClosed==closed)return now-stateAt>5000?C(QuickVendorAction.Stop,"未确认商店关闭。"):C(QuickVendorAction.None,"等待商店关闭。");if(TwoVisits&&++completedVisits<2){if(!Npc(f)||f.TargetToken!=token||f.TargetNameHash!=hash)return C(QuickVendorAction.Stop,"第二次交互前NPC身份变化，重新核验商人。");phase=Phase.Open;stateAt=now;inventoryQuietSince=-1;saleAt=-1;var reopen=C(QuickVendorAction.Key,"再次打开同一NPC窗口，等待3秒后关闭再原路返回。");reopen.Key=70;return reopen;}nav=ReturnTravel(f);phase=Phase.Return;stateAt=now;return C(QuickVendorAction.None,"沿已走去程逆序返回巡航中断位置。");}
            return phase==Phase.Done?C(QuickVendorAction.Complete,repair?"装备已修复并返回，继续巡航。":"自动售卖往返完成，继续巡航。"):C(QuickVendorAction.None,"等待商人行程。");
        }
    }
}
