using System;
using System.Drawing;
using System.Reflection;

namespace WoWProbe
{
    public enum OperationPurpose { Farm, Travel, Release, Resurrect, NpcSelect, MerchantOpen, BagsOpen, Sell, CloseUi, RecoveryRest }
    public enum OperationAction { None, Key, LeftClick, RightClick, Stop, Completed, ResumeFarm }
    public sealed class OperationCommand
    {
        public OperationAction Action;public OperationPurpose Purpose;public ushort Key;public int HoldMilliseconds;
        public Rectangle Region;public uint ExpectedTargetToken,ExpectedNameHash,ExpectedItemId,ExpectedItemFingerprint,ExpectedInventoryRevision;
        public int ExpectedItemCount,Bag,Slot;public FarmCommand Farm;public string Reason="";
    }
    public sealed class InventoryItem
    {
        public int Bag,Slot,Count,Quality=255;public uint ItemId,Fingerprint,UnitPrice=UInt32.MaxValue;
        public bool Known,Empty,Locked,QuestKnown,Quest,TypeKnown,Consumable,RectangleKnown;public Rectangle Region;
        public InventoryItem Clone(){return (InventoryItem)MemberwiseClone();}
    }
    public sealed class OperationObservation
    {
        public FarmObservation Farm=new FarmObservation();
        public string ClientVersion="";public uint ClientBuild,ClientInterfaceVersion;
        public int Sequence,FreeSlots=-1,ClientWidth,ClientHeight,RecoveryDelayMs=-1,PageIndex,TotalSlots;
        public uint MapId,LayoutToken,InventoryRevision,BagFullEvents,MerchantOpened,MerchantClosed,InventoryCycle,MerchantTargetToken,MerchantNameHash;
        public double CorpseX=Double.NaN,CorpseY=Double.NaN;public InventoryItem Item=new InventoryItem();public byte[] BagSizes=new byte[5],Occupancy=new byte[25];
        public bool Fresh,CorpseKnown,FreeSlotsKnown,GeometryKnown,ReleaseKnown,ReleaseVisible,ReleaseEnabled,ResurrectKnown,ResurrectVisible,ResurrectEnabled,RecoveryDelayKnown,MerchantKnown,MerchantOpen,CursorKnown,CursorEmpty,ConfirmationKnown,ConfirmationVisible,IncomingResurrectionKnown,IncomingResurrection,InventoryKnown,InventoryEventsKnown,InventoryCycleKnown,SellTabKnown,SellTab,BagEventsKnown;
        public Rectangle ReleaseRegion,ResurrectRegion;
        // Optional external image verification, bound to this observation's sequence and target.
        public bool PortraitCheckKnown,PortraitMatch;
        public int PortraitCheckSequence;public uint PortraitCheckTargetToken;
        public OperationObservation Clone()
        {
            var c=(OperationObservation)MemberwiseClone();c.Item=Item==null?null:Item.Clone();c.BagSizes=BagSizes==null?null:(byte[])BagSizes.Clone();c.Occupancy=Occupancy==null?null:(byte[])Occupancy.Clone();
            if(Farm!=null){c.Farm=new FarmObservation();foreach(FieldInfo f in typeof(FarmObservation).GetFields(BindingFlags.Public|BindingFlags.Instance))f.SetValue(c.Farm,f.GetValue(Farm));}return c;
        }
        public bool SlotOccupied(int bag,int slot)
        {
            if(!InventoryKnown)throw new InvalidOperationException("Inventory occupancy is unknown.");
            if(bag<0||bag>=5||BagSizes==null||BagSizes.Length!=5||slot<1||slot>BagSizes[bag]||Occupancy==null||Occupancy.Length!=25)throw new ArgumentOutOfRangeException("slot");
            return (Occupancy[bag*5+(slot-1)/8]&(1<<((slot-1)%8)))!=0;
        }
        public static OperationObservation FromPackets(ProbePacket core,CombatPacket combat,OperationsPacket operations,bool fresh)
        {
            var o=new OperationObservation{Farm=FarmObservation.FromPackets(core,combat,fresh)};if(core==null)return o;
            o.Sequence=core.Sequence;o.MapId=core.MapId;o.FreeSlotsKnown=(core.Capabilities&16)!=0;o.FreeSlots=core.FreeSlots;o.ClientVersion=core.ClientVersion;o.ClientBuild=core.Build;o.ClientInterfaceVersion=core.InterfaceVersion;
            o.CorpseKnown=(core.Capabilities&128)!=0;if(o.CorpseKnown){o.CorpseX=core.CorpseX;o.CorpseY=core.CorpseY;}
            if(operations==null||combat==null||core.Sequence!=operations.Sequence||core.Sequence!=combat.Sequence)return o;
            operations.ApplyTo(o);o.Fresh=o.Farm.Fresh;
            bool merchantKnown=(core.KnownFlags&32)!=0,merchant=(core.Flags&32)!=0;
            if(!merchantKnown)o.MerchantKnown=false;
            if(o.MerchantKnown&&merchant!=o.MerchantOpen)o.Fresh=false;
            return o;
        }
    }
    /// <summary>WO4 read-only operations data. Unknown metadata and geometry cannot turn into sellable items.</summary>
    public sealed class OperationsPacket
    {
        readonly OperationObservation value;
        public int Sequence{get{return value.Sequence;}}
        private OperationsPacket(OperationObservation value){this.value=value;}
        public void ApplyTo(OperationObservation o)
        {
            if(o==null)throw new ArgumentNullException("observation");var c=value.Clone();
            foreach(FieldInfo f in typeof(OperationObservation).GetFields(BindingFlags.Public|BindingFlags.Instance))
                if(f.Name!="Farm"&&f.Name!="Fresh"&&f.Name!="MapId"&&f.Name!="CorpseKnown"&&f.Name!="CorpseX"&&f.Name!="CorpseY"&&f.Name!="FreeSlotsKnown"&&f.Name!="FreeSlots"&&f.Name!="ClientVersion"&&f.Name!="ClientBuild"&&f.Name!="ClientInterfaceVersion")f.SetValue(o,f.GetValue(c));
        }
        public static OperationsPacket Decode(byte[] bytes)
        {
            if(bytes==null||bytes.Length!=128)throw Invalid("WO4 packet must contain exactly128 bytes.");var b=(byte[])bytes.Clone();
            if(b[0]!=87||b[1]!=79||b[2]!=52||b[3]!=1)throw Invalid("Unsupported WO4 signature or version.");
            int a=0,s=0;for(int i=0;i<126;i++){a=(a+b[i])%255;s=(s+a)%255;}if(b[126]!=a||b[127]!=s)throw Invalid("WO4 checksum mismatch.");
            int cap=U16(b,6),flags=U16(b,8);if((cap&~8191)!=0||(flags&~511)!=0)throw Invalid("WO4 reserved flags are not zero.");
            Flag(cap,1,flags,3);Flag(cap,2,flags,12);Flag(cap,8,flags,16);Flag(cap,32,flags,32);Flag(cap,64,flags,64);Flag(cap,512,flags,128);Flag(cap,4096,flags,256);
            if((flags&2)!=0&&(flags&1)==0||(flags&8)!=0&&(flags&4)==0)throw Invalid("Hidden WO4 buttons cannot be enabled.");
            var o=new OperationObservation{Sequence=U16(b,4),GeometryKnown=(cap&128)!=0,ClientWidth=U16(b,30),ClientHeight=U16(b,32),LayoutToken=U32(b,34),ReleaseKnown=(cap&1)!=0,ReleaseVisible=(flags&1)!=0,ReleaseEnabled=(flags&2)!=0,ResurrectKnown=(cap&2)!=0,ResurrectVisible=(flags&4)!=0,ResurrectEnabled=(flags&8)!=0,RecoveryDelayKnown=(cap&4)!=0,MerchantKnown=(cap&8)!=0,MerchantOpen=(flags&16)!=0,CursorKnown=(cap&32)!=0,CursorEmpty=(flags&32)!=0,ConfirmationKnown=(cap&64)!=0,ConfirmationVisible=(flags&64)!=0,IncomingResurrectionKnown=(cap&512)!=0,IncomingResurrection=(flags&128)!=0,SellTabKnown=(cap&4096)!=0,SellTab=(flags&256)!=0,InventoryKnown=(cap&16)!=0,InventoryEventsKnown=(cap&16)!=0,InventoryCycleKnown=(cap&2048)!=0,BagEventsKnown=(cap&256)!=0};
            if(o.GeometryKnown?(o.ClientWidth==0||o.ClientHeight==0||o.LayoutToken==0):(o.ClientWidth!=0||o.ClientHeight!=0||o.LayoutToken!=0))throw Invalid("WO4 geometry capability disagrees with dimensions/signature.");
            o.ReleaseRegion=Region(b,10,o.ReleaseVisible,o);o.ResurrectRegion=Region(b,18,o.ResurrectVisible,o);
            uint delay=U32(b,26);if(o.RecoveryDelayKnown?delay>3600000:delay!=UInt32.MaxValue)throw Invalid("WO4 recovery delay capability disagrees with value.");o.RecoveryDelayMs=o.RecoveryDelayKnown?(int)delay:-1;
            o.InventoryRevision=U32(b,38);o.BagFullEvents=U32(b,42);o.MerchantOpened=U32(b,46);o.MerchantClosed=U32(b,50);o.InventoryCycle=U32(b,54);o.PageIndex=U16(b,58);o.TotalSlots=U16(b,60);o.MerchantTargetToken=U32(b,88);o.MerchantNameHash=U32(b,92);
            if(!o.BagEventsKnown&&o.BagFullEvents!=0||!o.MerchantKnown&&(o.MerchantOpened!=0||o.MerchantClosed!=0))throw Invalid("Unknown WO4 event counters must be zero.");
            if(o.MerchantOpen?(o.MerchantTargetToken==0||o.MerchantNameHash==0):(o.MerchantTargetToken!=0||o.MerchantNameHash!=0))throw Invalid("WO4 open merchant must identify the actual NPC.");
            if(o.InventoryCycleKnown&&!o.InventoryKnown||!o.InventoryCycleKnown&&o.InventoryCycle!=0)throw Invalid("WO4 inventory cycle capability mismatch.");
            for(int i=0;i<5;i++)o.BagSizes[i]=b[96+i];for(int i=0;i<25;i++)o.Occupancy[i]=b[101+i];
            int total=0;for(int bag=0;bag<5;bag++){if(o.BagSizes[bag]>40)throw Invalid("WO4 supports at most40 slots per bag.");total+=o.BagSizes[bag];for(int slot=o.BagSizes[bag]+1;slot<=40;slot++)if((o.Occupancy[bag*5+(slot-1)/8]&(1<<((slot-1)%8)))!=0)throw Invalid("WO4 occupancy exceeds the actual bag size.");}
            var item=new InventoryItem{Bag=b[62],Slot=b[63],ItemId=U32(b,64),Count=U16(b,68),Quality=b[70],UnitPrice=U32(b,72),Fingerprint=U32(b,76)};int f=b[71];
            if((f&128)!=0)throw Invalid("WO4 reserved item flag.");item.Known=(f&8)!=0;item.Empty=(f&16)!=0;item.Locked=(f&1)!=0;item.QuestKnown=(f&32)!=0;item.Quest=(f&2)!=0;item.TypeKnown=(f&64)!=0;item.Consumable=(f&4)!=0;
            if(item.Quest&&!item.QuestKnown||item.Consumable&&!item.TypeKnown)throw Invalid("Unknown WO4 item flags cannot be true.");
            item.RectangleKnown=(cap&1024)!=0;item.Region=Region(b,80,item.RectangleKnown,o);o.Item=item;
            if(!o.InventoryKnown)
            {
                if(total!=0||o.PageIndex!=0||o.TotalSlots!=0||o.InventoryRevision!=0||item.Bag!=0||item.Slot!=0||item.ItemId!=0||item.Count!=0||item.Quality!=255||f!=0||item.UnitPrice!=UInt32.MaxValue||item.Fingerprint!=0||item.RectangleKnown)throw Invalid("Unknown WO4 inventory must use unavailable sentinels.");
            }
            else
            {
                if(total<1||total!=o.TotalSlots||o.PageIndex>=total||item.Bag>4||item.Slot<1||item.Slot>o.BagSizes[item.Bag])throw Invalid("Invalid WO4 inventory page or slot.");
                int expected=0;for(int i=0;i<item.Bag;i++)expected+=o.BagSizes[i];expected+=item.Slot-1;if(expected!=o.PageIndex)throw Invalid("WO4 page does not correspond to bag/slot.");
                bool occupied=o.SlotOccupied(item.Bag,item.Slot);
                if(item.Empty?occupied:!occupied)throw Invalid("WO4 slot occupancy and empty flag disagree.");
                if(item.Empty)
                {if(!item.Known||item.ItemId!=0||item.Count!=0||item.Quality!=255||item.UnitPrice!=0||item.Locked||item.Quest||item.Consumable||item.Fingerprint==0)throw Invalid("Malformed empty WO4 inventory slot.");}
                else if(item.Known)
                {if(item.ItemId==0||item.Count==0||item.Quality>7||item.UnitPrice==UInt32.MaxValue||item.Fingerprint==0)throw Invalid("Malformed known WO4 item metadata.");}
                else if(item.Quality!=255||item.UnitPrice!=UInt32.MaxValue||item.Fingerprint!=0)throw Invalid("Unknown WO4 item metadata needs sentinels.");
            }
            return new OperationsPacket(o);
        }
        static Rectangle Region(byte[] b,int p,bool known,OperationObservation o)
        {int x=U16(b,p),y=U16(b,p+2),w=U16(b,p+4),h=U16(b,p+6);if(known?(!o.GeometryKnown||w==0||h==0||x+w>o.ClientWidth||y+h>o.ClientHeight):(x!=0||y!=0||w!=0||h!=0))throw Invalid("WO4 physical rectangle is absent, hidden or outside client bounds.");return known?new Rectangle(x,y,w,h):Rectangle.Empty;}
        static void Flag(int cap,int bit,int flags,int mask){if((cap&bit)==0&&(flags&mask)!=0)throw Invalid("WO4 flag needs a known capability.");}
        static int U16(byte[] b,int i){return b[i]|b[i+1]<<8;}static uint U32(byte[] b,int i){return (uint)b[i]|(uint)b[i+1]<<8|(uint)b[i+2]<<16|(uint)b[i+3]<<24;}
        static FormatException Invalid(string why){return new FormatException(why);}
    }
}
