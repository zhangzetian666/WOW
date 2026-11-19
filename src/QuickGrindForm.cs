using System;
using System.Drawing;
using System.Threading;
using System.IO;
using System.Windows.Forms;
namespace WoWProbe
{
    public sealed class QuickGrindForm:Form
    {
        readonly QuickKillCounter killCounter=new QuickKillCounter();
        readonly Label killTotal=new Label{Text="窗口累计击杀：0",AutoSize=true};
        readonly Button start=new Button{Text="开始自动打怪",AutoSize=true};
        readonly Button stop=new Button{Text="停止",AutoSize=true,Enabled=false};
        readonly Button vendor=new Button{Text="自动售卖",AutoSize=true};
        readonly Button repair=new Button{Text="自动修理",AutoSize=true};
        readonly TextBox log=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill};
        readonly Label cadence=new Label{Text="技能与间隔见上方“技能设置”",AutoSize=true};
        readonly CheckBox skinning=new CheckBox{Text="击杀及路边尸体剥皮（7键）",AutoSize=true,Checked=true};
        readonly QuickRouteEditor routeEditor=new QuickRouteEditor();
        readonly CheckBox autoResume=new CheckBox{Text="人为中断后，空闲自动继续",AutoSize=true,Checked=true};
        readonly NumericUpDown idleSeconds=new NumericUpDown{Minimum=5,Maximum=3600,Value=30,Width=70};
        readonly QuickIdleResume resume=new QuickIdleResume();
        readonly System.Windows.Forms.Timer resumeTimer=new System.Windows.Forms.Timer{Interval=1000};
        readonly System.Diagnostics.Stopwatch resumeClock=System.Diagnostics.Stopwatch.StartNew();
        bool resumeVendor,resumeRepair;
        volatile bool cancelled;bool closeWhenStopped;Thread worker;
        readonly bool passivePreview;
        protected override bool ShowWithoutActivation {get{return passivePreview;}}
        public QuickGrindForm(bool autoStart,bool passivePreview=false)
        {
            this.passivePreview=passivePreview;
            Text="固定路线自动打怪 · NPC路线录制版 0.8.31";Size=new Size(1080,820);MinimumSize=new Size(960,700);Font=new Font("Microsoft YaHei UI",10);StartPosition=FormStartPosition.CenterScreen;
            if(passivePreview){StartPosition=FormStartPosition.Manual;Location=new Point(-20000,-20000);}
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=4,ColumnCount=1,Padding=new Padding(12)};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,70));layout.RowStyles.Add(new RowStyle(SizeType.Percent,70));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,88));layout.RowStyles.Add(new RowStyle(SizeType.Percent,30));Controls.Add(layout);
            layout.Controls.Add(new Label{Text="售卖和修理共用一条录制路线；包满售卖、装备红了修理，沿去程逆序返回。\n先找附近怪物，无怪再走巡航点；脱战后拾取剥皮和吃喝，受到攻击优先反击。\n持续运行，不限10分钟。死亡点自动记录，幽魂回尸体旁复活后恢复；保持游戏前台，F12急停。",Dock=DockStyle.Fill},0,0);
            layout.Controls.Add(routeEditor,0,1);
            var row=new FlowLayoutPanel{Dock=DockStyle.Fill};row.Controls.Add(start);row.Controls.Add(stop);row.Controls.Add(vendor);row.Controls.Add(repair);row.Controls.Add(cadence);row.Controls.Add(skinning);layout.Controls.Add(row,0,2);layout.Controls.Add(log,0,3);
            row.Controls.Add(killTotal);row.Controls.Add(autoResume);row.Controls.Add(idleSeconds);row.Controls.Add(new Label{Text="秒后恢复；停止/F12取消自动恢复",AutoSize=true});
            start.Click+=delegate{BeginRun();};vendor.Click+=delegate{BeginRun(true);};repair.Click+=delegate{BeginRun(false,true);};stop.Click+=delegate{resume.Cancel();cancelled=true;stop.Enabled=false;};
            autoResume.CheckedChanged+=delegate{if(!autoResume.Checked){resume.Cancel();if(worker==null)stop.Enabled=false;}};
            resumeTimer.Tick+=delegate{if(passivePreview||worker!=null||closeWhenStopped)return;if((Native.GetAsyncKeyState((int)Keys.F12)&0x8000)!=0){resume.Cancel();stop.Enabled=false;return;}if(autoResume.Checked&&!routeEditor.RecordingServiceRoute&&resume.Ready(resumeClock.ElapsedMilliseconds,Native.IdleMilliseconds(),(int)idleSeconds.Value)){resume.Cancel();Append("键鼠已空闲，重新识别游戏窗口并恢复运行。");BeginRun(resumeVendor,resumeRepair);}};
            if(!passivePreview)resumeTimer.Start();FormClosed+=delegate{resumeTimer.Stop();resumeTimer.Dispose();};
            routeEditor.ServiceRecordingChanged+=delegate{if(worker==null||!worker.IsAlive)start.Enabled=vendor.Enabled=repair.Enabled=!routeEditor.RecordingServiceRoute;};
            FormClosing+=delegate(object sender,FormClosingEventArgs e){if(worker!=null&&worker.IsAlive){closeWhenStopped=true;cancelled=true;e.Cancel=true;Append("正在停止，释放按键后关闭窗口。");}};
            Shown+=delegate{string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"quick-patrol.json"),draft=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"quick-patrol-recording.json");try{if(File.Exists(draft)&&( !File.Exists(path)||File.GetLastWriteTimeUtc(draft)>=File.GetLastWriteTimeUtc(path))){routeEditor.Bind(QuickRoutePlan.LoadRecording(draft));Append("已恢复上次录点草稿；可以继续保存当前巡航点，闭合后再开始。");}else if(File.Exists(path)){routeEditor.Bind(QuickRoutePlan.Load(path));Append("已载入本机上次巡航方案；已准备按当前路线自动开始。");}else Append("走到A点点击“保存当前巡航点”，再走到B、C、D依次保存，最后闭合到起点。");}catch(Exception e){Append("方案未载入："+e.Message);return;}if(autoStart&&!passivePreview&&(File.Exists(path)||File.Exists(draft)))BeginInvoke((Action)(()=>{if(IsDisposed||closeWhenStopped||cancelled||(Native.GetAsyncKeyState((int)Keys.F12)&0x8000)!=0)return;Append("自动启动：读取已保存方案，倒计时后开始运行。");BeginRun();}));};
        }
        void Append(string message){if(IsDisposed||message.StartsWith("KeyDown")||message.StartsWith("KeyUp"))return;if(InvokeRequired){BeginInvoke((Action)(()=>Append(message)));return;}killTotal.Text="窗口累计击杀："+killCounter.Total;if(log.TextLength>70000)log.Text=log.Text.Substring(log.TextLength-40000);log.AppendText(DateTime.Now.ToString("HH:mm:ss")+" "+message+Environment.NewLine);}
        void BeginRun(bool vendorFirst=false,bool repairFirst=false)
        {
            if(worker!=null&&worker.IsAlive)return;
            resume.Cancel();resumeVendor=vendorFirst;resumeRepair=repairFirst;
            QuickRoutePlan plan;try{if(vendorFirst)routeEditor.EnableAutoVendor();if(repairFirst)routeEditor.EnableAutoRepair();plan=routeEditor.ReadPlan(false);plan.Calibration.Confirmed=true;QuickRoutePlan.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"quick-patrol.json"),plan);}catch(Exception e){Append("未开始："+e.Message);return;}
            cancelled=false;start.Enabled=vendor.Enabled=repair.Enabled=cadence.Enabled=skinning.Enabled=routeEditor.Enabled=false;stop.Enabled=true;int interval=plan.SkillIntervalMs;bool useSkin=skinning.Checked;GrindCli.FocusGameWindow();
            worker=new Thread(()=>{string failure=null;try{GrindCli.RunQuick(0,interval,()=>cancelled,Append,useSkin,plan,vendorFirst,repairFirst,killCounter);}catch(Exception ex){failure=ex.Message;Append("中断："+ex.Message);}finally{if(!IsDisposed)BeginInvoke((Action)(()=>{worker=null;start.Enabled=vendor.Enabled=repair.Enabled=cadence.Enabled=skinning.Enabled=routeEditor.Enabled=true;if(!cancelled&&!closeWhenStopped&&autoResume.Checked)resume.Interrupted(failure,resumeClock.ElapsedMilliseconds);stop.Enabled=resume.Pending;if(resume.Pending)Append("等待键鼠空闲"+idleSeconds.Value+"秒后自动恢复；点击停止可取消。");if(closeWhenStopped)Close();}));}});worker.IsBackground=true;worker.Start();
        }
    }
}
