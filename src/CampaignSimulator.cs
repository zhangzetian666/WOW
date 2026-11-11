using System;
using System.Collections.Generic;
using System.Drawing;

namespace WoWProbe
{
    /// <summary>A deterministic virtual world. All effects stay in memory; native input is never constructed.</summary>
    public sealed class CampaignSimulator
    {
        readonly CampaignPlan plan;readonly RouteDocument farmRoute;readonly BattleTarget monster,vendor;readonly InventoryItem[] inventory=new InventoryItem[6];
        int sequence,page=-1,selection,selected,monsterHealth=100,lastDeathKill,lastBagKill;
        double facing,health,mana,targetX,targetY,corpseX,corpseY;
        bool playerDead,ghost,monsterDead,lootable,skinnable,attacking,eating,drinking,casting,merchantOpen,bagsOpen,deathInjected,bagInjected;
        long readyAt,skinEnds,corpseReadyAt,bagFullUntil;
        uint lootOpened,lootClosed,castSuccess,skinSuccess,inventoryRevision=1,inventoryCycle,merchantOpened,merchantClosed,bagFullEvents,targetToken=303;
        string lastMode="Farm";
        public readonly CampaignEngine Engine;
        public CampaignSimulator(CampaignPlan plan)
        {
            if(plan==null)throw new ArgumentNullException("plan");this.plan=plan.Clone();this.plan.Validate();Engine=new CampaignEngine(this.plan);
            InjectDeathAfterKills=1;InjectBagFullAfterKills=2;
            farmRoute=this.plan.FarmReverse?this.plan.FarmRoute.Reversed():this.plan.FarmRoute.Clone();
            foreach(var target in this.plan.Battle.Targets){if(target.Kind=="monster"&&monster==null)monster=target.Clone();if(target.Kind=="npc"&&vendor==null)vendor=target.Clone();}
            if(monster==null)throw new ArgumentException("The simulator needs a saved monster target.");
            if(this.plan.Settings.EnableVendor&&vendor==null)throw new ArgumentException("The simulator needs a saved merchant target.");
            var first=farmRoute.Points[Math.Max(0,this.plan.StartIndex-1)];X=first.X;Y=first.Y;
            health=Math.Max(this.plan.Battle.CriticalHealth+1,this.plan.Battle.HealBelow-10);mana=Math.Max(0,this.plan.Battle.ManaBelow-15);
            inventory[0]=MakeItem(1,1001,0,10);inventory[1]=MakeItem(2,1002,1,2);inventory[1].Consumable=true;
            inventory[2]=MakeItem(3,1003,0,1);inventory[2].Quest=true;
            uint keep=this.plan.Settings.ProtectedItemIds.Count==0?1004u:this.plan.Settings.ProtectedItemIds[0];inventory[3]=MakeItem(4,keep,1,10);
            inventory[5]=MakeItem(6,1006,0,0);
        }
        public long ElapsedMilliseconds{get;private set;}
        public int InputEvents{get{return 0;}}
        public double X{get;private set;}public double Y{get;private set;}
        public int Kills{get;private set;}public int SkillCasts{get;private set;}public int Loots{get;private set;}public int Skins{get;private set;}public int Heals{get;private set;}public int Drinks{get;private set;}public int Deaths{get;private set;}public int Resurrections{get;private set;}public int Sales{get;private set;}
        public bool Completed{get{return Engine.Completed;}}
        public bool RepeatInterruptions{get;set;}
        public int InjectDeathAfterKills{get;set;}
        public int InjectBagFullAfterKills{get;set;}
        public bool MissingCorpse{get;set;}public bool NoSupplies{get;set;}public bool WrongMerchant{get;set;}public bool UnknownItemMetadata{get;set;}public bool MerchantClosesBeforeSale{get;set;}
        InventoryItem MakeItem(int slot,uint id,int quality,uint price){return new InventoryItem{Bag=0,Slot=slot,ItemId=id,Count=1,Quality=quality,UnitPrice=price,Fingerprint=id*97u+(uint)slot,Known=true,QuestKnown=true,TypeKnown=true};}
        int FreeSlots(){int free=0;foreach(var item in inventory)if(item==null)free++;return free;}
        string Mode(){var text=Engine.Status;int slash=text.IndexOf('/');return slash<0?text:text.Substring(0,slash);}
        void ClearEncounter(){selected=selection=0;monsterDead=lootable=skinnable=attacking=casting=false;monsterHealth=100;targetToken=(uint)(303+Kills*10+Deaths);}
        public OperationObservation Sample()
        {
            string mode=Mode();if(mode=="Farm"&&lastMode!="Farm")ClearEncounter();lastMode=mode;
            if(mode=="Farm"&&Engine.Status=="Farm/Patrol"&&selected==3&&monsterDead&&!lootable&&!skinnable&&!casting)ClearEncounter();
            sequence=(sequence+1)&65535;page++;if(page==6){page=0;inventoryCycle++;}
            bool exists=selected!=0,isMonster=selected==3,isVendor=selected==4,isPlayer=selected==2;
            string name=isMonster?monster.Name:isVendor&&vendor!=null?vendor.Name:isPlayer?"Synthetic player":exists?"Synthetic friendly NPC":"";
            uint token=isMonster?targetToken:isVendor?77u:isPlayer?202u:exists?101u:0;
            double tx=isMonster?targetX:isVendor&&plan.VendorRoute!=null?plan.VendorRoute.Points[plan.VendorRoute.Points.Count-1].X:X;
            double ty=isMonster?targetY:isVendor&&plan.VendorRoute!=null?plan.VendorRoute.Points[plan.VendorRoute.Points.Count-1].Y:Y;
            bool inRange=exists&&NavigationMath.Distance(tx-X,ty-Y,plan.Battle.Calibration.AxisScale)<=.0025;
            int ready=ElapsedMilliseconds>=readyAt&&!casting?15:0;
            var f=new FarmObservation{Sequence=sequence,Fresh=true,KnownCore=true,Health=(int)Math.Floor(health),Power=(int)Math.Floor(mana),PowerType=0,MapId=farmRoute.MapId,MapMode=farmRoute.MapMode,Zone=farmRoute.Zone,X=X,Y=Y,Facing=facing,PlayerDead=playerDead,PlayerGhost=ghost,InCombat=!playerDead&&!ghost&&isMonster&&!monsterDead&&attacking,BagFull=FreeSlots()==0||ElapsedMilliseconds<bagFullUntil,TargetExists=exists,TargetIdentityKnown=exists,NameHashKnown=exists,TargetToken=token,TargetName=name,TargetNameHash=exists?TargetCatalog.HashName(name):0,TargetDeadKnown=exists,TargetDead=isMonster&&monsterDead,TargetHealth=isMonster?monsterHealth:exists?100:255,AttackabilityKnown=exists,TargetCanAttack=isPlayer||isMonster&&!monsterDead,TargetIsPlayer=isPlayer,RangeKnown=exists,RangeSlot=plan.Battle.RangeSlot,TargetInRange=inRange,TargetPositionKnown=exists,TargetX=exists?tx:Double.NaN,TargetY=exists?ty:Double.NaN,LootKnown=true,Lootable=isMonster&&lootable,SkinKnown=true,Skinnable=isMonster&&skinnable,CastingKnown=true,Casting=casting,ActionsKnown=true,KnownActionsMask=15,ReadyActionsMask=ready,UsableActionsMask=mana>=5?15:0,LootEventsKnown=true,CastEventsKnown=true,SkinSuccessKnown=true,LootOpened=lootOpened,LootClosed=lootClosed,CastSuccess=castSuccess,SkinSuccess=skinSuccess};
            var o=new OperationObservation{Farm=f,Fresh=true,Sequence=sequence,MapId=farmRoute.MapId,FreeSlotsKnown=true,FreeSlots=FreeSlots(),GeometryKnown=true,ClientWidth=plan.Settings.ClientWidth,ClientHeight=plan.Settings.ClientHeight,LayoutToken=123,ReleaseKnown=true,ReleaseVisible=playerDead&&!ghost,ReleaseEnabled=playerDead&&!ghost,ReleaseRegion=playerDead&&!ghost?ReleaseRegion():Rectangle.Empty,ResurrectKnown=true,ResurrectVisible=ghost&&NearCorpse()&&ElapsedMilliseconds>=corpseReadyAt,ResurrectEnabled=ghost&&NearCorpse()&&ElapsedMilliseconds>=corpseReadyAt,ResurrectRegion=ghost&&NearCorpse()&&ElapsedMilliseconds>=corpseReadyAt?ResurrectRegion():Rectangle.Empty,RecoveryDelayKnown=true,RecoveryDelayMs=(int)Math.Max(0,corpseReadyAt-ElapsedMilliseconds),IncomingResurrectionKnown=true,CorpseKnown=(playerDead||ghost)&&!MissingCorpse,CorpseX=(playerDead||ghost)&&!MissingCorpse?corpseX:Double.NaN,CorpseY=(playerDead||ghost)&&!MissingCorpse?corpseY:Double.NaN,MerchantKnown=true,MerchantOpen=merchantOpen,MerchantOpened=merchantOpened,MerchantClosed=merchantClosed,MerchantTargetToken=merchantOpen?(WrongMerchant?99u:77u):0,MerchantNameHash=merchantOpen&&vendor!=null?vendor.NameHash:0,CursorKnown=true,CursorEmpty=true,ConfirmationKnown=true,SellTabKnown=true,SellTab=merchantOpen,InventoryKnown=true,InventoryEventsKnown=true,InventoryCycleKnown=true,BagEventsKnown=true,InventoryRevision=inventoryRevision,InventoryCycle=inventoryCycle,BagFullEvents=bagFullEvents,BagSizes=new byte[]{6,0,0,0,0},Occupancy=new byte[25],PageIndex=page,TotalSlots=6};
            for(int slot=0;slot<6;slot++)if(inventory[slot]!=null)o.Occupancy[0]|=(byte)(1<<slot);
            var actual=inventory[page];o.Item=actual==null?new InventoryItem{Bag=0,Slot=page+1,Known=true,Empty=true,Quality=255,UnitPrice=0,Fingerprint=(uint)(page+1),QuestKnown=true,TypeKnown=true}:actual.Clone();
            o.Item.RectangleKnown=merchantOpen&&bagsOpen;o.Item.Region=o.Item.RectangleKnown?ItemRegion(page+1):Rectangle.Empty;
            if(UnknownItemMetadata&&!o.Item.Empty){o.Item.Known=false;o.Item.Quality=255;o.Item.UnitPrice=UInt32.MaxValue;o.Item.Fingerprint=0;}
            return o;
        }
        Rectangle ReleaseRegion(){return new Rectangle(Math.Max(1,plan.Settings.ClientWidth/2-70),Math.Max(1,plan.Settings.ClientHeight/2),140,30);}
        Rectangle ResurrectRegion(){return new Rectangle(Math.Max(1,plan.Settings.ClientWidth/2-70),Math.Max(1,plan.Settings.ClientHeight/2+40),140,30);}
        Rectangle ItemRegion(int slot){return new Rectangle(Math.Max(8,plan.Settings.ClientWidth-150)+(slot-1)%3*40,Math.Max(112,plan.Settings.ClientHeight-110)+(slot-1)/3*40,32,32);}
        bool NearCorpse(){return NavigationMath.Distance(X-corpseX,Y-corpseY,plan.Battle.Calibration.AxisScale)<=plan.Settings.CorpseTolerance;}
        public OperationCommand Step(){var o=Sample();var c=Engine.Step(o,ElapsedMilliseconds);if(c.Action==OperationAction.Key||c.Action==OperationAction.LeftClick||c.Action==OperationAction.RightClick)Apply(c);else Advance(200);return c;}
        public void Apply(OperationCommand c)
        {
            if(c==null)throw new ArgumentNullException("command");
            if(c.Action==OperationAction.None||c.Action==OperationAction.Stop||c.Action==OperationAction.Completed||c.Action==OperationAction.ResumeFarm){if(c.HoldMilliseconds!=0)throw new ArgumentException("Non-input commands cannot hold virtual input.");return;}
            if(c.HoldMilliseconds<60||c.HoldMilliseconds>250)throw new ArgumentException("Virtual input must be bounded from60 to250 milliseconds.");
            if(c.Action==OperationAction.LeftClick)
            {
                if(c.Purpose==OperationPurpose.Release){if(!playerDead||ghost||c.Region!=ReleaseRegion())throw new InvalidOperationException("Virtual release requires the actual dead state and enabled release button.");ghost=true;playerDead=false;corpseReadyAt=ElapsedMilliseconds+1000;selected=0;eating=drinking=casting=attacking=false;var grave=plan.CorpseRoute.Points[0];X=grave.X;Y=grave.Y;facing=0;}
                else if(c.Purpose==OperationPurpose.Resurrect){if(!ghost||MissingCorpse||!NearCorpse()||ElapsedMilliseconds<corpseReadyAt||c.Region!=ResurrectRegion())throw new InvalidOperationException("Virtual resurrection requires actual ghost, corpse range, zero cooldown and button evidence.");ghost=playerDead=false;Resurrections++;health=Math.Max(1,plan.Battle.CriticalHealth-5);mana=20;ClearEncounter();}
                else throw new InvalidOperationException("Unexpected virtual left-click purpose.");
            }
            else if(c.Action==OperationAction.RightClick)
            {
                if(c.Purpose!=OperationPurpose.Sell||playerDead||ghost||!merchantOpen||!bagsOpen||WrongMerchant||UnknownItemMetadata||selected!=4||c.ExpectedTargetToken!=77||vendor==null||c.ExpectedNameHash!=vendor.NameHash||c.ExpectedInventoryRevision!=inventoryRevision||c.Bag!=0||c.Slot<1||c.Slot>6)throw new InvalidOperationException("Virtual sale lacks the actual merchant and unchanged inventory destination.");
                var item=inventory[c.Slot-1];if(!SalePolicy.CanSell(item,plan.Settings)||c.ExpectedItemId!=item.ItemId||c.ExpectedItemCount!=item.Count||c.ExpectedItemFingerprint!=item.Fingerprint||c.Region!=ItemRegion(c.Slot))throw new InvalidOperationException("Virtual sale item does not match an eligible item and visible slot.");
                inventory[c.Slot-1]=null;inventoryRevision++;Sales++;
            }
            else if(c.Action==OperationAction.Key)
            {
                if(c.Purpose==OperationPurpose.Farm){if(c.Farm==null||c.Key!=CampaignEngine.FarmKey(c.Farm.Action,plan.Battle.Movement))throw new InvalidOperationException("Virtual farm key does not match its typed command.");Farm(c.Farm);}
                else if(c.Purpose==OperationPurpose.Travel){if(playerDead||casting||merchantOpen)throw new InvalidOperationException("Virtual travel cannot move a dead/casting player or open merchant.");Move(KeyMovement(c.Key),c.HoldMilliseconds);}
                else if(c.Purpose==OperationPurpose.NpcSelect){if(playerDead||ghost||c.Key!=plan.Settings.NpcSelectKey||vendor==null||plan.VendorRoute==null)throw new InvalidOperationException("Virtual NPC selection requires the configured friendly selection binding.");var at=plan.VendorRoute.Points[plan.VendorRoute.Points.Count-1];if(NavigationMath.Distance(X-at.X,Y-at.Y,plan.Battle.Calibration.AxisScale)>plan.Settings.JoinTolerance)throw new InvalidOperationException("Virtual NPC selection is outside the recorded vendor destination.");selected=4;attacking=false;}
                else if(c.Purpose==OperationPurpose.MerchantOpen){if(playerDead||ghost||selected!=4||vendor==null||c.Key!=0x46||c.ExpectedTargetToken!=77||c.ExpectedNameHash!=vendor.NameHash)throw new InvalidOperationException("Virtual merchant interaction requires the selected saved NPC.");merchantOpen=true;merchantOpened++;bagsOpen=false;if(plan.Settings.AutoCleanOnly){for(int i=0;i<inventory.Length;i++)if(SalePolicy.CanSell(inventory[i],plan.Settings)){inventory[i]=null;inventoryRevision++;Sales++;}}}
                else if(c.Purpose==OperationPurpose.BagsOpen){if(!merchantOpen||selected!=4||c.Key!=0x42)throw new InvalidOperationException("Virtual bag opening requires the current merchant.");bagsOpen=!bagsOpen;if(MerchantClosesBeforeSale){merchantOpen=false;merchantClosed++;bagsOpen=false;}}
                else if(c.Purpose==OperationPurpose.CloseUi){if(c.Key!=0x1B)throw new InvalidOperationException("Virtual cleanup requires Escape.");if(merchantOpen){merchantOpen=false;merchantClosed++;bagsOpen=false;selected=0;}}
                else if(c.Purpose==OperationPurpose.RecoveryRest){if(playerDead||ghost)throw new InvalidOperationException("Virtual supplies require confirmed life.");Supplies(c.Key);}
                else throw new InvalidOperationException("Unsupported virtual key purpose.");
            }
            else throw new InvalidOperationException("Unsupported virtual operation action.");
            Advance(c.HoldMilliseconds);
        }
        void Farm(FarmCommand c)
        {
            if(playerDead||ghost||merchantOpen)throw new InvalidOperationException("Virtual farming requires a living player without a merchant window.");
            if(c.RequireAliveTarget||c.RequireDeadTarget)
            {if(selected!=3||c.ExpectedTargetToken!=targetToken||c.RequireAliveTarget&&monsterDead||c.RequireDeadTarget&&!monsterDead)throw new InvalidOperationException("Virtual farm action no longer has its original live/corpse identity.");}
            switch(c.Action)
            {
                case FarmAction.Forward:case FarmAction.TurnLeft:case FarmAction.TurnRight:case FarmAction.StrafeLeft:case FarmAction.StrafeRight:case FarmAction.Jump:Move(c.Action,c.HoldMilliseconds);break;
                case FarmAction.SelectTarget:selection=(selection%3)+1;selected=selection;if(selected==3){monsterHealth=100;monsterDead=lootable=skinnable=attacking=false;PlaceMonster();}break;
                case FarmAction.Skill1:case FarmAction.Skill2:case FarmAction.Skill3:case FarmAction.Skill4:
                    if(selected!=3||monsterDead||casting||ElapsedMilliseconds<readyAt||mana<5||NavigationMath.Distance(targetX-X,targetY-Y,plan.Battle.Calibration.AxisScale)>.0025)throw new InvalidOperationException("Virtual skill requires a ready usable action and the actual locked monster in range.");
                    eating=drinking=false;attacking=true;SkillCasts++;castSuccess++;monsterHealth=Math.Max(0,monsterHealth-40);health=Math.Max(plan.Battle.CriticalHealth+1,health-12);mana=Math.Max(0,mana-8);readyAt=ElapsedMilliseconds+500;
                    if(monsterHealth==0){Kills++;monsterDead=true;lootable=true;skinnable=plan.Battle.Skinning;attacking=false;InjectInterruptions();}break;
                case FarmAction.Interact:if(selected!=3||!monsterDead||!lootable)throw new InvalidOperationException("Virtual loot requires a real lootable corpse.");lootOpened++;lootClosed++;lootable=false;Loots++;break;
                case FarmAction.Skin:if(selected!=3||!monsterDead||lootable||!skinnable||casting)throw new InvalidOperationException("Virtual skin requires a looted skinnable corpse.");casting=true;skinEnds=ElapsedMilliseconds+plan.Battle.SkinWaitMs;break;
                case FarmAction.Heal:Supplies(0x38);break;case FarmAction.Mana:Supplies(0x39);break;
                default:throw new InvalidOperationException("Unsupported virtual farm command.");
            }
        }
        void Supplies(ushort key){if(key!=0x38&&key!=0x39)throw new InvalidOperationException("Virtual supply binding is unsupported.");if(NoSupplies)return;if(key==0x38){eating=true;Heals++;}else{drinking=true;Drinks++;}}
        void InjectInterruptions()
        {
            int spacing=Math.Max(3,Math.Max(0,InjectDeathAfterKills)+Math.Max(0,InjectBagFullAfterKills));
            if(InjectDeathAfterKills>0&&Kills>=InjectDeathAfterKills&&(!deathInjected||RepeatInterruptions&&Kills>=lastDeathKill+spacing))
            {deathInjected=true;lastDeathKill=Kills;Deaths++;corpseX=X;corpseY=Y;playerDead=true;ghost=false;health=0;eating=drinking=casting=attacking=false;}
            if(InjectBagFullAfterKills>0&&Kills>=InjectBagFullAfterKills&&(!bagInjected||RepeatInterruptions&&Kills>=lastBagKill+spacing))
            {bagInjected=true;lastBagKill=Kills;inventory[0]=MakeItem(1,1001,0,10);inventory[4]=MakeItem(5,1005,0,10);inventoryRevision++;bagFullEvents++;bagFullUntil=ElapsedMilliseconds+5000;}
        }
        void PlaceMonster()
        {
            double best=Double.MaxValue;int chosen=0;double fraction=0;double scale=plan.Battle.Calibration.AxisScale;
            for(int i=0;i<farmRoute.Points.Count-1;i++){var a=farmRoute.Points[i];var b=farmRoute.Points[i+1];double dx=(b.X-a.X)*scale,dy=b.Y-a.Y,len=dx*dx+dy*dy;double t=len==0?0:Math.Max(0,Math.Min(1,((X-a.X)*scale*dx+(Y-a.Y)*dy)/len));double d=NavigationMath.Distance(X-(a.X+(b.X-a.X)*t),Y-(a.Y+(b.Y-a.Y)*t),scale);if(d<best){best=d;chosen=i;fraction=t;}}
            var first=farmRoute.Points[chosen];var next=farmRoute.Points[chosen+1];double distance=NavigationMath.Distance(next.X-first.X,next.Y-first.Y,scale);double along=distance==0?fraction:Math.Min(1,fraction+.006/distance);targetX=first.X+(next.X-first.X)*along;targetY=first.Y+(next.Y-first.Y)*along;
        }
        FarmAction KeyMovement(ushort key){var b=plan.Battle.Movement;foreach(var action in new[]{FarmAction.Forward,FarmAction.TurnLeft,FarmAction.TurnRight,FarmAction.StrafeLeft,FarmAction.StrafeRight,FarmAction.Jump})if(CampaignEngine.FarmKey(action,b)==key)return action;throw new InvalidOperationException("Virtual travel key is not a configured movement key.");}
        void Move(FarmAction action,int hold)
        {
            eating=drinking=false;var c=plan.Battle.Calibration;double seconds=hold/1000.0;double compass=NavigationMath.Normalize(NavigationMath.Normalize(c.FacingSign*facing)+NavigationMath.Normalize(c.FacingOffset));
            if(action==FarmAction.TurnLeft||action==FarmAction.TurnRight){compass+=(action==FarmAction.TurnRight?1:-1)*c.TurnRadiansPerSecond*seconds;facing=NavigationMath.PositiveAngle(c.FacingSign*(NavigationMath.Normalize(compass)-NavigationMath.Normalize(c.FacingOffset)));}
            else if(action!=FarmAction.Jump){if(action==FarmAction.StrafeLeft)compass-=Math.PI/2;if(action==FarmAction.StrafeRight)compass+=Math.PI/2;double amount=c.ForwardUnitsPerSecond*seconds;X=Math.Max(0,Math.Min(1,X+Math.Sin(compass)*amount/c.AxisScale));Y=Math.Max(0,Math.Min(1,Y-Math.Cos(compass)*amount));}
        }
        public void Advance(int ms)
        {
            if(ms<0||ElapsedMilliseconds>Int64.MaxValue-ms)throw new ArgumentOutOfRangeException("ms");ElapsedMilliseconds+=ms;
            if(!NoSupplies&&!playerDead&&!ghost){if(eating)health=Math.Min(100,health+ms*.008);if(drinking)mana=Math.Min(100,mana+ms*.01);}
            if(casting&&ElapsedMilliseconds>=skinEnds){casting=false;skinnable=false;skinSuccess++;castSuccess++;Skins++;}
        }
    }
}
