using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using WoWProbe;

class FishingUiTests
{
    sealed class PendingBackend:IInputBackend
    {public int Events;public bool IsTargetForeground(){return true;}public bool IsEmergencyPressed(){return false;}public bool IsKeyAlreadyDown(ushort key){return false;}public void KeyDown(ushort key){Events++;}public void KeyUp(ushort key){Events++;}}
    static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static object Field(object owner,string name){var f=owner.GetType().GetField(name,Private);if(f==null)throw new Exception("Missing UI field "+name);return f.GetValue(owner);}
    static object Invoke(object owner,string name,params object[] args)
    {try{var method=owner.GetType().GetMethod(name,Private);if(method==null)throw new Exception("Missing UI action "+name);return method.Invoke(owner,args);}catch(TargetInvocationException ex){throw ex.InnerException;}}
    static void Assert(bool value,string reason){if(!value)throw new Exception(reason);}
    static T Find<T>(Control parent,string caption)where T:Control
    {foreach(Control child in parent.Controls){var value=child as T;if(value!=null&&value.Text==caption)return value;value=Find<T>(child,caption);if(value!=null)return value;}return null;}
    static void Click(FishingForm form,string caption){var button=Find<Button>(form,caption);Assert(button!=null,"Missing actual button "+caption);button.PerformClick();}
    static void Fault(FishingForm form,bool noBite,bool noSale,bool wrongNpc)
    {((CheckBox)Field(form,"noBite")).Checked=noBite;((CheckBox)Field(form,"noSale")).Checked=noSale;((CheckBox)Field(form,"wrongNpc")).Checked=wrongNpc;}
    static string ReportDirectory()
    {var current=new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);while(current!=null){if(File.Exists(Path.Combine(current.FullName,"build.ps1"))&&Directory.Exists(Path.Combine(current.FullName,"tests")))return Path.Combine(current.FullName,"bin","test-output");current=current.Parent;}return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output");}
    static void Write(bool passed,int scenarios,int factories,int input,string error)
    {string directory=ReportDirectory();Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"fishing-ui-tests.json"),new JavaScriptSerializer().Serialize(new{passed=passed,scenarios=scenarios,native_backend_requests=factories,input_events=input,error=error}),new UTF8Encoding(true));}
    [STAThread]static int Main(){try{return Run();}catch(Exception ex){Write(false,0,0,0,ex.ToString());return 1;}}
    static int Run()
    {
        Native.SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        int scenarios=0,factories=0,stage=0,exit=1;bool initialized=false;string error=null;DateTime began=DateTime.UtcNow;
        var initial=FishingSimulator.CreateDemoPlan();var game=FishingFixtures.Observation(initial).Game;var pendingBackend=new PendingBackend();long pendingTime=0;
        var pending=new InputGuard(pendingBackend,()=>pendingTime,()=>true);
        string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output","fishing-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);string path=Path.Combine(directory,"fishing-plan.json");
        using(var form=new FishingForm(()=>game.Clone(),()=>{factories++;throw new Exception("Offline fishing UI requested a native input backend");},null,()=>pending.Stop("Fishing takes exclusive ownership"),null,new InputFailureLatch()))
        using(var timer=new System.Windows.Forms.Timer{Interval=50})
        {
            timer.Tick+=delegate{
                try{
                    if(!initialized)
                    {
                        initialized=true;((NumericUpDown)Field(form,"speed")).Value=100;
                        ((NumericUpDown)Field(form,"minimum")).Value=5;Invoke(form,"SaveSpot");var saved=(FishingPlan)Invoke(form,"ReadPlan",false);
                        Assert(saved.Settings.MinimumFreeSlots==5&&saved.Spot.X==game.Farm.X&&saved.Spot.Y==game.Farm.Y&&!saved.Settings.EnableVendor,"Saving the current spot discarded edited rules or retained a stale vendor route");scenarios++;
                        pending.Schedule(0x57,5000,120);Invoke(form,"BeginRecording");Assert(form.IsRecording&&form.Busy&&!pending.Active&&!pending.Pending,"Route recording failed to cancel the pending mode");
                        game.Farm.Y=.49;Invoke(form,"AddPoint");game.Farm.X=.505;Invoke(form,"AddPoint");Invoke(form,"EndRecording");saved=(FishingPlan)Invoke(form,"ReadPlan",false);
                        Assert(!form.IsRecording&&!form.Busy&&saved.VendorRoute!=null&&saved.VendorRoute.Points.Count==3&&saved.VendorRoute.AllowReverse&&saved.Settings.MinimumFreeSlots==5,"F6/F7/F8 route recording did not retain the recorded positions and rules");scenarios++;
                        string fullName="Test fishing merchant complete name";game.Farm.TargetExists=game.Farm.TargetIdentityKnown=game.Farm.NameHashKnown=game.Farm.AttackabilityKnown=game.Farm.TargetDeadKnown=true;game.Farm.TargetCanAttack=game.Farm.TargetIsPlayer=game.Farm.TargetDead=false;game.Farm.TargetToken=321;game.Farm.TargetName="Test fishing merchant";game.Farm.TargetNameHash=TargetCatalog.HashName(fullName);((TextBox)Field(form,"npcName")).Text=fullName;Invoke(form,"SaveNpc");saved=(FishingPlan)Invoke(form,"ReadPlan",false);
                        Assert(saved.Vendor!=null&&saved.Vendor.Name==fullName&&saved.Vendor.NameHash==game.Farm.TargetNameHash&&saved.Settings.MinimumFreeSlots==5,"F11 did not save the verified full NPC name");scenarios++;
                        ((CheckBox)Field(form,"vendor")).Checked=true;foreach(string name in new[]{"bindings","autoLoot","audioConfirmed","autoSell","calibrated"})((CheckBox)Field(form,name)).Checked=true;
                        FishingPlanStore.Save(path,(FishingPlan)Invoke(form,"ReadPlan",true));((NumericUpDown)Field(form,"minimum")).Value=2;Invoke(form,"LoadPlanFile",path);saved=(FishingPlan)Invoke(form,"ReadPlan",false);
                        Assert(saved.Settings.MinimumFreeSlots==5&&saved.Settings.EnableVendor&&saved.Vendor.Name==fullName&&saved.VendorRoute.Points.Count==3&&!form.Busy&&factories==0,"Plan roundtrip lost settings or started input");foreach(string name in new[]{"bindings","autoLoot","audioConfirmed","autoSell","calibrated"})Assert(!((CheckBox)Field(form,name)).Checked,"Imported plan retained live confirmation "+name);scenarios++;
                        string fingerprint=saved.Fingerprint();string broken=Path.Combine(directory,"broken.json");File.WriteAllText(broken,"{\"invalid\":true}");bool rejected=false;try{Invoke(form,"LoadPlanFile",broken);}catch(FormatException){rejected=true;}
                        Assert(rejected&&((FishingPlan)Invoke(form,"ReadPlan",false)).Fingerprint()==fingerprint&&!form.Busy&&factories==0,"Broken plan replaced the current configuration");scenarios++;
                        Click(form,"恢复钓鱼演示");((NumericUpDown)Field(form,"speed")).Value=100;Fault(form,false,false,false);Invoke(form,"Record","PRIOR-FISHING-RUN-MARKER");pendingTime=0;pending.Schedule(0x57,5000,120);Click(form,"开始钓鱼离线模拟");pendingTime=6000;pending.Tick();Assert(form.SimulationRunning&&form.Busy&&!pending.Active&&!pending.Pending&&pendingBackend.Events==0,"Fishing simulation failed to take exclusive ownership");
                    }
                    else if(!form.SimulationRunning)
                    {
                        Assert(form.Simulation!=null,"UI did not retain simulation results");var sim=form.Simulation;
                        if(stage==0)
                        {
                            Assert(form.SimulationCompleted&&sim.Engine.ConfirmedCatches==2&&sim.Engine.VendorVisits==1&&sim.Engine.Casts==3&&sim.Engine.Reels==2&&sim.InputEvents==0,"Normal fishing UI flow did not complete catches, vendor return and recast: "+sim.Engine.LastReason);scenarios++;
                            Click(form,"导出钓鱼记录");Assert(!String.IsNullOrEmpty(form.LastReportPath),"Export button did not create a report");string json=File.ReadAllText(Path.Combine(form.LastReportPath,"fishing-report.json"),Encoding.UTF8);var report=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(json);var manifest=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(form.LastReportPath,"runtime-evidence.json"),Encoding.UTF8));
                            Assert(Equals(report["source"],"offline_simulation")&&Equals(report["game_validation"],"pending")&&Convert.ToInt32(report["native_input_events"])==0&&Equals(report["run_id"],manifest["run_id"])&&!json.Contains("PRIOR-FISHING-RUN-MARKER")&&File.Exists(Path.Combine(form.LastReportPath,"fishing-plan.json")),"Fishing report lost run association, included old events or fabricated acceptance");scenarios++;
                            Click(form,"恢复钓鱼演示");Assert(form.Simulation==null&&!form.Busy,"Replacing the plan retained an active simulation");Fault(form,true,false,false);Click(form,"开始钓鱼离线模拟");stage=1;
                        }
                        else if(stage==1)
                        {
                            Assert(form.SimulationCompleted&&sim.Engine.Casts==2&&sim.Engine.Reels==0&&sim.ElapsedMilliseconds>=30000&&sim.InputEvents==0,"No-bite UI loop failed to wait thirty seconds without F");scenarios++;
                            Click(form,"恢复钓鱼演示");Fault(form,false,true,false);Click(form,"开始钓鱼离线模拟");stage=2;
                        }
                        else if(stage==2)
                        {
                            Assert(!form.SimulationCompleted&&sim.Engine.Status=="Stopped"&&sim.Engine.VendorVisits==0&&sim.InputEvents==0,"Unsold full bags incorrectly resumed fishing");scenarios++;
                            Click(form,"恢复钓鱼演示");Fault(form,false,false,true);Click(form,"开始钓鱼离线模拟");stage=3;
                        }
                        else if(stage==3)
                        {
                            Assert(!form.SimulationCompleted&&sim.Engine.Status=="Stopped"&&sim.Engine.VendorVisits==0&&!sim.Observation.Game.MerchantOpen&&sim.InputEvents==0,"Wrong NPC received an interaction");scenarios++;
                            Assert(factories==0&&pendingBackend.Events==0&&Field(form,"audio")==null,"Offline UI created native input or started audio capture");exit=0;timer.Stop();form.Close();return;
                        }
                    }
                    if((DateTime.UtcNow-began).TotalSeconds>90)throw new Exception("Fishing UI integration timed out at stage "+stage);
                }catch(Exception ex){error=ex.ToString();timer.Stop();pending.Stop("Test cleanup");form.Close();}
            };
            form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-16000,-16000);form.Opacity=0;form.Shown+=delegate{form.Hide();form.Show();timer.Start();};Application.Run(form);
        }
        Write(exit==0,scenarios,factories,pendingBackend.Events,error);return exit;
    }
}
