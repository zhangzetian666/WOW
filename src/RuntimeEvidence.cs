using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace WoWProbe
{
    /// <summary>Bounded local evidence. Planned commands are not represented as confirmed input.</summary>
    public sealed class RunJournal : IDisposable
    {
        const long MaximumLogBytes=10L*1024*1024;
        const int MaximumFiles=1000;
        static readonly Encoding Utf8=new UTF8Encoding(false);
        static readonly Regex OwnedName=new Regex("^(run|snapshot)-(\\d{8}-\\d{9})-([0-9a-f]{32})-(\\d{6,10})\\.(jsonl|png)$",RegexOptions.CultureInvariant);
        readonly object gate=new object();readonly string directory,runId=Guid.NewGuid().ToString("N");readonly bool simulation;readonly Func<DateTime> utcNow;
        readonly JavaScriptSerializer serializer=new JavaScriptSerializer{MaxJsonLength=4*1024*1024,RecursionLimit=32};
        readonly List<string> written=new List<string>();readonly List<SnapshotEvidence> snapshots=new List<SnapshotEvidence>();readonly DateTime startedUtc;
        StreamWriter writer;string currentPath;int part,snapshotIndex;long recordCount,lastVirtual;bool disposed;
        sealed class OwnedOutput{public string Path;public DateTime Utc;public bool Snapshot;}
        sealed class SnapshotEvidence{public string file,utc,reason;}
        public RunJournal(string directory,bool simulation):this(directory,simulation,delegate{return DateTime.UtcNow;}){}
        public RunJournal(string directory,bool simulation,Func<DateTime> utcNow)
        {
            if(String.IsNullOrWhiteSpace(directory))throw new ArgumentException("Journal directory is required.","directory");if(utcNow==null)throw new ArgumentNullException("utcNow");
            this.directory=Path.GetFullPath(directory);this.simulation=simulation;this.utcNow=utcNow;Directory.CreateDirectory(this.directory);RejectReparse(this.directory);startedUtc=Now();OpenLog();
        }
        DateTime Now(){DateTime value=utcNow();return value.Kind==DateTimeKind.Local?value.ToUniversalTime():DateTime.SpecifyKind(value,DateTimeKind.Utc);}
        string Source{get{return simulation?"simulation":"live_observation";}}
        static object Finite(double n){return Double.IsNaN(n)||Double.IsInfinity(n)?null:(object)n;}
        static object Percent(int n){return n>=0&&n<=100?(object)n:null;}
        static string Text(string s,int maximum){if(s==null)return "";return s.Length<=maximum?s:s.Substring(0,maximum)+" [truncated]";}
        static object RectangleData(Rectangle r){return new{x=r.X,y=r.Y,width=r.Width,height=r.Height};}
        object InputCount{get{return simulation?(object)0:null;}}
        static void RejectReparse(string path){if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Runtime evidence cannot follow a filesystem link.");}
        static void RejectExistingReparse(string path)
        {try{RejectReparse(path);}catch(FileNotFoundException){}catch(DirectoryNotFoundException){}}
        string OwnedSource(string path)
        {
            string full=Path.GetFullPath(path),parent=Path.GetDirectoryName(full);var match=OwnedName.Match(Path.GetFileName(full));
            if(!String.Equals(parent.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),directory.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)||!match.Success||match.Groups[3].Value!=runId||(match.Groups[1].Value=="snapshot")!=(match.Groups[5].Value=="png"))throw new InvalidOperationException("Export source does not belong to this runtime journal.");
            RejectReparse(full);return full;
        }
        void CheckOpen(){if(disposed)throw new ObjectDisposedException("RunJournal");}
        void OpenLog()
        {
            Prune(MaximumFiles-1);part=checked(part+1);string name="run-"+Now().ToString("yyyyMMdd-HHmmssfff",CultureInfo.InvariantCulture)+"-"+runId+"-"+part.ToString("D6",CultureInfo.InvariantCulture)+".jsonl";
            currentPath=Path.Combine(directory,name);writer=new StreamWriter(new FileStream(currentPath,FileMode.CreateNew,FileAccess.Write,FileShare.Read),Utf8){AutoFlush=true};written.Add(currentPath);
            writer.WriteLine(serializer.Serialize(new{kind="wowprobe_runtime_log",schema_version=1,run_id=runId,source=Source,game_validation="pending",started_utc=startedUtc.ToString("o"),native_input_events=InputCount}));
        }
        void WriteRecord(object record)
        {
            string line=serializer.Serialize(record);long bytes=Utf8.GetByteCount(line)+Utf8.GetByteCount(Environment.NewLine);if(bytes>MaximumLogBytes-4096)throw new InvalidOperationException("Evidence record exceeds its bounded log size.");
            if(writer.BaseStream.Position+bytes>MaximumLogBytes){writer.Dispose();writer=null;currentPath=null;OpenLog();}
            writer.WriteLine(line);
        }
        public void Record(string state,string reason,OperationObservation observation,OperationCommand command,long virtualMilliseconds)
        {
            lock(gate)
            {
                CheckOpen();if(virtualMilliseconds<0)throw new ArgumentOutOfRangeException("virtualMilliseconds");var o=observation;var f=o==null?null:o.Farm;var item=o==null?null:o.Item;
                object observed=o==null?null:new{fresh=o.Fresh,sequence=o.Sequence,core_sequence=f==null?(object)null:f.Sequence,known_core=f!=null&&f.KnownCore,read_failures=f==null?(object)null:f.ReadFailures,map_id=o.MapId,map_mode=f==null?(object)null:f.MapMode,zone=f==null?"":Text(f.Zone,128),x=f==null?null:Finite(f.X),y=f==null?null:Finite(f.Y),facing=f==null?null:Finite(f.Facing),health=f==null?null:Percent(f.Health),power=f==null?null:Percent(f.Power),power_type=f==null||f.PowerType==255?(object)null:f.PowerType,dead=f!=null&&f.PlayerDead,ghost=f!=null&&f.PlayerGhost,in_combat=f!=null&&f.InCombat,bag_full=f!=null&&f.BagFull,target_identity_known=f!=null&&f.TargetIdentityKnown,target_token=f==null?(object)null:f.TargetToken,name_hash_known=f!=null&&f.NameHashKnown,name_hash=f==null?(object)null:f.TargetNameHash,display_name=f==null?"":Text(f.TargetName,128),corpse_known=o.CorpseKnown,corpse_x=o.CorpseKnown?Finite(o.CorpseX):null,corpse_y=o.CorpseKnown?Finite(o.CorpseY):null,free_slots_known=o.FreeSlotsKnown,free_slots=o.FreeSlotsKnown?(object)o.FreeSlots:null,geometry_known=o.GeometryKnown,layout_token=o.LayoutToken,release_known=o.ReleaseKnown,release_visible=o.ReleaseVisible,release_enabled=o.ReleaseEnabled,resurrect_known=o.ResurrectKnown,resurrect_visible=o.ResurrectVisible,resurrect_enabled=o.ResurrectEnabled,recovery_delay_known=o.RecoveryDelayKnown,recovery_delay_ms=o.RecoveryDelayKnown?(object)o.RecoveryDelayMs:null,merchant_known=o.MerchantKnown,merchant_open=o.MerchantOpen,merchant_target_token=o.MerchantTargetToken,merchant_name_hash=o.MerchantNameHash,cursor_known=o.CursorKnown,cursor_empty=o.CursorEmpty,confirmation_known=o.ConfirmationKnown,confirmation_visible=o.ConfirmationVisible,inventory_known=o.InventoryKnown,inventory_events_known=o.InventoryEventsKnown,inventory_revision=o.InventoryRevision,inventory_cycle_known=o.InventoryCycleKnown,inventory_cycle=o.InventoryCycle,inventory_page=o.PageIndex,merchant_opened=o.MerchantOpened,merchant_closed=o.MerchantClosed,bag_full_events=o.BagFullEvents,item=item==null?null:new{known=item.Known,empty=item.Empty,bag=item.Bag,slot=item.Slot,item_id=item.Known?(object)item.ItemId:null,fingerprint=item.Fingerprint,count=item.Known?(object)item.Count:null,quality=item.Known&&item.Quality>=0&&item.Quality<=7?(object)item.Quality:null,unit_price=item.Known&&item.UnitPrice!=UInt32.MaxValue?(object)item.UnitPrice:null,locked=item.Locked,quest_known=item.QuestKnown,quest=item.Quest,type_known=item.TypeKnown,consumable=item.Consumable,rectangle_known=item.RectangleKnown,region=RectangleData(item.Region)}};
                object planned=command==null?null:new{kind="planned_command",action=command.Action.ToString(),purpose=command.Purpose.ToString(),key=command.Key,hold_milliseconds=command.HoldMilliseconds,region=RectangleData(command.Region),expected_target_token=command.ExpectedTargetToken,expected_name_hash=command.ExpectedNameHash,expected_item_id=command.ExpectedItemId,expected_item_fingerprint=command.ExpectedItemFingerprint,expected_inventory_revision=command.ExpectedInventoryRevision,expected_item_count=command.ExpectedItemCount,bag=command.Bag,slot=command.Slot,reason=Text(command.Reason,8192)};
                WriteRecord(new{kind="observation",schema_version=1,run_id=runId,source=Source,game_validation="pending",utc=Now().ToString("o"),virtual_milliseconds=virtualMilliseconds,state=Text(state,256),reason=Text(reason,8192),native_input_events=InputCount,observation=observed,command=planned});recordCount++;lastVirtual=virtualMilliseconds;
            }
        }
        public string SaveSnapshot(Bitmap image,string reason)
        {
            lock(gate)
            {
                CheckOpen();if(image==null)throw new ArgumentNullException("image");Prune(MaximumFiles-1);snapshotIndex=checked(snapshotIndex+1);DateTime utc=Now();string name="snapshot-"+utc.ToString("yyyyMMdd-HHmmssfff",CultureInfo.InvariantCulture)+"-"+runId+"-"+snapshotIndex.ToString("D6",CultureInfo.InvariantCulture)+".png";string path=Path.Combine(directory,name);
                // Save without disposing or taking ownership of the image supplied by the caller.
                image.Save(path,ImageFormat.Png);written.Add(path);snapshots.Add(new SnapshotEvidence{file=name,utc=utc.ToString("o"),reason=Text(reason,512)});return path;
            }
        }
        List<OwnedOutput> Outputs()
        {
            var result=new List<OwnedOutput>();foreach(string path in Directory.GetFiles(directory,"*",SearchOption.TopDirectoryOnly))
            {
                var match=OwnedName.Match(Path.GetFileName(path));if(!match.Success)continue;bool snapshot=match.Groups[1].Value=="snapshot";
                if(snapshot!=(match.Groups[5].Value=="png"))continue;DateTime utc;if(!DateTime.TryParseExact(match.Groups[2].Value,"yyyyMMdd-HHmmssfff",CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out utc))continue;
                if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)continue;
                // A matching filename alone is insufficient to treat a text file as an owned log.
                if(!snapshot){using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var reader=new StreamReader(input,Utf8,true)){char[] head=new char[512];int n=reader.Read(head,0,head.Length);if(new string(head,0,n).IndexOf("\"kind\":\"wowprobe_runtime_log\"",StringComparison.Ordinal)<0)continue;}}
                result.Add(new OwnedOutput{Path=path,Utc=utc,Snapshot=snapshot});
            }
            return result;
        }
        void Prune(int maximum)
        {
            DateTime now=Now();var outputs=Outputs();outputs.Sort(delegate(OwnedOutput a,OwnedOutput b){int c=a.Utc.CompareTo(b.Utc);return c!=0?c:String.CompareOrdinal(a.Path,b.Path);});
            for(int i=outputs.Count-1;i>=0;i--){var output=outputs[i];if(String.Equals(output.Path,currentPath,StringComparison.OrdinalIgnoreCase))continue;TimeSpan retention=output.Snapshot?TimeSpan.FromMinutes(30):TimeSpan.FromDays(7);if(now-output.Utc>retention){File.Delete(output.Path);outputs.RemoveAt(i);}}
            int count=outputs.Count;foreach(var output in outputs){if(count<=maximum)break;if(String.Equals(output.Path,currentPath,StringComparison.OrdinalIgnoreCase))continue;File.Delete(output.Path);count--;}
            written.RemoveAll(delegate(string path){return !File.Exists(path);});snapshots.RemoveAll(delegate(SnapshotEvidence snapshot){return !File.Exists(Path.Combine(directory,snapshot.file));});
        }
        public void Export(string directory)
        {
            lock(gate)
            {
                CheckOpen();if(String.IsNullOrWhiteSpace(directory))throw new ArgumentException("Export directory is required.","directory");string target=Path.GetFullPath(directory);Directory.CreateDirectory(target);RejectReparse(target);RejectReparse(this.directory);writer.Flush();Prune(MaximumFiles);var files=new List<string>();foreach(string path in written)if(File.Exists(path)){string source=OwnedSource(path),destination=Path.Combine(target,Path.GetFileName(source));RejectExistingReparse(destination);if(!String.Equals(source,Path.GetFullPath(destination),StringComparison.OrdinalIgnoreCase))using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var output=new FileStream(destination,FileMode.Create,FileAccess.Write,FileShare.Read))input.CopyTo(output);files.Add(Path.GetFileName(source));}
                string manifest=Path.Combine(target,"runtime-evidence.json");RejectExistingReparse(manifest);File.WriteAllText(manifest,serializer.Serialize(new{schema_version=1,run_id=runId,source=Source,game_validation="pending",started_utc=startedUtc.ToString("o"),exported_utc=Now().ToString("o"),records=recordCount,last_virtual_milliseconds=lastVirtual,native_input_events=InputCount,files=files,snapshots=snapshots}),Utf8);
            }
        }
        public void Dispose(){lock(gate){if(disposed)return;disposed=true;if(writer!=null){try{writer.Dispose();}finally{writer=null;}}}}
    }

    /// <summary>Optional reference-image comparison. This score never establishes target identity.</summary>
    public static class PortraitMatcher
    {
        static void Validate(Bitmap image,string parameter){if(image==null)throw new ArgumentNullException(parameter);if(image.Width<1||image.Height<1||image.Width>512||image.Height>512)throw new ArgumentException("Portrait images must be 1 through 512 pixels per axis.",parameter);}
        static Bitmap Normalize(Bitmap image)
        {
            var result=new Bitmap(32,32,PixelFormat.Format32bppArgb);using(var graphics=Graphics.FromImage(result)){graphics.Clear(Color.Black);graphics.InterpolationMode=InterpolationMode.HighQualityBilinear;graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;graphics.DrawImage(image,new Rectangle(0,0,32,32),0,0,image.Width,image.Height,GraphicsUnit.Pixel);}return result;
        }
        public static double Compare(Bitmap reference,Bitmap actual)
        {
            Validate(reference,"reference");Validate(actual,"actual");using(var a=Normalize(reference))using(var b=Normalize(actual)){long error=0;for(int y=0;y<32;y++)for(int x=0;x<32;x++){Color p=a.GetPixel(x,y),q=b.GetPixel(x,y);error+=Math.Abs(p.R-q.R)+Math.Abs(p.G-q.G)+Math.Abs(p.B-q.B);}return Math.Max(0,Math.Min(1,1-error/(32.0*32*3*255)));}
        }
        public static Bitmap LoadReference(string directory,BattleTarget target)
        {
            if(String.IsNullOrWhiteSpace(directory))throw new ArgumentException("Reference directory is required.","directory");if(target==null)throw new ArgumentNullException("target");var copy=target.Clone();copy.Validate();if(String.IsNullOrEmpty(copy.AvatarFile))throw new InvalidOperationException("This target has no saved portrait reference.");
            string root=Path.GetFullPath(directory),path=Path.GetFullPath(Path.Combine(root,copy.AvatarFile.Replace('/',Path.DirectorySeparatorChar)));string prefix=root.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Portrait reference must remain within its profile directory.");
            foreach(string check in new[]{root,Path.Combine(root,"avatars"),path})if((File.GetAttributes(check)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Portrait references cannot follow filesystem links.");
            var info=new FileInfo(path);if(info.Length<=0||info.Length>1024*1024)throw new InvalidOperationException("Portrait reference exceeds its one MiB size limit.");byte[] bytes=File.ReadAllBytes(path);if(bytes.Length>1024*1024||bytes.Length<33)throw new InvalidOperationException("Invalid bounded portrait PNG.");
            byte[] signature={137,80,78,71,13,10,26,10};for(int i=0;i<signature.Length;i++)if(bytes[i]!=signature[i])throw new InvalidOperationException("Portrait reference must be a PNG image.");
            if(bytes[8]!=0||bytes[9]!=0||bytes[10]!=0||bytes[11]!=13||bytes[12]!=73||bytes[13]!=72||bytes[14]!=68||bytes[15]!=82)throw new InvalidOperationException("Portrait PNG has no valid initial IHDR.");
            uint width=Big(bytes,16),height=Big(bytes,20);if(width<1||height<1||width>512||height>512)throw new InvalidOperationException("Portrait PNG dimensions exceed their 512-pixel limit.");
            using(var stream=new MemoryStream(bytes,false))using(var image=Image.FromStream(stream,true,true)){if(image.RawFormat.Guid!=ImageFormat.Png.Guid||image.Width!=width||image.Height!=height)throw new InvalidOperationException("Portrait PNG dimensions or format are inconsistent.");return new Bitmap(image);}
        }
        static uint Big(byte[] b,int i){return (uint)b[i]<<24|(uint)b[i+1]<<16|(uint)b[i+2]<<8|b[i+3];}
    }
}
