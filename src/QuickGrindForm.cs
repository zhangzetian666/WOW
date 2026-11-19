using System;
using System.Drawing;
using System.Threading;
using System.IO;
using System.Windows.Forms;
namespace WoWProbe
{
    public sealed class QuickGrindForm:Form
    {
        readonly Button start=new Button{Text="开始自动打怪",AutoSize=true};
        readonly Button stop=new Button{Text="停止",AutoSize=true,Enabled=false};
        readonly Button vendor=new Button{Text="自动售卖",AutoSize=true};
        readonly Button repair=new Button{Text="自动修理",AutoSize=true};
        readonly TextBox log=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill};
        readonly Label cadence=new Label{Text="技能与间隔见上方“技能设置”",AutoSize=true};
        readonly CheckBox skinning=new CheckBox{Text="击杀及路边尸体剥皮（7键）",AutoSize=true,Checked=true};
        readonly QuickRouteEditor routeEditor=new QuickRouteEditor();
        volatile bool cancelled;bool closeWhenStopped;Thread worker;
        readonly bool passivePreview;
        protected override bool ShowWithoutActivation {get{return passivePreview;}}
        public QuickGrindForm(bool autoStart,bool passivePreview=false)
        {
            this.passivePreview=passivePreview;
            Text="固定路线自动打怪 · NPC路线录制版 0.8.27";Size=new Size(1080,820);MinimumSize=new Size(960,700);Font=new Font("Microsoft YaHei UI",10);StartPosition=FormStartPosition.CenterScreen;
            if(passivePreview){StartPosition=FormStartPosition.Manual;Location=new Point(-20000,-20000);}
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=4,ColumnCount=1,Padding=new Padding(12)};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,70));layout.RowStyles.Add(new RowStyle(SizeType.Percent,70));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));layout.RowStyles.Add(new RowStyle(SizeType.Percent,30));Controls.Add(layout);
            layout.Controls.Add(new Label{Text="售卖和修理共用一条录制路线；包满售卖、装备红了修理，沿去程逆序返回。\n先找附近怪物，无怪再走巡航点；脱战后拾取剥皮和吃喝，受到攻击优先反击。\n持续运行，不限10分钟。死亡点自动记录，幽魂回尸体旁复活后恢复；保持游戏前台，F12急停。",Dock=DockStyle.Fill},0,0);
            layout.Controls.Add(routeEditor,0,1);
            var row=new FlowLayoutPanel{Dock=DockStyle.Fill};row.Controls.Add(start);row.Controls.Add(stop);row.Controls.Add(vendor);row.Controls.Add(repair);row.Controls.Add(cadence);row.Controls.Add(skinning);layout.Controls.Add(row,0,2);layout.Controls.Add(log,0,3);
            start.Click+=delegate{BeginRun();};vendor.Click+=delegate{BeginRun(true);};repair.Click+=delegate{BeginRun(false,true);};stop.Click+=delegate{cancelled=true;};
            routeEditor.ServiceRecordingChanged+=delegate{if(worker==null||!worker.IsAlive)start.Enabled=vendor.Enabled=repair.Enabled=!routeEditor.RecordingServiceRoute;};
            FormClosing+=delegate(object sender,FormClosingEventArgs e){if(worker!=null&&worker.IsAlive){closeWhenStopped=true;cancelled=true;e.Cancel=true;Append("正在停止，释放按键后关闭窗口。");}};
            Shown+=delegate{string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"quick-patrol.json"),draft=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"quick-patrol-recording.json");try{if(File.Exists(draft)&&( !File.Exists(path)||File.GetLastWriteTimeUtc(draft)>=File.GetLastWriteTimeUtc(path))){routeEditor.Bind(QuickRoutePlan.LoadRecording(draft));Append("已恢复上次录点草稿；可以继续保存当前巡航点，闭合后再开始。");}else if(File.Exists(path)){routeEditor.Bind(QuickRoutePlan.Load(path));Append("已载入本机上次巡航方案；核对移动参数后点击开始。");}else Append("走到A点点击“保存当前巡航点”，再走到B、C、D依次保存，最后闭合到起点。");}catch(Exception e){Append("方案未载入："+e.Message);}};
        }
        void Append(string message){if(IsDisposed||message.StartsWith("KeyDown")||message.StartsWith("KeyUp"))return;if(InvokeRequired){BeginInvoke((Action)(()=>Append(message)));return;}if(log.TextLength>70000)log.Text=log.Text.Substring(log.TextLength-40000);log.AppendText(DateTime.Now.ToString("HH:mm:ss")+" "+message+Environment.NewLine);}
        void BeginRun(bool vendorFirst=false,bool repairFirst=false)
        {
            if(worker!=null&&worker.IsAlive)return;
            QuickRoutePlan plan;try{if(vendorFirst)routeEditor.EnableAutoVendor();if(repairFirst)routeEditor.EnableAutoRepair();plan=routeEditor.ReadPlan(false);plan.Calibration.Confirmed=true;QuickRoutePlan.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"quick-patrol.json"),plan);}catch(Exception e){Append("未开始："+e.Message);return;}
            cancelled=false;start.Enabled=vendor.Enabled=repair.Enabled=cadence.Enabled=skinning.Enabled=routeEditor.Enabled=false;stop.Enabled=true;int interval=plan.SkillIntervalMs;bool useSkin=skinning.Checked;GrindCli.FocusGameWindow();
            worker=new Thread(()=>{try{GrindCli.RunQuick(0,interval,()=>cancelled,Append,useSkin,plan,vendorFirst,repairFirst);}catch(Exception ex){Append("停止："+ex.Message);}finally{if(!IsDisposed)BeginInvoke((Action)(()=>{worker=null;start.Enabled=vendor.Enabled=repair.Enabled=cadence.Enabled=skinning.Enabled=routeEditor.Enabled=true;stop.Enabled=false;if(closeWhenStopped)Close();}));}});worker.IsBackground=true;worker.Start();
        }
    }
}
