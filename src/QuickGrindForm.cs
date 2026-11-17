using System;
using System.Drawing;
using System.Threading;
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
        volatile bool cancelled;bool closeWhenStopped;Thread worker;
        public QuickGrindForm(bool autoStart)
        {
            Text="附近自动打怪 · 盗贼";Size=new Size(700,480);MinimumSize=new Size(600,380);Font=new Font("Microsoft YaHei UI",10);StartPosition=FormStartPosition.CenterScreen;
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,Padding=new Padding(12)};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,90));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));Controls.Add(layout);
            layout.Controls.Add(new Label{Text="把角色放在黄名怪附近，关闭聊天框。启动后保持游戏在前台。\n只主动选黄名怪，F面向接近、242223后持续2；F12急停。\n死亡按配置路线跑尸；先恢复满血，脱战低于80%吃食物。\n每轮最多10分钟、复活3次；无怪或单怪超时、失焦时停止。",Dock=DockStyle.Fill},0,0);
            var row=new FlowLayoutPanel{Dock=DockStyle.Fill};cadence.Items.AddRange(new object[]{"750ms","500ms"});cadence.SelectedIndex=0;row.Controls.Add(start);row.Controls.Add(stop);row.Controls.Add(cadence);row.Controls.Add(skinning);layout.Controls.Add(row,0,1);layout.Controls.Add(log,0,2);
            start.Click+=delegate{BeginRun();};stop.Click+=delegate{cancelled=true;};
            FormClosing+=delegate(object sender,FormClosingEventArgs e){if(worker!=null&&worker.IsAlive){closeWhenStopped=true;cancelled=true;e.Cancel=true;Append("正在停止，释放按键后关闭窗口。");}};
            if(autoStart)Shown+=delegate{BeginRun();};
        }
        void Append(string message){if(IsDisposed)return;if(InvokeRequired){BeginInvoke((Action)(()=>Append(message)));return;}log.AppendText(DateTime.Now.ToString("HH:mm:ss")+" "+message+Environment.NewLine);}
        void BeginRun()
        {
            if(worker!=null&&worker.IsAlive)return;cancelled=false;start.Enabled=cadence.Enabled=skinning.Enabled=false;stop.Enabled=true;int interval=cadence.SelectedIndex==1?500:750;bool useSkin=skinning.Checked;GrindCli.FocusGameWindow();
            worker=new Thread(()=>{try{GrindCli.RunQuick(600,interval,()=>cancelled,Append,useSkin);}catch(Exception ex){Append("停止："+ex.Message);}finally{if(!IsDisposed)BeginInvoke((Action)(()=>{worker=null;start.Enabled=cadence.Enabled=skinning.Enabled=true;stop.Enabled=false;if(closeWhenStopped)Close();}));}});worker.IsBackground=true;worker.Start();
        }
    }
}
