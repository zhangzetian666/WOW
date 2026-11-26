using System;
using System.Drawing;

namespace WoWProbe
{
    /// <summary>Validated read-only WC3 extension. Capability bits distinguish false from unavailable.</summary>
    public sealed class CombatPacket
    {
        public int Sequence { get; private set; }
        public int Capabilities { get; private set; }
        public int Flags { get; private set; }
        public int RangeSlot { get; private set; }
        public bool ExtendedActionRanges {get;private set;}
        public bool RangeErrorSupported {get;private set;}public bool RangeErrorActive {get;private set;}
        public bool ExtendedActionStates {get;private set;}
        public bool PetStateSupported {get;private set;}public bool PetStateKnown {get;private set;}public bool PetExists {get;private set;}public bool PetDead {get;private set;}
        public int KnownActionsMask { get; private set; }
        public int ReadyActionsMask { get; private set; }
        public int UsableActionsMask { get; private set; }
        public int ActionRangeKnownMask {get;private set;}public int ActionsInRangeMask {get;private set;}
        public int RepeatKnownMask {get;private set;}public int RepeatingMask {get;private set;}
        public int RangedActionsMask {get;private set;}public int MeleeActionsMask {get;private set;}
        public int ExtraKnownFlags { get; private set; }
        public uint TargetToken { get; private set; }
        public uint TargetNameHash { get; private set; }
        public uint CastEndMilliseconds { get; private set; }
        public uint LootOpened { get; private set; }
        public uint LootClosed { get; private set; }
        public uint CastSuccess { get; private set; }
        public uint MeleeHitToken {get;private set;}public uint MeleeHits {get;private set;}
        public bool MeleeEventsKnown {get;private set;}
        public uint CastFailed { get; private set; }
        public uint SkinSuccess { get; private set; }
        public uint DeathToken { get; private set; }
        public uint TargetDeaths { get; private set; }
        public bool ChatOpenKnown { get; private set; }
        public bool ChatOpen { get; private set; }
        public bool KillCreditsKnown { get; private set; }
        public uint KillCreditNameHash { get; private set; }
        public uint KillCredits { get; private set; }
        public bool SkinBindingKnown {get;private set;}public bool SkinBindingCorrect {get;private set;}
        public int FoodCount {get;private set;}
        public bool TargetClaimKnown {get;private set;}public bool TargetClaimedByOther {get;private set;}
        public bool TargetCombatKnown {get;private set;}public bool TargetInCombat {get;private set;}
        public bool TargetVictimKnown {get;private set;}public bool TargetTargetsPlayer {get;private set;}
        public bool GossipGoodsVisible {get;private set;}public Rectangle GossipGoodsRegion {get;private set;}
        public bool StealthKnown {get;private set;}public bool Stealthed {get;private set;}
        public bool InteractBindingKnown {get;private set;}public bool InteractBindingCorrect {get;private set;}
        public bool ClickToMoveKnown {get;private set;}public bool ClickToMoveEnabled {get;private set;}
        public bool GearKnown {get;private set;}public bool GearRed {get;private set;}public int Durability {get;private set;}
        public bool RepairButtonVisible {get;private set;}public bool RepairButtonEnabled {get;private set;}public Rectangle RepairRegion {get;private set;}
        public bool RepairCostKnown {get;private set;}public uint RepairCost {get;private set;}public bool MoneyKnown {get;private set;}public uint Money {get;private set;}
        public double TargetX { get; private set; }
        public double TargetY { get; private set; }
        public Rectangle Portrait { get; private set; }
        public bool TargetHoverKnown {get;private set;}public bool TargetHovered {get;private set;}

