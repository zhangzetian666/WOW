using System;
using System.Drawing;
namespace WoWProbe
{
    public sealed class QuickVendorSettings
    {
        public bool Enabled;public double X,Y;public string Name="";
        public void Validate(){if(Name==null||Name.Length>128)throw new FormatException("商人名称过长。");foreach(char c in Name)if(Char.IsControl(c)||c=='/')throw new FormatException("商人名称不能含控制字符或斜线。");if(!NavigationMath.InRange(X,0,1)||!NavigationMath.InRange(Y,0,1)||Enabled&&X==0&&Y==0)throw new FormatException("请填写商人X、Y坐标（0–100）。");}
        public QuickVendorSettings Clone(){return (QuickVendorSettings)MemberwiseClone();}
    }
    public enum QuickVendorAction {None,Key,Select,Goods,Repair,Complete,Stop}
    public sealed class QuickVendorCommand
    {public QuickVendorAction Action;public ushort Key;public int Hold=80;public string Text,Reason;public Rectangle Region;public uint Token,Hash,Cost;}
    // Opens the actual merchant and observes the user's existing auto-sell.
    // This driver never clicks an inventory item or invents a sale result.
    public sealed class QuickVendorEngine
    {
        enum Phase {Outbound,Select,Open,WaitSale,Close,Return,Done}
        readonly QuickRoutePlan plan;readonly QuickVendorSettings settings;readonly double startX,startY;Navigator nav;Phase phase;
        readonly RouteKeyBindings movement=new RouteKeyBindings{TurnLeft=37,TurnRight=39};
        readonly System.Collections.Generic.HashSet<uint> rejected=new System.Collections.Generic.HashSet<uint>();
        public void ResumeAfterCombat(OperationObservation o,long now)
        {bool done=merchantVerified&&(repair?o.Farm.GearKnown&&o.Farm.Durability>=10000:o.FreeSlotsKnown&&o.InventoryEventsKnown&&o.InventoryRevision!=revision&&o.FreeSlots>free&&!o.Farm.BagFull);nav=Travel(o.Farm.X,o.Farm.Y,done?startX:settings.X,done?startY:settings.Y);phase=done?Phase.Return:nav==null?Phase.Select:Phase.Outbound;stateAt=now;sequence=-1;token=hash=0;if(done&&o.MerchantOpen){phase=Phase.Close;closed=o.MerchantClosed;closeAfterCombat=true;}if(!done){repairIssued=false;merchantVerified=false;saleAt=-1;}}
        long started=-1,stateAt,nextSelect,saleAt=-1;int sequence=-1,attempts;uint token,hash,revision,opened,closed;int free;
        bool merchantVerified;
        bool closeAfterCombat;
        readonly bool repair;bool repairIssued;
        public QuickVendorEngine(QuickRoutePlan plan,OperationObservation start,bool repair=false)
        {
            if(plan==null)throw new ArgumentNullException("plan");plan.Validate(true);var service=repair?plan.Repair:plan.Vendor;if(!service.Enabled)throw new InvalidOperationException(repair?"请先启用并填写修理NPC坐标。":"请先启用并填写售卖NPC坐标。");if(start==null||!start.Fresh||start.Farm==null||!start.Farm.Fresh)throw new InvalidOperationException("商人及背包状态不可读，请确认配套插件已加载。");
            this.plan=plan.Clone();this.repair=repair;settings=service.Clone();var f=start.Farm;if(!plan.MapMatches(f)||f.InCombat||f.PlayerDead||f.PlayerGhost)throw new InvalidOperationException("商人行程须在当前地图存活脱战时开始。");startX=f.X;startY=f.Y;nav=Travel(startX,startY,settings.X,settings.Y);phase=nav==null?Phase.Select:Phase.Outbound;
        }
        Navigator Travel(double x,double y,double tx,double ty)
        {
            if(NavigationMath.Distance(tx-x,ty-y,plan.Calibration.AxisScale)<=.0015)return null;
            var r=new RouteDocument{RouteId="quick-vendor",Name="商人往返",RouteType="vendor_return",MapId=plan.Route.MapId,MapMode=plan.Route.MapMode,Zone=plan.Route.Zone};r.Points.Add(new Waypoint{X=x,Y=y,Tolerance=.001});r.Points.Add(new Waypoint{X=tx,Y=ty,Tolerance=.0015});return new Navigator(r,plan.Calibration,false,false,0,NavigationPurpose.VendorReturn,120000);
        }
        QuickVendorCommand C(QuickVendorAction a,string reason){return new QuickVendorCommand{Action=a,Reason=reason,Token=token,Hash=hash};}
        bool Npc(FarmObservation f){return f.TargetExists&&f.TargetIdentityKnown&&f.TargetToken!=0&&!rejected.Contains(f.TargetToken)&&f.NameHashKnown&&f.AttackabilityKnown&&!f.TargetCanAttack&&!f.TargetIsPlayer&&f.TargetDeadKnown&&!f.TargetDead&&(settings.Name.Length==0||f.TargetNameHash==TargetCatalog.HashName(settings.Name));}
        public bool Matches(QuickVendorCommand c,OperationObservation o,bool ownedChat)
        {
            if(o==null||!o.Fresh||o.Farm==null||!o.Farm.Fresh||!o.Farm.KnownCore||o.Farm.ReadFailures!=0||!plan.MapMatches(o.Farm)||o.Farm.PlayerDead||o.Farm.PlayerGhost||o.Farm.InCombat)return false;
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
            if(now<started||now-started>360000)return C(QuickVendorAction.Stop,"六分钟内未完成商人往返，停止检查路线及NPC。");
            if(o==null||!o.Fresh||o.Farm==null||!o.Farm.Fresh||!o.Farm.KnownCore||o.Farm.ReadFailures!=0||!plan.MapMatches(o.Farm)||o.Farm.PlayerDead||o.Farm.PlayerGhost)return C(QuickVendorAction.Stop,"商人行程状态或地图失效。");
            if(!o.CursorKnown||!o.CursorEmpty||!o.ConfirmationKnown||o.ConfirmationVisible)return C(QuickVendorAction.Stop,"鼠标持有物品、确认弹窗或状态未知，暂停商人操作。");
            if(o.Farm.InCombat)return C(QuickVendorAction.None,"先处理攻击者，暂缓售卖行程。");
            if(sequence==o.Sequence)return C(QuickVendorAction.None,"等待商人状态更新。");sequence=o.Sequence;
            var f=o.Farm;
            if(phase==Phase.Close&&closeAfterCombat){closeAfterCombat=false;var close=C(QuickVendorAction.Key,"反击结束，先关闭仍打开的商店再返回巡航。");close.Key=27;return close;}
            if(phase==Phase.Outbound||phase==Phase.Return)
            {
                if(nav==null){phase=phase==Phase.Outbound?Phase.Select:Phase.Done;stateAt=now;}
                else{var n=nav.Step(new NavigationSample{Fresh=f.Fresh,KnownCore=f.KnownCore,ReadFailures=f.ReadFailures,X=f.X,Y=f.Y,Facing=f.Facing,MapId=f.MapId,MapMode=f.MapMode,Zone=f.Zone,Sequence=f.Sequence,BagFull=f.BagFull},now);if(n.Action==NavigationAction.Stop)return C(QuickVendorAction.Stop,"商人行程受阻："+n.Reason);if(n.Action==NavigationAction.Completed){nav=null;phase=phase==Phase.Outbound?Phase.Select:Phase.Done;stateAt=now;}else if(n.Action!=NavigationAction.None){var c=C(QuickVendorAction.Key,phase==Phase.Outbound?"前往售卖NPC坐标。":"售卖结束，返回巡航中断位置。");c.Key=movement.For(n.Action);c.Hold=n.HoldMilliseconds;return c;}else return C(QuickVendorAction.None,n.Reason);}
            }
            if(phase==Phase.Select)
            {
                if(!o.FreeSlotsKnown||!o.InventoryEventsKnown||!o.MerchantKnown)return C(QuickVendorAction.Stop,"商人或背包事件不可读，不能确认售卖。");
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
                if(repair)
                {
                    if(f.GearKnown&&f.Durability>=10000){phase=Phase.Close;closed=o.MerchantClosed;stateAt=now;var close=C(QuickVendorAction.Key,"已确认装备耐久恢复满，关闭商店并返回巡航。");close.Key=27;return close;}
                    if(!repairIssued&&f.GearKnown&&f.RepairButtonVisible&&f.RepairButtonEnabled&&f.RepairCostKnown){if(f.MoneyKnown&&f.Money<f.RepairCost)return C(QuickVendorAction.Stop,"金币不足，无法完成全部修理。");repairIssued=true;var click=C(QuickVendorAction.Repair,"点击已核验商人的全部修理按钮，等待实际耐久恢复。");click.Region=f.RepairRegion;click.Cost=f.RepairCost;return click;}
                    return now-stateAt>15000?C(QuickVendorAction.Stop,"未确认装备修复，请核对NPC修理功能、金币和配套插件。"):C(QuickVendorAction.None,"等待全部修理按钮或实际装备耐久更新。");
                }
                if(o.InventoryEventsKnown&&o.InventoryRevision!=revision&&o.FreeSlotsKnown&&o.FreeSlots>free&&!f.BagFull){if(saleAt<0)saleAt=now;if(now-saleAt>=500){phase=Phase.Close;closed=o.MerchantClosed;stateAt=now;var c=C(QuickVendorAction.Key,"已确认背包空位增加，关闭商店并准备返回。");c.Key=27;return c;}}
                if(now-stateAt>20000)return C(QuickVendorAction.Stop,"打开货物界面后未确认空位增加，请核对客户端自动售卖设置。");
                return C(QuickVendorAction.None,"等待自动售卖产生真实背包空位变化。");
            }
            if(phase==Phase.Close){if(o.MerchantOpen||o.MerchantClosed==closed)return now-stateAt>5000?C(QuickVendorAction.Stop,"未确认商店关闭。"):C(QuickVendorAction.None,"等待商店关闭。");nav=Travel(f.X,f.Y,startX,startY);phase=Phase.Return;stateAt=now;return C(QuickVendorAction.None,"沿来路返回巡航中断位置。");}
            return phase==Phase.Done?C(QuickVendorAction.Complete,repair?"装备已修复并返回，继续巡航。":"自动售卖往返完成，继续巡航。"):C(QuickVendorAction.None,"等待商人行程。");
        }
    }
}
