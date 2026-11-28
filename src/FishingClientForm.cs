using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
namespace WoWProbe
{
    public sealed class FishingClientForm:Form
    {
        readonly string directory=AppDomain.CurrentDomain.BaseDirectory;FishingClientSettings settings;FishingClientRuntime runtime;
        readonly Dictionary<string,NumericUpDown> numbers=new Dictionary<string,NumericUpDown>();readonly Dictionary<string,CheckBox> checks=new Dictionary<string,CheckBox>();
        readonly RichTextBox log=new RichTextBox{ReadOnly=true,DetectUrls=false,ScrollBars=RichTextBoxScrollBars.Vertical,Dock=DockStyle.Fill};readonly Label status=new Label{AutoSize=true},routeLabel=new Label{AutoSize=true},corpseLabel=new Label{AutoSize=true};
        readonly TextBox pointsLabel=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Width=900,Height=160};
        readonly Button cancelRecording,cancelCorpseRecording;readonly Label regionStatus=new Label{AutoSize=true,MaximumSize=new Size(900,0),Margin=new Padding(4,6,4,8)};
        readonly TextBox npc=new TextBox{Width=200};
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=300};readonly object logGate=new object();FishingRouteRecorder recorder;bool busy;DateTime nextRecord;string recordingWarning="";DateTime lastRecordingWarning;
        bool recordingCorpse;string DraftPath{get{return Path.Combine(directory,recordingCorpse?"fishing-corpse-recording.json":"fishing-npc-recording.json");}}
        readonly FishingRestartSchedule restart=new FishingRestartSchedule();readonly Stopwatch restartClock=Stopwatch.StartNew();
        public FishingClientForm()
        {
            Text="独立钓鱼客户端 · FISHING 1.0.12";Font=new Font("Microsoft YaHei UI",10);Size=new Size(1050,820);MinimumSize=new Size(950,730);StartPosition=FormStartPosition.CenterScreen;
            settings=LoadSettings();var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(12)};root.RowStyles.Add(new RowStyle(SizeType.Absolute,60));root.RowStyles.Add(new RowStyle(SizeType.Percent,70));root.RowStyles.Add(new RowStyle(SizeType.Percent,30));Controls.Add(root);
            var top=Row();top.Controls.Add(Button("开始钓鱼",StartFishing));top.Controls.Add(Button("停止 / F12",StopFishing));top.Controls.Add(Button("保存设置",Save));top.Controls.Add(Button("导入方案",Import));top.Controls.Add(Button("导出方案",Export));top.Controls.Add(status);root.Controls.Add(top,0,0);
            var tabs=new TabControl{Dock=DockStyle.Fill};root.Controls.Add(tabs,0,1);root.Controls.Add(log,0,2);
            var page=Page(tabs,"钓鱼与时间");Info(page,"保存真实钓点、面水朝向和浮标观察区域后开始。默认1抛竿、F收杆，游戏需开启自动拾取；F12随时停止。");
            Number(page,"cast","抛竿快捷键（数字）",1,9,settings.Fishing.Settings.CastKey-48,0);
            Pair(page,"castMin","castMax","每次抛竿前随机等待（秒）",settings.CastDelay,0,60,2);
            Pair(page,"reelMin","reelMax","鱼讯后随机收杆延迟（秒，最高1秒）",settings.ReelDelay,0,1,2);
            Check(page,"audio","声音识别（系统播放声音，不采集麦克风）",settings.UseAudio);Check(page,"visual","水花图像识别（静音可用，需要选择观察区域）",settings.UseVisual);
            Check(page,"random","启用钓点连线随机位置（按保存顺序循环，不勾选则固定在最近保存点）",settings.RandomSpots);Pair(page,"stayMin","stayMax","每个随机钓点驻留（分钟）",settings.StayInterval(),.1m,1440,1,true);
            Check(page,"bags","定时按B查看背包，等待后再按B（无需扩展插件反馈）",settings.PeekBags);Pair(page,"bagMin","bagMax","查看背包间隔（分钟）",settings.BagMinutes,.1m,1440,1,true);Number(page,"bagOpenSeconds","背包保持打开（秒）",.1m,3600,settings.BagOpenSeconds,1);
            Check(page,"timed","启用随机下线和重新上线",settings.TimedSessions);Pair(page,"onMin","onMax","每轮上线时长（小时）",settings.OnlineHours,.01m,24,2);Pair(page,"offMin","offMax","每次离线等待（小时）",settings.OfflineHours,.01m,24,2);
            Info(page,"背包查看、换点、普通聊天及定时下线都等当前杆结束。重新上线后重新抽取在线时长。账号登录需要凭据时会等待，不保存密码。");
            page=Page(tabs,"定时按键");Check(page,"periodicKey","启用定时施放快捷键",settings.TimedKeyEnabled);
            Number(page,"periodicKeyValue","定时快捷键（数字0–9）",0,9,settings.TimedKey,0);Pair(page,"keyMin","keyMax","每次施放的随机间隔（分钟）",settings.KeyMinutes,.1m,1440,1);
            Info(page,"例如键8、间隔10至15分钟：每轮重新抽取执行时间。到期后等当前杆结束再按一次8，随后至少等待10秒且确认读条结束，再继续钓鱼并重新计时。等待期间不插入移动、抛竿、背包或聊天动作。");
            page=Page(tabs,"恢复与重启");Check(page,"retry","异常暂停后自动重新开始钓鱼",settings.AutoRetry);Number(page,"retrySeconds","暂停后的重试间隔（秒）",1,3600,settings.RetrySeconds,0);
            Info(page,"异常退出后按此间隔重新检查并启动；游戏失焦时保留任务，至少等待此间隔并等游戏回到前台后继续。不会弹出客户端或切走其他窗口。手动停止、F12急停、输入发送故障不会自动重试。点击“开始钓鱼”会先停止旧任务，待按键释放及线程退出后启动新任务，无需先点停止。");
            page=Page(tabs,"钓点与售卖路线");var row=Row();row.Controls.Add(Button("保存坐标（追加钓点）",SaveSpot));row.Controls.Add(Button("删除最后一个钓点",RemoveLastSpot));page.Controls.Add(row);page.Controls.Add(pointsLabel);
            Info(page,"每次保存都会追加一个钓点并立即列出坐标和朝向，支持最多500点。保存时面朝水面；所有点同图，相邻连线应可通行。按保存顺序连接，末点自动连接首点，无需重复保存起点；每条线轮换一次算一圈。");
            row=Row();row.Controls.Add(Button("开始录制NPC路线",BeginRecording));row.Controls.Add(Button("结束并保存路线",EndRecording));cancelRecording=Button("取消本次录制",CancelRecording);cancelRecording.Enabled=false;row.Controls.Add(cancelRecording);row.Controls.Add(Button("继续上次录制",ResumeRecording));row.Controls.Add(Button("保存当前NPC",SaveNpc));page.Controls.Add(row);page.Controls.Add(routeLabel);
            row=Row();row.Controls.Add(new Label{AutoSize=true,Text="NPC名称"});row.Controls.Add(npc);page.Controls.Add(row);npc.Text=settings.Service.Vendor.Name;
            Check(page,"vendor","背包满后沿已录路线自动售卖并原路返回",settings.Service.Vendor.Enabled);Check(page,"repair","同一NPC顺便自动修理",settings.Service.Repair.Enabled);
            row=Row();row.Controls.Add(Button("导入打怪客户端的路线/标定",ImportService));page.Controls.Add(row);
            Number(page,"axis","地图横纵比例",.1m,10,settings.Fishing.Calibration.AxisScale,3);Number(page,"turn","转向速度（弧度/秒）",.1m,10,settings.Fishing.Calibration.TurnRadiansPerSecond,3);Number(page,"speed","前进速度（地图单位/秒）",.0001m,.2m,settings.Fishing.Calibration.ForwardUnitsPerSecond,4);
            Info(page,"录制开始后手动走到商人旁，客户端自动连续采样。结束录制只保存真实走过的路线。默认方向键↑前进、←/→转向。");
            page=Page(tabs,"死亡跑尸");Check(page,"corpse","启用死亡后自动跑尸并返回钓点",settings.AutoCorpse);row=Row();row.Controls.Add(Button("开始录制跑尸路线",()=>BeginRecording(true)));row.Controls.Add(Button("结束并保存跑尸路线",EndRecording));cancelCorpseRecording=Button("取消本次录制",CancelRecording);row.Controls.Add(cancelCorpseRecording);row.Controls.Add(Button("继续上次跑尸录制",()=>ResumeRecording(true)));row.Controls.Add(Button("导入打怪跑尸路线",ImportCorpse));page.Controls.Add(row);page.Controls.Add(corpseLabel);
            Info(page,"从灵魂医者附近开始，沿实际可通行路径走到钓鱼区后结束录制；存活和灵魂状态均可录制，途中复活也可继续。死亡后只点击释放灵魂，不与灵魂医者交互；先到录制终点，再到实际死亡点接受尸体复活，确认存活后返回钓点。NPC与跑尸路线分别保存，死亡坐标自动记录。");
            page=Page(tabs,"水花与聊天");row=Row();row.Controls.Add(Button("框选浮标出现的水面",SelectRegion));row.Controls.Add(Button("查看 / 修改识别区域",SelectRegion));page.Controls.Add(row);page.Controls.Add(regionStatus);Number(page,"splash","水花检测阈值（较高更保守）",1.5m,10,settings.SplashSensitivity,1);
            Info(page,"框选小范围水面，尽量只包含自己的浮标，避开聊天框、姓名板和岸边动画。声音和水花只提供鱼讯候选；看到拾取事件才计收获。不会保存截图或原始录音。");
            Check(page,"chat","每完成全部钓点连线一圈，随机发一句普通说话",settings.ChatAtLap);Check(page,"whisper","自动回复新私信（首次电话回复，再次回复稍等）",settings.WhisperReply);
            Info(page,"话术库为assets/fishing/chat-phrases.txt，共1000句，可查看。私信按R前核对最后发送者，回复前再次核对收件人。自动聊天需要配套0.5.18-fishing扩展反馈；定时B和定时技能不需要此反馈。");
            timer.Tick+=Tick;timer.Start();FormClosing+=delegate(object sender,FormClosingEventArgs e){if(recorder!=null)try{recorder.Draft().Save(DraftPath);}catch(Exception ex){e.Cancel=true;MessageBox.Show("录制草稿保存失败，窗口保留："+ex.Message);return;}restart.Cancel();timer.Stop();if(runtime!=null)runtime.Dispose();};RefreshSummary();Write("独立钓鱼客户端已打开，配置文件独立于自动打怪客户端。");
        }
        FishingClientSettings LoadSettings()
        {
            var path=Path.Combine(directory,"fishing-settings.json");if(File.Exists(path))try{return FishingClientSettings.Load(path);}catch(Exception e){MessageBox.Show("配置未载入："+e.Message);}
            var p=new FishingClientSettings();p.Fishing.Movement=new RouteKeyBindings{Forward=38,Backward=40,TurnLeft=37,TurnRight=39,StrafeLeft=65,StrafeRight=68,Jump=32};p.Fishing.Calibration.AxisScale=1.5;p.Fishing.Calibration.TurnRadiansPerSecond=5.85;p.Fishing.Calibration.ForwardUnitsPerSecond=.008;return p;
        }
        static FlowLayoutPanel Row(){return new FlowLayoutPanel{AutoSize=true,WrapContents=true,Margin=new Padding(0,3,0,3)};}
        static FlowLayoutPanel Page(TabControl tabs,string title){var tab=new TabPage(title);tabs.TabPages.Add(tab);var page=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(10)};tab.Controls.Add(page);return page;}
        static void Info(Control page,string text){page.Controls.Add(new Label{Text=text,AutoSize=true,MaximumSize=new Size(900,0),Margin=new Padding(4,6,4,8)});}
        Button Button(string text,Action action){var b=new Button{Text=text,AutoSize=true,MinimumSize=new Size(105,32)};b.Click+=delegate{try{action();}catch(Exception e){Write(e.Message);MessageBox.Show(this,e.Message,"钓鱼客户端",MessageBoxButtons.OK,MessageBoxIcon.Information);}};return b;}
        void Check(Control page,string key,string title,bool value){var c=new CheckBox{Text=title,Checked=value,AutoSize=true,Margin=new Padding(4,6,4,6)};checks.Add(key,c);page.Controls.Add(c);}
        NumericUpDown N(string key,decimal min,decimal max,double value,int places){var n=new NumericUpDown{Minimum=min,Maximum=max,Value=Math.Max(min,Math.Min(max,(decimal)value)),DecimalPlaces=places,Increment=places==0?1m:places==1?.1m:.01m,Width=100};numbers.Add(key,n);return n;}
        void Number(Control page,string key,string text,decimal min,decimal max,double value,int places){var row=Row();row.Controls.Add(new Label{AutoSize=true,Text=text,Width=350,Margin=new Padding(4,6,4,0)});row.Controls.Add(N(key,min,max,value,places));page.Controls.Add(row);}
        void Pair(Control page,string a,string b,string text,FishingRange range,decimal min,decimal max,int places,bool labels=false){var row=Row();row.Controls.Add(new Label{AutoSize=true,Text=text+(labels?"　最短":""),Margin=new Padding(4,6,4,0)});row.Controls.Add(N(a,min,max,range.Min,places));row.Controls.Add(new Label{Text=labels?"最长":"至",AutoSize=true,Margin=new Padding(6,6,6,0)});row.Controls.Add(N(b,min,max,range.Max,places));page.Controls.Add(row);}
        double V(string key){return (double)numbers[key].Value;}bool C(string key){return checks[key].Checked;}FishingRange Range(string a,string b){return new FishingRange(V(a),V(b));}
        void Apply()
        {
            settings.Fishing.Settings.CastKey=(ushort)(48+V("cast"));settings.CastDelay=Range("castMin","castMax");settings.ReelDelay=Range("reelMin","reelMax");settings.BagMinutes=Range("bagMin","bagMax");settings.OnlineHours=Range("onMin","onMax");settings.OfflineHours=Range("offMin","offMax");
            settings.RandomSpots=C("random");settings.StayRange=Range("stayMin","stayMax");settings.StayMinutes=settings.StayRange.Min;settings.PeekBags=C("bags");settings.TimedSessions=C("timed");settings.UseAudio=C("audio");settings.UseVisual=C("visual");settings.ChatAtLap=C("chat");settings.WhisperReply=C("whisper");settings.SplashSensitivity=V("splash");
            settings.AutoRetry=C("retry");settings.RetrySeconds=(int)V("retrySeconds");
            settings.BagOpenSeconds=V("bagOpenSeconds");
            settings.AutoCorpse=C("corpse");
            settings.TimedKeyEnabled=C("periodicKey");settings.TimedKey=(int)V("periodicKeyValue");settings.KeyMinutes=Range("keyMin","keyMax");
            var cal=settings.Fishing.Calibration;cal.AxisScale=V("axis");cal.TurnRadiansPerSecond=V("turn");cal.ForwardUnitsPerSecond=V("speed");cal.Confirmed=true;settings.Service.Calibration=cal.Clone();settings.Service.Vendor.Enabled=C("vendor");settings.Service.Repair.Enabled=C("repair");settings.Service.Vendor.Name=npc.Text.Trim();settings.Service.Repair.Name=npc.Text.Trim();
        }
        void IdleRequired(){if(runtime!=null&&runtime.Running)throw new InvalidOperationException("请先停止钓鱼，再修改钓点或录制路线。");}
        void Save(){Apply();settings.Save(Path.Combine(directory,"fishing-settings.json"));Write("钓鱼设置已保存。");}
        OperationObservation Observe(bool focus)
        {if(focus){WindowState=FormWindowState.Minimized;GrindCli.FocusGameWindow();System.Threading.Thread.Sleep(150);}try{GrindCli.ReadRecordingPoint();return GrindCli.ReadOperations().Clone();}finally{if(focus){WindowState=FormWindowState.Normal;Activate();}}}
        void SetMap(FarmObservation f)
        {var c=settings.Fishing.Calibration;if(settings.Points.Count>0&&!MapIdentity.Matches(f.MapId,f.MapMode,f.Zone,c.MapId,c.MapMode,c.Zone))throw new InvalidOperationException("新钓点地图不同，请新建配置后录制。");c.MapId=f.MapId;c.MapMode=f.MapMode;c.Zone=f.Zone;c.Confirmed=true;settings.Service.Calibration=c.Clone();}
        void SaveSpot()
        {IdleRequired();Apply();var f=Observe(true).Farm;if(f.PlayerDead||f.PlayerGhost||f.InCombat)throw new InvalidOperationException("请在存活且脱战时保存钓点。");SetMap(f);FishingPointList.Append(settings,f);RefreshSummary();Save();Write("已追加钓点"+settings.Points.Count+"：("+(f.X*100).ToString("F3")+", "+(f.Y*100).ToString("F3")+") 朝向"+f.Facing.ToString("F3"));}
        void RemoveLastSpot(){IdleRequired();if(settings.Points.Count==0)return;settings.Points.RemoveAt(settings.Points.Count-1);RefreshSummary();Save();}
        void BeginRecording()
        {BeginRecording(false);}
        void BeginRecording(bool corpse)
        {
            IdleRequired();if(recorder!=null)throw new InvalidOperationException("正在录制，请先结束本次录制；已录点不会被覆盖。");restart.Cancel();Apply();recordingCorpse=corpse;
            WindowState=FormWindowState.Minimized;GrindCli.FocusGameWindow();System.Threading.Thread.Sleep(150);
            try{
                var f=Observe(false).Farm;SetMap(f);
                if(File.Exists(DraftPath))File.Copy(DraftPath,Path.ChangeExtension(DraftPath,"previous.json"),true);
                var next=new FishingRouteRecorder(settings.Fishing.Calibration,f,null,corpse);next.Draft().Save(DraftPath);recorder=next;nextRecord=DateTime.MinValue;recordingWarning="";
                Write(corpse?"开始录制跑尸路线，已保存起点；沿可通行路径走到钓鱼区后结束。":"开始录制NPC路线，已自动保存起点；请手动走到商人旁再结束。");RefreshSummary();
            }catch{WindowState=FormWindowState.Normal;Activate();throw;}
        }
        void ResumeRecording()
        {ResumeRecording(false);}
        void ResumeRecording(bool corpse)
        {
            IdleRequired();if(recorder!=null)throw new InvalidOperationException("本次录制仍在进行，请先结束。");
            restart.Cancel();recordingCorpse=corpse;if(!File.Exists(DraftPath))throw new InvalidOperationException("没有未完成的录制草稿。");
            var draft=FishingRecordingDraft.Load(DraftPath);var o=Observe(true);
            var next=FishingRouteRecorder.Resume(draft,o.Farm);SetMap(o.Farm);recorder=next;nextRecord=DateTime.MinValue;recordingWarning="";
            WindowState=FormWindowState.Minimized;GrindCli.FocusGameWindow();Write("继续上次录制，已保留"+recorder.Count+"个点。");RefreshSummary();
        }
        void CancelRecording()
        {
            IdleRequired();if(recorder==null){Write("当前没有正在进行的录制。");return;}
            busy=true;
            try{
                // Discard only this active recording. Saved routes and the previous draft are separate files.
                if(File.Exists(DraftPath))File.Delete(DraftPath);
                recorder=null;recordingWarning="";nextRecord=DateTime.MaxValue;RefreshSummary();
                Write("已取消本次录制。已保存的钓点和NPC路线保留，可重新开始录制。");
            }finally{busy=false;}
        }
        void EndRecording()
        {
            IdleRequired();if(recorder==null)throw new InvalidOperationException("尚未开始录制。");busy=true;
            try{
                Apply();OperationObservation observed=null;
                var result=FishingRecordingFinalizer.Finish(recorder,()=>{observed=Observe(true);return observed.Farm;},DraftPath);
                if(!result.Complete){recorder=null;RefreshSummary();Write(result.Message);MessageBox.Show(this,result.Message,"录制已结束，草稿已保存");return;}
                var candidate=settings.Clone();if(recordingCorpse){candidate.CorpseRoute=FishingRecoveryRoutes.Create(recorder.Calibration,result.Points);candidate.AutoCorpse=true;}else candidate.Service=FishingRecordingFinalizer.Service(candidate.Service,recorder.Calibration,result.Points,observed.Farm,npc.Text.Trim(),C("repair"));
                candidate.Save(Path.Combine(directory,"fishing-settings.json"));settings=candidate;recorder=null;Populate();
                // Keep the persisted samples as a recoverable draft, even after a successful finish.
                Write((recordingCorpse?"跑尸":"NPC")+"路线录制已结束并保存，共"+result.Points.Count+"个点。");
            }finally{busy=false;}
        }
        void SaveNpc(){IdleRequired();var f=Observe(true).Farm;if(!f.TargetExists||f.TargetIsPlayer||f.TargetCanAttack||f.TargetDead)throw new InvalidOperationException("请先选中售卖NPC。");npc.Text=f.TargetName;Save();}
        void RefreshSummary(){cancelRecording.Enabled=cancelCorpseRecording.Enabled=recorder!=null;bool areaValid=settings.VisualRegion!=null&&settings.VisualRegion.Valid;regionStatus.Text=areaValid?"水面选区：已保存。可点击上方按钮查看绿色选框或重新框选。":"水面选区：尚未保存。请在图片上按住鼠标左键拖出绿色框，再点击保存选区。";regionStatus.ForeColor=areaValid?Color.DarkGreen:Color.Firebrick;pointsLabel.Text=FishingPointList.Describe(settings.Points);pointsLabel.SelectionStart=pointsLabel.TextLength;pointsLabel.ScrollToCaret();routeLabel.Text="NPC路线："+settings.Service.Vendor.Points.Count+"个点"+(recorder!=null&&!recordingCorpse?"（录制中："+recorder.Count+"） 最后有效坐标："+recorder.Location:File.Exists(Path.Combine(directory,"fishing-npc-recording.json"))?"；有录制草稿，可继续补录":"");corpseLabel.Text="跑尸路线："+(settings.CorpseRoute==null?0:settings.CorpseRoute.Points.Count)+"个点"+(recorder!=null&&recordingCorpse?"（录制中："+recorder.Count+"） 最后有效坐标："+recorder.Location:File.Exists(Path.Combine(directory,"fishing-corpse-recording.json"))?"；有录制草稿，可继续补录":"");}
        void ImportCorpse(){IdleRequired();using(var d=new OpenFileDialog{Filter="跑尸路线 (*.json)|*.json"})if(d.ShowDialog(this)==DialogResult.OK){var r=RouteStore.Load(d.FileName);if(r.RouteType!="corpse_run")throw new InvalidOperationException("请选择打怪客户端的quick-corpse-route.json跑尸路线。");r.RouteId="recorded-ghost";var next=settings.Clone();next.CorpseRoute=r;next.AutoCorpse=true;next.Save(Path.Combine(directory,"fishing-settings.json"));settings=next;Populate();Write("已导入跑尸路线，共"+r.Points.Count+"个点。");}}
        Bitmap CaptureGame(){WindowState=FormWindowState.Minimized;try{GrindCli.FocusGameWindow();System.Threading.Thread.Sleep(200);GrindCli.ReadRecordingPoint();return GrindCli.FishingCapture();}finally{WindowState=FormWindowState.Normal;Activate();}}
        void SelectRegion(){IdleRequired();using(var capture=CaptureGame())using(var select=new FishingRegionForm(capture,settings.VisualRegion)){if(select.ShowDialog(this)==DialogResult.OK){settings.VisualRegion=select.SelectedRegion.Clone();Save();RefreshSummary();Write("水面识别选区已保存，下次打开仍保留绿色选框。");}}}
        void ImportService(){IdleRequired();using(var dialog=new OpenFileDialog{Filter="路线配置 (*.json)|*.json"})if(dialog.ShowDialog(this)==DialogResult.OK){var p=QuickRoutePlan.Load(dialog.FileName);settings.Service=p.Clone();settings.Fishing.Calibration=p.Calibration.Clone();numbers["axis"].Value=(decimal)p.Calibration.AxisScale;numbers["speed"].Value=(decimal)p.Calibration.ForwardUnitsPerSecond;numbers["turn"].Value=(decimal)p.Calibration.TurnRadiansPerSecond;checks["vendor"].Checked=p.Vendor.Enabled;checks["repair"].Checked=p.Repair.Enabled;npc.Text=p.Vendor.Name;RefreshSummary();Save();}}
        void Import(){IdleRequired();using(var d=new OpenFileDialog{Filter="钓鱼方案 (*.json)|*.json"})if(d.ShowDialog(this)==DialogResult.OK){var p=FishingClientSettings.Load(d.FileName);settings=p;Populate();p.Save(Path.Combine(directory,"fishing-settings.json"));Write("已导入钓鱼方案，全部设置已立即载入。");}}
        void Populate()
        {
            SetNumber("cast",settings.Fishing.Settings.CastKey-48);SetRange("cast",settings.CastDelay);SetRange("reel",settings.ReelDelay);SetRange("bag",settings.BagMinutes);SetRange("on",settings.OnlineHours);SetRange("off",settings.OfflineHours);
            SetRange("stay",settings.StayInterval());SetNumber("splash",settings.SplashSensitivity);SetNumber("axis",settings.Fishing.Calibration.AxisScale);SetNumber("turn",settings.Fishing.Calibration.TurnRadiansPerSecond);SetNumber("speed",settings.Fishing.Calibration.ForwardUnitsPerSecond);
            checks["retry"].Checked=settings.AutoRetry;SetNumber("retrySeconds",settings.RetrySeconds);
            SetNumber("bagOpenSeconds",settings.BagOpenSeconds);
            checks["corpse"].Checked=settings.AutoCorpse;
            checks["periodicKey"].Checked=settings.TimedKeyEnabled;SetNumber("periodicKeyValue",settings.TimedKey);SetRange("key",settings.KeyMinutes);
            checks["audio"].Checked=settings.UseAudio;checks["visual"].Checked=settings.UseVisual;checks["random"].Checked=settings.RandomSpots;checks["bags"].Checked=settings.PeekBags;checks["timed"].Checked=settings.TimedSessions;checks["chat"].Checked=settings.ChatAtLap;checks["whisper"].Checked=settings.WhisperReply;checks["vendor"].Checked=settings.Service.Vendor.Enabled;checks["repair"].Checked=settings.Service.Repair.Enabled;npc.Text=settings.Service.Vendor.Name;RefreshSummary();
        }
        void SetNumber(string key,double value){var n=numbers[key];n.Value=Math.Max(n.Minimum,Math.Min(n.Maximum,(decimal)value));}
        void SetRange(string key,FishingRange range){SetNumber(key+"Min",range.Min);SetNumber(key+"Max",range.Max);}
        void Export(){Apply();using(var d=new SaveFileDialog{Filter="钓鱼方案 (*.json)|*.json",FileName="我的钓鱼方案.json"})if(d.ShowDialog(this)==DialogResult.OK)settings.Save(d.FileName);}
        void StartFishing(){if(recorder!=null)throw new InvalidOperationException("请先结束NPC路线录制。");Save();settings.Validate(true);if(runtime!=null)runtime.Stop();restart.RequestStart();Write("已请求开始钓鱼：先释放旧任务按键，待旧线程退出后开始新一轮。");}
        void StopFishing(){restart.Cancel();if(runtime!=null)runtime.Stop();Write("已请求停止钓鱼，自动重试已取消。");}
        void LaunchFishing(bool manual)
        {try{if(runtime!=null)runtime.Dispose();runtime=new FishingClientRuntime(settings,directory,Write);if(manual)WindowState=FormWindowState.Minimized;runtime.Start(manual);Write(manual?"旧任务已退出，开始新一轮钓鱼。":"自动重试：重新核对游戏窗口和钓鱼状态。");}catch(Exception e){restart.Cancel();Write("未开始："+e.Message);}}
        void Tick(object sender,EventArgs args)
        {
            if((Native.GetAsyncKeyState((int)Keys.F12)&0x8000)!=0&&restart.Armed)StopFishing();
            if(recorder==null){long now=restartClock.ElapsedMilliseconds,oldDue=restart.DueAt;bool manual=restart.ManualPending;
                if(restart.Ready(now,runtime!=null&&runtime.WorkerAlive,runtime!=null&&runtime.Retryable,C("retry"),(int)V("retrySeconds")))LaunchFishing(manual);
                else if(oldDue<0&&restart.DueAt>=0)Write("本轮异常暂停；"+(int)V("retrySeconds")+"秒后自动重试，停止/F12可取消。");}
            if(runtime!=null)status.Text=(restart.ManualPending?"等待旧任务退出":restart.DueAt>=0?"重试倒计时 "+Math.Max(0,(restart.DueAt-restartClock.ElapsedMilliseconds+999)/1000)+"秒":runtime.Running?"运行中":"已停止")+" 抛竿"+runtime.Casts+" / 收获"+runtime.Catches+" / 圈"+runtime.Laps;
            if(recorder!=null&&!busy&&DateTime.UtcNow>=nextRecord){busy=true;try{if(recorder.Observe(Observe(false).Farm,false))recorder.Draft().Save(DraftPath);recordingWarning="";RefreshSummary();}catch(Exception e){if(recordingWarning!=e.Message||DateTime.UtcNow-lastRecordingWarning>TimeSpan.FromSeconds(10)){Write("录制提示："+e.Message);recordingWarning=e.Message;lastRecordingWarning=DateTime.UtcNow;}}finally{nextRecord=DateTime.UtcNow.AddMilliseconds(400);busy=false;}}
        }
        void Write(string message)
        {
            string line=DateTime.Now.ToString("HH:mm:ss")+" "+message;
            lock(logGate){var path=Path.Combine(directory,"fishing-runtime.log");if(File.Exists(path)&&new FileInfo(path).Length>2*1024*1024){for(int i=4;i>=1;i--){string old=path+"."+i;if(File.Exists(old)){if(i==4)File.Delete(old);else File.Move(old,path+"."+(i+1));}}File.Move(path,path+".1");}File.AppendAllText(path,line+Environment.NewLine,System.Text.Encoding.UTF8);}
            if(IsDisposed)return;if(InvokeRequired){try{BeginInvoke(new Action<string>(WriteUi),line);}catch(InvalidOperationException){}return;}WriteUi(line);
        }
        void WriteUi(string line){if(IsDisposed)return;if(log.TextLength>60000){log.Select(0,log.TextLength-30000);log.SelectedText="";}log.Select(log.TextLength,0);log.SelectionColor=FishingLogColors.For(line);log.AppendText(line+Environment.NewLine);log.SelectionColor=log.ForeColor;log.ScrollToCaret();}
    }
    public sealed class FishingRegionForm:Form
    {
        readonly PictureBox picture=new PictureBox{Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.StretchImage};Point start,end;bool dragging;public FishingRegion SelectedRegion;
        public FishingRegionForm(Bitmap source,FishingRegion initial=null)
        {
            Text="框选浮标水面：按住左键拖出绿色框，再保存";Size=new Size(1000,730);StartPosition=FormStartPosition.CenterParent;
            SelectedRegion=initial!=null&&initial.Valid?initial.Clone():null;picture.Image=(Bitmap)source.Clone();Controls.Add(picture);
            var hint=new Label{Dock=DockStyle.Top,Height=52,Padding=new Padding(10,6,10,0),Text="图片是当前游戏画面。请按住鼠标左键拖出绿色矩形，只框自己的浮标附近水面；松开后点击下方保存。"};Controls.Add(hint);
            var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=48,Padding=new Padding(6)};Controls.Add(footer);
            var save=new Button{Text="保存选区",Width=110,Height=32,Enabled=SelectedRegion!=null};var cancel=new Button{Text="取消",Width=80,Height=32,DialogResult=DialogResult.Cancel};var selection=new Label{AutoSize=true,Padding=new Padding(8,7,0,0)};footer.Controls.Add(save);footer.Controls.Add(cancel);footer.Controls.Add(selection);CancelButton=cancel;
            Action update=()=>{bool valid=SelectedRegion!=null&&SelectedRegion.Valid;save.Enabled=valid;selection.Text=valid?"已有绿色选框，点击保存才会写入设置。":"尚无有效选框，请在图片上按住左键拖动（不要只单击）。";};update();
            save.Click+=delegate{if(SelectedRegion!=null&&SelectedRegion.Valid){DialogResult=DialogResult.OK;Close();}};
            picture.MouseDown+=delegate(object sender,MouseEventArgs e){if(e.Button!=MouseButtons.Left)return;start=end=e.Location;dragging=true;picture.Capture=true;};
            picture.MouseMove+=delegate(object sender,MouseEventArgs e){if(dragging){end=e.Location;picture.Invalidate();}};
            picture.MouseUp+=delegate(object sender,MouseEventArgs e){if(!dragging||e.Button!=MouseButtons.Left)return;end=e.Location;dragging=false;picture.Capture=false;SelectedRegion=FishingRegion.FromSelection(start,end,picture.ClientSize);update();picture.Invalidate();};
            picture.Paint+=delegate(object sender,PaintEventArgs e){Rectangle r=dragging?Rectangle.FromLTRB(Math.Min(start.X,end.X),Math.Min(start.Y,end.Y),Math.Max(start.X,end.X),Math.Max(start.Y,end.Y)):SelectedRegion!=null?new Rectangle((int)(SelectedRegion.X*picture.Width),(int)(SelectedRegion.Y*picture.Height),(int)(SelectedRegion.Width*picture.Width),(int)(SelectedRegion.Height*picture.Height)):Rectangle.Empty;if(r.Width>0&&r.Height>0)using(var pen=new Pen(Color.Lime,2))e.Graphics.DrawRectangle(pen,r);};FormClosed+=delegate{picture.Image.Dispose();};
        }
    }

    static class FishingProgram
    {
        [STAThread]static int Main(string[] args){Native.SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);bool owner;using(var mutex=new System.Threading.Mutex(true,"WoWProbe-IndependentFishing",out owner)){if(!owner)return 0;try{Application.Run(new FishingClientForm());}finally{mutex.ReleaseMutex();}}return 0;}
    }
}
