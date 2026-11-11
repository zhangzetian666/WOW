using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace WoWProbe
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new ProbeForm());
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "第一阶段检测工具启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }

    public sealed class ProbeForm : Form
    {
        readonly Stopwatch clock=Stopwatch.StartNew();
        readonly Heartbeat heartbeat=new Heartbeat();
        readonly object stateLock=new object();
        readonly object logLock=new object();
        readonly List<string> eventLog=new List<string>();
        readonly InputFailureLatch inputSafety=new InputFailureLatch();
        readonly ComboBox windows=new ComboBox(), keys=new ComboBox();
        readonly NumericUpDown size=new NumericUpDown();
        readonly Label connection=new Label(), calibration=new Label(), actionStatus=new Label();
        readonly TextBox stateText=new TextBox(), notes=new TextBox();
        readonly CheckBox consent=new CheckBox();
        readonly DataGridView checklist=new DataGridView();
        readonly System.Windows.Forms.Timer poll=new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer picker=new System.Windows.Forms.Timer();
        System.Threading.Timer watchdog;
        WindowInfo target;
        volatile InputGuard guard;
        ProbePacket packet;
        CombatPacket combatPacket;
        OperationsPacket operationsPacket;
        OperationObservation imageEvidence;
        long capturedAtMilliseconds=-1;
        Point clientCenter;
        Bitmap lastGrid;
        bool calibrated,reading,closing;
        int pickSeconds;
        string lastError="等待选择游戏窗口并定位色块。";
        string previousGuardStatus="";
        RouteForm routeWindow;
        FarmForm farmWindow;
        CampaignForm campaignWindow;
        volatile int calibratedCellSize=8;
        readonly Dictionary<string, ushort> keyCodes=new Dictionary<string, ushort>();

        public ProbeForm()
        {
            Text="状态检测 · 路线、打怪与完整流程  v0.6.0";
            Font=new Font("Microsoft YaHei UI",9F);
            BackColor=Color.FromArgb(247,249,252);
            ForeColor=Color.FromArgb(24,35,52);
            MinimumSize=new Size(900,740);
            Size=new Size(1010,860);
            StartPosition=FormStartPosition.CenterScreen;
            AutoScaleMode=AutoScaleMode.Font;
            var main=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(14)};
            main.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute,146));
            main.RowStyles.Add(new RowStyle(SizeType.Percent,50));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute,132));
            main.RowStyles.Add(new RowStyle(SizeType.Percent,50));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
            Controls.Add(main);
            var title=new Label {Text="先读取状态，再做一次按键测试。F12 或鼠标移入屏幕左上角可取消按键。\n游戏需保持窗口化并露出插件色块；本工具默认只读。",Dock=DockStyle.Fill,AutoSize=false};
            main.Controls.Add(title,0,0);

            var setup=Group("1  选择游戏窗口与定位",main,1);
            var setupRows=new TableLayoutPanel {Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,Padding=new Padding(7)};
            setupRows.RowStyles.Add(new RowStyle(SizeType.Absolute,34));
            setupRows.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
            setupRows.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            setup.Controls.Add(setupRows);
            var row1=Row(); setupRows.Controls.Add(row1);
            windows.DropDownStyle=ComboBoxStyle.DropDownList; windows.Width=630;
            windows.SelectedIndexChanged+=delegate { ChangeTarget(); };
            row1.Controls.Add(windows); row1.Controls.Add(Button("刷新窗口",RefreshWindows));
            var row2=Row(); setupRows.Controls.Add(row2);
            row2.Controls.Add(Button("定位首个色块中心（3秒）",BeginPick));
            row2.Controls.Add(new Label {Text="色块宽度",AutoSize=true,Margin=new Padding(12,8,3,0)});
            size.Minimum=2;size.Maximum=32;size.Value=8;size.Width=55;
            size.ValueChanged+=delegate { InvalidateCalibration("色块尺寸已变更，请重新定位。"); };
            row2.Controls.Add(size);
            row2.Controls.Add(Button("暂停读取",delegate { PauseReading(); }));
            calibration.AutoSize=true;calibration.Text="尚未定位";calibration.Margin=new Padding(8,8,0,0);row2.Controls.Add(calibration);
            connection.Dock=DockStyle.Fill;connection.AutoSize=true;connection.Text=lastError;setupRows.Controls.Add(connection);

            var data=Group("2  状态读取结果",main,2);
            stateText.Multiline=true;stateText.ReadOnly=true;stateText.ScrollBars=ScrollBars.Vertical;
            stateText.Dock=DockStyle.Fill;stateText.BackColor=Color.White;stateText.Font=new Font("Microsoft YaHei UI",10F);
            stateText.Text="尚未读取。安装插件后，用鼠标定位左上角网格的第一个色块中心。";
            data.Controls.Add(stateText);

            var action=Group("3  单次按键验证",main,3);
            var actions=new TableLayoutPanel {Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,Padding=new Padding(7)};
            action.Controls.Add(actions);
            consent.Text="我已确认游戏内没有打开聊天框或菜单，并已核对按键绑定。";consent.AutoSize=true;
            actions.Controls.Add(consent);
            var actionRow=Row();actions.Controls.Add(actionRow);
            keys.DropDownStyle=ComboBoxStyle.DropDownList;keys.Width=185;
            AddKey("W  前进",0x57);AddKey("S  后退",0x53);AddKey("Q  按当前绑定",0x51);AddKey("E  按当前绑定",0x45);
            AddKey("A  按当前绑定",0x41);AddKey("D  按当前绑定",0x44);AddKey("TAB  选怪",9);AddKey("F  按当前绑定",0x46);
            foreach (int k in new int[] {1,2,3,4,5,8,9}) AddKey(k+"  动作条",(ushort)(0x30+k));
            AddKey("空格  跳跃",32);keys.SelectedIndex=0;
            actionRow.Controls.Add(keys);actionRow.Controls.Add(Button("5秒后发送一次（120毫秒）",Schedule));
            var stop=Button("立即取消 / 释放按键",delegate { Stop("用户点击停止"); });stop.ForeColor=Color.DarkRed;actionRow.Controls.Add(stop);
            actionStatus.Dock=DockStyle.Fill;actionStatus.Text="未启用按键。勾选确认后，每次只测试一个按键；倒计时中请切回游戏。";actions.Controls.Add(actionStatus);

            var verification=Group("4  实机验收记录",main,4);
            var verifyLayout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};
            verifyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,64));verifyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,36));
            verification.Controls.Add(verifyLayout);
            checklist.Dock=DockStyle.Fill;checklist.RowHeadersVisible=false;checklist.AllowUserToAddRows=false;checklist.AllowUserToDeleteRows=false;
            checklist.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;checklist.BackgroundColor=Color.White;checklist.EditMode=DataGridViewEditMode.EditOnEnter;
            checklist.Columns.Add(new DataGridViewTextBoxColumn {Name="项目",ReadOnly=true,FillWeight=75});
            var resultColumn=new DataGridViewComboBoxColumn {Name="结果",FillWeight=25};resultColumn.Items.AddRange("未测","通过","失败","不适用");checklist.Columns.Add(resultColumn);
            foreach(string name in new string[] {"插件加载与版本","坐标随移动更新","朝向随转向更新","血量与资源变化","目标名称与目标切换","死亡与灵魂状态","背包空格与包满提示","商人及拾取窗口","一次移动或转向按键","F交互及剥皮键","F12急停释放","失焦与数据失效停止"}) checklist.Rows.Add(name,"未测");
            verifyLayout.Controls.Add(checklist,0,0);
            notes.Dock=DockStyle.Fill;notes.Multiline=true;notes.ScrollBars=ScrollBars.Vertical;notes.Text="备注：请填写客户端版本、职业、地图、失败步骤和实际现象。";verifyLayout.Controls.Add(notes,1,0);
            var bottom=Row();bottom.Dock=DockStyle.Fill;main.Controls.Add(bottom,0,5);
            bottom.Controls.Add(Button("导出本轮测试报告",ExportReport));
            bottom.Controls.Add(Button("路线录制与巡航",OpenRoutes));
            bottom.Controls.Add(Button("目标与基础打怪",OpenFarming));
            bottom.Controls.Add(Button("完整运行与恢复",OpenCampaign));
            bottom.Controls.Add(new Label {AutoSize=true,Text="支持无游戏离线模拟。",Margin=new Padding(10,10,0,0)});
            poll.Interval=200;poll.Tick+=delegate { ReadTick(); };poll.Start();
            picker.Interval=1000;picker.Tick+=delegate { PickTick(); };
            watchdog=new System.Threading.Timer(delegate { try { InputGuard g=guard;if(g!=null){g.Tick();if(g.Status=="Faulted")inputSafety.Fail(g.LastReason);} } catch { InputGuard g=guard;if(g!=null){g.Stop("输入监控异常");if(g.Status=="Faulted")inputSafety.Fail(g.LastReason);} } },null,25,25);
            FormClosing+=delegate { closing=true;poll.Stop();picker.Stop();Stop("检测工具关闭");if(routeWindow!=null&&!routeWindow.IsDisposed)routeWindow.Close();if(farmWindow!=null&&!farmWindow.IsDisposed)farmWindow.Close();if(campaignWindow!=null&&!campaignWindow.IsDisposed)campaignWindow.Close();watchdog.Dispose();if(lastGrid!=null)lastGrid.Dispose(); };
            RefreshWindows();
        }

        static GroupBox Group(string title,TableLayoutPanel parent,int row)
        { var g=new GroupBox {Text=title,Dock=DockStyle.Fill,Padding=new Padding(8),Margin=new Padding(0,3,0,6)};parent.Controls.Add(g,0,row);return g; }
        static FlowLayoutPanel Row() { return new FlowLayoutPanel {AutoSize=true,WrapContents=false,Dock=DockStyle.Fill,Margin=new Padding(0)}; }
        static Button Button(string text,Action action)
        { var b=new Button {Text=text,AutoSize=true,MinimumSize=new Size(90,30),Margin=new Padding(3),Padding=new Padding(4,0,4,0)};b.Click+=delegate {action();};return b; }
        void AddKey(string label,ushort code) { keys.Items.Add(label);keyCodes[label]=code; }
        void Log(string text)
        { lock(logLock) {eventLog.Add(DateTimeOffset.Now.ToString("o")+" "+text);if(eventLog.Count>200)eventLog.RemoveAt(0);} }
        bool FrameFresh(long now){return reading&&calibrated&&capturedAtMilliseconds>=0&&now>=capturedAtMilliseconds&&unchecked((ulong)now-(ulong)capturedAtMilliseconds)<900UL&&heartbeat.IsFresh(now);}
        bool Fresh() { lock(stateLock) return FrameFresh(clock.ElapsedMilliseconds); }
        bool CanInput()
        { lock(stateLock) return !inputSafety.Faulted && InputEligibility.Allows(packet,FrameFresh(clock.ElapsedMilliseconds)); }

        void Stop(string reason)
        { Log(reason);InputGuard g=guard;if(g!=null){g.Stop(reason);if(g.Status=="Faulted")inputSafety.Fail(g.LastReason);}if(routeWindow!=null&&!routeWindow.IsDisposed)routeWindow.StopWorkspace(reason);if(farmWindow!=null&&!farmWindow.IsDisposed)farmWindow.StopWorkspace(reason);if(campaignWindow!=null&&!campaignWindow.IsDisposed)campaignWindow.StopWorkspace(reason);if(!closing) {consent.Checked=false;actionStatus.Text=reason;} }
        RouteTelemetry RouteSnapshot()
        {lock(stateLock)return new RouteTelemetry{Packet=packet,Fresh=FrameFresh(clock.ElapsedMilliseconds)};}
        void OpenRoutes()
        {
            if(routeWindow==null||routeWindow.IsDisposed)
                routeWindow=new RouteForm(RouteSnapshot,delegate{return target==null?null:new WindowsInputBackend(target,Log);},delegate{Stop("已停止单次按键，准备路线操作");},Log,inputSafety);
            routeWindow.Show(this);routeWindow.Activate();
        }
        FarmObservation FarmSnapshot()
        {lock(stateLock)return FarmObservation.FromPackets(packet,combatPacket,FrameFresh(clock.ElapsedMilliseconds));}
        OperationObservation OperationSnapshot()
        {
            lock(stateLock){var o=OperationObservation.FromPackets(packet,combatPacket,operationsPacket,FrameFresh(clock.ElapsedMilliseconds));if(imageEvidence!=null&&imageEvidence.Sequence==o.Sequence){o.PortraitCheckKnown=imageEvidence.PortraitCheckKnown;o.PortraitMatch=imageEvidence.PortraitMatch;o.PortraitCheckSequence=imageEvidence.PortraitCheckSequence;o.PortraitCheckTargetToken=imageEvidence.PortraitCheckTargetToken;}return o;}
        }
        void OpenCampaign()
        {
            if(campaignWindow==null||campaignWindow.IsDisposed)
                campaignWindow=new CampaignForm(OperationSnapshot,delegate{return target==null?null:new WindowsCampaignInputBackend(target,Log);},CaptureAvatar,CaptureGameClient,delegate{
                    if(routeWindow!=null&&!routeWindow.IsDisposed&&routeWindow.IsRecording)throw new InvalidOperationException("请先结束或取消路线录制。");
                    Stop("切换到完整运行工作区。");
                },Log,inputSafety,ClickRegionAllowed);
            campaignWindow.Show(this);campaignWindow.Activate();
        }
        Bitmap CaptureGameClient()
        {
            if(target==null||!Native.Matches(target)||Native.GetForegroundWindow()!=target.Handle)throw new InvalidOperationException("游戏不在前台，未获取游戏截图。");
            return Native.Capture(Native.ClientBounds(target));
        }
        bool ClickRegionAllowed(Rectangle r)
        {
            if(!calibrated||target==null)return false;var bounds=Native.ClientBounds(target);if(!new Rectangle(Point.Empty,bounds.Size).Contains(r))return false;
            var first=Native.CaptureGeometry(clientCenter,calibratedCellSize,new Rectangle(Point.Empty,bounds.Size));
            for(int i=0;i<3;i++)if(r.IntersectsWith(new Rectangle(first.X+i*34*calibratedCellSize,first.Y,first.Width,first.Height)))return false;return true;
        }
        void OpenFarming()
        {
            if(farmWindow==null||farmWindow.IsDisposed)
                farmWindow=new FarmForm(FarmSnapshot,delegate{return target==null?null:new WindowsInputBackend(target,Log);},CaptureAvatar,delegate{
                    if(routeWindow!=null&&!routeWindow.IsDisposed&&routeWindow.IsRecording)throw new InvalidOperationException("请先结束或取消路线录制。");
                    Stop("切换到基础打怪工作区");
                },Log,inputSafety);
            farmWindow.Show(this);farmWindow.Activate();
        }
        Bitmap CaptureAvatar(Rectangle clientRectangle)
        {
            if(!calibrated||target==null)throw new InvalidOperationException("请先选择游戏窗口并定位状态面板。");
            Rectangle bounds=Native.ClientBounds(target);var region=new Rectangle(bounds.X+clientRectangle.X,bounds.Y+clientRectangle.Y,clientRectangle.Width,clientRectangle.Height);
            if(clientRectangle.Width<1||clientRectangle.Height<1||clientRectangle.Width>512||clientRectangle.Height>512||!bounds.Contains(region))throw new InvalidOperationException("头像参考区域无效。");
            var first=Native.CaptureGeometry(new Point(bounds.X+clientCenter.X,bounds.Y+clientCenter.Y),calibratedCellSize,bounds);
            for(int i=0;i<3;i++)if(region.IntersectsWith(new Rectangle(first.X+i*34*calibratedCellSize,first.Y,first.Width,first.Height)))throw new InvalidOperationException("头像区域与状态色块重叠，本次只保存名称。");
            return Native.Capture(region);
        }
        void ResetData()
        { lock(stateLock) {heartbeat.Reset();packet=null;combatPacket=null;operationsPacket=null;imageEvidence=null;capturedAtMilliseconds=-1;} if(lastGrid!=null){lastGrid.Dispose();lastGrid=null;} }
        void InvalidateCalibration(string message)
        { Stop(message);reading=false;calibrated=false;calibratedCellSize=(int)size.Value;ResetData();calibration.Text="尚未定位";lastError=message; }
        void ChangeTarget()
        {
            picker.Stop();InvalidateCalibration("窗口已变更，请定位色块。");
            target=windows.SelectedItem as WindowInfo;
            guard=target==null?null:new InputGuard(new WindowsInputBackend(target,Log),delegate{return clock.ElapsedMilliseconds;},CanInput);
        }
        void RefreshWindows()
        {
            Stop("窗口列表刷新");windows.Items.Clear();
            foreach(var window in Native.Windows())windows.Items.Add(window);
            if(windows.Items.Count>0 && ((WindowInfo)windows.Items[0]).ProcessName.StartsWith("wow",StringComparison.OrdinalIgnoreCase))windows.SelectedIndex=0;
            else {target=null;guard=null;InvalidateCalibration("请选择实际游戏窗口；未发现时请先进入游戏，再刷新。");}
        }
        void PauseReading()
        { picker.Stop();Stop("已暂停读取");reading=false;lock(stateLock)heartbeat.Invalidate();lastError="读取暂停。点击定位色块可重新开始。"; }
        void BeginPick()
        {
            if(target==null){MessageBox.Show("请先选择实际游戏窗口。");return;}
            Stop("准备定位色块");reading=false;ResetData();calibrated=false;pickSeconds=3;picker.Start();
            calibration.Text="请把鼠标移到第一个色块中心，3秒后自动记录。";
        }
        void PickTick()
        {
            pickSeconds--;
            if(pickSeconds>0){calibration.Text=pickSeconds+" 秒后记录位置";return;}
            picker.Stop();
            try
            {
                Rectangle bounds=Native.ClientBounds(target);Point point=Cursor.Position;
                Native.CaptureGeometry(point,(int)size.Value,bounds);
                clientCenter=new Point(point.X-bounds.X,point.Y-bounds.Y);
                calibratedCellSize=(int)size.Value;
                calibrated=true;reading=true;ResetData();calibration.Text="已定位，持续读取中";lastError="等待两个连续更新的数据包。";
            }
            catch(Exception ex){InvalidateCalibration(ex.Message);}
        }
        void ReadTick()
        {
            InputGuard g=guard;
            if(g!=null && g.Status!=previousGuardStatus) {previousGuardStatus=g.Status;actionStatus.Text=GuardMessage(g);Log("InputGuard "+g.Status+" "+g.LastReason);}
            if(!reading || !calibrated){connection.Text=lastError;return;}
            try
            {
                var bounds=Native.ClientBounds(target);
                var center=new Point(bounds.X+clientCenter.X,bounds.Y+clientCenter.Y);
                var region=Native.CaptureGeometry(center,(int)size.Value,bounds);
                var extended=new Rectangle(region.X,region.Y,66*(int)size.Value,region.Height);
                if(bounds.Contains(extended)&&SystemInformation.VirtualScreen.Contains(extended))region=extended;
                extended=new Rectangle(region.X,region.Y,100*(int)size.Value,region.Height);
                if(bounds.Contains(extended)&&SystemInformation.VirtualScreen.Contains(extended))region=extended;
                long captureMoment=clock.ElapsedMilliseconds;
                using(var image=Native.Capture(region))
                {
                    var bytes=PixelCodec.Decode(image,(int)size.Value/2,(int)size.Value/2,(int)size.Value);
                    var parsed=ProbePacket.Decode(bytes);
                    CombatPacket extra=null;
                    if(image.Width>=66*(int)size.Value)try{var raw=PixelCodec.Decode(image,34*(int)size.Value+(int)size.Value/2,(int)size.Value/2,(int)size.Value);var candidate=CombatPacket.Decode(raw);if(candidate.Sequence==parsed.Sequence)extra=candidate;}catch(FormatException){}catch(ArgumentException){}
                    OperationsPacket operations=null;
                    if(image.Width>=100*(int)size.Value)try{var raw=PixelCodec.Decode(image,68*(int)size.Value+(int)size.Value/2,(int)size.Value/2,(int)size.Value);var candidate=OperationsPacket.Decode(raw);if(candidate.Sequence==parsed.Sequence)operations=candidate;}catch(FormatException){}catch(ArgumentException){}
                    var derived=OperationObservation.FromPackets(parsed,extra,operations,true);
                    if(campaignWindow!=null&&!campaignWindow.IsDisposed)campaignWindow.PrepareImageEvidence(derived);
                    lock(stateLock){heartbeat.Observe(parsed,clock.ElapsedMilliseconds);packet=parsed;combatPacket=extra;operationsPacket=operations;imageEvidence=derived;capturedAtMilliseconds=captureMoment;}
                    if(lastGrid!=null)lastGrid.Dispose();lastGrid=(Bitmap)image.Clone();
                    lastError=Fresh()?"数据正在更新；这只说明状态通道可读，游戏内动作仍需实测。":"已读到数据，正在等待序号更新。";
                    connection.Text=lastError;connection.ForeColor=Fresh()?Color.DarkGreen:Color.DarkGoldenrod;
                    stateText.Text=Describe(parsed,Fresh())+Environment.NewLine+"战斗扩展："+(extra==null?"不可用或序号不一致；基础读取仍可用，禁止实机打怪。":"已配对。身份/射程/坐标/拾取/剥皮/读条能力掩码 "+extra.Capabilities);
                }
            }
            catch(Exception ex)
            {
                lock(stateLock){heartbeat.Invalidate();combatPacket=null;operationsPacket=null;imageEvidence=null;capturedAtMilliseconds=-1;}
                if(g!=null && (g.Active || g.Pending))Stop("读取失败，已取消按键："+ex.Message);
                if(lastError!=ex.Message)Log("Read failure: "+ex.Message);
                lastError=ex.Message;connection.ForeColor=Color.DarkRed;connection.Text="读取失败："+ex.Message;
                stateText.Text="当前读取失败。以下仅为上一次成功数据，已禁止据此发送按键。"+Environment.NewLine+(packet==null?"尚无成功数据。":Describe(packet,false));
            }
        }
        static string Percent(int n) {return n==255?"不可读":n+"%";}
        static string Coordinate(double n) {return Double.IsNaN(n)?"不可读":n.ToString("F5");}
        static string Describe(ProbePacket p,bool fresh)
        {
            return "客户端 "+p.ClientVersion+"  build "+p.Build+"  接口 "+p.InterfaceVersion+"  序号 "+p.Sequence+(fresh?"（正在更新）":"（等待更新或已过期）")+Environment.NewLine+
                "坐标 X "+Coordinate(p.X)+"    Y "+Coordinate(p.Y)+"    朝向 "+Coordinate(p.Facing)+Environment.NewLine+
                "地图 "+p.MapId+"（模式 "+p.MapMode+"）  区域 "+p.Zone+Environment.NewLine+
                "血量 "+Percent(p.Health)+"    资源 "+Percent(p.Power)+"（类型 "+p.PowerType+"，0为法力）  背包空格 "+(p.FreeSlots<0?"不可读":p.FreeSlots.ToString())+Environment.NewLine+
                "目标 "+p.TargetName+"    目标血量 "+Percent(p.TargetHealth)+Environment.NewLine+
                "死亡 "+Flag(p,1)+"  灵魂 "+Flag(p,2)+"  战斗 "+Flag(p,4)+"  商店 "+Flag(p,32)+"  拾取 "+Flag(p,64)+"  包满提示 "+Flag(p,128)+Environment.NewLine+
                "尸体坐标 "+Coordinate(p.CorpseX)+", "+Coordinate(p.CorpseY)+"    能力掩码 "+p.Capabilities+"  读取失败掩码 "+p.ReadFailures;
        }
        static string Flag(ProbePacket p,int flag) {return (p.KnownFlags&flag)==0?"不可读":((p.Flags&flag)!=0?"是":"否");}
        static string GuardMessage(InputGuard g)
        {
            switch(g.Status)
            {
                case "Idle": return "等待单次按键测试。";
                case "Countdown": return "倒计时中：请切回游戏，5秒后仅发送一次。F12可取消。";
                case "Holding": return "正在短按所选键，随后自动释放。";
                case "Faulted": return "按键发送或释放失败，已锁定测试。请导出报告并检查权限和窗口。";
                case "Stopped":
                    if(g.LastReason=="Single key pulse completed.")return "本次短按已结束。请观察游戏实际效果并填写记录。";
                    if(g.LastReason=="Target window is not foreground.")return "已取消：游戏未处于最前台。";
                    if(g.LastReason=="Screen packet is stale or invalid.")return "已取消：状态数据无效或停止更新。";
                    if(g.LastReason=="F12 emergency stop.")return "已急停：检测到F12或鼠标移入左上角。";
                    if(g.LastReason=="Key is already held; pulse cancelled.")return "已取消：你正在按住该键。";
                    return "已停止："+g.LastReason;
                default:return "等待下一次测试。";
            }
        }
        void Schedule()
        {
            try
            {
                if(inputSafety.Faulted)throw new InvalidOperationException("输入发送或释放曾失败。请手动松开按键，检查游戏状态并重启工具后再试。");
                if(routeWindow!=null&&!routeWindow.IsDisposed&&routeWindow.NavigationBusy)throw new InvalidOperationException("请先停止路线模拟、录制或真实巡航，再进行单次按键测试。");
                if(farmWindow!=null&&!farmWindow.IsDisposed&&farmWindow.Busy)throw new InvalidOperationException("请先停止打怪模拟或实机会话，再进行单次按键测试。");
                if(campaignWindow!=null&&!campaignWindow.IsDisposed&&campaignWindow.Busy)throw new InvalidOperationException("请先停止完整流程，再进行单次按键测试。");
                if(!consent.Checked)throw new InvalidOperationException("请先核对游戏内按键和聊天框状态，再勾选确认。");
                if(!CanInput())throw new InvalidOperationException("状态必须持续更新、关键状态可读且无读取错误后才能测试按键。请先导出报告排查不可读字段。");
                if(guard==null)throw new InvalidOperationException("请重新选择游戏窗口。");
                guard.Schedule(keyCodes[(string)keys.SelectedItem],5000,120);
                Log("Scheduled single pulse: "+keys.SelectedItem+" delay=5000 hold=120");
                consent.Checked=false;actionStatus.Text="5秒后仅发送一次所选键。现在切回游戏，并将鼠标移出屏幕左上角。";
            }
            catch(Exception ex){MessageBox.Show(ex.Message,"未开始按键测试",MessageBoxButtons.OK,MessageBoxIcon.Information);}
        }
        void ExportReport()
        {
            try
            {
                string inputStatus=guard==null?"No window selected":guard.Status;
                string inputReason=guard==null?"":guard.LastReason;
                Stop("已停止按键，正在导出报告");checklist.EndEdit();
                string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"reports",DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(directory);
                var checks=new List<object>();
                foreach(DataGridViewRow row in checklist.Rows)checks.Add(new {item=row.Cells[0].Value,result=row.Cells[1].Value});
                var report=new Dictionary<string,object>();
                report["tool_version"]="0.6.0";report["report_kind"]="game_observation_pending_manual_acceptance";
                report["created_local"]=DateTimeOffset.Now.ToString("o");report["windows"]=Environment.OSVersion.VersionString;
                report["process_bitness"]=IntPtr.Size*8;report["virtual_screen"]=SystemInformation.VirtualScreen.ToString();
                report["selected_window"]=target==null?null:target.ToString();report["fresh_at_export"]=Fresh();report["status"]=lastError;
                report["calibration"]=new {is_calibrated=calibrated,client_center_x=clientCenter.X,client_center_y=clientCenter.Y,cell_pixels=(int)size.Value};
                report["telemetry"]=packet==null?null:ReportData.From(packet);
                report["manual_results"]=checks;report["notes"]=notes.Text;
                report["input_before_export"]=new {status=inputStatus,reason=inputReason};
                lock(logLock)report["events"]=eventLog.ToArray();
                File.WriteAllText(Path.Combine(directory,"report.json"),new JavaScriptSerializer().Serialize(report),new UTF8Encoding(true));
                File.WriteAllText(Path.Combine(directory,"结果摘要.txt"),"本轮游戏电脑测试记录\r\n"+DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+"\r\n\r\n"+stateText.Text+"\r\n\r\n"+notes.Text,new UTF8Encoding(true));
                if(lastGrid!=null)lastGrid.Save(Path.Combine(directory,"overlay.png"),System.Drawing.Imaging.ImageFormat.Png);
                MessageBox.Show("报告已保存：\n"+directory+"\n\n请把本次整个文件夹发回，用于定位问题。","导出完成");
            }
            catch(Exception ex){MessageBox.Show(ex.Message,"导出失败：请将测试包放在可写的普通文件夹");}
        }
    }

    public static class ReportData
    {
        static object Number(double value) {return Double.IsNaN(value)?null:(object)value;}
        public static object From(ProbePacket p)
        {
            return new {sequence=p.Sequence,flags=p.Flags,known_flags=p.KnownFlags,capabilities=p.Capabilities,health=p.Health,power=p.Power,power_type=p.PowerType,
                x=Number(p.X),y=Number(p.Y),facing=Number(p.Facing),free_slots=p.FreeSlots,map_id=p.MapId,map_mode=p.MapMode,
                corpse_x=Number(p.CorpseX),corpse_y=Number(p.CorpseY),build=p.Build,interface_version=p.InterfaceVersion,
                client_version=p.ClientVersion,target=p.TargetName,target_health=p.TargetHealth,zone=p.Zone,read_failures=p.ReadFailures};
        }
    }
}
