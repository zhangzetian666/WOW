using System;
using System.IO;
using System.Web.Script.Serialization;
namespace WoWProbe
{
    public sealed class QuickVendorCheckpoint
    {
        public QuickRoutePlan Plan;
        public RouteDocument Outbound,Path;
        public int Next;
        public bool Returning,Repair,Emergency,StealthRequested;
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
