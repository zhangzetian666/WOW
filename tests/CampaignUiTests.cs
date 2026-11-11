using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using WoWProbe;
class CampaignUiTests
{
    static int preflightPassed;
    sealed class Fake:IInputBackend{public int Events;public bool IsTargetForeground(){return true;}public bool IsEmergencyPressed(){return false;}public bool IsKeyAlreadyDown(ushort k){return false;}public void KeyDown(ushort k){Events++;}public void KeyUp(ushort k){Events++;}}
    static T Find<T>(Control parent,string text)where T:Control{foreach(Control c in parent.Controls){var t=c as T;if(t!=null&&c.Text==text)return t;var nested=Find<T>(c,text);if(nested!=null)return nested;}return null;}
    static void Click(CampaignForm f,string text){var b=Find<Button>(f,text);if(b==null)throw new Exception("Missing button "+text);b.PerformClick();}
    [STAThread]static int Main(){try{return Run();}catch(Exception ex){Write(false,0,0,0,ex.ToString());return 1;}}
    static void Write(bool passed,int scenarios,int events,int factories,string error){string p=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output");Directory.CreateDirectory(p);File.WriteAllText(Path.Combine(p,"campaign-ui-tests.json"),new JavaScriptSerializer().Serialize(new{passed=passed,scenarios=scenarios,preflight_checks_passed=preflightPassed,input_events=events,native_backend_requests=factories,error=error}),new UTF8Encoding(true));}
    static void VerifySnapshotReadDoesNotCapture()
    {
        string dir=Path.Combine(Path.GetTempPath(),"wow-portrait-reader-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(dir,"avatars"));using(var image=new System.Drawing.Bitmap(16,16))image.Save(Path.Combine(dir,"avatars","test.png"),System.Drawing.Imaging.ImageFormat.Png);
        var p=CampaignDemo.Create();p.Settings.RequirePortraitMatch=true;p.Battle.Targets[0].AvatarFile="avatars/test.png";int captures=0;var o=new OperationObservation{Fresh=true,Farm=new FarmObservation{Fresh=true,KnownCore=true,TargetExists=true,TargetIdentityKnown=true,TargetToken=303,NameHashKnown=true,TargetNameHash=p.Battle.Targets[0].NameHash,AttackabilityKnown=true,TargetCanAttack=true,TargetDeadKnown=true,TargetHealth=100,PortraitKnown=true,Portrait=new System.Drawing.Rectangle(832,8,64,64)}};
        using(var f=new CampaignForm(delegate{return o;},delegate{return null;},delegate(System.Drawing.Rectangle r){captures++;return new System.Drawing.Bitmap(16,16);},null,null,null,new InputFailureLatch()))
        {
            f.ShowInTaskbar=false;f.Opacity=0;f.StartPosition=FormStartPosition.Manual;f.Location=new System.Drawing.Point(-16000,-16000);f.Show();
            try{var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;typeof(CampaignForm).GetField("livePlan",flags).SetValue(f,p);typeof(CampaignForm).GetField("runAvatarDirectory",flags).SetValue(f,dir);typeof(CampaignForm).GetMethod("ReadLive",flags).Invoke(f,null);if(captures!=0)throw new Exception("Watchdog observation reader performs image capture or disk comparison");}finally{f.Close();}
        }
    }
    static void VerifyOldCaptureCannotBecomeFresh()
    {
        Func<int,ProbePacket> packet=delegate(int sequence){var b=new byte[128];b[0]=87;b[1]=80;b[2]=49;b[3]=1;b[4]=(byte)sequence;b[8]=b[9]=b[10]=b[11]=255;b[125]=255;foreach(int at in new[]{12,14,16,18,24,26})b[at]=b[at+1]=255;int a=0,c=0;for(int i=0;i<126;i++){a=(a+b[i])%255;c=(c+a)%255;}b[126]=(byte)a;b[127]=(byte)c;return ProbePacket.Decode(b);};
        using(var f=new ProbeForm())
        {
            f.ShowInTaskbar=false;f.Opacity=0;f.StartPosition=FormStartPosition.Manual;f.Location=new System.Drawing.Point(-16000,-16000);f.Show();
            try{System.Threading.Thread.Sleep(950);var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;var type=typeof(ProbeForm);var clock=(System.Diagnostics.Stopwatch)type.GetField("clock",flags).GetValue(f);var heartbeat=(Heartbeat)type.GetField("heartbeat",flags).GetValue(f);long now=clock.ElapsedMilliseconds;heartbeat.Observe(packet(1),now);heartbeat.Observe(packet(2),now);type.GetField("reading",flags).SetValue(f,true);type.GetField("calibrated",flags).SetValue(f,true);type.GetField("capturedAtMilliseconds",flags).SetValue(f,0L);if((bool)type.GetMethod("Fresh",flags).Invoke(f,null))throw new Exception("A delayed capture inherited a fresh processing-time heartbeat");type.GetField("capturedAtMilliseconds",flags).SetValue(f,now);if(!(bool)type.GetMethod("Fresh",flags).Invoke(f,null))throw new Exception("A recent advancing frame was incorrectly expired");}finally{f.Close();}
        }
    }
    static int Run()
    {
        Native.SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        VerifySnapshotReadDoesNotCapture();
        preflightPassed++;
        VerifyOldCaptureCannotBecomeFresh();
        preflightPassed++;
        var fake=new Fake();long time=0;var pending=new InputGuard(fake,delegate{return time;},delegate{return true;});pending.Schedule(0x57,5000,120);int factory=0,stage=0,scenarios=0,exit=1;bool started=false;string error=null;var began=DateTime.UtcNow;
        string[] faults={"模拟缺少尸体坐标","模拟没有补给","模拟错误商人","模拟物品信息未知","模拟出售前商店关闭"};
        using(var f=new CampaignForm(delegate{return new OperationObservation();},delegate{factory++;throw new Exception("Offline simulation requested native input");},null,null,delegate{pending.Stop("Switching to campaign");},null,new InputFailureLatch()))
        using(var timer=new System.Windows.Forms.Timer{Interval=40})
        {
            timer.Tick+=delegate{
                try{
                    if(!started){started=true;Click(f,"开始完整离线模拟");time=6000;pending.Tick();if(!f.Busy||pending.Pending||pending.Active||fake.Events!=0)throw new Exception("Input mode ownership failed");}
                    else if(!f.SimulationRunning){
                        if(stage==0){var s=f.Simulation;if(!f.SimulationCompleted||s.Engine.RecoveryCount!=1||s.Engine.VendorCount!=1||s.Sales!=2)throw new Exception("Full UI campaign did not complete both interruptions: "+s.Engine.LastReason);}
                        else if(f.SimulationCompleted)throw new Exception("Fault case incorrectly completed");
                        Click(f,"导出本次记录");var r=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(f.LastReportPath,"campaign-report.json"),Encoding.UTF8));var sim=(Dictionary<string,object>)r["simulation"];
                        if(!Equals(r["game_validation"],"pending")||Convert.ToInt32(sim["native_input_events"])!=0||factory!=0||fake.Events!=0)throw new Exception("Report source/input labeling invalid");
                        if(!File.Exists(Path.Combine(f.LastReportPath,"runtime-evidence.json")))throw new Exception("Persistent evidence was not exported");scenarios++;stage++;
                        if(stage==6){exit=0;timer.Stop();f.Close();return;}
                        Click(f,"恢复演示方案");if(f.Simulation!=null||f.Busy)throw new Exception("Plan replacement retained stale run");Find<CheckBox>(f,faults[stage-1]).Checked=true;Click(f,"开始完整离线模拟");
                    }
                    if((DateTime.UtcNow-began).TotalSeconds>100)throw new Exception("UI campaign timed out at "+stage);
                }catch(Exception ex){error=ex.ToString();timer.Stop();pending.Stop("cleanup");f.Close();}
            };
            f.ShowInTaskbar=false;f.StartPosition=FormStartPosition.Manual;f.Location=new System.Drawing.Point(-16000,-16000);f.Opacity=0;
            f.Shown+=delegate{f.Hide();f.Show();timer.Start();};Application.Run(f);
        }
        Write(exit==0,scenarios,fake.Events,factory,error);return exit;
    }
}
