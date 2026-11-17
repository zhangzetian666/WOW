using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using WoWProbe;
class QuickRouteUiTests
{
    [STAThread]static int Main()
    {
        try
        {
            Native.SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            string recording=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output","recording-"+Guid.NewGuid()+".json");
            var observation=new FarmObservation{Fresh=true,KnownCore=true,MapId=42,MapMode=2,Zone="SIMULATION",X=.5,Y=.5};
            using(var recorder=new QuickRouteEditor(()=>observation,recording))
            {
                var capture=typeof(QuickRouteEditor).GetMethod("CapturePoint",BindingFlags.Instance|BindingFlags.NonPublic);
                capture.Invoke(recorder,null);
                if(QuickRoutePlan.LoadRecording(recording).Route.Points.Count!=1)throw new Exception("First recorded point was not persisted.");
                string before=File.ReadAllText(recording);bool rejected=false;try{capture.Invoke(recorder,null);}catch(TargetInvocationException){rejected=true;}
                if(!rejected||File.ReadAllText(recording)!=before)throw new Exception("Duplicate capture modified recording.");
                observation.X=.52;capture.Invoke(recorder,null);observation.Y=.52;capture.Invoke(recorder,null);observation.X=.5;capture.Invoke(recorder,null);
                var saved=QuickRoutePlan.LoadRecording(recording);if(saved.Route.Points.Count!=4||saved.Route.Points[1].X!=.52)throw new Exception("Repeated capture did not append A B C D.");
                recorder.Bind(saved);typeof(QuickRouteEditor).GetMethod("CloseLoop",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(recorder,null);
                if(recorder.ReadPlan(false).Route.Points.Count!=5||QuickRoutePlan.LoadRecording(recording).Route.Points.Count!=5)throw new Exception("Closure was not persisted as ABCDA.");
                before=File.ReadAllText(recording);observation.Fresh=false;rejected=false;try{capture.Invoke(recorder,null);}catch(TargetInvocationException){rejected=true;}
                if(!rejected||File.ReadAllText(recording)!=before)throw new Exception("Stale capture modified recording.");
                observation.Fresh=true;observation.MapId=43;rejected=false;try{capture.Invoke(recorder,null);}catch(TargetInvocationException){rejected=true;}
                if(!rejected||File.ReadAllText(recording)!=before)throw new Exception("Cross-map capture modified recording.");
            }
            using(var form=new QuickGrindForm(false))
            {
                var editor=(QuickRouteEditor)typeof(QuickGrindForm).GetField("routeEditor",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
                var route=new RouteDocument{RouteId="ui-demo",Name="ABCDA界面检查",MapId=42,MapMode=2,Zone="SIMULATION",Loop=true};
                foreach(var p in new[]{new[]{.50,.50},new[]{.52,.50},new[]{.52,.52},new[]{.50,.52},new[]{.50,.50}})route.Points.Add(new Waypoint{X=p[0],Y=p[1],Tolerance=.001});
                editor.Bind(new QuickRoutePlan{Route=route,Calibration=new NavigationCalibration{MapId=42,MapMode=2,Zone="SIMULATION",AxisScale=1.5,TurnRadiansPerSecond=5.85}});
                var grid=(DataGridView)typeof(QuickRouteEditor).GetField("grid",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor);
                grid.Rows[1].Cells[1].Value="51.25";var plan=editor.ReadPlan(false);
                if(plan.Route.Points.Count!=5||plan.Route.Points[1].X!=.5125||!plan.Route.Loop)throw new Exception("Manual percent coordinates did not round-trip.");
                bool rejected=false;try{editor.ReadPlan(true);}catch(InvalidOperationException){rejected=true;}if(!rejected)throw new Exception("Imported calibration was implicitly confirmed.");
                form.Show();Application.DoEvents();
                string output=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output");Directory.CreateDirectory(output);
                using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(output,"fixed-route-ui.png"),System.Drawing.Imaging.ImageFormat.Png);}
                form.Close();
            }
            Console.WriteLine("Fixed route UI: repeated A B C D capture, immediate persistence, draft restore, closure, duplicate/stale/map rejection, manual percentage editing, unconfirmed import and preview rendering passed. No game input.");return 0;
        }
        catch(Exception e){Console.WriteLine(e);return 1;}
    }
}
