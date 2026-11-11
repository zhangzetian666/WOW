using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Drawing.Imaging;

namespace WoWProbe
{
    public sealed class LoadedCampaignBundle
    {
        public CampaignPlan Plan;
        public string AvatarDirectory;
    }
    /// <summary>A bounded portable plan. Manifest hashes detect corruption, not trusted authorship.</summary>
    public static class CampaignBundleStore
    {
        const long MaximumArchive=16L*1024*1024,MaximumExpanded=16L*1024*1024;
        const int MaximumEntries=256,MaximumManifest=65536,MaximumJson=2*1024*1024,MaximumPng=1024*1024;
        static readonly Regex AvatarName=new Regex("^avatars/[0-9a-f]{8}\\.png$",RegexOptions.CultureInvariant);
        static readonly Regex Digest=new Regex("^[0-9a-f]{64}$",RegexOptions.CultureInvariant);
        static readonly HashSet<string> JsonNames=new HashSet<string>(new[]{"farm.json","battle.json","settings.json","corpse.json","vendor.json"},StringComparer.Ordinal);
        public static void Save(string path,CampaignPlan plan,string avatarDirectory)
        {
            if(plan==null)throw new ArgumentNullException("plan");var copy=Normalize(plan);string scratch=NewScratch();
            try
            {
                Directory.CreateDirectory(scratch);var files=new SortedDictionary<string,byte[]>(StringComparer.Ordinal);
                foreach(var target in copy.Battle.Targets)if(!String.IsNullOrEmpty(target.AvatarFile))
                {
                    using(var image=PortraitMatcher.LoadReference(avatarDirectory,target))using(var bytes=new MemoryStream())
                    {image.Save(bytes,ImageFormat.Png);if(bytes.Length>MaximumPng)throw Invalid("A portable portrait exceeds one MiB.");string name=Canonical(target.NameHash);target.AvatarFile=name;files.Add(name,bytes.ToArray());}
                }
                RouteStore.Save(Path.Combine(scratch,"farm.json"),copy.FarmRoute);BattleProfileStore.Save(Path.Combine(scratch,"battle.json"),copy.Battle);CampaignSettingsStore.Save(Path.Combine(scratch,"settings.json"),copy.Settings);
                if(copy.Settings.EnableRecovery)RouteStore.Save(Path.Combine(scratch,"corpse.json"),copy.CorpseRoute);
                if(copy.Settings.EnableVendor)RouteStore.Save(Path.Combine(scratch,"vendor.json"),copy.VendorRoute);
                foreach(string name in JsonNames)if(File.Exists(Path.Combine(scratch,name)))files.Add(name,ReadBounded(Path.Combine(scratch,name),MaximumJson));
                var manifestFiles=new List<object>();long total=0;
                foreach(var pair in files){total=AddExpanded(total,pair.Value.LongLength);manifestFiles.Add(new{path=pair.Key,length=pair.Value.Length,sha256=Hash(pair.Value)});}
                var manifest=new{schema_version=1,farm_reverse=copy.FarmReverse,farm_loop=copy.FarmLoop,plan_fingerprint=copy.Fingerprint(),files=manifestFiles};
                byte[] metadata=RouteFiles.Utf8.GetBytes(new JavaScriptSerializer{MaxJsonLength=MaximumManifest}.Serialize(manifest));if(metadata.Length>MaximumManifest)throw Invalid("Portable plan manifest exceeds64 KiB.");total=AddExpanded(total,metadata.LongLength);
                if(files.Count+1>MaximumEntries)throw Invalid("Portable plan has too many entries.");
                byte[] archive;
                using(var stream=new MemoryStream())
                {
                    using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true))
                    {WriteEntry(zip,"manifest.json",metadata);foreach(var pair in files)WriteEntry(zip,pair.Key,pair.Value);}
                    if(stream.Length>MaximumArchive)throw Invalid("Portable plan archive exceeds16 MiB.");archive=stream.ToArray();
                }
                AtomicSave(path,archive);
            }
            finally{CleanOwned(scratch,Path.GetTempPath(),"wowprobe-bundle-");}
        }
        public static LoadedCampaignBundle Load(string path,string cacheDirectory)
        {
            byte[] archive=ReadBounded(path,MaximumArchive);var files=new Dictionary<string,byte[]>(StringComparer.Ordinal);var insensitive=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
            using(var stream=new MemoryStream(archive,false))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read,false))
            {
                if(zip.Entries.Count<1||zip.Entries.Count>MaximumEntries)throw Invalid("Portable plan entry count is outside its limit.");
                foreach(var entry in zip.Entries)
                {
                    string name=entry.FullName;ValidateName(name);if(!insensitive.Add(name))throw Invalid("Duplicate or case-colliding archive entry.");
                    int attributes=entry.ExternalAttributes;int unixKind=(attributes>>16)&0xF000;
                    if(unixKind==0xA000||unixKind==0x4000||(attributes&((int)FileAttributes.ReparsePoint|(int)FileAttributes.Directory))!=0)throw Invalid("Portable plan links and directory entries are not allowed.");
                    int maximum=name=="manifest.json"?MaximumManifest:name.EndsWith(".png",StringComparison.Ordinal)?MaximumPng:MaximumJson;
                    if(entry.Length<0||entry.Length>maximum)throw Invalid("Portable plan entry exceeds its bounded size.");total=AddExpanded(total,entry.Length);
                    using(var input=entry.Open()){var bytes=ReadStream(input,maximum);if(bytes.LongLength!=entry.Length)throw Invalid("Portable plan entry length is inconsistent.");files.Add(name,bytes);}
                }
            }
            byte[] manifestBytes;if(!files.TryGetValue("manifest.json",out manifestBytes))throw Invalid("Portable plan manifest is missing.");
            var manifest=Object(manifestBytes);RouteJson.Keys(manifest,"schema_version,farm_reverse,farm_loop,plan_fingerprint,files",null);RouteJson.Integer(manifest,"schema_version",1,1);
            bool reverse=RouteJson.Boolean(manifest,"farm_reverse"),loop=RouteJson.Boolean(manifest,"farm_loop");string fingerprint=RouteJson.String(manifest,"plan_fingerprint");if(!Digest.IsMatch(fingerprint))throw Invalid("Invalid normalized plan fingerprint.");
            var listed=manifest["files"] as List<object>;if(listed==null||listed.Count<3||listed.Count>MaximumEntries-1)throw Invalid("Invalid portable plan file manifest.");var expected=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var value in listed)
            {
                var entry=RouteJson.Object(value);RouteJson.Keys(entry,"path,length,sha256",null);string name=RouteJson.String(entry,"path");ValidateName(name);if(name=="manifest.json"||!expected.Add(name))throw Invalid("Duplicate or recursive manifest entry.");
                int length=RouteJson.Integer(entry,"length",0,(int)MaximumExpanded);string digest=RouteJson.String(entry,"sha256");byte[] bytes;
                if(!Digest.IsMatch(digest)||!files.TryGetValue(name,out bytes)||bytes.Length!=length||Hash(bytes)!=digest)throw Invalid("Portable plan file length or checksum mismatch.");
            }
            if(expected.Count+1!=files.Count)throw Invalid("Archive and manifest file sets differ.");foreach(string name in files.Keys)if(name!="manifest.json"&&!expected.Contains(name))throw Invalid("Archive contains an unlisted file.");
            Require(files,"farm.json");Require(files,"battle.json");Require(files,"settings.json");
            // Container metadata, paths, bounds and every digest are checked before any
            // scratch write. Existing strict stores then validate the actual payloads.
            string scratch=NewScratch();CampaignPlan plan;
            try
            {
                Directory.CreateDirectory(scratch);foreach(var pair in files)if(pair.Key!="manifest.json")WriteFixed(scratch,pair.Key,pair.Value);
                var settings=CampaignSettingsStore.Load(Path.Combine(scratch,"settings.json"));
                if(files.ContainsKey("corpse.json")!=settings.EnableRecovery||files.ContainsKey("vendor.json")!=settings.EnableVendor)throw Invalid("Interruption route files must match the enabled modes.");
                plan=Normalize(new CampaignPlan{FarmRoute=RouteStore.Load(Path.Combine(scratch,"farm.json")),Battle=BattleProfileStore.Load(Path.Combine(scratch,"battle.json")),Settings=settings,FarmReverse=reverse,FarmLoop=loop,
                    CorpseRoute=settings.EnableRecovery?RouteStore.Load(Path.Combine(scratch,"corpse.json")):null,VendorRoute=settings.EnableVendor?RouteStore.Load(Path.Combine(scratch,"vendor.json")):null});
                var referenced=new HashSet<string>(StringComparer.Ordinal);
                foreach(var target in plan.Battle.Targets)if(!String.IsNullOrEmpty(target.AvatarFile))
                {
                    if(target.AvatarFile!=Canonical(target.NameHash)||!referenced.Add(target.AvatarFile)||!files.ContainsKey(target.AvatarFile))throw Invalid("Portrait references must use unique canonical target hashes.");
                    using(var image=PortraitMatcher.LoadReference(scratch,target)){}
                }
                foreach(string name in files.Keys)if(name.StartsWith("avatars/",StringComparison.Ordinal)&&!referenced.Contains(name))throw Invalid("Archive contains an unreferenced portrait.");
                if(plan.Fingerprint()!=fingerprint)throw Invalid("Portable plan content does not match its normalized fingerprint.");
            }
            finally{CleanOwned(scratch,Path.GetTempPath(),"wowprobe-bundle-");}
            if(String.IsNullOrWhiteSpace(cacheDirectory))throw new ArgumentException("A plan cache directory is required.","cacheDirectory");string cache=Path.GetFullPath(cacheDirectory);RejectReparse(cache);Directory.CreateDirectory(cache);RejectReparse(cache);string published=Path.Combine(cache,"bundle-"+Guid.NewGuid().ToString("N"));bool success=false;
            try
            {
                Directory.CreateDirectory(published);RejectReparse(published);foreach(var pair in files)if(pair.Key!="manifest.json")WriteFixed(published,pair.Key,pair.Value);success=true;
                return new LoadedCampaignBundle{Plan=plan,AvatarDirectory=published};
            }
            finally{if(!success)CleanOwned(published,cache,"bundle-");}
        }
        static CampaignPlan Normalize(CampaignPlan source)
        {
            var p=source.Clone();if(p.Settings==null||p.Battle==null||p.Battle.Calibration==null)throw Invalid("Portable plan settings and battle calibration are required.");p.StartIndex=0;p.Settings.BindingsConfirmed=p.Settings.UiConfirmed=p.Settings.AutoResumeConfirmed=false;p.Battle.BindingsConfirmed=p.Battle.ApproachPositionConfirmed=p.Battle.Calibration.Confirmed=false;
            if(!p.Settings.EnableRecovery)p.CorpseRoute=null;if(!p.Settings.EnableVendor)p.VendorRoute=null;p.Validate();return p;
        }
        static string Canonical(uint hash){return "avatars/"+hash.ToString("x8",CultureInfo.InvariantCulture)+".png";}
        static void Require(Dictionary<string,byte[]> files,string name){if(!files.ContainsKey(name))throw Invalid("Missing portable plan file: "+name);}
        static void ValidateName(string name)
        {if(String.IsNullOrEmpty(name)||name.IndexOf('\\')>=0||name.IndexOf(':')>=0||name.StartsWith("/",StringComparison.Ordinal)||name.Contains("..")||name.EndsWith("/",StringComparison.Ordinal)||(name!="manifest.json"&&!JsonNames.Contains(name)&&!AvatarName.IsMatch(name)))throw Invalid("Portable plan entry is not a fixed relative path.");}
        static Dictionary<string,object> Object(byte[] bytes)
        {try{int start=bytes.Length>=3&&bytes[0]==239&&bytes[1]==187&&bytes[2]==191?3:0;return RouteJson.Object(new RouteJson.Parser(RouteFiles.Utf8.GetString(bytes,start,bytes.Length-start)).Parse());}catch(DecoderFallbackException ex){throw new FormatException("Portable plan manifest must be valid UTF8.",ex);}}
        static long AddExpanded(long total,long value){if(value<0||total>MaximumExpanded-value)throw Invalid("Portable plan expanded data exceeds16 MiB.");return total+value;}
        static byte[] ReadStream(Stream input,long maximum)
        {using(var output=new MemoryStream()){var buffer=new byte[8192];int count;while((count=input.Read(buffer,0,buffer.Length))!=0){if(output.Length>maximum-count)throw Invalid("Portable plan data exceeds its read bound.");output.Write(buffer,0,count);}return output.ToArray();}}
        static byte[] ReadBounded(string path,long maximum)
        {if(String.IsNullOrWhiteSpace(path))throw new ArgumentException("A file path is required.","path");string full=Path.GetFullPath(path);RejectReparse(full);using(var stream=new FileStream(full,FileMode.Open,FileAccess.Read,FileShare.Read)){if(stream.Length<0||stream.Length>maximum)throw Invalid("Portable plan file exceeds its size limit.");var bytes=ReadStream(stream,maximum);if(bytes.LongLength!=stream.Length)throw Invalid("Portable plan file changed while being read.");return bytes;}}
        static void RejectReparse(string path)
        {
            string current=Path.GetFullPath(path);while(!String.IsNullOrEmpty(current))
            {if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw Invalid("Portable plans cannot follow filesystem links.");string parent=Path.GetDirectoryName(current);if(String.Equals(parent,current,StringComparison.OrdinalIgnoreCase))break;current=parent;}
        }
        static string Hash(byte[] bytes){using(var sha=SHA256.Create()){var result=new StringBuilder(64);foreach(byte b in sha.ComputeHash(bytes))result.Append(b.ToString("x2",CultureInfo.InvariantCulture));return result.ToString();}}
        static void WriteEntry(ZipArchive archive,string name,byte[] bytes){var entry=archive.CreateEntry(name,CompressionLevel.Optimal);using(var output=entry.Open())output.Write(bytes,0,bytes.Length);}
        static void WriteFixed(string root,string name,byte[] bytes)
        {ValidateName(name);if(name=="manifest.json")throw Invalid("Manifest is not a published asset.");RejectReparse(root);string file=Path.Combine(root,name.Replace('/',Path.DirectorySeparatorChar)),parent=Path.GetDirectoryName(file);Directory.CreateDirectory(parent);RejectReparse(parent);RejectReparse(file);using(var stream=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}}
        static void AtomicSave(string path,byte[] bytes)
        {
            if(String.IsNullOrWhiteSpace(path))throw new ArgumentException("A portable plan destination is required.","path");string full=Path.GetFullPath(path),parent=Path.GetDirectoryName(full);RejectReparse(full);Directory.CreateDirectory(parent);RejectReparse(parent);string temporary=Path.Combine(parent,"."+Path.GetFileName(full)+"."+Guid.NewGuid().ToString("N")+".tmp");
            try{using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){output.Write(bytes,0,bytes.Length);output.Flush(true);}RejectReparse(full);if(File.Exists(full))File.Replace(temporary,full,null);else File.Move(temporary,full);}finally{if(File.Exists(temporary)){RejectReparse(temporary);File.Delete(temporary);}}
        }
        static string NewScratch(){return Path.Combine(Path.GetTempPath(),"wowprobe-bundle-"+Guid.NewGuid().ToString("N"));}
        static void CleanOwned(string directory,string expectedParent,string prefix)
        {
            try
            {
                string full=Path.GetFullPath(directory),parent=Path.GetFullPath(expectedParent).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),actual=Path.GetDirectoryName(full).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);string name=Path.GetFileName(full);Guid id;
                if(!String.Equals(parent,actual,StringComparison.OrdinalIgnoreCase)||!name.StartsWith(prefix,StringComparison.Ordinal)||!Guid.TryParseExact(name.Substring(prefix.Length),"N",out id)||!Directory.Exists(full))return;
                RejectReparse(full);foreach(string child in Directory.GetDirectories(full)){if(Path.GetFileName(child)!="avatars")return;RejectReparse(child);if(Directory.GetDirectories(child).Length!=0)return;foreach(string file in Directory.GetFiles(child)){string relative="avatars/"+Path.GetFileName(file);if(!AvatarName.IsMatch(relative))return;RejectReparse(file);} }
                foreach(string file in Directory.GetFiles(full)){if(!JsonNames.Contains(Path.GetFileName(file)))return;RejectReparse(file);}
                foreach(string child in Directory.GetDirectories(full)){foreach(string file in Directory.GetFiles(child))File.Delete(file);Directory.Delete(child,false);}foreach(string file in Directory.GetFiles(full))File.Delete(file);Directory.Delete(full,false);
            }
            catch(IOException){}catch(UnauthorizedAccessException){}catch(FormatException){}catch(ArgumentException){}
        }
        static FormatException Invalid(string reason){return new FormatException(reason);}
    }
}
