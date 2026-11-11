using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using WoWProbe;

class RouteUiTests
{
    sealed class Fake : IInputBackend
    {
        public int Events;
        public bool IsTargetForeground(){return true;}public bool IsEmergencyPressed(){return false;}public bool IsKeyAlreadyDown(ushort key){return false;}
        public void KeyDown(ushort key){Events++;}public void KeyUp(ushort key){Events++;}
    }
    static Button Find(Control control,string text)
    {foreach(Control child in control.Controls){var button=child as Button;if(button!=null&&button.Text==text)return button;var nested=Find(child,text);if(nested!=null)return nested;}return null;}
    static CheckBox Check(Control control,string text)
    {foreach(Control child in control.Controls){var check=child as CheckBox;if(check!=null&&check.Text==text)return check;var nested=Check(child,text);if(nested!=null)return nested;}return null;}
    static RouteCanvas Canvas(Control control)
    {foreach(Control child in control.Controls){var canvas=child as RouteCanvas;if(canvas!=null)return canvas;var nested=Canvas(child);if(nested!=null)return nested;}return null;}
    [STAThread]
    static int Main()
    {
        Native.SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        int exit=1,nativeFactoryCalls=0;long time=0;var fake=new Fake();var single=new InputGuard(fake,delegate{return time;},delegate{return true;});single.Schedule(0x57,5000,120);
        string error=null;DateTime started=DateTime.UtcNow;bool clicked=false;
        string output=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output");Directory.CreateDirectory(output);
        using(var form=new RouteForm(delegate{return new RouteTelemetry();},delegate{nativeFactoryCalls++;throw new Exception("Offline simulation must not request a native backend");},delegate{single.Stop("Entering offline simulation");},null))
        using(var timer=new System.Windows.Forms.Timer{Interval=100})
        {
            timer.Tick+=delegate {
                try
                {
                    if(!clicked)
                    {
                        clicked=true;Check(form,"反向").Checked=true;
                        var preview=Canvas(form);if(preview.Route.Points[1].X!=.50||preview.Route.Points[1].Y!=.52)throw new Exception("Reverse preview still uses the original waypoint order.");
                        Find(form,"开始离线模拟").PerformClick();
                        time=6000;single.Tick();
                        if(single.Pending||single.Active||fake.Events!=0)throw new Exception("Entering simulation did not cancel the phase-one key pulse.");
                        if(!form.NavigationBusy)throw new Exception("Offline running mode must prevent phase-one input scheduling.");
                    }
                    if(form.SimulationCompleted)
                    {
                        Find(form,"导出本轮记录").PerformClick();
                        var reports=new DirectoryInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"reports")).GetDirectories("route-*");Array.Sort(reports,delegate(DirectoryInfo a,DirectoryInfo b){return a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc);});
                        var report=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(reports[reports.Length-1].FullName,"route-report.json"),Encoding.UTF8)) as Dictionary<string,object>;
                        var sim=(Dictionary<string,object>)report["simulation"];
                        if(Convert.ToInt32(sim["native_input_events"])!=0||nativeFactoryCalls!=0||fake.Events!=0||!Equals(report["game_validation"],"pending"))throw new Exception("Simulation report or input isolation is invalid.");
                        Check(form,"循环").Checked=true;Find(form,"载入演示路线").PerformClick();
                        if(Check(form,"循环").Checked)throw new Exception("Loaded route failed to restore its saved loop default.");
                        if(form.SimulationCompleted)throw new Exception("Replacing a route retained the previous simulation's completion/report context.");
                        exit=0;timer.Stop();form.Close();
                    }
                    else if((DateTime.UtcNow-started).TotalSeconds>35)throw new Exception("Offline UI route failed to finish within 35 seconds.");
                }
                catch(Exception ex){error=ex.ToString();timer.Stop();single.Stop("test cleanup");form.Close();}
            };
            form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new System.Drawing.Point(-16000,-16000);form.Opacity=0;
            form.Shown+=delegate{timer.Start();};Application.Run(form);
        }
        File.WriteAllText(Path.Combine(output,"route-ui-tests.json"),new JavaScriptSerializer().Serialize(new {passed=exit==0,input_events=fake.Events,native_backend_requests=nativeFactoryCalls,error=error}),new UTF8Encoding(true));
        return exit;
    }
}
