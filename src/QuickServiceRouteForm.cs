using System;
using System.Diagnostics;
using System.Drawing;
using System.Collections.Generic;
using System.Windows.Forms;
namespace WoWProbe
{
    public sealed class QuickServiceRoutePanel:UserControl
    {
        readonly Func<FarmObservation> current,sample;readonly Func<QuickRoutePlan> plan;readonly Func<QuickVendorSettings> settings;
        readonly Action<QuickVendorSettings> apply;readonly Action save,changed;readonly Func<bool> anotherRecording;readonly bool desktop,ghost;
        readonly Button begin=new Button{Text="开始录制",AutoSize=true},end=new Button{Text="结束录制",AutoSize=true,Enabled=false},cancel=new Button{Text="取消本次录制",AutoSize=true,Enabled=false};
        readonly Label status=new Label{AutoSize=true,MaximumSize=new Size(760,0)};readonly DataGridView grid=new DataGridView();
        readonly Timer timer=new Timer{Interval=200};readonly Stopwatch clock=Stopwatch.StartNew();long lastSave;
        QuickServiceRecording recording;QuickVendorSettings backup;List<Waypoint> points=new List<Waypoint>();bool complete;
        public bool IsRecording {get{return recording!=null;}}
        public QuickServiceRoutePanel(Func<FarmObservation> current,Func<FarmObservation> sample,Func<QuickRoutePlan> plan,Func<QuickVendorSettings> settings,Action<QuickVendorSettings> apply,Action save,Action changed,Func<bool> anotherRecording,bool desktop,bool ghost=false)
        {
            this.current=current;this.sample=sample;this.plan=plan;this.settings=settings;this.apply=apply;this.save=save;this.changed=changed;this.anotherRecording=anotherRecording;this.desktop=desktop;this.ghost=ghost;
            Width=780;Height=200;var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,34));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));Controls.Add(layout);
            var row=new FlowLayoutPanel{Dock=DockStyle.Fill};row.Controls.Add(begin);row.Controls.Add(end);row.Controls.Add(cancel);layout.Controls.Add(row,0,0);layout.Controls.Add(status,0,1);
            grid.Dock=DockStyle.Fill;grid.ReadOnly=true;grid.RowHeadersVisible=false;grid.AllowUserToAddRows=grid.AllowUserToDeleteRows=false;grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;grid.BackgroundColor=Color.White;grid.Columns.Add("Point","路点");grid.Columns.Add("X","X（0–100）");grid.Columns.Add("Y","Y（0–100）");layout.Controls.Add(grid,0,2);
            begin.Click+=delegate{Run(StartRecording);};end.Click+=delegate{Run(EndRecording);};cancel.Click+=delegate{Run(CancelRecording);};timer.Tick+=delegate{RecordSample();};RefreshRows();
        }
        void Run(Action action){try{action();}catch(Exception e){MessageBox.Show(e.Message,"路线未完成",MessageBoxButtons.OK,MessageBoxIcon.Information);}}
        public void Bind(QuickVendorSettings s){points=new List<Waypoint>();foreach(var p in s.Points)points.Add(p.Clone());complete=s.RouteComplete;RefreshRows();}
        public void ApplyTo(QuickVendorSettings s){s.Points=new List<Waypoint>();foreach(var p in points)s.Points.Add(p.Clone());s.RouteComplete=complete;}
        void RefreshRows(){grid.Rows.Clear();foreach(var p in points)grid.Rows.Add(grid.Rows.Count+1,(p.X*100).ToString("F3"),(p.Y*100).ToString("F3"));if(grid.Rows.Count>0)grid.FirstDisplayedScrollingRowIndex=grid.Rows.Count-1;begin.Text=points.Count==0?"开始录制":"重新录制";status.Text=IsRecording?"正在录制，共"+points.Count+"点；切回游戏走到NPC旁，再回此处结束录制。":complete?"已保存完整路线，共"+points.Count+"点；结束后按去程逆序返回。":"尚无完整路线；可在当前地图任意位置开始录制，走到NPC旁结束。";if(ghost)status.Text=IsRecording?"正在录制跑尸路线（每6秒一点）："+points.Count+"点；走到打怪区后结束保存，途中复活也可。":complete?"已保存跑尸路线："+points.Count+"点；死亡释放后沿此路线返回。":"请从墓地开始录制，存活或灵魂状态均可，走到打怪区域后结束保存。";}
        void SetActive(){begin.Enabled=!IsRecording;end.Enabled=cancel.Enabled=IsRecording;changed();RefreshRows();}
        void StartRecording()
        {
            if(IsRecording||anotherRecording())throw new InvalidOperationException("请先结束另一条NPC路线的录制。");
            var o=current();var r=new QuickServiceRecording(plan(),o,clock.ElapsedMilliseconds,ghost);backup=settings().Clone();
            points=r.Snapshot();complete=false;var draft=settings();draft.Enabled=false;draft.X=o.X;draft.Y=o.Y;ApplyTo(draft);apply(draft);recording=r;
            try{save();}catch{recording=null;apply(backup);SetActive();throw;}
            lastSave=clock.ElapsedMilliseconds;SetActive();timer.Start();
            if(desktop){var form=FindForm();if(form!=null&&form.Visible){form.WindowState=FormWindowState.Minimized;GrindCli.FocusGameWindow();}}
        }
        void RecordSample()
        {
            if(recording==null)return;
            try{if(recording.Observe(sample(),clock.ElapsedMilliseconds)){points=recording.Snapshot();RefreshRows();if(clock.ElapsedMilliseconds-lastSave>=1000){save();lastSave=clock.ElapsedMilliseconds;}}}
            catch(Exception e){status.Text="采样暂停："+e.Message;}
        }
        void EndRecording()
        {
            if(recording==null)throw new InvalidOperationException("请先开始录制。");timer.Stop();
            try{var result=recording.Finish(current(),clock.ElapsedMilliseconds,settings());apply(result);save();recording=null;backup=null;SetActive();}
            catch{timer.Start();throw;}
        }
        void CancelRecording(){if(recording==null)return;timer.Stop();apply(backup);recording=null;backup=null;if(!ghost)save();SetActive();}
        protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
    }
}
