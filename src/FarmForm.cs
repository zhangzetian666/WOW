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
    public sealed class FarmForm : Form
    {
        readonly Func<FarmObservation> reader;
        readonly Func<IInputBackend> backendFactory;
        readonly Func<Rectangle,Bitmap> avatarCapture;
        readonly Action prepareMode;
        readonly Action<string> parentLog;
        readonly InputFailureLatch safety;
        readonly Stopwatch clock=Stopwatch.StartNew();
        readonly System.Windows.Forms.Timer refresh=new System.Windows.Forms.Timer(),simulationTimer=new System.Windows.Forms.Timer();
        readonly System.Threading.Timer watchdog;
        readonly List<string> events=new List<string>();readonly object logGate=new object();
        readonly Label summary=new Label(),status=new Label(),observation=new Label();
        readonly TextBox targetName=new TextBox(),sequence=new TextBox(),output=new TextBox();
        readonly ListView targets=new ListView();readonly RouteCanvas canvas=new RouteCanvas();
        readonly NumericUpDown interval=Number(1000,5000,1500,0),heal=Number(2,80,40,0),mana=Number(1,80,35,0),recover=Number(3,100,85,0),range=Number(1,4,1,0);
        readonly CheckBox skin=new CheckBox(),approach=new CheckBox(),bindings=new CheckBox(),calibrated=new CheckBox(),positionConfirmed=new CheckBox(),reverse=new CheckBox(),loop=new CheckBox(),missingPosition=new CheckBox(),noSupplies=new CheckBox();
        readonly ComboBox startMode=new ComboBox();
        RouteDocument route;BattleProfile profile,runProfile;RouteDocument runRoute;FarmEngine engine;FarmSimulator simulator;volatile FarmSession session;
        bool changing,closing;string lastState="",profileDirectory;
        public bool Busy{get{return SimulationRunning||(session!=null&&session.IsActive);}}
        public bool SimulationRunning{get{return simulationTimer.Enabled;}}
        public bool SimulationCompleted{get{return engine!=null&&engine.Completed;}}
        public FarmSimulator Simulation{get{return simulator;}}
        public FarmForm(Func<FarmObservation> reader,Func<IInputBackend> backendFactory,Func<Rectangle,Bitmap> avatarCapture,Action prepareMode,Action<string> parentLog,InputFailureLatch safety)
        {
            this.reader=reader;this.backendFactory=backendFactory;this.avatarCapture=avatarCapture;this.prepareMode=prepareMode;this.parentLog=parentLog;this.safety=safety??new InputFailureLatch();
            Text="第三阶段 · 目标与基础打怪  v0.3.0";Font=new Font("Microsoft YaHei UI",9F);BackColor=Color.FromArgb(247,249,252);ForeColor=Color.FromArgb(24,35,52);
            Size=new Size(1180,900);MinimumSize=new Size(1100,820);StartPosition=FormStartPosition.CenterScreen;
            var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(12),ColumnCount=1,RowCount=6};Controls.Add(root);
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,48));root.RowStyles.Add(new RowStyle(SizeType.Absolute,122));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,182));root.RowStyles.Add(new RowStyle(SizeType.Absolute,98));root.RowStyles.Add(new RowStyle(SizeType.Absolute,90));
            root.Controls.Add(new Label{Text="先用离线模拟检查流程；实机运行仍待游戏电脑验证。目标按完整名称匹配，头像仅供人工参考。\nF12、失焦、数据过期会停止并释放按键；游戏中已经开始的动作可能需要手动取消。",Dock=DockStyle.Fill});
            var files=Group("1  路线、目标与配置",root,1);var rows=Rows(files,3);
            var row=Row();rows.Controls.Add(row);row.Controls.Add(Button("载入路线",LoadRoute));row.Controls.Add(Button("载入战斗配置",LoadProfile));row.Controls.Add(Button("保存战斗配置",SaveProfile));row.Controls.Add(Button("恢复演示配置",ResetDemo));row.Controls.Add(Button("导入地图标定",LoadCalibration));
            row=Row();rows.Controls.Add(row);targetName.Width=210;targetName.Text="";row.Controls.Add(Label("完整目标名称"));row.Controls.Add(targetName);row.Controls.Add(Button("保存当前怪物",delegate{SaveTarget("monster",true);}));row.Controls.Add(Button("保存当前NPC",delegate{SaveTarget("npc",true);}));row.Controls.Add(Button("手动添加怪物",delegate{SaveTarget("monster",false);}));row.Controls.Add(Button("删除选中目标",DeleteTarget));
            summary.Dock=DockStyle.Fill;rows.Controls.Add(summary);
            var center=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,55));center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,45));root.Controls.Add(center,0,2);
            canvas.Dock=DockStyle.Fill;canvas.Margin=new Padding(0,5,8,5);center.Controls.Add(canvas);
            targets.Dock=DockStyle.Fill;targets.View=View.Details;targets.FullRowSelect=true;targets.HideSelection=false;targets.Columns.Add("类型",65);targets.Columns.Add("完整名称",170);targets.Columns.Add("参考头像文件",235);center.Controls.Add(targets);
            var controls=Group("2  战斗与恢复设置",root,3);rows=Rows(controls,5);
            row=Row();rows.Controls.Add(row);sequence.Width=80;row.Controls.Add(Label("技能顺序 1–4"));row.Controls.Add(sequence);row.Controls.Add(Label("间隔 ms"));row.Controls.Add(interval);row.Controls.Add(Label("回血低于 %"));row.Controls.Add(heal);row.Controls.Add(Label("回蓝低于 %"));row.Controls.Add(mana);row.Controls.Add(Label("恢复至 %"));row.Controls.Add(recover);row.Controls.Add(Label("测距槽位"));row.Controls.Add(range);
            row=Row();rows.Controls.Add(row);Check(row,skin,"拾取后剥皮");Check(row,approach,"允许按目标坐标接近");Check(row,reverse,"逆向");Check(row,loop,"循环");row.Controls.Add(Label("起点"));startMode.DropDownStyle=ComboBoxStyle.DropDownList;startMode.Width=140;startMode.Items.AddRange(new object[]{"从第一点","读取保存的断点"});startMode.SelectedIndex=0;row.Controls.Add(startMode);
            row=Row();rows.Controls.Add(row);Check(row,bindings,"已核对动作条、F交互、5剥皮、8食物、9饮水、移动键");
            row=Row();rows.Controls.Add(row);Check(row,calibrated,"已在当前地图实测标定");Check(row,positionConfirmed,"已验证客户端可提供目标坐标（允许接近时必需）");
            observation.Dock=DockStyle.Fill;rows.Controls.Add(observation);
            var run=Group("3  运行与记录",root,4);rows=Rows(run,2);row=Row();rows.Controls.Add(row);row.Controls.Add(Button("开始离线战斗模拟",StartSimulation));row.Controls.Add(Button("停止",delegate{StopWorkspace("用户停止");}));Check(row,missingPosition,"模拟缺少目标坐标");Check(row,noSupplies,"模拟没有补给");row.Controls.Add(Button("实机打怪（5秒后）",StartLive));row.Controls.Add(Button("导出本次记录",Export));status.Dock=DockStyle.Fill;rows.Controls.Add(status);
            output.Dock=DockStyle.Fill;output.Multiline=true;output.ReadOnly=true;output.ScrollBars=ScrollBars.Vertical;output.BackColor=Color.White;root.Controls.Add(output,0,5);
            foreach(var n in new[]{interval,heal,mana,recover,range})n.ValueChanged+=delegate{SettingsChanged();};sequence.TextChanged+=delegate{SettingsChanged();};
            foreach(var c in new[]{skin,approach,reverse,loop,bindings,calibrated,positionConfirmed,missingPosition,noSupplies})c.CheckedChanged+=delegate{SettingsChanged();};startMode.SelectedIndexChanged+=delegate{SettingsChanged();};
            refresh.Interval=150;refresh.Tick+=delegate{RefreshState();};refresh.Start();simulationTimer.Interval=25;simulationTimer.Tick+=delegate{SimulationTick();};
            watchdog=new System.Threading.Timer(delegate{var s=session;if(s!=null)s.Tick();},null,25,25);
            FormClosing+=delegate{closing=true;refresh.Stop();StopWorkspace("战斗窗口关闭");watchdog.Dispose();simulationTimer.Dispose();refresh.Dispose();};ResetDemo();
        }
        static NumericUpDown Number(decimal min,decimal max,decimal value,int dp){return new NumericUpDown{Minimum=min,Maximum=max,Value=value,DecimalPlaces=dp,Width=65};}
        static Label Label(string s){return new Label{Text=s,AutoSize=true,Margin=new Padding(5,6,2,0)};}
        static FlowLayoutPanel Row(){return new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=new Padding(0)};}
        static GroupBox Group(string s,TableLayoutPanel parent,int index){var g=new GroupBox{Text=s,Dock=DockStyle.Fill,Padding=new Padding(8)};parent.Controls.Add(g,0,index);return g;}
        static TableLayoutPanel Rows(Control parent,int count){var p=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=count,ColumnCount=1};for(int i=0;i<count-1;i++)p.RowStyles.Add(new RowStyle(SizeType.Absolute,30));p.RowStyles.Add(new RowStyle(SizeType.Percent,100));parent.Controls.Add(p);return p;}
        static void Check(Control row,CheckBox c,string text){c.Text=text;c.AutoSize=true;row.Controls.Add(c);}
        static Button Button(string text,Action action){var b=new Button{Text=text,AutoSize=true,MinimumSize=new Size(65,27),Margin=new Padding(3,0,3,0)};b.Click+=delegate{try{action();}catch(Exception ex){MessageBox.Show(ex.Message,"本次操作未执行",MessageBoxButtons.OK,MessageBoxIcon.Information);}};return b;}
        public static RouteDocument DemoRoute(){return new RouteDocument{RouteId="farm-demo",Name="离线战斗演示",MapId=900001,MapMode=2,Zone="Simulation only",Points=new List<Waypoint>{new Waypoint{X=.50,Y=.50},new Waypoint{X=.52,Y=.50},new Waypoint{X=.52,Y=.52},new Waypoint{X=.50,Y=.52},new Waypoint{X=.50,Y=.50}}};}
        public static BattleProfile DemoProfile(){return new BattleProfile{Name="离线战斗演示",Skinning=true,AllowApproach=true,Targets=new List<BattleTarget>{new BattleTarget{Name="演示野兽",NameHash=TargetCatalog.HashName("演示野兽")}},Calibration=new NavigationCalibration{MapId=900001,MapMode=2,Zone="Simulation only"}};}
        void Record(string s){lock(logGate){events.Add(DateTimeOffset.Now.ToString("o")+" "+s);if(events.Count>2000)events.RemoveAt(0);}if(parentLog!=null)try{parentLog(s);}catch{} }
        static string UserDirectory(){var p=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"profiles","user");Directory.CreateDirectory(p);return p;}
        void ResetDemo(){StopWorkspace("恢复演示配置");session=null;engine=null;simulator=null;runProfile=null;runRoute=null;route=DemoRoute();profile=DemoProfile();profileDirectory=UserDirectory();changing=true;reverse.Checked=loop.Checked=missingPosition.Checked=noSupplies.Checked=false;startMode.SelectedIndex=0;changing=false;Populate();status.Text="默认离线模拟，系统输入为 0。实机功能待验证。";}
        void Populate()
        {
            changing=true;try{sequence.Text=profile.SkillSequence;interval.Value=profile.SkillIntervalMs;heal.Value=profile.HealBelow;mana.Value=profile.ManaBelow;recover.Value=profile.RecoverTo;range.Value=profile.RangeSlot;skin.Checked=profile.Skinning;approach.Checked=profile.AllowApproach;bindings.Checked=calibrated.Checked=positionConfirmed.Checked=false;}finally{changing=false;}
            UpdateTargets();Preview();summary.Text="路线 "+route.Name+" · 地图 "+route.MapId+" / "+route.MapMode+" / "+route.Zone+" · "+route.Points.Count+" 点；标定转速 "+profile.Calibration.TurnRadiansPerSecond+"，移速 "+profile.Calibration.ForwardUnitsPerSecond+"，请导入当前地图实测值。";
        }
        void Preview(){canvas.Route=reverse.Checked&&route.AllowReverse?route.Reversed():route.Clone();canvas.AxisScale=profile.Calibration.AxisScale;canvas.Invalidate();}
        void UpdateTargets(){targets.Items.Clear();foreach(var t in profile.Targets){var item=new ListViewItem(new[]{t.Kind=="monster"?"怪物":"NPC",t.Name,String.IsNullOrEmpty(t.AvatarFile)?"未保存":t.AvatarFile});item.Tag=t;targets.Items.Add(item);}}
        void SettingsChanged(){if(changing)return;StopWorkspace("设置已修改，请重新开始");Preview();}
        BattleProfile Settings(bool live)
        {
            var p=profile.Clone();p.SkillSequence=sequence.Text.Trim();p.SkillIntervalMs=(int)interval.Value;p.HealBelow=(int)heal.Value;p.ManaBelow=(int)mana.Value;p.RecoverTo=(int)recover.Value;p.RangeSlot=(int)range.Value;p.Skinning=skin.Checked;p.AllowApproach=approach.Checked;
            p.BindingsConfirmed=live&&bindings.Checked;p.Calibration.Confirmed=live&&calibrated.Checked;p.ApproachPositionConfirmed=live&&positionConfirmed.Checked;p.Validate();return p;
        }
        FarmObservation Snapshot(){return reader==null?new FarmObservation():reader()??new FarmObservation();}
        void SaveTarget(string kind,bool current)
        {
            StopWorkspace("修改目标库");string name=targetName.Text.Trim();var o=Snapshot();
            if(current){if(!o.Fresh||!o.KnownCore||!o.TargetExists||!o.TargetIdentityKnown||!o.NameHashKnown||!o.AttackabilityKnown||o.TargetIsPlayer)throw new InvalidOperationException("需要新鲜的插件扩展数据和已确认的非玩家目标。");if(name.Length==0)name=o.TargetName;if(TargetCatalog.HashName(name)!=o.TargetNameHash)throw new InvalidOperationException("显示名称可能被截断。请在名称框填写完整目标名称后重试。");if(kind=="monster"&&(!o.TargetCanAttack||!o.TargetDeadKnown||o.TargetDead))throw new InvalidOperationException("当前目标不是可确认攻击的存活怪物。");}
            var t=new BattleTarget{Name=name,NameHash=TargetCatalog.HashName(name),Kind=kind,SavedAtUtc=DateTime.UtcNow.ToString("o")};t.Validate();
            var next=profile.Clone();next.Targets.RemoveAll(delegate(BattleTarget x){return x.NameHash==t.NameHash;});next.Targets.Add(t);next.Validate();
            if(current&&o.PortraitKnown&&avatarCapture!=null)
            {
                try{string dir=Path.Combine(profileDirectory,"avatars");Directory.CreateDirectory(dir);string file=t.NameHash+"-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")+".png";using(var image=avatarCapture(o.Portrait))image.Save(Path.Combine(dir,file),System.Drawing.Imaging.ImageFormat.Png);t.AvatarFile="avatars/"+file;}
                catch(Exception ex){Record("头像参考图未保存："+ex.Message);}
            }
            profile=next;targetName.Text=name;UpdateTargets();Record("已保存"+(kind=="monster"?"怪物":"NPC")+"完整名称："+name+"。请保存战斗配置以写入文件。");
        }
        void DeleteTarget(){if(targets.SelectedItems.Count==0)return;StopWorkspace("删除目标");var next=profile.Clone();uint hash=((BattleTarget)targets.SelectedItems[0].Tag).NameHash;next.Targets.RemoveAll(delegate(BattleTarget x){return x.NameHash==hash;});next.Validate();profile=next;UpdateTargets();}
        void LoadRoute()
        {
            using(var d=new OpenFileDialog{Filter="路线 JSON|*.json",InitialDirectory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"routes")})if(d.ShowDialog()==DialogResult.OK)
            {var r=RouteStore.Load(d.FileName);StopWorkspace("路线已替换");session=null;engine=null;simulator=null;runProfile=null;runRoute=null;route=r;profile.Calibration=new NavigationCalibration{MapId=r.MapId,MapMode=r.MapMode,Zone=r.Zone};changing=true;reverse.Checked=false;loop.Checked=r.Loop;changing=false;Populate();status.Text="新地图使用候选标定值。请导入该地图的实测标定，不能直接沿用旧地图。";}
        }
        void LoadProfile()
        {
            using(var d=new OpenFileDialog{Filter="战斗配置 JSON|*.json",InitialDirectory=UserDirectory()})if(d.ShowDialog()==DialogResult.OK)
            {var p=BattleProfileStore.Load(d.FileName);RequireMap(p.Calibration);StopWorkspace("配置已替换");session=null;engine=null;simulator=null;runProfile=null;runRoute=null;profile=p;profileDirectory=Path.GetDirectoryName(d.FileName);Populate();}
        }
        void SaveProfile()
        {
            var p=Settings(false);StopWorkspace("保存战斗配置");using(var d=new SaveFileDialog{Filter="战斗配置 JSON|*.json",InitialDirectory=UserDirectory(),FileName="battle-profile.json"})if(d.ShowDialog()==DialogResult.OK)
            {
                string destination=Path.GetDirectoryName(d.FileName);foreach(var t in p.Targets)if(!String.IsNullOrEmpty(t.AvatarFile))
                {string source=Path.Combine(profileDirectory,t.AvatarFile.Replace('/',Path.DirectorySeparatorChar));string dest=Path.Combine(destination,t.AvatarFile.Replace('/',Path.DirectorySeparatorChar));if(!File.Exists(source)){t.AvatarFile="";continue;}if(!String.Equals(Path.GetFullPath(source),Path.GetFullPath(dest),StringComparison.OrdinalIgnoreCase)){Directory.CreateDirectory(Path.GetDirectoryName(dest));File.Copy(source,dest,true);}}
                BattleProfileStore.Save(d.FileName,p);profile=p;profileDirectory=destination;Populate();status.Text="配置已保存。实机确认勾选不会随文件启用。";
            }
        }
        void RequireMap(NavigationCalibration c){if(c.MapId!=route.MapId||c.MapMode!=route.MapMode||!String.Equals(c.Zone,route.Zone,StringComparison.Ordinal))throw new InvalidOperationException("地图不一致，请先载入对应路线。");}
        void LoadCalibration()
        {
            using(var d=new OpenFileDialog{Filter="地图标定 JSON|*.json",InitialDirectory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"routes")})if(d.ShowDialog()==DialogResult.OK)
            {if(new FileInfo(d.FileName).Length>65536)throw new InvalidOperationException("标定文件过大。");var c=new JavaScriptSerializer().Deserialize<NavigationCalibration>(File.ReadAllText(d.FileName,Encoding.UTF8));if(c==null)throw new InvalidOperationException("标定为空。");c.Validate();RequireMap(c);StopWorkspace("标定已修改");c.Confirmed=false;profile.Calibration=c;Populate();status.Text="已导入地图标定。实机运行前请核对并勾选确认。";}
        }
        void Prepare(){if(prepareMode!=null)prepareMode();if(safety.Faulted)throw new InvalidOperationException("输入故障尚未处理，请手动松开按键并重启工具。 "+safety.Reason);}
        void StartSimulation()
        {
            var p=Settings(false);StopWorkspace("开始离线模拟");Prepare();session=null;engine=new FarmEngine(route,p,reverse.Checked,loop.Checked,0);simulator=new FarmSimulator(route,p,reverse.Checked,loop.Checked,0){MissingTargetPosition=missingPosition.Checked,NoSupplies=noSupplies.Checked};runProfile=p.Clone();runRoute=route.Clone();lastState="";Preview();Record("离线战斗模拟启动，系统输入 0。");simulationTimer.Start();
        }
        void SimulationTick()
        {
            try{
                var o=simulator.Sample();var c=engine.Step(o,simulator.ElapsedMilliseconds);if(c.Action==FarmAction.None)simulator.Advance(200);else if(c.Action==FarmAction.Stop||c.Action==FarmAction.Completed){simulationTimer.Stop();Record("模拟结束："+engine.Status+" · "+c.Reason);}else{simulator.Apply(c);Record("模拟 "+c.Action+" · "+c.Reason);}
                canvas.Player=new PointF((float)simulator.X,(float)simulator.Y);canvas.Highlight=engine.RouteIndex;canvas.Invalidate();status.Text="离线 "+engine.Status+" · 路点 "+(engine.RouteIndex+1)+" · 击杀 "+simulator.Kills+" / 施放 "+simulator.SkillCasts+" / 拾取 "+simulator.Loots+" / 剥皮 "+simulator.Skins+" · 系统输入 0";
            }catch(Exception ex){simulationTimer.Stop();engine.Stop(ex.Message);status.Text="模拟停止："+ex.Message;Record(status.Text);}
        }
        void StartLive()
        {
            var p=Settings(true);RequireMap(p.Calibration);if(!p.Calibration.Confirmed||!p.BindingsConfirmed||(p.AllowApproach&&!p.ApproachPositionConfirmed))throw new InvalidOperationException("请先完成当前地图、键位和目标坐标能力的实机核对。");
            var o=Snapshot();var work=reverse.Checked?route.Reversed():route.Clone();int index=0;if(startMode.SelectedIndex==1){var cp=CheckpointStore.Load(CheckpointPath(),route,reverse.Checked,loop.Checked);if(cp.Completed)throw new InvalidOperationException("保存的断点已完成，请从第一点重新开始。");index=cp.NextIndex;}
            if(!o.Fresh||!o.KnownCore)throw new InvalidOperationException("需要同步、有效的战斗扩展数据。请更新插件并重新定位色块。");RequireMap(new NavigationCalibration{MapId=o.MapId,MapMode=o.MapMode,Zone=o.Zone});
            var start=work.Points[Math.Max(0,index-1)];if(NavigationMath.Distance(o.X-start.X,o.Y-start.Y,p.Calibration.AxisScale)>.02)throw new InvalidOperationException("请先移动到起点或断点附近（地图距离不超过 0.02）。");
            StopWorkspace("准备实机战斗");Prepare();var backend=backendFactory==null?null:backendFactory();if(backend==null)throw new InvalidOperationException("请先选择并定位游戏窗口。");
            var candidate=new FarmSession(backend,delegate{return clock.ElapsedMilliseconds;},Snapshot,Record,safety);candidate.Start(route,p,reverse.Checked,loop.Checked,index);session=candidate;runProfile=p.Clone();runRoute=route.Clone();engine=null;simulator=null;lastState="";Record("实机会话已倒计时，请在 5 秒内切换到游戏。游戏验证仍待人工记录。");
        }
        string CheckpointPath(){return Path.Combine(UserDirectory(),"farm-"+route.Fingerprint()+".checkpoint.json");}
        void SaveCheckpoint(){var s=session;if(s==null)return;var cp=s.CurrentCheckpoint;if(cp!=null)CheckpointStore.Save(CheckpointPath(),cp);}
        public void StopWorkspace(string why){bool wasSimulating=simulationTimer.Enabled;simulationTimer.Stop();if(wasSimulating&&engine!=null){engine.Stop(why);Record("模拟停止："+why);}var s=session;if(s!=null){s.Stop(why);try{SaveCheckpoint();}catch(Exception ex){Record("断点未保存："+ex.Message);}}if(!closing)status.Text=why;}
        void RefreshState()
        {
            if(closing)return;var o=Snapshot();observation.Text=o.Fresh?"插件扩展已同步 · HP "+o.Health+"% · 目标 "+o.TargetName+" · 目标坐标 "+(o.TargetPositionKnown?"可读":"未知")+" · 距离 "+(o.RangeKnown?(o.TargetInRange?"范围内":"范围外"):"未知"):"尚无有效的战斗扩展数据；可以运行离线模拟。";
            var s=session;if(s!=null){string state=s.Status+" / "+s.EngineState;if(state!=lastState){lastState=state;Record("实机状态 "+state+" · "+s.LastReason);if(!s.IsActive)try{SaveCheckpoint();}catch(Exception ex){Record(ex.Message);}}status.Text="实机 "+state+" · "+s.LastReason;}
            lock(logGate){int first=Math.Max(0,events.Count-7);output.Text=String.Join(Environment.NewLine,events.GetRange(first,events.Count-first).ToArray());}
        }
        public void Export()
        {
            string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"reports","farm-"+DateTime.Now.ToString("yyyyMMdd-HHmmssfff"));Directory.CreateDirectory(path);string[] logs;lock(logGate)logs=events.ToArray();var s=session;
            var o=Snapshot();
            var report=new {tool_version="0.3.0",created_at=DateTimeOffset.Now.ToString("o"),game_validation="pending",mode=simulator!=null?"offline_simulation":s!=null?"live_attempt":"not_started",route=runRoute,profile=runProfile,simulation=simulator==null?null:new {completed=SimulationCompleted,status=engine.Status,reason=engine.LastReason,native_input_events=simulator.InputEvents,kills=simulator.Kills,skill_casts=simulator.SkillCasts,loots=simulator.Loots,skins=simulator.Skins,heals=simulator.Heals,drinks=simulator.Drinks,missing_target_position=simulator.MissingTargetPosition,no_supplies=simulator.NoSupplies},live=s==null?null:new {status=s.Status,engine=s.EngineState,reason=s.LastReason,checkpoint=s.CurrentCheckpoint},observed_game_state=new {fresh=o.Fresh,known_core=o.KnownCore,sequence=o.Sequence,read_failures=o.ReadFailures,map_id=o.MapId,map_mode=o.MapMode,zone=o.Zone,health=o.Health,power=o.Power,power_type=o.PowerType,dead=o.PlayerDead,ghost=o.PlayerGhost,in_combat=o.InCombat,bag_full=o.BagFull,target_exists=o.TargetExists,target_identity_known=o.TargetIdentityKnown,target_token=o.TargetToken,name_hash_known=o.NameHashKnown,name_hash=o.TargetNameHash,display_name=o.TargetName,attackability_known=o.AttackabilityKnown,can_attack=o.TargetCanAttack,is_player=o.TargetIsPlayer,target_position_known=o.TargetPositionKnown,range_known=o.RangeKnown,range_slot=o.RangeSlot,in_range=o.TargetInRange,casting_known=o.CastingKnown,casting=o.Casting,channeling=o.Channeling,actions_known=o.ActionsKnown,known_actions_mask=o.KnownActionsMask,ready_actions_mask=o.ReadyActionsMask,usable_actions_mask=o.UsableActionsMask,loot_known=o.LootKnown,lootable=o.Lootable,loot_events_known=o.LootEventsKnown,loot_opened=o.LootOpened,loot_closed=o.LootClosed,skin_known=o.SkinKnown,skinnable=o.Skinnable,skin_event_known=o.SkinSuccessKnown,skin_success=o.SkinSuccess,cast_events_known=o.CastEventsKnown,cast_success=o.CastSuccess,cast_failed=o.CastFailed},input_fault=safety.Faulted,events=logs};
            File.WriteAllText(Path.Combine(path,"farm-report.json"),new JavaScriptSerializer{MaxJsonLength=4*1024*1024}.Serialize(report),new UTF8Encoding(true));Record("记录已导出："+path);status.Text="记录已导出："+path;
        }
    }
}
