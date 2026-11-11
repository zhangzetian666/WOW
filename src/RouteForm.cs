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
    public sealed class RouteForm : Form
    {
        readonly Func<RouteTelemetry> telemetry;
        readonly Func<IInputBackend> backendFactory;
        readonly Action stopSingleInput;
        readonly Action<string> parentLog;
        readonly InputFailureLatch inputSafety;
        readonly Stopwatch clock=Stopwatch.StartNew();
        readonly RouteRecorder recorder=new RouteRecorder();
        readonly System.Windows.Forms.Timer refresh=new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer simulationTimer=new System.Windows.Forms.Timer();
        readonly System.Threading.Timer watchdog;
        readonly TextBox nameBox=new TextBox(),output=new TextBox();
        readonly Label routeInfo=new Label(),status=new Label();
        readonly ComboBox type=new ComboBox(),sign=new ComboBox(),turnKeys=new ComboBox(),startMode=new ComboBox();
        readonly NumericUpDown axis=Number(.1m,10,1,3),north=Number(0,6.2832m,0,4),turnSpeed=Number(.1m,10,2,3),walkSpeed=Number(.0001m,.2m,.01m,4);
        readonly CheckBox reverse=new CheckBox(),loop=new CheckBox(),confirmed=new CheckBox(),blockSimulation=new CheckBox();
        readonly DataGridView points=new DataGridView();
        readonly RouteCanvas canvas=new RouteCanvas();
        readonly object eventLock=new object();
        readonly List<string> events=new List<string>();
        volatile RouteSession session;
        RouteDocument route;
        Navigator simulatorNavigator;
        NavigationSimulator simulator;
        bool closing;
        string sourcePath,lastCheckpoint="",lastStatus="";
        public bool LiveActive {get {var s=session;return s!=null&&s.IsActive;}}
        public bool SimulationRunning {get{return simulationTimer.Enabled;}}
        public bool SimulationCompleted {get{return simulatorNavigator!=null&&simulatorNavigator.Completed;}}
        public bool NavigationBusy {get{return LiveActive||SimulationRunning||recorder.IsRecording;}}
        public bool IsRecording {get{return recorder.IsRecording;}}

        public RouteForm(Func<RouteTelemetry> telemetry,Func<IInputBackend> backendFactory,Action stopSingleInput,Action<string> parentLog,InputFailureLatch inputSafety=null)
        {
            this.telemetry=telemetry;this.backendFactory=backendFactory;this.stopSingleInput=stopSingleInput;this.parentLog=parentLog;
            this.inputSafety=inputSafety??new InputFailureLatch();
            Text="第二阶段 · 路线录制与巡航  v0.2.0";Font=new Font("Microsoft YaHei UI",9F);BackColor=Color.FromArgb(247,249,252);ForeColor=Color.FromArgb(24,35,52);
            Size=new Size(1160,870);MinimumSize=new Size(1060,780);StartPosition=FormStartPosition.CenterScreen;
            var root=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=5,ColumnCount=1,Padding=new Padding(12)};
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,42));root.RowStyles.Add(new RowStyle(SizeType.Absolute,120));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,150));root.RowStyles.Add(new RowStyle(SizeType.Absolute,98));Controls.Add(root);
            root.Controls.Add(new Label{Text="先用演示路线做离线模拟。模拟只移动虚拟角色，不发送系统按键。\n真实游戏仍待实测；巡航遇到失焦、F12、旧数据、地图变化或战斗会停止。",Dock=DockStyle.Fill},0,0);

            var recordGroup=new GroupBox{Text="1  路线录制与文件",Dock=DockStyle.Fill,Padding=new Padding(8)};root.Controls.Add(recordGroup,0,1);
            var records=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};records.RowStyles.Add(new RowStyle(SizeType.Absolute,34));records.RowStyles.Add(new RowStyle(SizeType.Absolute,34));records.RowStyles.Add(new RowStyle(SizeType.Percent,100));recordGroup.Controls.Add(records);
            var meta=Row();records.Controls.Add(meta);meta.Controls.Add(Label("路线名称"));nameBox.Width=180;nameBox.Text="新路线";meta.Controls.Add(nameBox);
            type.DropDownStyle=ComboBoxStyle.DropDownList;type.Items.AddRange(new object[]{"farm","corpse_run","vendor_return"});type.SelectedIndex=0;type.Width=120;meta.Controls.Add(type);
            meta.Controls.Add(Button("开始录制游戏位置",StartRecording));meta.Controls.Add(Button("保存当前位置",AddPoint));meta.Controls.Add(Button("结束录制",FinishRecording));meta.Controls.Add(Button("取消录制",delegate{recorder.Cancel();UpdateInfo();}));
            var files=Row();records.Controls.Add(files);files.Controls.Add(Button("载入路线",LoadRoute));files.Controls.Add(Button("保存路线",SaveRoute));files.Controls.Add(Button("载入演示路线",delegate{if(!recorder.IsRecording)BindRoute(Demo(),null);}));files.Controls.Add(Button("导出本轮记录",Export));
            routeInfo.Dock=DockStyle.Fill;records.Controls.Add(routeInfo);

            var center=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,67));center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,33));root.Controls.Add(center,0,2);
            canvas.Dock=DockStyle.Fill;canvas.Margin=new Padding(0,8,8,8);center.Controls.Add(canvas,0,0);
            points.Dock=DockStyle.Fill;points.Margin=new Padding(0,8,0,8);points.RowHeadersVisible=false;points.AllowUserToAddRows=false;points.AllowUserToDeleteRows=false;points.ReadOnly=true;points.BackgroundColor=Color.White;points.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;
            foreach(string column in new string[]{"点","X","Y","等待毫秒"})points.Columns.Add(column,column);center.Controls.Add(points,1,0);

            var controls=new GroupBox{Text="2  模拟与真实巡航",Dock=DockStyle.Fill,Padding=new Padding(8)};root.Controls.Add(controls,0,3);
            var controlRows=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4};for(int i=0;i<3;i++)controlRows.RowStyles.Add(new RowStyle(SizeType.Absolute,30));controlRows.RowStyles.Add(new RowStyle(SizeType.Percent,100));controls.Controls.Add(controlRows);
            var mode=Row();controlRows.Controls.Add(mode);reverse.Text="反向";reverse.AutoSize=true;loop.Text="循环";loop.AutoSize=true;mode.Controls.Add(reverse);mode.Controls.Add(loop);
            mode.Controls.Add(Button("开始离线模拟",StartSimulation));mode.Controls.Add(Button("停止",delegate{StopAll("用户停止");}));blockSimulation.Text="模拟前进受阻";blockSimulation.AutoSize=true;mode.Controls.Add(blockSimulation);
            startMode.DropDownStyle=ComboBoxStyle.DropDownList;startMode.Width=115;startMode.Items.AddRange(new object[]{"从第一个点","最近路点","保存的断点"});startMode.SelectedIndex=0;mode.Controls.Add(startMode);
            mode.Controls.Add(Button("真实巡航（5秒后）",StartLive));
            var settings=Row();controlRows.Controls.Add(settings);settings.Controls.Add(Label("地图横纵比"));settings.Controls.Add(axis);settings.Controls.Add(Label("北向原始值"));settings.Controls.Add(north);settings.Controls.Add(Button("记录北向",CaptureNorth));
            sign.DropDownStyle=ComboBoxStyle.DropDownList;sign.Width=95;sign.Items.AddRange(new object[]{"方向 -1","方向 +1"});sign.SelectedIndex=0;settings.Controls.Add(sign);
            settings.Controls.Add(Label("转速 rad/s"));settings.Controls.Add(turnSpeed);settings.Controls.Add(Label("移动速度"));settings.Controls.Add(walkSpeed);
            var bindings=Row();controlRows.Controls.Add(bindings);bindings.Controls.Add(Label("转向键"));turnKeys.DropDownStyle=ComboBoxStyle.DropDownList;turnKeys.Width=95;turnKeys.Items.AddRange(new object[]{"Q / E","A / D"});turnKeys.SelectedIndex=0;bindings.Controls.Add(turnKeys);
            confirmed.AutoSize=true;confirmed.Text="已在当前地图核对以上标定、转向/平移键及聊天框状态";bindings.Controls.Add(confirmed);bindings.Controls.Add(Button("保存标定参数",SaveCalibration));bindings.Controls.Add(Button("载入标定参数",LoadCalibration));
            status.Dock=DockStyle.Fill;status.Text="默认离线模拟。真实巡航的默认数值只是候选值，需要实机标定。";controlRows.Controls.Add(status);
            output.Dock=DockStyle.Fill;output.Multiline=true;output.ReadOnly=true;output.ScrollBars=ScrollBars.Vertical;output.BackColor=Color.White;root.Controls.Add(output,0,4);
            foreach(var numeric in new[]{axis,north,turnSpeed,walkSpeed})numeric.ValueChanged+=delegate{StopExternal("标定参数已修改");confirmed.Checked=false;};
            sign.SelectedIndexChanged+=delegate{StopExternal("朝向方向已修改");confirmed.Checked=false;};turnKeys.SelectedIndexChanged+=delegate{StopExternal("键位已修改");confirmed.Checked=false;};
            reverse.CheckedChanged+=delegate{StopAll("路线方向已修改");if(reverse.Checked&&route!=null&&!route.AllowReverse){reverse.Checked=false;status.Text="该路线不允许逆序。";}UpdatePreview();};loop.CheckedChanged+=delegate{StopAll("循环设置已修改");};
            refresh.Interval=150;refresh.Tick+=delegate{UpdateLive();};refresh.Start();simulationTimer.Interval=50;simulationTimer.Tick+=delegate{SimulationTick();};
            watchdog=new System.Threading.Timer(delegate{var s=session;if(s!=null)s.Tick();},null,25,25);
            FormClosing+=delegate{closing=true;refresh.Stop();simulationTimer.Stop();StopAll("路线窗口关闭");try{SaveCheckpoint();}catch(Exception ex){Record("Checkpoint save failed: "+ex.Message);}watchdog.Dispose();};
            BindRoute(Demo(),null);
        }

        static NumericUpDown Number(decimal min,decimal max,decimal value,int decimals){return new NumericUpDown{Minimum=min,Maximum=max,Value=value,DecimalPlaces=decimals,Increment=decimals>2?.001m:.1m,Width=75};}
        static Label Label(string text){return new Label{Text=text,AutoSize=true,Margin=new Padding(6,7,1,0)};}
        static FlowLayoutPanel Row(){return new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=new Padding(0)};}
        static Button Button(string text,Action action){var b=new Button{Text=text,AutoSize=true,MinimumSize=new Size(70,27),Margin=new Padding(3,0,3,0),Padding=new Padding(3,0,3,0)};b.Click+=delegate{try{action();}catch(Exception ex){MessageBox.Show(ex.Message,"本次操作未执行",MessageBoxButtons.OK,MessageBoxIcon.Information);}};return b;}
        public static RouteDocument Demo()
        {return new RouteDocument{RouteId="square-demo",Name="离线方形演示",MapId=42,MapMode=2,Zone="SIMULATION",Points=new List<Waypoint>{new Waypoint{X=.50,Y=.50},new Waypoint{X=.52,Y=.50},new Waypoint{X=.52,Y=.52},new Waypoint{X=.50,Y=.52},new Waypoint{X=.50,Y=.50}}};}
        void Record(string text){lock(eventLock){events.Add(DateTimeOffset.Now.ToString("o")+" "+text);if(events.Count>2000)events.RemoveAt(0);}if(parentLog!=null)try{parentLog(text);}catch{} }
        void BindRoute(RouteDocument value,string path)
        {
            value.Validate();StopAll("路线已更换");SaveCheckpoint();session=null;simulator=null;simulatorNavigator=null;lastStatus="";route=value.Clone();sourcePath=path;nameBox.Text=route.Name;type.SelectedItem=route.RouteType;lastCheckpoint="";
            reverse.Checked=false;loop.Checked=route.Loop;UpdatePreview();canvas.Player=null;canvas.Highlight=0;canvas.Invalidate();confirmed.Checked=false;UpdateInfo();Record("Loaded route "+route.RouteId+" points="+route.Points.Count);
        }
        void UpdatePreview()
        {
            if(route==null)return;var working=reverse.Checked&&route.AllowReverse?route.Reversed():route.Clone();canvas.Route=working;canvas.AxisScale=(double)axis.Value;points.Rows.Clear();
            for(int i=0;i<working.Points.Count;i++){var p=working.Points[i];points.Rows.Add(i+1,p.X.ToString("F5"),p.Y.ToString("F5"),p.WaitMilliseconds);}canvas.Invalidate();
        }
        void UpdateInfo(){routeInfo.Text=recorder.IsRecording?"正在录制游戏路线，已有 "+recorder.Count+" 个点。拐角和障碍物前后分别保存。":route==null?"尚无路线。":"路线 "+route.Name+"   地图 "+route.MapId+" / 模式 "+route.MapMode+" / "+route.Zone+"   "+route.Points.Count+" 个点";}
        RouteTelemetry Snapshot(){var s=telemetry==null?null:telemetry();return s??new RouteTelemetry();}
        void StartRecording(){StopAll("开始录制");if(stopSingleInput!=null)stopSingleInput();RequireNoInputFault();var s=Snapshot();recorder.Start(s.Packet,s.Fresh,nameBox.Text,(string)type.SelectedItem);UpdateInfo();Record("Started recording actual game position.");}
        void AddPoint(){if(!recorder.IsRecording)throw new InvalidOperationException("请先开始录制。");var s=Snapshot();bool added=recorder.Add(s.Packet,s.Fresh);UpdateInfo();status.Text=added?"已保存当前位置。":"位置与上一点过近，未重复添加。";}
        void FinishRecording(){var completed=recorder.Stop();BindRoute(completed,null);status.Text="录制已结束。请保存路线文件。";}
        void LoadRoute(){if(recorder.IsRecording)throw new InvalidOperationException("请先结束或取消录制。");using(var d=new OpenFileDialog{Filter="路线 JSON|*.json",InitialDirectory=UserRouteDirectory()})if(d.ShowDialog()==DialogResult.OK)BindRoute(RouteStore.Load(d.FileName),d.FileName);}
        void SaveRoute(){if(route==null||recorder.IsRecording)throw new InvalidOperationException("请先完成路线录制或载入路线。");using(var d=new SaveFileDialog{Filter="路线 JSON|*.json",InitialDirectory=UserRouteDirectory(),FileName=route.RouteId+".json"})if(d.ShowDialog()==DialogResult.OK){var copy=route.Clone();copy.Name=nameBox.Text;copy.Loop=loop.Checked;RouteStore.Save(d.FileName,copy);BindRoute(copy,d.FileName);status.Text="路线已保存。";}}
        static string UserRouteDirectory(){string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"routes","user");Directory.CreateDirectory(path);return path;}
        NavigationCalibration Settings(bool live)
        {
            if(route==null)throw new InvalidOperationException("请先载入路线。");double direction=sign.SelectedIndex==0?-1:1;
            var c=new NavigationCalibration{AxisScale=(double)axis.Value,FacingSign=direction,FacingOffset=-direction*(double)north.Value,TurnRadiansPerSecond=(double)turnSpeed.Value,ForwardUnitsPerSecond=(double)walkSpeed.Value,Confirmed=live&&confirmed.Checked,MapId=route.MapId,MapMode=route.MapMode,Zone=route.Zone};c.Validate();return c;
        }
        void CaptureNorth(){north.Value=(decimal)CalibrationObservation.North(route,Snapshot());status.Text="已记录当前北向原始值。还需核对方向、转速与地图横纵比。";}
        void SaveCalibration(){var c=Settings(false);using(var d=new SaveFileDialog{Filter="标定 JSON|*.json",InitialDirectory=UserRouteDirectory(),FileName="calibration-"+route.MapMode+"-"+route.MapId+".json"})if(d.ShowDialog()==DialogResult.OK)File.WriteAllText(d.FileName,new JavaScriptSerializer().Serialize(c),new UTF8Encoding(true));}
        void LoadCalibration()
        {
            if(route==null)throw new InvalidOperationException("请先载入路线。");
            using(var d=new OpenFileDialog{Filter="标定 JSON|*.json",InitialDirectory=UserRouteDirectory()})if(d.ShowDialog()==DialogResult.OK)
            {
                var file=new FileInfo(d.FileName);if(file.Length>65536)throw new InvalidOperationException("标定文件过大。");
                var c=new JavaScriptSerializer().Deserialize<NavigationCalibration>(File.ReadAllText(d.FileName,Encoding.UTF8));if(c==null)throw new InvalidOperationException("空标定文件。");c.Validate();
                if(c.MapId!=route.MapId||c.MapMode!=route.MapMode||c.Zone!=route.Zone)throw new InvalidOperationException("标定参数属于其他地图或区域。");
                StopAll("载入标定参数");axis.Value=(decimal)c.AxisScale;sign.SelectedIndex=c.FacingSign<0?0:1;double n=NormalizePositive(-c.FacingOffset/c.FacingSign);north.Value=(decimal)n;turnSpeed.Value=(decimal)c.TurnRadiansPerSecond;walkSpeed.Value=(decimal)c.ForwardUnitsPerSecond;confirmed.Checked=false;
                status.Text="标定参数已载入；真实巡航前仍需核对并勾选确认。";
            }
        }
        static double NormalizePositive(double angle){double n=angle%(2*Math.PI);return n<0?n+2*Math.PI:n;}
        void StartSimulation()
        {
            if(recorder.IsRecording)throw new InvalidOperationException("请先结束或取消游戏路线录制。");StopAll("开始离线模拟");if(stopSingleInput!=null)stopSingleInput();RequireNoInputFault();var c=Settings(false);UpdatePreview();
            simulatorNavigator=new Navigator(route,c,reverse.Checked,loop.Checked,0);simulator=new NavigationSimulator(route,c,reverse.Checked,loop.Checked,0);simulationTimer.Start();status.Text="离线模拟运行中；未创建真实输入后端。";Record("Simulation started; native input events=0.");
        }
        void SimulationTick()
        {
            try
            {
                var state=simulator.Sample();var command=simulatorNavigator.Step(state,simulator.ElapsedMilliseconds);
                if(command.Action==NavigationAction.Completed||command.Action==NavigationAction.Stop){simulationTimer.Stop();status.Text="模拟"+ActionName(command.Action)+"："+command.Reason;Record(status.Text);}
                else if(command.Action==NavigationAction.None)simulator.Advance(200);
                else if(blockSimulation.Checked&&command.Action==NavigationAction.Forward)simulator.Advance(command.HoldMilliseconds);
                else simulator.Apply(command);
                canvas.Player=new PointF((float)simulator.X,(float)simulator.Y);canvas.Highlight=simulatorNavigator.TargetIndex;canvas.AxisScale=(double)axis.Value;canvas.Invalidate();
                if(simulationTimer.Enabled)status.Text="离线模拟 · 路点 "+(simulatorNavigator.TargetIndex+1)+" / "+route.Points.Count+" · "+ActionName(command.Action)+" · 脱困 "+simulatorNavigator.RecoveryAttempts+" · 系统按键 0";
            }
            catch(Exception ex){simulationTimer.Stop();status.Text="模拟已停止："+ex.Message;Record(status.Text);}
        }
        int StartIndex(RouteDocument working,NavigationSample state)
        {
            if(startMode.SelectedIndex==2)
            {
                var checkpoint=CheckpointStore.Load(CheckpointPath(),route,reverse.Checked,loop.Checked);
                if(checkpoint.Completed)throw new InvalidOperationException("这条断点已完成，请选择起点重新开始。");return checkpoint.NextIndex;
            }
            if(startMode.SelectedIndex!=1)return 0;
            double distance=Double.MaxValue;int index=0;
            for(int i=0;i<working.Points.Count;i++){double d=Distance(state.X,state.Y,working.Points[i].X,working.Points[i].Y,(double)axis.Value);if(d<distance){distance=d;index=i;}}
            return index;
        }
        void StartLive()
        {
            if(recorder.IsRecording)throw new InvalidOperationException("请先结束或取消路线录制。");if(!confirmed.Checked)throw new InvalidOperationException("请先在实际游戏里完成地图与转向标定，并勾选确认。");
            var calibration=Settings(true);var current=Snapshot().Navigation();var working=reverse.Checked?route.Reversed():route.Clone();int index=StartIndex(working,current);
            if(!current.Fresh||Distance(current.X,current.Y,working.Points[index].X,working.Points[index].Y,calibration.AxisScale)>.02)throw new InvalidOperationException("请先建立有效状态读取，并把角色手动移到所选起点附近。");
            StopAll("开始真实巡航");if(stopSingleInput!=null)stopSingleInput();RequireNoInputFault();UpdatePreview();
            var backend=backendFactory==null?null:backendFactory();if(backend==null)throw new InvalidOperationException("请在主窗口选择实际游戏窗口。");
            var candidate=new RouteSession(backend,delegate{return clock.ElapsedMilliseconds;},delegate{return Snapshot().Navigation();},Record,inputSafety);
            candidate.Start(route,calibration,Bindings(),reverse.Checked,loop.Checked,index);session=candidate;lastCheckpoint="";confirmed.Checked=false;status.Text="真实巡航5秒后开始。切回游戏，鼠标移出左上角；F12停止。";
        }
        RouteKeyBindings Bindings(){return turnKeys.SelectedIndex==0?new RouteKeyBindings():new RouteKeyBindings{TurnLeft=0x41,TurnRight=0x44,StrafeLeft=0x51,StrafeRight=0x45};}
        static string ActionName(NavigationAction action)
        {switch(action){case NavigationAction.Forward:return "前进";case NavigationAction.TurnLeft:return "左转";case NavigationAction.TurnRight:return "右转";case NavigationAction.StrafeLeft:return "左移脱困";case NavigationAction.StrafeRight:return "右移脱困";case NavigationAction.Jump:return "短跳脱困";case NavigationAction.Completed:return "已完成";case NavigationAction.Stop:return "已停止";default:return "等待新数据";}}
        static string SessionName(string value)
        {switch(value){case "Countdown":return "倒计时";case "Holding":return "执行短按";case "WaitingForFrame":return "等待新数据";case "Completed":return "已完成";case "Faulted":return "输入故障已锁定";case "Stopped":return "已停止";default:return "待开始";}}
        void RequireNoInputFault(){if(inputSafety.Faulted)throw new InvalidOperationException("输入发送或释放曾失败。请手动松开相关按键，检查游戏状态并重启工具。");}
        string CheckpointPath(){if(route==null)throw new InvalidOperationException("没有路线。");return Path.Combine(UserRouteDirectory(),"checkpoint-"+route.Fingerprint().Substring(0,16)+".json");}
        void SaveCheckpoint()
        {
            var s=session;if(s==null)return;var cp=s.CurrentCheckpoint;if(cp==null)return;string identity=cp.NextIndex+":"+cp.Reverse+":"+cp.Loop+":"+cp.Completed+":"+s.Status;
            if(identity==lastCheckpoint)return;CheckpointStore.Save(Path.Combine(UserRouteDirectory(),"checkpoint-"+cp.RouteFingerprint.Substring(0,16)+".json"),cp);lastCheckpoint=identity;
        }
        public void StopExternal(string reason){var s=session;if(s!=null)s.Stop(reason);}
        public void StopWorkspace(string reason){StopAll(reason);}
        void StopAll(string reason){simulationTimer.Stop();StopExternal(reason);if(!closing){status.Text=reason;UpdateLive();}}
        void UpdateLive()
        {
            var s=session;
            if(s!=null)
            {
                try{SaveCheckpoint();}catch(Exception ex){s.Stop("断点保存失败："+ex.Message);status.Text=s.LastReason;}
                if(s.IsActive)
                {
                    status.Text="真实巡航 · "+SessionName(s.Status)+" · 路点 "+(s.TargetIndex+1)+" / "+route.Points.Count+" · F12急停";
                    var snap=Snapshot().Navigation();if(snap.Fresh)canvas.Player=new PointF((float)snap.X,(float)snap.Y);canvas.Highlight=s.TargetIndex;canvas.AxisScale=(double)axis.Value;canvas.Invalidate();
                }
                else if(!simulationTimer.Enabled&&lastStatus!=s.Status){status.Text="真实巡航"+SessionName(s.Status)+"："+s.LastReason;}
                lastStatus=s.Status;
            }
            string[] recent;lock(eventLock){int take=Math.Min(5,events.Count);recent=events.GetRange(events.Count-take,take).ToArray();}output.Text=String.Join(Environment.NewLine,recent);
        }
        void Export()
        {
            StopAll("导出记录");string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"reports","route-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(path);
            string[] entries;lock(eventLock)entries=events.ToArray();
            var record=new {tool_version="0.2.0",game_validation="pending",route=route==null?null:new {route_id=route.RouteId,map_id=route.MapId,map_mode=route.MapMode,zone=route.Zone,points=route.Points.Count},reverse=reverse.Checked,loop=loop.Checked,displayed_calibration=route==null?null:Settings(false),source_path=sourcePath,simulation=simulator==null?null:new {x=simulator.X,y=simulator.Y,milliseconds=simulator.ElapsedMilliseconds,native_input_events=simulator.InputEvents},live=session==null?null:new {status=session.Status,reason=session.LastReason,checkpoint=session.CurrentCheckpoint},events=entries};
            File.WriteAllText(Path.Combine(path,"route-report.json"),new JavaScriptSerializer().Serialize(record),new UTF8Encoding(true));File.WriteAllLines(Path.Combine(path,"events.txt"),entries,new UTF8Encoding(true));status.Text="记录已导出到 "+path;
        }
        static double Distance(double x,double y,double xt,double yt,double scale){double dx=(xt-x)*scale,dy=yt-y;return Math.Sqrt(dx*dx+dy*dy);}
    }

    public sealed class RouteCanvas : Control
    {
        public RouteDocument Route;
        public PointF? Player;
        public int Highlight;
        public double AxisScale=1;
        public RouteCanvas(){DoubleBuffered=true;BackColor=Color.White;}
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);if(Route==null||Route.Points.Count==0)return;
            e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            double minX=1,maxX=0,minY=1,maxY=0;
            foreach(var p in Route.Points){minX=Math.Min(minX,p.X);maxX=Math.Max(maxX,p.X);minY=Math.Min(minY,p.Y);maxY=Math.Max(maxY,p.Y);}
            if(Player.HasValue){minX=Math.Min(minX,Player.Value.X);maxX=Math.Max(maxX,Player.Value.X);minY=Math.Min(minY,Player.Value.Y);maxY=Math.Max(maxY,Player.Value.Y);}
            double spanX=Math.Max(.005,(maxX-minX)*AxisScale),spanY=Math.Max(.005,maxY-minY);double factor=Math.Min(Math.Max(1,Width-70)/spanX,Math.Max(1,Height-70)/spanY);
            Func<double,double,PointF> point=delegate(double x,double y){return new PointF((float)(35+(x-minX)*AxisScale*factor),(float)(35+(y-minY)*factor));};
            using(var line=new Pen(Color.FromArgb(70,105,170),2))
            {
                for(int i=1;i<Route.Points.Count;i++){var a=Route.Points[i-1];var b=Route.Points[i];e.Graphics.DrawLine(line,point(a.X,a.Y),point(b.X,b.Y));}
                for(int i=0;i<Route.Points.Count;i++){var p=point(Route.Points[i].X,Route.Points[i].Y);e.Graphics.FillEllipse(i==Highlight?Brushes.DarkOrange:Brushes.SteelBlue,p.X-4,p.Y-4,8,8);if(Route.Points.Count<100)e.Graphics.DrawString((i+1).ToString(),Font,Brushes.Black,p.X+5,p.Y+2);}
                if(Highlight>=0&&Highlight<Route.Points.Count){var t=Route.Points[Highlight];var selected=point(t.X,t.Y);e.Graphics.FillEllipse(Brushes.DarkOrange,selected.X-5,selected.Y-5,10,10);}
                if(Player.HasValue){var p=point(Player.Value.X,Player.Value.Y);e.Graphics.FillEllipse(Brushes.DarkGreen,p.X-6,p.Y-6,12,12);e.Graphics.DrawString("角色",Font,Brushes.DarkGreen,p.X+8,p.Y-10);}
            }
            e.Graphics.DrawString("蓝线：录制路线   橙点：当前目标   绿点：角色",Font,Brushes.DimGray,8,8);
        }
    }
}
