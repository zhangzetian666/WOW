using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace WoWProbe
{
    public sealed class CampaignForm:Form
    {
        readonly Func<OperationObservation> reader;readonly Func<ICampaignInputBackend> backendFactory;readonly Func<Rectangle,Bitmap> avatarCapture;readonly Func<Bitmap> gameCapture;readonly Action prepare;readonly Action<string> parentLog;readonly InputFailureLatch safety;readonly Func<Rectangle,bool> allowedRegion;
        readonly object logGate=new object();readonly List<string> events=new List<string>();readonly Stopwatch clock=Stopwatch.StartNew(),runClock=new Stopwatch();
        readonly System.Windows.Forms.Timer refresh=new System.Windows.Forms.Timer(),simulationTimer=new System.Windows.Forms.Timer();readonly System.Threading.Timer watchdog;
        readonly Label summary=new Label(),state=new Label(),capabilities=new Label();readonly TextBox output=new TextBox(),keepIds=new TextBox();readonly RouteCanvas canvas=new RouteCanvas();
        readonly CheckBox recovery=new CheckBox(),vendor=new CheckBox(),autoClean=new CheckBox(),reverse=new CheckBox(),loop=new CheckBox(),portrait=new CheckBox(),bindings=new CheckBox(),calibrated=new CheckBox(),uiConfirmed=new CheckBox(),resumeConfirmed=new CheckBox(),positionConfirmed=new CheckBox();
        readonly CheckBox missingCorpse=new CheckBox(),noSupplies=new CheckBox(),wrongMerchant=new CheckBox(),unknownItems=new CheckBox(),closedMerchant=new CheckBox();
        readonly ComboBox quality=new ComboBox(),npcKey=new ComboBox(),startMode=new ComboBox();readonly NumericUpDown minimum=Number(1,40,2,0),width=Number(320,8192,1920,0),height=Number(240,8192,1080,0),similarity=Number(.5m,1,.9m,2),speed=Number(1,100,20,0);
        CampaignPlan plan,runPlan;volatile CampaignPlan livePlan;volatile CampaignSession session;CampaignSimulator simulation;RunJournal journal;OperationObservation lastObservation;OperationCommand lastCommand;string avatarDirectory,runAvatarDirectory,lastState="",lastEvidenceState="",lastReport="",runClientVersion="";uint runClientBuild,runClientInterface;long lastSnapshotAt;bool changing,closing;
        public bool Busy{get{return SimulationRunning||(session!=null&&session.IsActive);}}public bool SimulationRunning{get{return simulationTimer.Enabled;}}public bool SimulationCompleted{get{return simulation!=null&&simulation.Completed;}}public CampaignSimulator Simulation{get{return simulation;}}public string LastReportPath{get{return lastReport;}}
        public PreflightReport LastPreflight{get;private set;}public FeedbackSummary LastFeedback{get;private set;}
        public CampaignForm(Func<OperationObservation> reader,Func<ICampaignInputBackend> backendFactory,Func<Rectangle,Bitmap> avatarCapture,Func<Bitmap> gameCapture,Action prepare,Action<string> parentLog,InputFailureLatch safety,Func<Rectangle,bool> allowedRegion=null)
        {
            this.reader=reader;this.backendFactory=backendFactory;this.avatarCapture=avatarCapture;this.gameCapture=gameCapture;this.prepare=prepare;this.parentLog=parentLog;this.safety=safety??new InputFailureLatch();this.allowedRegion=allowedRegion;
            Text="完整流程 · 方案与实测准备  v0.6.0";Font=new Font("Microsoft YaHei UI",9F);BackColor=Color.FromArgb(247,249,252);ForeColor=Color.FromArgb(24,35,52);Size=new Size(1220,1020);MinimumSize=new Size(1140,900);StartPosition=FormStartPosition.CenterScreen;AutoScroll=true;AutoScrollMinSize=new Size(1120,960);
            var root=new TableLayoutPanel{Dock=DockStyle.Fill,MinimumSize=new Size(1120,960),Padding=new Padding(12),RowCount=7,ColumnCount=1};Controls.Add(root);foreach(int h in new[]{52,122,0,138,134,118,94})root.RowStyles.Add(h==0?new RowStyle(SizeType.Percent,100):new RowStyle(SizeType.Absolute,h));
            root.Controls.Add(new Label{Text="默认离线模拟：打怪 → 死亡跑尸复活 → 恢复续跑 → 包满找商人清包 → 原路返回。\n实机需要三类已录制路线、已保存商人和有效的只读数据。F12、失焦和数据过期均停止；游戏动作可能需要手动取消。",Dock=DockStyle.Fill},0,0);
            var group=Group("1  载入三类路线与配置",root,1);var rows=Rows(group,3);var row=Row();rows.Controls.Add(row);
            row.Controls.Add(Button("载入打怪路线",delegate{LoadRoute("farm");}));row.Controls.Add(Button("载入跑尸路线",delegate{LoadRoute("corpse_run");}));row.Controls.Add(Button("载入商人路线",delegate{LoadRoute("vendor_return");}));row.Controls.Add(Button("载入战斗配置",LoadBattle));row.Controls.Add(Button("导入地图标定",LoadCalibration));row.Controls.Add(Button("恢复演示方案",ResetDemo));
            row.Controls.Add(Button("载入完整方案",OpenCompletePlan));row.Controls.Add(Button("保存完整方案",SaveCompletePlanDialog));
            row=Row();rows.Controls.Add(row);row.Controls.Add(Button("载入完整流程设置",LoadSettings));row.Controls.Add(Button("保存完整流程设置",SaveSettings));row.Controls.Add(Label("断点"));startMode.DropDownStyle=ComboBoxStyle.DropDownList;startMode.Width=185;startMode.Items.AddRange(new object[]{"从第一点开始","实机读取保存的断点"});startMode.SelectedIndex=0;row.Controls.Add(startMode);Check(row,reverse,"打怪路线逆向");Check(row,loop,"循环运行");
            row.Controls.Add(Button("检查启动条件",InspectCurrentPlan));row.Controls.Add(Button("读取测试反馈",OpenFeedback));
            summary.Dock=DockStyle.Fill;rows.Controls.Add(summary);
            var middle=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,58));middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,42));root.Controls.Add(middle,0,2);canvas.Dock=DockStyle.Fill;canvas.Margin=new Padding(0,5,8,5);middle.Controls.Add(canvas);capabilities.Dock=DockStyle.Fill;capabilities.BackColor=Color.White;capabilities.Padding=new Padding(12);middle.Controls.Add(capabilities);
            group=Group("2  恢复与清包规则",root,3);rows=Rows(group,3);row=Row();rows.Controls.Add(row);Check(row,recovery,"死亡后自动跑尸恢复");Check(row,vendor,"包满后返回商人");Check(row,autoClean,"仅等待服务器交互后自动清包");row.Controls.Add(Label("至少空位"));row.Controls.Add(minimum);row.Controls.Add(Label("出售"));quality.DropDownStyle=ComboBoxStyle.DropDownList;quality.Items.AddRange(new object[]{"仅灰色","灰色与白色"});quality.SelectedIndex=0;quality.Width=110;row.Controls.Add(quality);
            row=Row();rows.Controls.Add(row);row.Controls.Add(Label("保护物品ID（逗号分隔）"));keepIds.Width=250;row.Controls.Add(keepIds);row.Controls.Add(Label("选商人键"));npcKey.DropDownStyle=ComboBoxStyle.DropDownList;npcKey.Width=65;npcKey.Items.AddRange(new object[]{"F6","TAB"});npcKey.SelectedIndex=0;row.Controls.Add(npcKey);row.Controls.Add(Label("F6需预先绑定目标NPC宏；不自动修改游戏键位"));
            row=Row();rows.Controls.Add(row);Check(row,portrait,"同时核对保存的头像");row.Controls.Add(Label("相似度"));row.Controls.Add(similarity);row.Controls.Add(Label("已核对的客户区尺寸"));row.Controls.Add(width);row.Controls.Add(Label("×"));row.Controls.Add(height);row.Controls.Add(Button("读取当前窗口尺寸",ReadGeometry));
            group=Group("3  实机运行前人工核对（导入文件不会自动勾选）",root,4);rows=Rows(group,3);row=Row();rows.Controls.Add(row);Check(row,bindings,"已核对移动、技能、商人选择、F交互、B打开全部背包、食物与饮水键");
            row=Row();rows.Controls.Add(row);Check(row,calibrated,"已在本地图实测移动标定");Check(row,uiConfirmed,"已核对释放、复活、背包格子位置及商店出售页");
            row=Row();rows.Controls.Add(row);Check(row,resumeConfirmed,"允许处理完成后继续原打怪路线");Check(row,positionConfirmed,"已验证目标坐标来源（启用接近时必需）");
            group=Group("4  默认离线运行；真实运行会先倒计时5秒",root,5);rows=Rows(group,3);row=Row();rows.Controls.Add(row);row.Controls.Add(Button("开始完整离线模拟",StartSimulation));row.Controls.Add(Button("停止",delegate{StopWorkspace("用户停止");}));row.Controls.Add(Label("模拟步数/帧"));row.Controls.Add(speed);row.Controls.Add(Button("实机完整运行（5秒后）",StartLive));row.Controls.Add(Button("导出本次记录",Export));
            row=Row();rows.Controls.Add(row);Check(row,missingCorpse,"模拟缺少尸体坐标");Check(row,noSupplies,"模拟没有补给");Check(row,wrongMerchant,"模拟错误商人");Check(row,unknownItems,"模拟物品信息未知");Check(row,closedMerchant,"模拟出售前商店关闭");state.Dock=DockStyle.Fill;rows.Controls.Add(state);
            output.Dock=DockStyle.Fill;output.Multiline=true;output.ReadOnly=true;output.ScrollBars=ScrollBars.Vertical;output.BackColor=Color.White;root.Controls.Add(output,0,6);
            foreach(var c in new[]{recovery,vendor,autoClean,reverse,loop,portrait,bindings,calibrated,uiConfirmed,resumeConfirmed,positionConfirmed,missingCorpse,noSupplies,wrongMerchant,unknownItems,closedMerchant})c.CheckedChanged+=delegate{SettingsChanged();};foreach(var n in new[]{minimum,width,height,similarity})n.ValueChanged+=delegate{SettingsChanged();};foreach(var combo in new[]{quality,npcKey,startMode})combo.SelectedIndexChanged+=delegate{SettingsChanged();};keepIds.TextChanged+=delegate{SettingsChanged();};
            refresh.Interval=150;refresh.Tick+=delegate{RefreshState();};refresh.Start();simulationTimer.Interval=25;simulationTimer.Tick+=delegate{SimulationTick();};watchdog=new System.Threading.Timer(delegate{var s=session;if(s!=null)s.Tick();},null,25,25);
            FormClosing+=delegate{closing=true;refresh.Stop();StopWorkspace("完整流程窗口关闭");watchdog.Dispose();simulationTimer.Dispose();refresh.Dispose();if(journal!=null)journal.Dispose();};ResetDemo();
        }
        static NumericUpDown Number(decimal a,decimal b,decimal value,int dp){return new NumericUpDown{Minimum=a,Maximum=b,Value=value,DecimalPlaces=dp,Width=70,Increment=dp==0?1:.01m};}
        static Label Label(string text){return new Label{Text=text,AutoSize=true,Margin=new Padding(5,6,2,0)};}static FlowLayoutPanel Row(){return new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=new Padding(0)};}static void Check(Control row,CheckBox c,string s){c.Text=s;c.AutoSize=true;row.Controls.Add(c);}
        static GroupBox Group(string s,TableLayoutPanel root,int i){var g=new GroupBox{Text=s,Dock=DockStyle.Fill,Padding=new Padding(8)};root.Controls.Add(g,0,i);return g;}static TableLayoutPanel Rows(Control root,int count){var p=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=count,ColumnCount=1};for(int i=0;i<count-1;i++)p.RowStyles.Add(new RowStyle(SizeType.Absolute,30));p.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.Controls.Add(p);return p;}
        static Button Button(string text,Action a){var b=new Button{Text=text,AutoSize=true,MinimumSize=new Size(70,27),Margin=new Padding(3,0,3,0)};b.Click+=delegate{try{a();}catch(Exception ex){MessageBox.Show(ex.Message,"本次操作未执行",MessageBoxButtons.OK,MessageBoxIcon.Information);}};return b;}
        static string UserDirectory(){string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"profiles","user");Directory.CreateDirectory(path);return path;}
        void Record(string text){lock(logGate){events.Add(DateTimeOffset.Now.ToString("o")+" "+text);if(events.Count>2000)events.RemoveAt(0);}if(parentLog!=null)try{parentLog(text);}catch{}}
        void ClearRun(){session=null;livePlan=null;simulation=null;runPlan=null;lastObservation=null;lastCommand=null;lastState=lastEvidenceState="";lastReport="";runClientVersion="";runClientBuild=runClientInterface=0;LastPreflight=null;LastFeedback=null;lock(logGate)events.Clear();if(journal!=null){journal.Dispose();journal=null;}}
        void ResetDemo(){StopWorkspace("恢复演示方案");ClearRun();plan=CampaignDemo.Create();avatarDirectory=UserDirectory();changing=true;reverse.Checked=loop.Checked=missingCorpse.Checked=noSupplies.Checked=wrongMerchant.Checked=unknownItems.Checked=closedMerchant.Checked=false;startMode.SelectedIndex=0;changing=false;Populate();state.Text="默认离线演示，系统输入为0；游戏电脑验证待完成。";}
        void Populate()
        {
            changing=true;try{var s=plan.Settings;recovery.Checked=s.EnableRecovery;vendor.Checked=s.EnableVendor;autoClean.Checked=s.AutoCleanOnly;minimum.Value=s.MinimumFreeSlots;quality.SelectedIndex=s.MaxSaleQuality;npcKey.SelectedIndex=s.NpcSelectKey==9?1:0;portrait.Checked=s.RequirePortraitMatch;similarity.Value=(decimal)s.PortraitMinimumSimilarity;width.Value=s.ClientWidth;height.Value=s.ClientHeight;var ids=new List<string>();foreach(uint id in s.ProtectedItemIds)ids.Add(id.ToString(System.Globalization.CultureInfo.InvariantCulture));keepIds.Text=String.Join(",",ids.ToArray());bindings.Checked=calibrated.Checked=uiConfirmed.Checked=resumeConfirmed.Checked=positionConfirmed.Checked=false;}finally{changing=false;}
            Preview();var npcs=new List<string>();foreach(var t in plan.Battle.Targets)if(t.Kind=="npc")npcs.Add(t.Name);summary.Text="地图 "+plan.FarmRoute.MapId+" / "+plan.FarmRoute.MapMode+" / "+plan.FarmRoute.Zone+"；打怪 "+plan.FarmRoute.Points.Count+" 点，跑尸 "+(plan.CorpseRoute==null?0:plan.CorpseRoute.Points.Count)+" 点，商人 "+(plan.VendorRoute==null?0:plan.VendorRoute.Points.Count)+" 点；已存NPC："+String.Join("、",npcs.ToArray());
        }
        void Preview(){if(plan==null)return;canvas.Route=reverse.Checked&&plan.FarmRoute.AllowReverse?plan.FarmRoute.Reversed():plan.FarmRoute.Clone();canvas.AxisScale=plan.Battle.Calibration.AxisScale;canvas.Player=null;canvas.Highlight=0;canvas.Invalidate();}
        void SettingsChanged(){if(changing)return;StopWorkspace("设置已修改，请重新开始。");if(reverse.Checked&&!plan.FarmRoute.AllowReverse){changing=true;reverse.Checked=false;changing=false;state.Text="这条打怪路线不允许反向。";}Preview();}
        CampaignSettings ReadSettings(bool live)
        {
            var s=plan.Settings.Clone();s.EnableRecovery=recovery.Checked;s.EnableVendor=vendor.Checked;s.AutoCleanOnly=autoClean.Checked;s.MinimumFreeSlots=(int)minimum.Value;s.MaxSaleQuality=quality.SelectedIndex;s.NpcSelectKey=(ushort)(npcKey.SelectedIndex==0?0x75:9);s.RequirePortraitMatch=portrait.Checked;s.PortraitMinimumSimilarity=(double)similarity.Value;s.ClientWidth=(int)width.Value;s.ClientHeight=(int)height.Value;s.BindingsConfirmed=live&&bindings.Checked;s.UiConfirmed=live&&uiConfirmed.Checked;s.AutoResumeConfirmed=live&&resumeConfirmed.Checked;s.ProtectedItemIds.Clear();foreach(string id in keepIds.Text.Replace('，',',').Split(',')){if(String.IsNullOrWhiteSpace(id))continue;uint n;if(!UInt32.TryParse(id.Trim(),out n))throw new FormatException("保护物品ID需要是正整数。");s.ProtectedItemIds.Add(n);}s.Validate();return s;
        }
        CampaignPlan ReadPlan(bool live,bool validate=true){var p=plan.Clone();p.Settings=ReadSettings(live);p.FarmReverse=reverse.Checked;p.FarmLoop=loop.Checked;p.StartIndex=0;p.Battle.BindingsConfirmed=live&&bindings.Checked;p.Battle.Calibration.Confirmed=live&&calibrated.Checked;p.Battle.ApproachPositionConfirmed=live&&positionConfirmed.Checked;if(validate)p.Validate();return p;}
        void StageChange(string why,Action change,bool keepRules){var rules=keepRules?ReadSettings(false):null;StopWorkspace(why);ClearRun();if(rules!=null)plan.Settings=rules;change();Populate();state.Text=why+"；三类路线与标定必须同图，全部载入后再启动。";}
        void Changed(string why){StageChange(why,delegate{},true);}
        void ApplyRoute(string kind,RouteDocument route){if(route==null)throw new ArgumentNullException("route");var r=route.Clone();r.Validate();if(r.RouteType!=kind||(kind!="farm"&&kind!="corpse_run"&&kind!="vendor_return"))throw new FormatException("路线类型与当前载入位置不一致。");StageChange("路线已载入",delegate{if(kind=="farm")plan.FarmRoute=r;else if(kind=="corpse_run")plan.CorpseRoute=r;else plan.VendorRoute=r;},true);}
        void ApplyBattle(BattleProfile profile,string directory){var p=profile.Clone();p.Validate();StageChange("战斗配置已载入",delegate{plan.Battle=p;avatarDirectory=directory;},true);}
        void ApplyCalibration(NavigationCalibration calibration){var c=calibration.Clone();c.Validate();if(c.MapId!=plan.FarmRoute.MapId||c.MapMode!=plan.FarmRoute.MapMode||c.Zone!=plan.FarmRoute.Zone)throw new FormatException("标定不属于当前打怪地图。");c.Confirmed=false;StageChange("地图标定已载入",delegate{plan.Battle.Calibration=c;},true);}
        void LoadRoute(string kind)
        {
            using(var d=new OpenFileDialog{Filter="路线 JSON|*.json",InitialDirectory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"routes")})if(d.ShowDialog()==DialogResult.OK)ApplyRoute(kind,RouteStore.Load(d.FileName));
        }
        void LoadBattle(){using(var d=new OpenFileDialog{Filter="战斗配置 JSON|*.json",InitialDirectory=UserDirectory()})if(d.ShowDialog()==DialogResult.OK)ApplyBattle(BattleProfileStore.Load(d.FileName),Path.GetDirectoryName(d.FileName));}
        void LoadSettings(){using(var d=new OpenFileDialog{Filter="完整流程设置 JSON|*.json",InitialDirectory=UserDirectory()})if(d.ShowDialog()==DialogResult.OK){var s=CampaignSettingsStore.Load(d.FileName);StageChange("完整流程设置已载入",delegate{plan.Settings=s;},false);}}
        void SaveSettings(){var s=ReadSettings(false);StopWorkspace("保存完整流程设置");using(var d=new SaveFileDialog{Filter="完整流程设置 JSON|*.json",InitialDirectory=UserDirectory(),FileName="complete-settings.json"})if(d.ShowDialog()==DialogResult.OK){CampaignSettingsStore.Save(d.FileName,s);plan.Settings=s;Populate();state.Text="设置已保存；实机确认勾选不会随文件启用。";}}
        void LoadCalibration()
        {
            using(var d=new OpenFileDialog{Filter="地图标定 JSON|*.json",InitialDirectory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"routes")})if(d.ShowDialog()==DialogResult.OK){if(new FileInfo(d.FileName).Length>65536)throw new FormatException("标定文件过大。");var c=new JavaScriptSerializer().Deserialize<NavigationCalibration>(File.ReadAllText(d.FileName,Encoding.UTF8));if(c==null)throw new FormatException("标定为空。");ApplyCalibration(c);}
        }
        void OpenCompletePlan(){using(var d=new OpenFileDialog{Filter="完整方案|*.wowplan",InitialDirectory=UserDirectory()})if(d.ShowDialog()==DialogResult.OK)LoadCompletePlan(d.FileName);}
        void LoadCompletePlan(string path){StopWorkspace("载入完整方案");var loaded=CampaignBundleStore.Load(path,Path.Combine(UserDirectory(),"imported"));var p=loaded.Plan.Clone();p.Validate();ClearRun();plan=p;avatarDirectory=loaded.AvatarDirectory;changing=true;reverse.Checked=p.FarmReverse;loop.Checked=p.FarmLoop;startMode.SelectedIndex=0;changing=false;Populate();state.Text="完整方案已载入；方向与循环已恢复，实机确认已清除，尚未启动。";}
        void SaveCompletePlanDialog(){using(var d=new SaveFileDialog{Filter="完整方案|*.wowplan",FileName="完整方案.wowplan",InitialDirectory=UserDirectory()})if(d.ShowDialog()==DialogResult.OK)SaveCompletePlan(d.FileName);}
        void SaveCompletePlan(string path){var p=ReadPlan(false);StopWorkspace("保存完整方案");CampaignBundleStore.Save(path,p,avatarDirectory);state.Text="完整方案已保存，包含方向、循环与有效参考图；运行断点和实机确认不随包启用。";}
        void InspectCurrentPlan(){StopWorkspace("检查启动条件");CampaignPlan p=null;string problem=null;try{p=ReadPlan(true,false);}catch(Exception ex){problem=ex.Message;}LastPreflight=CampaignPreflight.Evaluate(p,ReadLive(),safety.Faulted,avatarDirectory);if(problem!=null)LastPreflight.Checks.Add(new PreflightCheck{Code="controls",Label="当前规则编辑",State="block",Detail=problem});state.Text=LastPreflight.ReadyToArm?"当前准备条件已满足，仍需人工启动与游戏验收。":"存在未满足条件；离线模拟仍可单独使用。";new ReadinessReportForm("启动条件检查",LastPreflight.ToMarkdown()).Show(this);}
        void OpenFeedback(){using(var d=new FolderBrowserDialog{Description="选择 reports/campaign-时间戳 报告文件夹",ShowNewFolderButton=false})if(d.ShowDialog()==DialogResult.OK)InspectFeedback(d.SelectedPath);}
        void InspectFeedback(string directory){StopWorkspace("读取历史测试反馈");LastFeedback=null;var feedback=FeedbackSummary.Load(directory);LastFeedback=feedback;new ReadinessReportForm("测试反馈摘要",feedback.ToMarkdown()).Show(this);state.Text="反馈已只读整理，未修改原报告或实机验收状态。";}
        void ReadGeometry(){var o=ReadLive();if(!o.Fresh||!o.GeometryKnown)throw new InvalidOperationException("需要同序号的有效三面板数据。");width.Value=o.ClientWidth;height.Value=o.ClientHeight;state.Text="已填写当前客户区尺寸；还需要人工核对界面和标定。";}
        OperationObservation ReadLive()
        {
            // The safety monitor reads immutable in-memory telemetry only. Disk/image work
            // is performed by the UI producer before publishing the matching frame.
            return reader==null?new OperationObservation():reader()??new OperationObservation();
        }
        public void PrepareImageEvidence(OperationObservation o)
        {
            if(closing||o==null)return;var p=livePlan;
            if(p!=null&&p.Settings.RequirePortraitMatch&&o.Fresh&&o.Farm!=null&&!o.Farm.TargetDead)
            {
                var target=TargetCatalog.Match(p.Battle,o.Farm);if(target!=null&&o.Farm.PortraitKnown&&avatarCapture!=null)
                {try{using(var reference=PortraitMatcher.LoadReference(runAvatarDirectory,target))using(var actual=avatarCapture(o.Farm.Portrait)){o.PortraitMatch=PortraitMatcher.Compare(reference,actual)>=p.Settings.PortraitMinimumSimilarity;o.PortraitCheckKnown=true;o.PortraitCheckSequence=o.Sequence;o.PortraitCheckTargetToken=o.Farm.TargetToken;}}catch(Exception ex){Record("头像参考核对不可用："+ex.Message);}}
            }
        }
        void Prepare(){if(prepare!=null)prepare();if(safety.Faulted)throw new InvalidOperationException("输入故障尚未处理，请检查并手动松开按键/鼠标，重启工具后再运行。");}
        void NewJournal(bool simulated){if(journal!=null)journal.Dispose();journal=new RunJournal(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"runtime"),simulated);runClock.Restart();lastSnapshotAt=-30000;lastEvidenceState="";lastReport="";}
        void StartSimulation()
        {
            var p=ReadPlan(false);StopWorkspace("开始完整离线模拟");Prepare();ClearRun();simulation=new CampaignSimulator(p){MissingCorpse=missingCorpse.Checked,NoSupplies=noSupplies.Checked,WrongMerchant=wrongMerchant.Checked,UnknownItemMetadata=unknownItems.Checked,MerchantClosesBeforeSale=closedMerchant.Checked};runPlan=p.Clone();runAvatarDirectory=avatarDirectory;NewJournal(true);lastState="";Record("完整离线模拟启动，未创建真实输入后端。");simulationTimer.Start();
        }
        void SimulationTick()
        {
            try{
                for(int i=0;i<(int)speed.Value&&simulationTimer.Enabled;i++)
                {
                    var o=simulation.Sample();var c=simulation.Engine.Step(o,simulation.ElapsedMilliseconds);lastObservation=o;lastCommand=c;CaptureEvidence(simulation.Engine.Status,o,c,simulation.ElapsedMilliseconds);
                    if(c.Action==OperationAction.Key||c.Action==OperationAction.LeftClick||c.Action==OperationAction.RightClick)simulation.Apply(c);else if(c.Action==OperationAction.Stop||c.Action==OperationAction.Completed){simulationTimer.Stop();Record("模拟结束："+simulation.Engine.Status+" · "+c.Reason);TakeSnapshot("模拟结束");}else simulation.Advance(200);
                }
                canvas.Player=new PointF((float)simulation.X,(float)simulation.Y);canvas.Highlight=simulation.Engine.RouteIndex;canvas.Invalidate();state.Text="离线 "+Display(simulation.Engine.Status)+" · 击杀 "+simulation.Kills+" / 复活 "+simulation.Resurrections+" / 商人返回 "+simulation.Engine.VendorCount+" / 售出 "+simulation.Sales+" · 虚拟时间 "+TimeSpan.FromMilliseconds(simulation.ElapsedMilliseconds).ToString(@"hh\:mm\:ss")+" · 系统输入0";
            }catch(Exception ex){simulationTimer.Stop();simulation.Engine.Stop(ex.Message);Record("模拟停止："+ex.Message);state.Text="模拟停止："+ex.Message;TakeSnapshot("模拟异常");}
        }
        void StartLive()
        {
            var p=ReadPlan(true);if(startMode.SelectedIndex==1){var cp=CampaignCheckpointStore.Load(CheckpointPath(p.Fingerprint()),p);if(cp.Completed)throw new InvalidOperationException("此方案已完成，请选择从第一点开始。");p.StartIndex=cp.FarmIndex;}
            StopWorkspace("准备完整实机运行");Prepare();ClearRun();runAvatarDirectory=avatarDirectory;livePlan=p.Clone();CampaignSession candidate=null;
            try{
                var o=ReadLive();if(!o.Fresh)throw new InvalidOperationException("需要同序号的有效 WP1、WC3、WO4 数据，请更新插件并定位三个面板。");
                LastPreflight=CampaignPreflight.Evaluate(p,o,safety.Faulted,runAvatarDirectory);if(!LastPreflight.ReadyToArm){var blocks=new List<string>();foreach(var c in LastPreflight.Checks)if(c.State=="block")blocks.Add(c.Label+"："+c.Detail);throw new InvalidOperationException("启动检查未满足：\n"+String.Join("\n",blocks.ToArray()));}
                if(!o.Farm.PlayerDead&&!o.Farm.PlayerGhost){var working=p.FarmReverse?p.FarmRoute.Reversed():p.FarmRoute;var a=working.Points[Math.Max(0,p.StartIndex-1)];if(NavigationMath.Distance(o.Farm.X-a.X,o.Farm.Y-a.Y,p.Battle.Calibration.AxisScale)>p.Settings.JoinTolerance)throw new InvalidOperationException("请先移动到所选打怪起点/断点附近，不能从未记录区域直接接入。");}
                runClientVersion=o.ClientVersion;runClientBuild=o.ClientBuild;runClientInterface=o.ClientInterfaceVersion;NewJournal(false);
                var b=backendFactory==null?null:backendFactory();if(b==null)throw new InvalidOperationException("请先选择并定位游戏窗口。");candidate=new CampaignSession(b,delegate{return clock.ElapsedMilliseconds;},ReadLive,Record,safety,allowedRegion);candidate.Start(p);session=candidate;runPlan=p.Clone();lastState="";Record("完整实机会话已开始5秒倒计时；请切换到游戏。实机结果仍需记录。");
            }catch{
                if(candidate!=null)candidate.Stop("完整流程启动未完成。");session=null;livePlan=null;runPlan=null;
                if(journal!=null){var failedJournal=journal;journal=null;try{failedJournal.Dispose();}catch(Exception ex){Record("启动记录关闭失败："+ex.Message);}}
                throw;
            }
        }
        static string CheckpointPath(string fingerprint){return Path.Combine(UserDirectory(),"campaign-"+fingerprint+".checkpoint.json");}
        void SaveCheckpoint(){var s=session;if(s==null)return;var cp=s.CurrentCheckpoint;if(cp!=null)CampaignCheckpointStore.Save(CheckpointPath(cp.PlanFingerprint),cp);}
        public void StopWorkspace(string why)
        {
            bool sim=simulationTimer.Enabled;simulationTimer.Stop();if(sim&&simulation!=null){simulation.Engine.Stop(why);Record("模拟停止："+why);}
            var s=session;if(s!=null){s.Stop(why);try{SaveCheckpoint();}catch(Exception ex){Record("断点未保存："+ex.Message);}}livePlan=null;if(!closing)state.Text=why;
        }
        void CaptureEvidence(string status,OperationObservation o,OperationCommand c,long virtualMs)
        {
            if(journal==null)return;if(status!=lastEvidenceState||c!=null&&(c.Action==OperationAction.Key||c.Action==OperationAction.LeftClick||c.Action==OperationAction.RightClick||c.Action==OperationAction.Stop||c.Action==OperationAction.Completed))
            {try{journal.Record(status,c==null?"":c.Reason,o,c,virtualMs);}catch(Exception ex){Record("运行记录未能写入："+ex.Message);}}
            if(status!=lastEvidenceState){Record(Display(status)+" · "+(c==null?"":c.Reason));if(status.StartsWith("Recovery",StringComparison.Ordinal)||status.StartsWith("Vendor",StringComparison.Ordinal)||status=="Stopped")TakeSnapshot(status);lastEvidenceState=status;}
        }
        void TakeSnapshot(string why)
        {
            if(journal==null)return;long now=runClock.ElapsedMilliseconds;if(now-lastSnapshotAt<30000&&why!="模拟结束"&&why!="模拟异常")return;lastSnapshotAt=now;
            try{if(simulation!=null){using(var image=new Bitmap(Math.Max(1,canvas.Width),Math.Max(1,canvas.Height))){canvas.DrawToBitmap(image,new Rectangle(Point.Empty,canvas.Size));journal.SaveSnapshot(image,"simulation_canvas: "+why);}}else if(gameCapture!=null)using(var image=gameCapture())journal.SaveSnapshot(image,"game_client_capture: "+why);}catch(Exception ex){Record("本次截图不可用："+ex.Message);}
        }
        void RefreshState()
        {
            if(closing)return;try{
                var o=ReadLive();capabilities.Text="实际游戏状态（与模拟分开）\n\n"+(o.Fresh?"三面板已同步":"尚无有效三面板数据")+"\n释放按钮："+Known(o.ReleaseKnown,o.ReleaseVisible)+"\n复活按钮："+Known(o.ResurrectKnown,o.ResurrectVisible)+"\n尸体坐标："+(o.CorpseKnown?"已知":"未知")+"\n商店："+Known(o.MerchantKnown,o.MerchantOpen)+"\n背包及物品："+(o.InventoryKnown?"可读":"未知")+"\n光标无物品："+Known(o.CursorKnown,o.CursorEmpty)+"\n客户区："+(o.GeometryKnown?o.ClientWidth+" × "+o.ClientHeight:"未知")+"\n\n默认只卖已知灰色物品；任务物品、食物水和保护清单始终保留。\n\n实机适配、屏幕抓取及至少2小时游戏运行均待测试。";
                var s=session;if(s!=null){lastObservation=o;lastCommand=s.LastCommand;CaptureEvidence(s.EngineStatus,o,lastCommand,0);string value=s.Status+" / "+s.EngineStatus;state.Text="实机 "+Display(s.EngineStatus)+" · "+s.LastReason;if(value!=lastState){lastState=value;if(!s.IsActive){livePlan=null;try{SaveCheckpoint();}catch(Exception ex){Record(ex.Message);}TakeSnapshot(value);}}}
                if(Busy&&runClock.ElapsedMilliseconds-lastSnapshotAt>=30000)TakeSnapshot("周期记录");
            }catch(Exception ex){Record("状态更新失败："+ex.Message);}
            lock(logGate){int first=Math.Max(0,events.Count-6);output.Text=String.Join(Environment.NewLine,events.GetRange(first,events.Count-first).ToArray());}
        }
        static string Known(bool known,bool value){return known?(value?"是":"否"):"未知";}
        static string Display(string value){if(value==null)return "等待";var parts=value.Split('/');for(int i=0;i<parts.Length;i++)switch(parts[i]){case "Farm":parts[i]="打怪";break;case "Recovery":parts[i]="死亡恢复";break;case "Vendor":parts[i]="商人清包";break;case "Patrol":parts[i]="巡逻";break;case "Scan":parts[i]="选怪";break;case "Approach":parts[i]="接近";break;case "Combat":parts[i]="攻击";break;case "Loot":parts[i]="拾取";break;case "Skin":parts[i]="剥皮";break;case "Rest":parts[i]="吃喝恢复";break;case "CorpseRun":parts[i]="跑尸";break;case "Release":parts[i]="释放灵魂";break;case "Resurrect":parts[i]="复活";break;case "ReturnFarm":parts[i]="返回原断点";break;case "AwaitSale":parts[i]="确认售出";break;case "SelectNpc":parts[i]="选择商人";break;case "InspectInventory":parts[i]="筛选物品";break;case "Completed":parts[i]="已完成";break;case "Stopped":parts[i]="已停止";break;}return String.Join(" / ",parts);}
        public void Export()
        {
            if(runPlan==null||journal==null)throw new InvalidOperationException("请先运行一次模拟或实机会话。");string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"reports","campaign-"+DateTime.Now.ToString("yyyyMMdd-HHmmssfff"));Directory.CreateDirectory(path);TakeSnapshot("手工导出");journal.Export(path);var s=session;string[] log;lock(logGate)log=events.ToArray();
            var report=new{schema_version=1,tool_version="0.6.0",run_id=journal.RunId,client_version=runClientVersion,client=new{client_version=runClientVersion,build=runClientBuild==0?(object)null:runClientBuild,interface_version=runClientInterface==0?(object)null:runClientInterface},game_validation="pending",created_at=DateTimeOffset.Now.ToString("o"),source=simulation==null?"live_attempt":"offline_simulation",plan_fingerprint=runPlan.Fingerprint(),run_context=new{farm_reverse=runPlan.FarmReverse,farm_loop=runPlan.FarmLoop,start_index=runPlan.StartIndex},wall_elapsed_ms=runClock.ElapsedMilliseconds,simulation=simulation==null?null:new{completed=simulation.Completed,status=simulation.Engine.Status,reason=simulation.Engine.LastReason,virtual_elapsed_ms=simulation.ElapsedMilliseconds,native_input_events=simulation.InputEvents,kills=simulation.Kills,skill_casts=simulation.SkillCasts,loots=simulation.Loots,skins=simulation.Skins,deaths=simulation.Deaths,resurrections=simulation.Resurrections,recoveries=simulation.Engine.RecoveryCount,vendor_returns=simulation.Engine.VendorCount,sales=simulation.Sales,heals=simulation.Heals,drinks=simulation.Drinks},live=s==null?null:new{status=s.Status,engine=s.EngineStatus,reason=s.LastReason,checkpoint=s.CurrentCheckpoint,native_input_events=(object)null},input_fault=safety.Faulted,events=log};
            File.WriteAllText(Path.Combine(path,"campaign-report.json"),new JavaScriptSerializer{MaxJsonLength=8*1024*1024}.Serialize(report),new UTF8Encoding(true));RouteStore.Save(Path.Combine(path,"farm.json"),runPlan.FarmRoute);if(runPlan.CorpseRoute!=null)RouteStore.Save(Path.Combine(path,"corpse.json"),runPlan.CorpseRoute);if(runPlan.VendorRoute!=null)RouteStore.Save(Path.Combine(path,"vendor.json"),runPlan.VendorRoute);CampaignSettingsStore.Save(Path.Combine(path,"settings.json"),runPlan.Settings);
            var battle=runPlan.Battle.Clone();battle.BindingsConfirmed=battle.ApproachPositionConfirmed=battle.Calibration.Confirmed=false;foreach(var target in battle.Targets)if(!String.IsNullOrEmpty(target.AvatarFile)){try{using(var image=PortraitMatcher.LoadReference(runAvatarDirectory,target)){Directory.CreateDirectory(Path.Combine(path,"avatars"));target.AvatarFile="avatars/"+target.NameHash+".png";image.Save(Path.Combine(path,target.AvatarFile.Replace('/',Path.DirectorySeparatorChar)),System.Drawing.Imaging.ImageFormat.Png);}}catch{target.AvatarFile="";}}
            BattleProfileStore.Save(Path.Combine(path,"battle.json"),battle);lastReport=path;Record("本次记录已导出："+path);state.Text="本次记录已导出："+path;
        }
    }
}
