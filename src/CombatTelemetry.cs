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
        public int KnownActionsMask { get; private set; }
        public int ReadyActionsMask { get; private set; }
        public int UsableActionsMask { get; private set; }
        public int ExtraKnownFlags { get; private set; }
        public uint TargetToken { get; private set; }
        public uint TargetNameHash { get; private set; }
        public uint CastEndMilliseconds { get; private set; }
        public uint LootOpened { get; private set; }
        public uint LootClosed { get; private set; }
        public uint CastSuccess { get; private set; }
        public uint CastFailed { get; private set; }
        public uint SkinSuccess { get; private set; }
        public double TargetX { get; private set; }
        public double TargetY { get; private set; }
        public Rectangle Portrait { get; private set; }

        private CombatPacket() { }
        public static CombatPacket Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 128) throw Invalid("WC3 packet must contain exactly 128 bytes.");
            byte[] b=(byte[])bytes.Clone();
            if(b[0]!=87 || b[1]!=67 || b[2]!=51 || b[3]!=1) throw Invalid("Unsupported WC3 signature or version.");
            int a=0,s=0;for(int i=0;i<126;i++){a=(a+b[i])%255;s=(s+a)%255;}
            if(b[126]!=a || b[127]!=s)throw Invalid("WC3 Fletcher16 checksum mismatch.");
            for(int i=57;i<126;i++)if(b[i]!=0)throw Invalid("WC3 reserved bytes must be zero.");
            int c=b[6], f=b[7], k=b[56];
            if((f&128)!=0 || (k&~31)!=0 || b[16]<1 || b[16]>4)throw Invalid("Invalid WC3 flags or range slot.");
            Flag(c,2,f,3);Flag(c,4,f,4);Flag(c,16,f,8);Flag(c,32,f,16);Flag(c,64,f,96);
            uint token=U32(b,8), name=U32(b,44), end=U32(b,20);
            Match(c,1,token!=0,"target identity");Match(k,4,name!=0,"full target name hash");
            int x=U16(b,12),y=U16(b,14);
            if((c&8)!=0 ? x==65535 || y==65535 : x!=65535 || y!=65535)throw Invalid("WC3 position capability disagrees with coordinates.");
            if((f&96)==96 || ((f&96)!=0)!=(end!=0))throw Invalid("WC3 cast end disagrees with cast/channel state.");
            int known=b[17],ready=b[18],usable=b[19];
            if((known&~15)!=0 || (ready&~known)!=0 || (usable&~known)!=0)throw Invalid("WC3 action masks disagree.");
            Match(c,128,known!=0,"action knowledge");
            uint opened=U32(b,24),closed=U32(b,28),success=U32(b,32),failed=U32(b,36),skin=U32(b,40);
            if((k&8)==0 && (opened!=0 || closed!=0))throw Invalid("Unknown WC3 loot counters must be zero.");
            if((k&16)==0 && (success!=0 || failed!=0))throw Invalid("Unknown WC3 cast counters must be zero.");
            if((k&2)==0 && skin!=0)throw Invalid("Unknown WC3 skin counter must be zero.");
            int px=U16(b,48),py=U16(b,50),pw=U16(b,52),ph=U16(b,54);
            if((k&1)!=0 ? pw==0 || ph==0 || pw>512 || ph>512 || px+pw>65535 || py+ph>65535 : px!=0 || py!=0 || pw!=0 || ph!=0)
                throw Invalid("WC3 portrait capability disagrees with client pixel rectangle.");
            return new CombatPacket { Sequence=U16(b,4),Capabilities=c,Flags=f,RangeSlot=b[16],KnownActionsMask=known,
                ReadyActionsMask=ready,UsableActionsMask=usable,ExtraKnownFlags=k,TargetToken=token,TargetNameHash=name,
                CastEndMilliseconds=end,LootOpened=opened,LootClosed=closed,CastSuccess=success,CastFailed=failed,SkinSuccess=skin,
                TargetX=x==65535?Double.NaN:x/65534.0,TargetY=y==65535?Double.NaN:y/65534.0,
                Portrait=(k&1)!=0?new Rectangle(px,py,pw,ph):Rectangle.Empty };
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
        public int Sequence,ReadFailures,Health=255,Power=255,PowerType=255,TargetHealth=255,MapMode,RangeSlot,
            KnownActionsMask,ReadyActionsMask,UsableActionsMask;
        public uint MapId,TargetToken,TargetNameHash,LootOpened,LootClosed,CastSuccess,CastFailed,SkinSuccess;
        public string Zone="",TargetName="";
        public double X=Double.NaN,Y=Double.NaN,Facing=Double.NaN,TargetX=Double.NaN,TargetY=Double.NaN;
        public Rectangle Portrait;
        public bool Fresh,KnownCore,PlayerDead,PlayerGhost,InCombat,BagFull,TargetExists,TargetDeadKnown,TargetDead,
            TargetIdentityKnown,NameHashKnown,AttackabilityKnown,TargetCanAttack,TargetIsPlayer,RangeKnown,TargetInRange,
            TargetPositionKnown,LootKnown,Lootable,SkinKnown,Skinnable,CastingKnown,Casting,Channeling,ActionsKnown,
            LootEventsKnown,CastEventsKnown,SkinSuccessKnown,PortraitKnown;

        public static FarmObservation FromPackets(ProbePacket core,CombatPacket extra,bool fresh)
        {
            var o=new FarmObservation();
            if(core==null)return o;
            o.Sequence=core.Sequence;o.ReadFailures=core.ReadFailures;o.Health=core.Health;o.Power=core.Power;o.PowerType=core.PowerType;
            o.TargetHealth=core.TargetHealth;o.MapMode=core.MapMode;o.MapId=core.MapId;o.Zone=core.Zone;o.TargetName=core.TargetName;
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
            o.LootEventsKnown=(k&8)!=0;o.CastEventsKnown=(k&16)!=0;o.SkinSuccessKnown=(k&2)!=0;
            o.LootOpened=extra.LootOpened;o.LootClosed=extra.LootClosed;o.CastSuccess=extra.CastSuccess;o.CastFailed=extra.CastFailed;o.SkinSuccess=extra.SkinSuccess;
            if(!o.TargetExists)return o;
            o.TargetIdentityKnown=(c&1)!=0;o.TargetToken=extra.TargetToken;o.NameHashKnown=(k&4)!=0;o.TargetNameHash=extra.TargetNameHash;
            o.AttackabilityKnown=(c&2)!=0;o.TargetCanAttack=o.AttackabilityKnown && (f&1)!=0;o.TargetIsPlayer=o.AttackabilityKnown && (f&2)!=0;
            o.RangeKnown=(c&4)!=0;o.TargetInRange=o.RangeKnown && (f&4)!=0;
            o.TargetPositionKnown=(c&8)!=0 && (core.Capabilities&65)==65 && core.MapMode==1;
            if(o.TargetPositionKnown){o.TargetX=extra.TargetX;o.TargetY=extra.TargetY;}
            o.LootKnown=(c&16)!=0;o.Lootable=o.LootKnown && (f&8)!=0;o.SkinKnown=(c&32)!=0;o.Skinnable=o.SkinKnown && (f&16)!=0;
            o.PortraitKnown=(k&1)!=0;if(o.PortraitKnown)o.Portrait=extra.Portrait;
            return o;
        }
        private static bool Finite(double n){return !Double.IsNaN(n) && !Double.IsInfinity(n);}
    }
}
