using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace WoWProbe
{
    public sealed class ReadinessReportForm:Form
    {
        public string Markdown{get;private set;}
        public ReadinessReportForm(string title,string markdown)
        {
            Text=title;Markdown=markdown??"";Font=new Font("Microsoft YaHei UI",9F);Size=new Size(920,740);MinimumSize=new Size(650,480);StartPosition=FormStartPosition.CenterParent;
            var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(12),RowCount=2,ColumnCount=1};root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,42));Controls.Add(root);
            var body=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,Dock=DockStyle.Fill,BackColor=Color.White,Text=Markdown};root.Controls.Add(body);
            var row=new FlowLayoutPanel{Dock=DockStyle.Fill};var save=new Button{Text="保存摘要",AutoSize=true};save.Click+=delegate{using(var d=new SaveFileDialog{Filter="Markdown 摘要|*.md",FileName="检查摘要-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".md"})if(d.ShowDialog()==DialogResult.OK)try{File.WriteAllText(d.FileName,Markdown,new UTF8Encoding(true));}catch(Exception ex){MessageBox.Show(ex.Message,"摘要未保存");}};row.Controls.Add(save);var close=new Button{Text="关闭",AutoSize=true};close.Click+=delegate{Close();};row.Controls.Add(close);row.Controls.Add(new Label{Text="只读查看；不会启动输入或改变实机验收状态。",AutoSize=true,Margin=new Padding(15,8,0,0)});root.Controls.Add(row);
        }
    }
}
