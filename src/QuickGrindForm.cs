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
        readonly TextBox log=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill};
        readonly ComboBox cadence=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=100};
        readonly CheckBox skinning=new CheckBox{Text="击杀后剥皮（7键）",AutoSize=true,Checked=true};
        readonly QuickRouteEditor routeEditor=new QuickRouteEditor();
        volatile bool cancelled;bool closeWhenStopped;Thread worker;
        public QuickGrindForm(bool autoStart)
        {
            Text="固定路线自动打怪 · 盗贼";Size=new Size(1080,820);MinimumSize=new Size(960,700);Font=new Font("Microsoft YaHei UI",10);StartPosition=FormStartPosition.CenterScreen;
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=4,ColumnCount=1,Padding=new Padding(12)};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,70));layout.RowStyles.Add(new RowStyle(SizeType.Percent,70));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));layout.RowStyles.Add(new RowStyle(SizeType.Percent,30));Controls.Add(layout);
            layout.Controls.Add(new Label{Text="先配置A→B→C→D→A等闭合路线，角色放在路线范围内。点击开始后沿线循环找怪。\n仅主动攻击黄名怪，242223后持续2；低于80%吃食物，恢复后回到路线。F12急停。\n保持游戏前台。每轮最多10分钟；跑尸仍需另配路线，不自动修理或售卖。",Dock=DockStyle.Fill},0,0);
            layout.Controls.Add(routeEditor,0,1);
            var row=new FlowLayoutPanel{Dock=DockStyle.Fill};cadence.Items.AddRange(new object[]{"750ms","500ms"});cadence.SelectedIndex=0;row.Controls.Add(start);row.Controls.Add(stop);row.Controls.Add(cadence);row.Controls.Add(skinning);layout.Controls.Add(row,0,2);layout.Controls.Add(log,0,3);
            start.Click+=delegate{BeginRun();};stop.Click+=delegate{cancelled=true;};
            FormClosing+=delegate(object sender,FormClosingEventArgs e){if(worker!=null&&worker.IsAlive){closeWhenStopped=true;cancelled=true;e.Cancel=true;Append("正在停止，释放按键后关闭窗口。");}};
            Shown+=delegate{string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"quick-patrol.json"),draft=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"quick-patrol-recording.json");try{if(File.Exists(draft)&&( !File.Exists(path)||File.GetLastWriteTimeUtc(draft)>=File.GetLastWriteTimeUtc(path))){routeEditor.Bind(QuickRoutePlan.LoadRecording(draft));Append("已恢复上次录点草稿；可以继续保存当前巡航点，闭合后再开始。");}else if(File.Exists(path)){routeEditor.Bind(QuickRoutePlan.Load(path));Append("已载入本机上次巡航方案；核对移动参数后点击开始。");}else Append("走到A点点击“保存当前巡航点”，再走到B、C、D依次保存，最后闭合到起点。");}catch(Exception e){Append("方案未载入："+e.Message);}};
        }
        void Append(string message){if(IsDisposed)return;if(InvokeRequired){BeginInvoke((Action)(()=>Append(message)));return;}log.AppendText(DateTime.Now.ToString("HH:mm:ss")+" "+message+Environment.NewLine);}
        void BeginRun()
        {
            if(worker!=null&&worker.IsAlive)return;
            QuickRoutePlan plan;try{plan=routeEditor.ReadPlan(true);QuickRoutePlan.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"quick-patrol.json"),plan);}catch(Exception e){Append("未开始："+e.Message);return;}
            cancelled=false;start.Enabled=cadence.Enabled=skinning.Enabled=routeEditor.Enabled=false;stop.Enabled=true;int interval=cadence.SelectedIndex==1?500:750;bool useSkin=skinning.Checked;GrindCli.FocusGameWindow();
            worker=new Thread(()=>{try{GrindCli.RunQuick(600,interval,()=>cancelled,Append,useSkin,plan);}catch(Exception ex){Append("停止："+ex.Message);}finally{if(!IsDisposed)BeginInvoke((Action)(()=>{worker=null;start.Enabled=cadence.Enabled=skinning.Enabled=routeEditor.Enabled=true;stop.Enabled=false;if(closeWhenStopped)Close();}));}});worker.IsBackground=true;worker.Start();
        }
    }
}
