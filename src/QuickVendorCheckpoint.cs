using System;
using System.IO;
using System.Web.Script.Serialization;
namespace WoWProbe
{
    public sealed class QuickVendorCheckpoint
    {
        public QuickRoutePlan Plan;
        public RouteDocument Outbound,Path,ReturnVariant;public int OutboundChoice,ReturnChoice;
        public int Next;
        public bool Returning,Repair,Emergency,StealthRequested;
        public bool AutomaticSale;
        public bool ObsoleteAutomaticSale(QuickRoutePlan current,FarmObservation f)
        {
            // Legacy and manual trips have no automatic-sale marker and remain resumable.
            return AutomaticSale&&!Repair&&!Emergency&&!Returning&&Plan!=null&&current!=null&&
                f!=null&&f.Fresh&&f.KnownCore&&f.ReadFailures==0&&!f.PlayerDead&&!f.PlayerGhost&&
                Plan.MapMatches(f)&&current.MapMatches(f)&&GearOperational(f)&&!f.BagFull;
        }
        public bool ObsoleteEmergencyRepair(QuickRoutePlan current,FarmObservation f)
        {
            // A completed repair invalidates an old automatic outbound task,
            // but never discards a return path, a manual task or a full-bag trip.
            return Repair&&Emergency&&!Returning&&Plan!=null&&current!=null&&
                f!=null&&f.Fresh&&f.KnownCore&&f.ReadFailures==0&&!f.PlayerDead&&!f.PlayerGhost&&
                Plan.MapMatches(f)&&current.MapMatches(f)&&GearOperational(f)&&!f.BagFull;
        }
        public static bool GearOperational(FarmObservation f)
        {return f!=null&&f.GearKnown&&!f.GearRed&&f.Durability>0&&f.Durability<=10000;}
        public static QuickVendorCheckpoint Load(string path)
        {if(!File.Exists(path))return null;return new JavaScriptSerializer().Deserialize<QuickVendorCheckpoint>(File.ReadAllText(path));}
        public static void Save(string path,QuickVendorCheckpoint value)
        {
            if(value==null){if(File.Exists(path))File.Delete(path);return;}
            string body=new JavaScriptSerializer().Serialize(value);
            if(File.Exists(path)&&File.ReadAllText(path)==body)return;
            string temp=path+".tmp";File.WriteAllText(temp,body);
            if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
        }
    }
}
