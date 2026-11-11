using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace WoWProbe
{
    /// <summary>Read-only, bounded feedback interpretation. It never grants game acceptance or executes a planned command.</summary>
    public sealed class FeedbackSummary
    {
        const long ReportLimit=8L*1024*1024,ManifestLimit=4L*1024*1024,LogLimit=10L*1024*1024,LogBudget=64L*1024*1024;
        const int LogCountLimit=64,LineLimit=64*1024;
        static readonly Encoding Utf8=new UTF8Encoding(false,true);
        static readonly Regex RunPattern=new Regex("^[0-9a-f]{32}$",RegexOptions.CultureInvariant);
        static readonly Regex OwnedFile=new Regex("^(run|snapshot)-[0-9]{8}-[0-9]{9}-([0-9a-f]{32})-[0-9]{6,10}\\.(jsonl|png)$",RegexOptions.CultureInvariant);
        public string Source{get;private set;}public string ToolVersion{get;private set;}public string RunId{get;private set;}public string LastState{get;private set;}public string StopReason{get;private set;}public string ClientVersion{get;private set;}
        public long VirtualMilliseconds{get;private set;}public long WallMilliseconds{get;private set;}public string GameValidation{get{return "pending";}}
        readonly List<string> warnings=new List<string>(),recent=new List<string>();
        public List<string> Warnings{get{return warnings;}}public List<string> RecentEvents{get{return recent;}}
        string lastLogState="",lastLogReason="";long readRecords;bool omittedWarnings;
        FeedbackSummary(){RunId="";LastState=StopReason=ClientVersion="未知";VirtualMilliseconds=WallMilliseconds=-1;}
        public static FeedbackSummary Load(string reportDirectory)
        {
            if(System.String.IsNullOrWhiteSpace(reportDirectory))throw new ArgumentException("请选择反馈目录。","reportDirectory");string root=Path.GetFullPath(reportDirectory);CheckDirectory(root);
            var s=new FeedbackSummary();var report=Object(ReadJson(Path.Combine(root,"campaign-report.json"),ReportLimit));
            s.Source=String(report,"source",true);if(s.Source!="offline_simulation"&&s.Source!="live_attempt")throw Bad("反馈来源不受支持。");
            s.ToolVersion=String(report,"tool_version",true);Version version;if(!Version.TryParse(s.ToolVersion,out version)||version.Major!=0||(version.Minor!=5&&version.Minor!=6))throw Bad("只支持0.5与0.6版本的反馈格式。");
            bool legacy=!report.ContainsKey("schema_version");if(!legacy){if(Long(report,"schema_version",true)!=1)throw Bad("反馈结构版本不受支持。");s.RunId=Run(String(report,"run_id",true));}
            else{if(version.Minor!=5)throw Bad("新版反馈缺少结构版本和运行标识。");s.Warn("旧版报告没有run_id，报告与日志仅作弱关联；不会重算已清理确认标志后的配置指纹。");}
            string declared=String(report,"game_validation",false);if(declared!="pending")s.Warn("原报告的验收声明不作为证明；本摘要始终保持实机待验收。");
            var simulation=OptionalObject(report,"simulation");var live=OptionalObject(report,"live");bool simulated=s.Source=="offline_simulation";
            if(simulated?(simulation==null||live!=null):(live==null||simulation!=null))throw Bad("反馈来源与模拟/实机分支矛盾。");
            var terminal=simulated?simulation:live;s.LastState=String(terminal,"engine",false);if(s.LastState.Length==0)s.LastState=String(terminal,"status",false);if(s.LastState.Length==0)s.LastState="未知";
            s.StopReason=String(terminal,"reason",false);if(s.StopReason.Length==0)s.StopReason="未知";
            s.WallMilliseconds=Long(report,"wall_elapsed_ms",false);s.VirtualMilliseconds=simulated?Long(terminal,"virtual_elapsed_ms",false):-1;
            if(simulated&&terminal.ContainsKey("native_input_events")&&Long(terminal,"native_input_events",true)!=0)throw Bad("模拟报告声称发生真实输入，来源存在矛盾。");
            string flat=String(report,"client_version",false);var client=OptionalObject(report,"client");string nested=client==null?"":String(client,"client_version",false);
            if(flat.Length!=0&&nested.Length!=0&&flat!=nested)throw Bad("客户端版本字段互相矛盾。");s.ClientVersion=flat.Length!=0?flat:nested.Length!=0?nested:"未知";if(s.ClientVersion=="未知")s.Warn("报告没有客户端版本，请补充主窗口诊断或现场版本信息。");
            string manifestPath=Path.Combine(root,"runtime-evidence.json");
            if(!ExistsOrdinary(manifestPath))s.Warn("反馈清单缺失，只能显示报告摘要，日志未建立关联。");else s.ReadManifest(root,Object(ReadJson(manifestPath,ManifestLimit)),simulated);
            if(s.LastState=="未知"&&s.lastLogState.Length!=0)s.LastState=s.lastLogState;if(s.StopReason=="未知"&&s.lastLogReason.Length!=0)s.StopReason=s.lastLogReason;
            if(s.recent.Count==0)
            {
                object raw;var events=report.TryGetValue("events",out raw)?raw as List<object>:null;
                if(events!=null&&events.Count!=0){s.Warn("缺少可关联的日志事件；下列导出事件可能包含旧运行历史，不用于判定当前停止原因。");for(int i=Math.Max(0,events.Count-20);i<events.Count;i++){var value=events[i] as string;if(value==null)throw Bad("导出事件必须是文本。");s.AddEvent("未关联事件："+value);}}
            }
            return s;
        }
        void ReadManifest(string root,Dictionary<string,object> manifest,bool simulated)
        {
            if(Long(manifest,"schema_version",true)!=1)throw Bad("运行清单结构版本不受支持。");string id=Run(String(manifest,"run_id",true)),expected=simulated?"simulation":"live_observation";
            if(String(manifest,"source",true)!=expected)throw Bad("报告与清单的运行来源不一致。");if(RunId.Length!=0&&RunId!=id)throw Bad("报告与清单的run_id不一致。");RunId=id;
            if(String(manifest,"game_validation",false)!="pending")Warn("清单的验收声明未被采信，仍需人工实机验收。");
            if(simulated&&manifest.ContainsKey("native_input_events")&&Long(manifest,"native_input_events",true)!=0)throw Bad("模拟清单与零真实输入来源矛盾。");
            object value;if(!manifest.TryGetValue("files",out value))throw Bad("运行清单缺少文件列表。");var files=value as List<object>;if(files==null||files.Count>1000)throw Bad("运行清单文件列表无效或过长。");
            var unique=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var logs=new List<string>();
            foreach(object entry in files)
            {
                var name=entry as string;if(name==null||!unique.Add(name))throw Bad("运行清单存在无效或重复文件名。");string path=OwnedPath(root,name,id);
                bool exists=ExistsOrdinary(path);if(!exists){Warn("清单文件缺失："+name);continue;}if(name.EndsWith(".jsonl",StringComparison.Ordinal))logs.Add(path);
            }
            object snapshotsValue;if(manifest.TryGetValue("snapshots",out snapshotsValue))
            {var snapshots=snapshotsValue as List<object>;if(snapshots==null||snapshots.Count>1000)throw Bad("截图清单无效。");foreach(var snapshot in snapshots){string name=String(Object(snapshot),"file",true);OwnedPath(root,name,id);if(!name.EndsWith(".png",StringComparison.Ordinal))throw Bad("截图清单包含非PNG文件。");if(!unique.Contains(name))Warn("截图未列入导出文件："+name);}}
            long budget=LogBudget;int count=0;
            foreach(string path in logs)
            {
                if(count++>=LogCountLimit){Warn("日志数超过64个上限，摘要只分析已读取部分。");break;}if(budget<=0){Warn("日志合计超过64MiB上限，摘要只分析已读取部分。");break;}
                long length=new FileInfo(path).Length;if(length>LogLimit){Warn("日志超过10MiB上限，未读取："+Path.GetFileName(path));continue;}long allowed=Math.Min(length,budget);ReadLog(path,id,expected,allowed);budget-=allowed;if(allowed<length){Warn("日志合计超过64MiB上限，尾部未读取。");break;}
            }
            long declared=Long(manifest,"records",false);if(declared>=0&&declared!=readRecords)Warn("清单记录数与本次读取数量不同，缺失、截断或限额部分未分析。");
        }
        void ReadLog(string path,string id,string source,long limit)
        {
            try{bool header=false;using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var limited=new LimitedReadStream(file,limit))using(var reader=new StreamReader(limited,new UTF8Encoding(true,true),false,4096))
            {
                while(true)
                {
                    bool ended;string line=Line(reader,out ended);if(line==null)break;if(System.String.IsNullOrWhiteSpace(line))continue;
                    Dictionary<string,object> row;
                    try{row=Object(new Json(line).Parse());}catch(JsonError ex){if(ex.Incomplete&&!ended&&reader.Peek()<0){Warn("日志尾行截断，未把该行作为证据："+Path.GetFileName(path));break;}throw;}
                    if(String(row,"run_id",true)!=id||String(row,"source",true)!=source)throw Bad("日志运行身份或来源与清单不一致。");if(Long(row,"schema_version",true)!=1)throw Bad("日志结构版本不受支持。");
                    if(source=="simulation"){long nativeInputs=Long(row,"native_input_events",false);if(nativeInputs<0)Warn("模拟日志缺少 native_input_events；实际输入计数未知，不能视为零。");else if(nativeInputs!=0)throw Bad("模拟日志声明真实输入，来源互相矛盾。");}
                    string kind=String(row,"kind",true);
                    if(!header){if(kind!="wowprobe_runtime_log")throw Bad("日志缺少同运行的首行标识。");header=true;continue;}
                    if(kind!="observation")throw Bad("运行日志包含未知记录类型。");
                    if(String(row,"game_validation",false)!="pending")Warn("日志的验收声明不作为游戏通过证明。");
                    string state=String(row,"state",false),reason=String(row,"reason",false);lastLogState=state;if(reason.Length!=0)lastLogReason=reason;AddEvent(state+(reason.Length==0?"":" — "+reason));readRecords++;
                    var command=OptionalObject(row,"command");if(command!=null&&String(command,"kind",true)!="planned_command")throw Bad("日志命令类型不明确，不能当作已执行动作。");
                }
                if(!header)Warn("日志为空或没有完整标识，未形成关联证据："+Path.GetFileName(path));if(file.Length>limit)Warn("日志读取达到上限，追加内容未分析："+Path.GetFileName(path));
            }}catch(DecoderFallbackException ex){throw new FormatException("反馈日志不是有效UTF8。",ex);}
        }
        static string Line(StreamReader reader,out bool terminated)
        {
            terminated=false;var text=new StringBuilder();while(true){int n=reader.Read();if(n<0)return text.Length==0?null:CheckedLine(text);if(n=='\n'){terminated=true;return CheckedLine(text);}if(text.Length>=LineLimit)throw Bad("日志单行超过64KiB上限。");text.Append((char)n);if(text.Length%4096==0&&Utf8.GetByteCount(text.ToString())>LineLimit)throw Bad("日志单行超过64KiB上限。");}
        }
        static string CheckedLine(StringBuilder value){string line=value.ToString();if(Utf8.GetByteCount(line)>LineLimit)throw Bad("日志单行超过64KiB上限。");return line;}
        void AddEvent(string value){recent.Add(Clean(value,8192));if(recent.Count>20)recent.RemoveAt(0);}
        void Warn(string value){if(warnings.Contains(value))return;if(warnings.Count<200)warnings.Add(Clean(value,8192));else if(!omittedWarnings){omittedWarnings=true;warnings.Add("其他警告已省略；原始反馈未被修改。");}}
        public string ToMarkdown()
        {
            var text=new StringBuilder();text.AppendLine("# 测试反馈只读摘要").AppendLine();text.AppendLine("来源："+(Source=="offline_simulation"?"离线模拟":"实机尝试（尚未验收）"));
            text.AppendLine("工具版本："+Escape(ToolVersion));text.AppendLine("客户端："+Escape(ClientVersion));text.AppendLine("运行标识："+Escape(RunId.Length==0?"未知":RunId));text.AppendLine("状态："+Escape(LastState));text.AppendLine("停止/最后原因："+Escape(StopReason));text.AppendLine("虚拟时间："+Time(VirtualMilliseconds));text.AppendLine("墙钟时间："+Time(WallMilliseconds));text.AppendLine("实机验收：pending（始终待人工确认）");text.AppendLine().AppendLine("计划指令不等于游戏已实际执行；导入、启动前自检和模拟成功均不构成实机验收。");
            if(warnings.Count!=0){text.AppendLine().AppendLine("## 警告与缺失信息").AppendLine();foreach(var w in warnings)text.AppendLine("- "+Escape(w));}
            if(recent.Count!=0){text.AppendLine().AppendLine("## 最近事件（最多20条）").AppendLine();foreach(var e in recent)text.AppendLine("- "+Escape(e));}return text.ToString();
        }
        static string Time(long value){return value<0?"未知":value.ToString(CultureInfo.InvariantCulture)+" ms";}
        static string Clean(string value,int limit){if(value==null)return "";var text=new StringBuilder();for(int i=0;i<value.Length&&text.Length<limit;i++){char c=value[i];if(Char.IsControl(c)||Char.GetUnicodeCategory(c)==UnicodeCategory.Format){if(text.Length>0&&text[text.Length-1]!=' ')text.Append(' ');}else text.Append(c);}if(value.Length>limit)text.Append(" …");return text.ToString();}
        static string Escape(string value){var text=new StringBuilder();foreach(char c in Clean(value,8192)){if("\\`*_{}[]()#+-.!|<>".IndexOf(c)>=0)text.Append('\\');text.Append(c);}return text.ToString();}
        static object ReadJson(string path,long maximum)
        {CheckFile(path);using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){if(input.Length>maximum)throw Bad("反馈JSON超过大小上限。");using(var data=new MemoryStream()){var buffer=new byte[8192];int n;while((n=input.Read(buffer,0,buffer.Length))>0){if(data.Length+n>maximum)throw Bad("反馈JSON超过大小上限。");data.Write(buffer,0,n);}byte[] bytes=data.ToArray();int start=bytes.Length>=3&&bytes[0]==239&&bytes[1]==187&&bytes[2]==191?3:0;try{return new Json(Utf8.GetString(bytes,start,bytes.Length-start)).Parse();}catch(DecoderFallbackException ex){throw new FormatException("反馈文件不是有效UTF8。",ex);}}}}
        static void CheckDirectory(string root){var info=new DirectoryInfo(root);if(!info.Exists)throw new DirectoryNotFoundException("反馈目录不存在。");for(var d=info;d!=null;d=d.Parent)if((d.Attributes&FileAttributes.ReparsePoint)!=0)throw Bad("反馈目录不能经过重解析点。");}
        static void CheckFile(string path){var a=File.GetAttributes(path);if((a&(FileAttributes.ReparsePoint|FileAttributes.Directory))!=0)throw Bad("反馈引用必须是同目录普通文件。");}
        static bool ExistsOrdinary(string path){try{CheckFile(path);return true;}catch(FileNotFoundException){return false;}catch(DirectoryNotFoundException){return false;}}
        static string OwnedPath(string root,string name,string run)
        {if(name.Length>200||name.IndexOf('/')>=0||name.IndexOf('\\')>=0||name.IndexOf(':')>=0||name.IndexOf("..",StringComparison.Ordinal)>=0||Path.IsPathRooted(name))throw Bad("反馈清单引用了非法路径。");var match=OwnedFile.Match(name);if(!match.Success||match.Groups[2].Value!=run||(match.Groups[1].Value=="snapshot")!=(match.Groups[3].Value=="png"))throw Bad("反馈文件名不属于当前运行。");return Path.Combine(root,name);}
        static string Run(string value){if(!RunPattern.IsMatch(value))throw Bad("反馈运行标识无效。");return value;}
        static Dictionary<string,object> Object(object value){var o=value as Dictionary<string,object>;if(o==null)throw Bad("反馈JSON需要对象。");return o;}
        static Dictionary<string,object> OptionalObject(Dictionary<string,object> o,string key){object value;if(!o.TryGetValue(key,out value)||value==null)return null;return Object(value);}
        static string String(Dictionary<string,object> o,string key,bool required){object value;if(!o.TryGetValue(key,out value)||value==null){if(required)throw Bad("反馈缺少必要文本："+key);return "";}var s=value as string;if(s==null||s.Length>131072)throw Bad("反馈文本字段类型或长度无效："+key);return s;}
        static long Long(Dictionary<string,object> o,string key,bool required){object raw;if(!o.TryGetValue(key,out raw)||raw==null){if(required)throw Bad("反馈缺少必要整数："+key);return -1;}var n=raw as Number;long value;if(n==null||!Int64.TryParse(n.Text,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out value)||value<0)throw Bad("反馈整数无效："+key);return value;}
        static FormatException Bad(string text){return new FormatException(text);}
        sealed class Number{public readonly string Text;public Number(string text){Text=text;}}
        sealed class JsonError:FormatException{public readonly bool Incomplete;public JsonError(bool incomplete):base("反馈JSON无效、重复键或超出结构上限。"){Incomplete=incomplete;}}
        sealed class Json
        {
            readonly string text;int at,nodes;public Json(string text){this.text=text;}
            public object Parse(){object value=Value(0);Space();if(at!=text.Length)throw Error(false);return value;}
            JsonError Error(bool incomplete){return new JsonError(incomplete);}void Space(){while(at<text.Length&&(text[at]==' '||text[at]=='\r'||text[at]=='\n'||text[at]=='\t'))at++;}
            bool Take(char c){Space();if(at<text.Length&&text[at]==c){at++;return true;}return false;}
            object Value(int depth)
            {
                if(depth>16||++nodes>50000)throw Error(false);Space();if(at>=text.Length)throw Error(true);char c=text[at];
                if(c=='{'){at++;var o=new Dictionary<string,object>(StringComparer.Ordinal);if(Take('}'))return o;while(true){Space();if(at>=text.Length)throw Error(true);if(text[at]!='"')throw Error(false);string key=Text();if(!Take(':'))throw Error(at>=text.Length);if(o.ContainsKey(key)||o.Count>=256)throw Error(false);o.Add(key,Value(depth+1));if(Take('}'))return o;if(!Take(','))throw Error(at>=text.Length);}}
                if(c=='['){at++;var a=new List<object>();if(Take(']'))return a;while(true){if(a.Count>=8192)throw Error(false);a.Add(Value(depth+1));if(Take(']'))return a;if(!Take(','))throw Error(at>=text.Length);}}
                if(c=='"')return Text();if(c=='t'){Literal("true");return true;}if(c=='f'){Literal("false");return false;}if(c=='n'){Literal("null");return null;}
                if(c=='-'||(c>='0'&&c<='9'))return Numeric();throw Error(false);
            }
            void Literal(string word){for(int i=0;i<word.Length;i++){if(at>=text.Length)throw Error(true);if(text[at++]!=word[i])throw Error(false);}}
            object Numeric()
            {int start=at;if(text[at]=='-'){at++;if(at==text.Length)throw Error(true);}if(text[at]=='0')at++;else{if(text[at]<'1'||text[at]>'9')throw Error(false);while(at<text.Length&&text[at]>='0'&&text[at]<='9')at++;}if(at<text.Length&&text[at]=='.'){at++;Digits();}if(at<text.Length&&(text[at]=='e'||text[at]=='E')){at++;if(at<text.Length&&(text[at]=='+'||text[at]=='-'))at++;Digits();}if(at-start>128)throw Error(false);return new Number(text.Substring(start,at-start));}
            void Digits(){if(at>=text.Length)throw Error(true);if(text[at]<'0'||text[at]>'9')throw Error(false);while(at<text.Length&&text[at]>='0'&&text[at]<='9')at++;}
            string Text()
            {
                at++;var result=new StringBuilder();while(at<text.Length)
                {
                    char c=text[at++];if(c=='"')return result.ToString();if(c<32)throw Error(false);
                    if(c=='\\'){if(at>=text.Length)throw Error(true);c=text[at++];switch(c){case '"':case '\\':case '/':break;case 'b':c='\b';break;case 'f':c='\f';break;case 'n':c='\n';break;case 'r':c='\r';break;case 't':c='\t';break;case 'u':c=Hex();break;default:throw Error(false);}}
                    if(Char.IsHighSurrogate(c)){char low;if(at+1<text.Length&&text[at]=='\\'&&text[at+1]=='u'){at+=2;low=Hex();}else{if(at>=text.Length)throw Error(true);low=text[at++];}if(!Char.IsLowSurrogate(low))throw Error(false);result.Append(c).Append(low);}else{if(Char.IsLowSurrogate(c))throw Error(false);result.Append(c);}if(result.Length>131072)throw Error(false);
                }throw Error(true);
            }
            char Hex(){if(at+4>text.Length)throw Error(true);int value=0;for(int i=0;i<4;i++){char c=text[at++];int n=c>='0'&&c<='9'?c-'0':c>='a'&&c<='f'?c-'a'+10:c>='A'&&c<='F'?c-'A'+10:-1;if(n<0)throw Error(false);value=value*16+n;}return (char)value;}
        }
        sealed class LimitedReadStream:Stream
        {
            readonly Stream input;long remaining;public LimitedReadStream(Stream input,long maximum){this.input=input;remaining=maximum;}
            public override int Read(byte[] b,int offset,int count){if(remaining<=0)return 0;int n=input.Read(b,offset,(int)Math.Min(count,remaining));remaining-=n;return n;}
            public override bool CanRead{get{return true;}}public override bool CanSeek{get{return false;}}public override bool CanWrite{get{return false;}}public override long Length{get{throw new NotSupportedException();}}public override long Position{get{throw new NotSupportedException();}set{throw new NotSupportedException();}}public override void Flush(){}public override long Seek(long o,SeekOrigin s){throw new NotSupportedException();}public override void SetLength(long value){throw new NotSupportedException();}public override void Write(byte[] b,int o,int c){throw new NotSupportedException();}
        }
    }
}
