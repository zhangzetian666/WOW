using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace WoWProbe
{
    public sealed class CampaignSettings
    {
        public int SchemaVersion=1;
        public string Name="完整流程";
        public bool EnableRecovery=true,EnableVendor=true,BindingsConfirmed,UiConfirmed,AutoResumeConfirmed,AutoCleanOnly,RequirePortraitMatch;
        public int ClientWidth=1920,ClientHeight=1080,DeathConfirmFrames=2,ReleaseTimeoutMs=15000,CorpseTimeoutMs=300000,
            ResurrectTimeoutMs=15000,VendorTimeoutMs=180000,NpcScanLimit=10,NpcScanIntervalMs=1000,MinimumFreeSlots=2,
            SaleLimit=100,SaleTimeoutMs=5000,InventoryTimeoutMs=120000,AutoCleanTimeoutMs=10000,MaxSaleQuality;
        public double JoinTolerance=.006,CorpseTolerance=.006,PortraitMinimumSimilarity=.9;
        public ushort NpcSelectKey=0x75;
        public List<uint> ProtectedItemIds=new List<uint>();
        public CampaignSettings Clone(){var c=(CampaignSettings)MemberwiseClone();c.ProtectedItemIds=ProtectedItemIds==null?null:new List<uint>(ProtectedItemIds);return c;}
        public void Validate()
        {
            if(SchemaVersion!=1)throw new FormatException("Unsupported campaign settings version.");RouteChecks.Label(Name,"campaign name");
            Range(ClientWidth,320,8192);Range(ClientHeight,240,8192);Range(DeathConfirmFrames,2,4);
            if(!NavigationMath.InRange(JoinTolerance,.0001,.02)||!NavigationMath.InRange(CorpseTolerance,.0001,.01))throw new FormatException("Invalid recorded-route connection tolerance.");
            if(!NavigationMath.InRange(PortraitMinimumSimilarity,.5,1))throw new FormatException("Portrait similarity must be from0.5 through1.");
            Range(ReleaseTimeoutMs,5000,60000);Range(CorpseTimeoutMs,10000,1800000);Range(ResurrectTimeoutMs,5000,60000);Range(VendorTimeoutMs,10000,1800000);
            Range(NpcScanLimit,1,20);Range(NpcScanIntervalMs,500,5000);Range(MinimumFreeSlots,1,40);Range(SaleLimit,1,500);
            Range(SaleTimeoutMs,500,30000);Range(InventoryTimeoutMs,5000,300000);Range(AutoCleanTimeoutMs,1000,60000);Range(MaxSaleQuality,0,1);
            if(NpcSelectKey!=9&&NpcSelectKey!=0x75)throw new FormatException("NPC selection supports confirmed TAB or F6 only.");
            if(ProtectedItemIds==null||ProtectedItemIds.Count>1000)throw new FormatException("Invalid protected item list.");
            var unique=new HashSet<uint>();foreach(uint id in ProtectedItemIds)if(id==0||!unique.Add(id))throw new FormatException("Protected item IDs must be unique and positive.");
        }
        static void Range(int n,int lo,int hi){if(n<lo||n>hi)throw new FormatException("Campaign setting is outside its supported bounds.");}
    }
    public sealed class CampaignPlan
    {
        public RouteDocument FarmRoute,CorpseRoute,VendorRoute;
        public BattleProfile Battle;
        public CampaignSettings Settings;
        public bool FarmReverse,FarmLoop;
        public int StartIndex;
        public CampaignPlan Clone(){return new CampaignPlan{FarmRoute=FarmRoute==null?null:FarmRoute.Clone(),CorpseRoute=CorpseRoute==null?null:CorpseRoute.Clone(),VendorRoute=VendorRoute==null?null:VendorRoute.Clone(),Battle=Battle==null?null:Battle.Clone(),Settings=Settings==null?null:Settings.Clone(),FarmReverse=FarmReverse,FarmLoop=FarmLoop,StartIndex=StartIndex};}
        public void Validate()
        {
            if(FarmRoute==null||Battle==null||Settings==null)throw new FormatException("Farm route, battle profile and campaign settings are required.");
            FarmRoute.Validate();Battle.Validate();Settings.Validate();CheckRoute(FarmRoute,"farm");
            if(StartIndex<0||StartIndex>=FarmRoute.Points.Count)throw new FormatException("Campaign start index is outside the recorded farm route.");
            if(Battle.Calibration.MapId!=FarmRoute.MapId||Battle.Calibration.MapMode!=FarmRoute.MapMode||!String.Equals(Battle.Calibration.Zone,FarmRoute.Zone,StringComparison.Ordinal))throw new FormatException("Battle calibration is bound to another map.");
            var work=FarmReverse?FarmRoute.Reversed():FarmRoute.Clone();
            if(FarmLoop)NavigationMath.Prepare(FarmRoute,Battle.Calibration,FarmReverse,true,StartIndex);
            if(Settings.EnableRecovery)
            {
                if(!FarmRoute.AllowReverse)throw new FormatException("Recovery requires a reversible recorded farm route to return from the corpse to the saved anchor.");
                if(CorpseRoute==null)throw new FormatException("A recorded corpse route is required.");CheckRoute(CorpseRoute,"corpse_run");
                Join(CorpseRoute.Points[CorpseRoute.Points.Count-1],work.Points[0]);
            }
            if(Settings.EnableVendor)
            {
                if(VendorRoute==null)throw new FormatException("A recorded vendor return route is required.");CheckRoute(VendorRoute,"vendor_return");
                if(!FarmRoute.AllowReverse||!VendorRoute.AllowReverse)throw new FormatException("Vendor travel requires reversible recorded farm and vendor routes.");
                Join(VendorRoute.Points[0],work.Points[0]);bool npc=false;foreach(var target in Battle.Targets)if(target.Kind=="npc")npc=true;if(!npc)throw new FormatException("Save the intended friendly merchant NPC before enabling vendor travel.");
            }
        }
        void CheckRoute(RouteDocument r,string kind)
        {
            r.Validate();if(r.RouteType!=kind||r.MapId!=FarmRoute.MapId||r.MapMode!=FarmRoute.MapMode||!String.Equals(r.Zone,FarmRoute.Zone,StringComparison.Ordinal))throw new FormatException("Recorded route type or map does not match its campaign role.");
            foreach(var p in r.Points)if(p.Action=="interact")throw new FormatException("Campaign routes cannot embed unguarded interaction actions.");
        }
        void Join(Waypoint a,Waypoint b){if(NavigationMath.Distance(a.X-b.X,a.Y-b.Y,Battle.Calibration.AxisScale)>Settings.JoinTolerance)throw new FormatException("Recorded routes do not share a verified connection at the farm start.");}
        public string Fingerprint()
        {
            Validate();var json=new JavaScriptSerializer{MaxJsonLength=8*1024*1024}.Serialize(new{farm=FarmRoute.Fingerprint(),corpse=Settings.EnableRecovery?CorpseRoute.Fingerprint():"",vendor=Settings.EnableVendor?VendorRoute.Fingerprint():"",battle=Battle,settings=CampaignSettingsStore.Write(Settings,false),reverse=FarmReverse,loop=FarmLoop});
            using(var sha=SHA256.Create()){var hash=sha.ComputeHash(Encoding.UTF8.GetBytes(json));var text=new StringBuilder();foreach(byte b in hash)text.Append(b.ToString("x2",CultureInfo.InvariantCulture));return text.ToString();}
        }
    }
    public static class CampaignSettingsStore
    {
        static FieldInfo[] Fields(){var fields=typeof(CampaignSettings).GetFields(BindingFlags.Public|BindingFlags.Instance);Array.Sort(fields,delegate(FieldInfo a,FieldInfo b){return String.CompareOrdinal(a.Name,b.Name);});return fields;}
        static string Key(string s){var b=new StringBuilder();for(int i=0;i<s.Length;i++){if(i>0&&Char.IsUpper(s[i]))b.Append('_');b.Append(Char.ToLowerInvariant(s[i]));}return b.ToString();}
        internal static string Write(CampaignSettings settings,bool clearConfirmations)
        {
            var s=settings.Clone();s.Validate();if(clearConfirmations)s.BindingsConfirmed=s.UiConfirmed=s.AutoResumeConfirmed=false;
            var json=new JavaScriptSerializer();var text=new StringBuilder("{");foreach(var f in Fields()){if(text.Length>1)text.Append(',');text.Append(json.Serialize(Key(f.Name))).Append(':').Append(json.Serialize(f.GetValue(s)));}return text.Append('}').ToString();
        }
        public static void Save(string path,CampaignSettings settings){if(settings==null)throw new ArgumentNullException("settings");RouteFiles.Save(path,Write(settings,true));}
        public static CampaignSettings Load(string path)
        {
            if(new System.IO.FileInfo(path).Length>1024*1024)throw new FormatException("Campaign settings exceed the 1 MB limit.");var root=RouteJson.Object(RouteFiles.Load(path));var fields=Fields();var keys=new List<string>();foreach(var f in fields)keys.Add(Key(f.Name));RouteJson.Keys(root,String.Join(",",keys.ToArray()),null);var s=new CampaignSettings();
            foreach(var f in fields)
            {
                string key=Key(f.Name);object value;
                if(f.FieldType==typeof(bool))value=RouteJson.Boolean(root,key);
                else if(f.FieldType==typeof(string))value=RouteJson.String(root,key);
                else if(f.FieldType==typeof(double))value=RouteJson.Number(root,key);
                else if(f.FieldType==typeof(int))value=RouteJson.Integer(root,key,Int32.MinValue,Int32.MaxValue);
                else if(f.FieldType==typeof(ushort))value=(ushort)RouteJson.Integer(root,key,0,UInt16.MaxValue);
                else{var list=root[key] as List<object>;if(list==null||list.Count>1000)throw new FormatException("Protected IDs must be a bounded JSON array.");var ids=new List<uint>();foreach(var item in list)ids.Add(RouteJson.Unsigned(new Dictionary<string,object>{{"id",item}},"id"));value=ids;}
                f.SetValue(s,value);
            }
            s.Validate();s.BindingsConfirmed=s.UiConfirmed=s.AutoResumeConfirmed=false;return s;
        }
    }
}
