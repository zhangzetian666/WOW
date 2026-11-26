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
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var service=(QuickServiceRoutePanel)typeof(QuickRouteEditor).GetField("vendorRoute",flags).GetValue(recorder);
                recorder.EnableAutoVendor();recorder.EnableAutoRepair();
                typeof(QuickServiceRoutePanel).GetMethod("StartRecording",flags).Invoke(service,null);
                if(!recorder.RecordingServiceRoute||QuickRoutePlan.LoadRecording(recording).Vendor.RouteComplete)throw new Exception("Recording did not keep an incomplete draft.");
                observation.Sequence++;observation.Y+=.001;typeof(QuickServiceRoutePanel).GetMethod("RecordSample",flags).Invoke(service,null);
                observation.Sequence++;observation.X+=.001;typeof(QuickServiceRoutePanel).GetMethod("EndRecording",flags).Invoke(service,null);
                var serviceSaved=QuickRoutePlan.LoadRecording(recording);if(recorder.RecordingServiceRoute||!serviceSaved.Vendor.RouteComplete||serviceSaved.Vendor.Points.Count!=3||!serviceSaved.Vendor.Enabled)throw new Exception("Recorded NPC route was not persisted on finish.");
                var repairService=(QuickServiceRoutePanel)typeof(QuickRouteEditor).GetField("repairRoute",flags).GetValue(recorder);
                typeof(QuickServiceRoutePanel).GetMethod("StartRecording",flags).Invoke(repairService,null);typeof(QuickServiceRoutePanel).GetMethod("CancelRecording",flags).Invoke(repairService,null);
                if(recorder.RecordingServiceRoute||recorder.ReadPlan(false).Repair.Points.Count!=3||recorder.ReadPlan(false).Vendor.Points.Count!=3)throw new Exception("Cancel failed to preserve the shared NPC route.");
                var ghostPanel=(QuickServiceRoutePanel)typeof(QuickRouteEditor).GetField("corpseRoute",flags).GetValue(recorder);
                observation.PlayerDead=observation.PlayerGhost=true;
                typeof(QuickServiceRoutePanel).GetMethod("StartRecording",flags).Invoke(ghostPanel,null);
                observation.X+=.001;observation.Sequence++;
                observation.PlayerDead=observation.PlayerGhost=false;typeof(QuickServiceRoutePanel).GetMethod("EndRecording",flags).Invoke(ghostPanel,null);
                var ghostFile=Path.Combine(Path.GetDirectoryName(recording),"quick-corpse-route.json");var ghostSaved=RouteStore.Load(ghostFile);
                if(ghostSaved.RouteType!="corpse_run"||ghostSaved.RouteId!="recorded-ghost"||ghostSaved.Points.Count!=2||recorder.RecordingServiceRoute)throw new Exception("Ghost route recording did not save or unlock controls.");
                observation.PlayerDead=observation.PlayerGhost=false;File.Delete(ghostFile);
                before=File.ReadAllText(recording);observation.Fresh=false;rejected=false;try{capture.Invoke(recorder,null);}catch(TargetInvocationException){rejected=true;}
                if(!rejected||File.ReadAllText(recording)!=before)throw new Exception("Stale capture modified recording.");
                observation.Fresh=true;observation.MapId=43;rejected=false;try{capture.Invoke(recorder,null);}catch(TargetInvocationException){rejected=true;}
                if(!rejected||File.ReadAllText(recording)!=before)throw new Exception("Cross-map capture modified recording.");
            }
            using(var form=new QuickGrindForm(false,true))
            {
                var editor=(QuickRouteEditor)typeof(QuickGrindForm).GetField("routeEditor",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
                var route=new RouteDocument{RouteId="ui-demo",Name="ABCDA界面检查",MapId=42,MapMode=2,Zone="SIMULATION",Loop=true};
                foreach(var p in new[]{new[]{.50,.50},new[]{.52,.50},new[]{.52,.52},new[]{.50,.52},new[]{.50,.50}})route.Points.Add(new Waypoint{X=p[0],Y=p[1],Tolerance=.001});
                editor.Bind(new QuickRoutePlan{Route=route,Calibration=new NavigationCalibration{MapId=42,MapMode=2,Zone="SIMULATION",AxisScale=1.5,TurnRadiansPerSecond=5.85}});
                var grid=(DataGridView)typeof(QuickRouteEditor).GetField("grid",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor);
                grid.Rows[1].Cells[1].Value="51.25";var plan=editor.ReadPlan(false);
                if(plan.Route.Points.Count!=5||plan.Route.Points[1].X!=.5125||!plan.Route.Loop)throw new Exception("Manual percent coordinates did not round-trip.");
                plan.SkillRows=new[]{"23","32","22","33","232"};plan.RangedCombat=true;plan.HunterMode=true;plan.RevivePetAfterDeath=true;plan.RevivePetKey="E";plan.NoChatCombat=plan.SkinWithInteract=true;plan.RandomRoutes=plan.TimedBreaks=true;editor.Bind(plan);var newSettings=editor.ReadPlan(false);
                if(!newSettings.RevivePetAfterDeath||newSettings.RevivePetKey!="E"||!newSettings.HunterMode||!newSettings.NoChatCombat||!newSettings.SkinWithInteract)throw new Exception("No-chat and interaction skin settings did not round-trip through the UI.");
                if(newSettings.SkillRows.Length!=5||newSettings.SkillRows[4]!="232"||!newSettings.RandomRoutes||!newSettings.TimedBreaks||!newSettings.RangedCombat)throw new Exception("Combat style, random rows and session settings did not round-trip through the UI.");
                bool rejected=false;try{editor.ReadPlan(true);}catch(InvalidOperationException){rejected=true;}if(!rejected)throw new Exception("Imported calibration was implicitly confirmed.");
                var vendorX=(NumericUpDown)typeof(QuickRouteEditor).GetField("vendorX",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor);vendorX.Value=51.25m;
                var vendorY=(NumericUpDown)typeof(QuickRouteEditor).GetField("vendorY",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor);vendorY.Value=52.5m;
                ((CheckBox)typeof(QuickRouteEditor).GetField("vendorEnabled",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor)).Checked=true;
                ((NumericUpDown)typeof(QuickRouteEditor).GetField("restBelow",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor)).Value=70;
                ((TextBox)typeof(QuickRouteEditor).GetField("drinkKey",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor)).Text="5";
                plan=editor.ReadPlan(false);if(!plan.Vendor.Enabled||plan.Vendor.X!=.5125||plan.Vendor.Y!=.525||plan.RestBelowPercent!=70||plan.DrinkKey!="5")throw new Exception("Vendor coordinates or recovery settings did not bind.");
                ((NumericUpDown)typeof(QuickRouteEditor).GetField("repairX",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor)).Value=53;
                ((NumericUpDown)typeof(QuickRouteEditor).GetField("repairY",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor)).Value=54;
                editor.EnableAutoRepair();plan=editor.ReadPlan(false);if(!plan.Repair.Enabled||plan.Repair.X!=.53||plan.Repair.Y!=.54)throw new Exception("Repair point did not bind.");
                form.Show();Application.DoEvents();
                string output=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output");Directory.CreateDirectory(output);
                using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(output,"fixed-route-ui.png"),System.Drawing.Imaging.ImageFormat.Png);}
                var tabs=(TabControl)editor.Controls[0];foreach(TabPage page in tabs.TabPages)if(page.Text=="自动售卖"||page.Text=="恢复设置"||page.Text=="自动修理"){tabs.SelectedTab=page;Application.DoEvents();using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(output,page.Text=="自动售卖"?"quick-vendor-ui.png":page.Text=="自动修理"?"quick-repair-ui.png":"quick-recovery-ui.png"),System.Drawing.Imaging.ImageFormat.Png);}}
                form.Close();
            }
            var newFile=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output","new-patrol-"+Guid.NewGuid(),"draft.json");
            var position=new FarmObservation{Fresh=true,KnownCore=true,MapId=42,MapMode=2,Zone="SIMULATION",X=.5,Y=.5};
            using(var editor=new QuickRouteEditor(()=>position,newFile))
            {
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;var create=typeof(QuickRouteEditor).GetMethod("NewRoute",flags);
                create.Invoke(editor,null);var first=QuickRoutePlan.LoadRecording(newFile);if(first.Route.Points.Count!=1)throw new Exception("New route did not start at A.");
                first.Vendor=new QuickVendorSettings{Enabled=true,RouteComplete=true,X=.51,Y=.5};first.Vendor.Points.Add(new Waypoint{X=.5,Y=.5,Tolerance=.0004});first.Vendor.Points.Add(new Waypoint{X=.51,Y=.5,Tolerance=.0004});first.Repair=first.Vendor.Clone();editor.Bind(first);create.Invoke(editor,null);
                var retained=QuickRoutePlan.LoadRecording(newFile);if(!retained.Vendor.Enabled||retained.Vendor.Points.Count!=2||retained.Repair.Points.Count!=2)throw new Exception("Same map new patrol discarded merchant route.");
                position.MapId=43;position.Zone="OTHER";position.X=.6;create.Invoke(editor,null);
                var next=QuickRoutePlan.LoadRecording(newFile);var archives=Directory.GetFiles(Path.Combine(Path.GetDirectoryName(newFile),"patrol-routes"),"*.json");
                if(next.Route.MapId!=43||next.Route.Points.Count!=1||next.Route.Points[0].X!=.6||archives.Length!=2||next.Vendor.Points.Count!=0||QuickRoutePlan.LoadRecording(archives[0]).Route.MapId!=42)throw new Exception("New route did not preserve old route and bind new map.");
            }
            Console.WriteLine("Fixed route UI: recording, imports, new-route creation with old-route archive, and preview checks passed. No game input.");return 0;
        }
        catch(Exception e){Console.WriteLine(e);return 1;}
    }
}