        private CombatPacket() { }
        public static CombatPacket Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 128) throw Invalid("WC3 packet must contain exactly 128 bytes.");
            byte[] b=(byte[])bytes.Clone();
            if(b[0]!=87 || b[1]!=67 || b[2]!=51 || b[3]<1||b[3]>5) throw Invalid("Unsupported WC3 signature or version.");
            int a=0,s=0;for(int i=0;i<126;i++){a=(a+b[i])%255;s=(s+a)%255;}
            if(b[126]!=a || b[127]!=s)throw Invalid("WC3 Fletcher16 checksum mismatch.");
            if(b[118]!=0&&b[118]!=1&&b[118]!=3)throw Invalid("Invalid stealth flags.");
            if(b[119]!=0&&b[119]!=1&&b[119]!=3)throw Invalid("Invalid interact binding flags.");
            if(b[120]!=0&&b[120]!=1&&b[120]!=3)throw Invalid("Invalid click-to-move flags.");
            int hover=b[125]&3;
            if(hover==2||(b[125]&~(b[3]>=5?63:b[3]>=3?7:3))!=0||(b[125]&7)!=0&&(b[6]&1)==0)throw Invalid("WC3 target hover/range error needs readable target identity.");
            int pet=b[125]&56;if(pet!=0&&pet!=8&&pet!=24&&pet!=56)throw Invalid("WC3 pet state flags disagree.");
            int extraRangeMask=b[3]>=4?112:b[3]>=2?48:0;
            int rangeKnown=(b[121]&15)|(b[123]&extraRangeMask),inRange=(b[121]>>4)|(b[124]&extraRangeMask);
            if((inRange&~rangeKnown)!=0||((b[122]>>4)&~(b[122]&15))!=0||b[123]>(15|extraRangeMask)||b[124]>(15|extraRangeMask)||(b[123]&b[124]&15)!=0)throw Invalid("WC3 per-action range or repeat masks disagree.");
            if(b[109]>1||b[109]==0&&(U32(b,110)!=0||U32(b,114)!=0)||(U32(b,110)==0)!=(U32(b,114)==0))throw Invalid("WC3 melee evidence is inconsistent.");
            int durability=U16(b,90),repair=b[92],rx=U16(b,93),ry=U16(b,95),rw=U16(b,97),rh=U16(b,99);
            if((b[89]!=0&&b[89]!=1&&b[89]!=3)||b[89]==0&&durability!=0||durability>10000||b[89]==3&&durability!=0)throw Invalid("WC3 durability evidence is inconsistent.");
            if((repair&~31)!=0||(repair&2)!=0&&(repair&1)==0||(repair&4)!=0&&(repair&3)!=3||((repair&2)!=0?(rw==0||rh==0||rw>512||rh>512||rx+rw>65535||ry+rh>65535):(rx!=0||ry!=0||rw!=0||rh!=0))||(repair&8)==0&&U32(b,101)!=0||(repair&16)==0&&U32(b,105)!=0)throw Invalid("WC3 repair button or cost evidence is inconsistent.");
            if(b[80]!=0&&b[80]!=1&&b[80]!=3&&b[80]!=7)throw Invalid("WC3 gossip flags are inconsistent.");
            int gx=U16(b,81),gy=U16(b,83),gw=U16(b,85),gh=U16(b,87);if(b[80]==7?(gw==0||gh==0||gw>2048||gh>512||gx+gw>65535||gy+gh>65535):(gx!=0||gy!=0||gw!=0||gh!=0))throw Invalid("WC3 goods option rectangle is invalid.");
            if((b[79]&~63)!=0||(b[79]&3)==2||(b[79]&12)==8||(b[79]&48)==32)throw Invalid("WC3 target ownership flags are inconsistent.");
            if(b[75]>3||b[75]==2||b[76]>1||b[76]==0&&U16(b,77)!=0||b[76]==1&&U16(b,77)==65535)throw Invalid("WC3 optional action setup is inconsistent.");
            uint creditName=U32(b,66),credits=U32(b,70);
            if(b[74]>1||b[74]==0&&(creditName!=0||credits!=0)||(creditName==0)!=(credits==0))throw Invalid("WC3 kill credit evidence is inconsistent.");
            if((b[65]&~3)!=0||b[65]==2)throw Invalid("WC3 chat focus flags are inconsistent.");
            int c=b[6], f=b[7], k=b[56];
            if((f&128)!=0 || (k&~63)!=0 || b[16]<1 || b[16]>4)throw Invalid("Invalid WC3 flags or range slot.");
            uint deathToken=U32(b,57),deaths=U32(b,61);
            if((k&32)==0&&(deathToken!=0||deaths!=0)||(deathToken==0)!=(deaths==0))throw Invalid("WC3 death evidence is inconsistent.");
            Flag(c,2,f,3);Flag(c,4,f,4);Flag(c,16,f,8);Flag(c,32,f,16);Flag(c,64,f,96);
            uint token=U32(b,8), name=U32(b,44), end=U32(b,20);
            Match(c,1,token!=0,"target identity");Match(k,4,name!=0,"full target name hash");
            int x=U16(b,12),y=U16(b,14);
            if((c&8)!=0 ? x==65535 || y==65535 : x!=65535 || y!=65535)throw Invalid("WC3 position capability disagrees with coordinates.");
            if((f&96)==96 || ((f&96)!=0)!=(end!=0))throw Invalid("WC3 cast end disagrees with cast/channel state.");
            int known=b[17],ready=b[18],usable=b[19];
            if((known&~(b[3]>=4?127:15))!=0 || (ready&~known)!=0 || (usable&~known)!=0)throw Invalid("WC3 action masks disagree.");
            Match(c,128,known!=0,"action knowledge");
            uint opened=U32(b,24),closed=U32(b,28),success=U32(b,32),failed=U32(b,36),skin=U32(b,40);
            if((k&8)==0 && (opened!=0 || closed!=0))throw Invalid("Unknown WC3 loot counters must be zero.");
            if((k&16)==0 && (success!=0 || failed!=0))throw Invalid("Unknown WC3 cast counters must be zero.");
            if((k&2)==0 && skin!=0)throw Invalid("Unknown WC3 skin counter must be zero.");
            int px=U16(b,48),py=U16(b,50),pw=U16(b,52),ph=U16(b,54);
            if((k&1)!=0 ? pw==0 || ph==0 || pw>512 || ph>512 || px+pw>65535 || py+ph>65535 : px!=0 || py!=0 || pw!=0 || ph!=0)
                throw Invalid("WC3 portrait capability disagrees with client pixel rectangle.");
            return new CombatPacket { Sequence=U16(b,4),Capabilities=c,Flags=f,RangeSlot=b[16],KnownActionsMask=known,
                ExtendedActionStates=b[3]>=4,
                PetStateSupported=b[3]>=5,PetStateKnown=(pet&8)!=0,PetExists=(pet&16)!=0,PetDead=(pet&32)!=0,
                MeleeEventsKnown=b[109]==1,MeleeHitToken=U32(b,110),MeleeHits=U32(b,114),
                InteractBindingKnown=(b[119]&1)!=0,InteractBindingCorrect=(b[119]&2)!=0,
                ClickToMoveKnown=(b[120]&1)!=0,ClickToMoveEnabled=(b[120]&2)!=0,
                ExtendedActionRanges=b[3]>=2,RangeErrorSupported=b[3]>=3,RangeErrorActive=(b[125]&4)!=0,ActionRangeKnownMask=rangeKnown,ActionsInRangeMask=inRange,RepeatKnownMask=b[122]&15,RepeatingMask=b[122]>>4,RangedActionsMask=b[123]&15,MeleeActionsMask=b[124]&15,
                ReadyActionsMask=ready,UsableActionsMask=usable,ExtraKnownFlags=k,TargetToken=token,TargetNameHash=name,
                CastEndMilliseconds=end,LootOpened=opened,LootClosed=closed,CastSuccess=success,CastFailed=failed,SkinSuccess=skin,DeathToken=deathToken,TargetDeaths=deaths,ChatOpenKnown=(b[65]&1)!=0,ChatOpen=(b[65]&2)!=0,KillCreditsKnown=b[74]==1,KillCreditNameHash=creditName,KillCredits=credits,SkinBindingKnown=(b[75]&1)!=0,SkinBindingCorrect=(b[75]&2)!=0,FoodCount=b[76]==1?U16(b,77):-1,
                TargetX=x==65535?Double.NaN:x/65534.0,TargetY=y==65535?Double.NaN:y/65534.0,
                TargetClaimKnown=(b[79]&1)!=0,TargetClaimedByOther=(b[79]&2)!=0,TargetCombatKnown=(b[79]&4)!=0,TargetInCombat=(b[79]&8)!=0,
                TargetVictimKnown=(b[79]&16)!=0,TargetTargetsPlayer=(b[79]&32)!=0,
                GossipGoodsVisible=b[80]==7,GossipGoodsRegion=b[80]==7?new Rectangle(gx,gy,gw,gh):Rectangle.Empty,
                StealthKnown=(b[118]&1)!=0,Stealthed=(b[118]&2)!=0,GearKnown=(b[89]&1)!=0,GearRed=(b[89]&2)!=0,Durability=(b[89]&1)!=0?durability:-1,RepairButtonVisible=(repair&2)!=0,RepairButtonEnabled=(repair&4)!=0,RepairRegion=(repair&2)!=0?new Rectangle(rx,ry,rw,rh):Rectangle.Empty,RepairCostKnown=(repair&8)!=0,RepairCost=U32(b,101),MoneyKnown=(repair&16)!=0,Money=U32(b,105),
                TargetHoverKnown=(b[125]&1)!=0,TargetHovered=(b[125]&2)!=0,Portrait=(k&1)!=0?new Rectangle(px,py,pw,ph):Rectangle.Empty };
        }
        private static void Flag(int c,int cap,int flags,int mask){if((c&cap)==0 && (flags&mask)!=0)throw Invalid("Unknown WC3 flag cannot be true.");}
        private static void Match(int bits,int mask,bool value,string name){if(((bits&mask)!=0)!=value)throw Invalid("WC3 capability disagrees with "+name+".");}
        private static int U16(byte[] b,int i){return b[i]|b[i+1]<<8;}
        private static uint U32(byte[] b,int i){return (uint)b[i]|(uint)b[i+1]<<8|(uint)b[i+2]<<16|(uint)b[i+3]<<24;}
        private static FormatException Invalid(string message){return new FormatException(message);}
    }

    /// <summary>Passive observation shared by the state machine and simulator; no native input dependencies.</summary>
    public sealed class FarmObservation
    {
        public bool MeleeEventsKnown;public uint MeleeHitToken,MeleeHits;
        public bool ExtendedActionRanges,RangeErrorSupported,RangeErrorActive;
        public bool ExtendedActionStates;
        public bool PetStateSupported,PetStateKnown,PetExists,PetDead;
        public int ActionRangeKnownMask,ActionsInRangeMask,RepeatKnownMask,RepeatingMask,RangedActionsMask,MeleeActionsMask;
        public int Sequence,ReadFailures,Health=255,Power=255,PowerType=255,TargetHealth=255,MapMode,RangeSlot,
            KnownActionsMask,ReadyActionsMask,UsableActionsMask,FoodCount=-1,TargetReaction=255;
        public uint MapId,TargetToken,TargetNameHash,LootOpened,LootClosed,CastSuccess,CastFailed,SkinSuccess,DeathToken,TargetDeaths,KillCreditNameHash,KillCredits;
        public string Zone="",TargetName="";
        public double X=Double.NaN,Y=Double.NaN,Facing=Double.NaN,TargetX=Double.NaN,TargetY=Double.NaN;
        public Rectangle Portrait;
        public bool TargetHoverKnown,TargetHovered;
        public bool Fresh,KnownCore,PlayerDead,PlayerGhost,InCombat,BagFull,TargetExists,TargetDeadKnown,TargetDead,
            TargetIdentityKnown,NameHashKnown,AttackabilityKnown,TargetCanAttack,TargetIsPlayer,RangeKnown,TargetInRange,
            TargetPositionKnown,LootKnown,Lootable,SkinKnown,Skinnable,CastingKnown,Casting,Channeling,ActionsKnown,
            LootEventsKnown,CastEventsKnown,SkinSuccessKnown,PortraitKnown,VisualHealth,VisualPower,DeathEventsKnown,ChatOpenKnown,ChatOpen,KillCreditsKnown,SkinBindingKnown,SkinBindingCorrect;
        public bool TargetClaimKnown,TargetClaimedByOther,TargetCombatKnown,TargetInCombat,TargetVictimKnown,TargetTargetsPlayer;
        public bool InteractBindingKnown,InteractBindingCorrect;
        public bool ClickToMoveKnown,ClickToMoveEnabled;
        public bool GossipGoodsVisible;public Rectangle GossipGoodsRegion;
        public bool StealthKnown,Stealthed;public bool GearKnown,GearRed,RepairButtonVisible,RepairButtonEnabled,RepairCostKnown,MoneyKnown;public int Durability=-1;public uint RepairCost,Money;public Rectangle RepairRegion;

        public static FarmObservation FromPackets(ProbePacket core,CombatPacket extra,bool fresh)
        {
            var o=new FarmObservation();
            if(core==null)return o;
            o.Sequence=core.Sequence;o.ReadFailures=core.ReadFailures;o.Health=core.Health;o.Power=core.Power;o.PowerType=core.PowerType;
            o.TargetHealth=core.TargetHealth;o.TargetReaction=core.TargetReaction;o.MapMode=core.MapMode;o.MapId=core.MapId;o.Zone=core.Zone;o.TargetName=core.TargetName;
            o.X=core.X;o.Y=core.Y;o.Facing=core.Facing;
            o.KnownCore=(core.KnownFlags&15)==15;
            o.PlayerDead=(core.KnownFlags&core.Flags&1)!=0;o.PlayerGhost=(core.KnownFlags&core.Flags&2)!=0;
            o.InCombat=(core.KnownFlags&core.Flags&4)!=0;
            o.BagFull=(core.KnownFlags&core.Flags&128)!=0 || ((core.Capabilities&16)!=0 && core.FreeSlots==0);
            o.TargetExists=(core.KnownFlags&core.Flags&8)!=0;
            o.TargetDeadKnown=(core.KnownFlags&16)!=0;o.TargetDead=o.TargetDeadKnown && (core.Flags&16)!=0;
            if(extra==null || core.Sequence!=extra.Sequence)return o;
            o.Fresh=fresh && (core.Capabilities&83)==83 && (core.KnownFlags&128)!=0 && core.MapId!=0 && core.MapMode!=0
                && Finite(core.X) && Finite(core.Y) && Finite(core.Facing);
            int c=extra.Capabilities,f=extra.Flags,k=extra.ExtraKnownFlags;
            o.RangeSlot=extra.RangeSlot;
            o.CastingKnown=(c&64)!=0;o.Casting=o.CastingKnown && (f&32)!=0;o.Channeling=o.CastingKnown && (f&64)!=0;
            o.ActionsKnown=(c&128)!=0;o.KnownActionsMask=extra.KnownActionsMask;o.ReadyActionsMask=extra.ReadyActionsMask;o.UsableActionsMask=extra.UsableActionsMask;
            o.ExtendedActionStates=extra.ExtendedActionStates;
            o.PetStateSupported=extra.PetStateSupported;o.PetStateKnown=extra.PetStateKnown;o.PetExists=extra.PetExists;o.PetDead=extra.PetDead;
            o.RangeErrorSupported=extra.RangeErrorSupported;o.RangeErrorActive=extra.RangeErrorActive;o.ExtendedActionRanges=extra.ExtendedActionRanges;o.ActionRangeKnownMask=extra.ActionRangeKnownMask;o.ActionsInRangeMask=extra.ActionsInRangeMask;o.RepeatKnownMask=extra.RepeatKnownMask;o.RepeatingMask=extra.RepeatingMask;o.RangedActionsMask=extra.RangedActionsMask;o.MeleeActionsMask=extra.MeleeActionsMask;
            o.LootEventsKnown=(k&8)!=0;o.CastEventsKnown=(k&16)!=0;o.SkinSuccessKnown=(k&2)!=0;
            o.LootOpened=extra.LootOpened;o.LootClosed=extra.LootClosed;o.CastSuccess=extra.CastSuccess;o.CastFailed=extra.CastFailed;o.SkinSuccess=extra.SkinSuccess;
            o.MeleeEventsKnown=extra.MeleeEventsKnown;o.MeleeHitToken=extra.MeleeHitToken;o.MeleeHits=extra.MeleeHits;
            o.DeathEventsKnown=(k&32)!=0;o.DeathToken=extra.DeathToken;o.TargetDeaths=extra.TargetDeaths;
            o.ChatOpenKnown=extra.ChatOpenKnown;o.ChatOpen=extra.ChatOpen;
            o.KillCreditsKnown=extra.KillCreditsKnown;o.KillCreditNameHash=extra.KillCreditNameHash;o.KillCredits=extra.KillCredits;
            o.SkinBindingKnown=extra.SkinBindingKnown;o.SkinBindingCorrect=extra.SkinBindingCorrect;o.FoodCount=extra.FoodCount;
            o.InteractBindingKnown=extra.InteractBindingKnown;o.InteractBindingCorrect=extra.InteractBindingCorrect;
            o.ClickToMoveKnown=extra.ClickToMoveKnown;o.ClickToMoveEnabled=extra.ClickToMoveEnabled;
            o.GossipGoodsVisible=extra.GossipGoodsVisible;o.GossipGoodsRegion=extra.GossipGoodsRegion;
            o.StealthKnown=extra.StealthKnown;o.Stealthed=extra.Stealthed;o.GearKnown=extra.GearKnown;o.GearRed=extra.GearRed;o.Durability=extra.Durability;o.RepairButtonVisible=extra.RepairButtonVisible;o.RepairButtonEnabled=extra.RepairButtonEnabled;o.RepairRegion=extra.RepairRegion;o.RepairCostKnown=extra.RepairCostKnown;o.RepairCost=extra.RepairCost;o.MoneyKnown=extra.MoneyKnown;o.Money=extra.Money;
            if(!o.TargetExists)return o;
            o.TargetClaimKnown=extra.TargetClaimKnown;o.TargetClaimedByOther=extra.TargetClaimedByOther;o.TargetCombatKnown=extra.TargetCombatKnown;o.TargetInCombat=extra.TargetInCombat;
            o.TargetVictimKnown=extra.TargetVictimKnown;o.TargetTargetsPlayer=extra.TargetTargetsPlayer;
            o.TargetIdentityKnown=(c&1)!=0;o.TargetToken=extra.TargetToken;o.NameHashKnown=(k&4)!=0;o.TargetNameHash=extra.TargetNameHash;
            o.AttackabilityKnown=(c&2)!=0;o.TargetCanAttack=o.AttackabilityKnown && (f&1)!=0;o.TargetIsPlayer=o.AttackabilityKnown && (f&2)!=0;
            o.RangeKnown=(c&4)!=0;o.TargetInRange=o.RangeKnown && (f&4)!=0;
            o.TargetPositionKnown=(c&8)!=0 && (core.Capabilities&65)==65 && core.MapMode==1;
            if(o.TargetPositionKnown){o.TargetX=extra.TargetX;o.TargetY=extra.TargetY;}
            o.LootKnown=(c&16)!=0;o.Lootable=o.LootKnown && (f&8)!=0;o.SkinKnown=(c&32)!=0;o.Skinnable=o.SkinKnown && (f&16)!=0;
            o.TargetHoverKnown=extra.TargetHoverKnown;o.TargetHovered=extra.TargetHovered;
            o.PortraitKnown=(k&1)!=0;if(o.PortraitKnown)o.Portrait=extra.Portrait;
            return o;
        }
        private static bool Finite(double n){return !Double.IsNaN(n) && !Double.IsInfinity(n);}
    }
}
