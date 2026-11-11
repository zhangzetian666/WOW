using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using WoWProbe;
class ReadinessUiTests
{
    static readonly BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    static object Field(object f,string n){return f.GetType().GetField(n,Private).GetValue(f);}static object Invoke(object f,string n,params object[] args){try{return f.GetType().GetMethod(n,Private).Invoke(f,args);}catch(TargetInvocationException e){throw e.InnerException;}}
    static void Assert(bool b,string why){if(!b)throw new Exception(why);}static T Find<T>(Control p,string text)where T:Control{foreach(Control c in p.Controls){var t=c as T;if(t!=null&&t.Text==text)return t;var v=Find<T>(c,text);if(v!=null)return v;}return null;}
    static void Click(CampaignForm f,string text){var b=Find<Button>(f,text);Assert(b!=null,"Missing button "+text);b.PerformClick();}
    static void CloseReports(CampaignForm f){foreach(var child in f.OwnedForms){child.Opacity=0;child.Close();}}
    [STAThread]static int Main(){try{return Run();}catch(Exception e){Write(false,0,0,e.ToString());return 1;}}
    static void Write(bool pass,int cases,int factories,string error){string p=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output");Directory.CreateDirectory(p);File.WriteAllText(Path.Combine(p,"readiness-ui-tests.json"),new JavaScriptSerializer().Serialize(new{passed=pass,scenarios=cases,native_backend_requests=factories,input_events=0,error=error}),new UTF8Encoding(true));}
    static int Run()
    {
        Native.SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);int cases=0,factories=0,exit=1;bool initialized=false;string error=null;DateTime began=DateTime.UtcNow;
        string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output","readiness-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);string bundle=Path.Combine(dir,"完整方案.wowplan");
        using(var f=new CampaignForm(delegate{return new OperationObservation();},delegate{factories++;throw new Exception("Read-only workflow requested native backend");},null,null,null,null,new InputFailureLatch()))
        using(var timer=new System.Windows.Forms.Timer{Interval=50})
        {
            timer.Tick+=delegate{
                try{
                    if(!initialized){initialized=true;var plan=CampaignDemo.Create();((TextBox)Field(f,"keepIds")).Text="123,456";((NumericUpDown)Field(f,"minimum")).Value=3;((ComboBox)Field(f,"quality")).SelectedIndex=1;
                        Invoke(f,"ApplyRoute","corpse_run",plan.CorpseRoute);Assert(((TextBox)Field(f,"keepIds")).Text=="123,456"&&((NumericUpDown)Field(f,"minimum")).Value==3&&((ComboBox)Field(f,"quality")).SelectedIndex==1,"Route import discarded unsaved sale rules");cases++;
                        Invoke(f,"ApplyBattle",plan.Battle,dir);Invoke(f,"ApplyCalibration",plan.Battle.Calibration);Assert(((TextBox)Field(f,"keepIds")).Text=="123,456"&&((NumericUpDown)Field(f,"minimum")).Value==3,"Battle/calibration import discarded unsaved rules");cases++;
                        Find<CheckBox>(f,"打怪路线逆向").Checked=true;Find<CheckBox>(f,"循环运行").Checked=true;Invoke(f,"SaveCompletePlan",bundle);Click(f,"恢复演示方案");Invoke(f,"LoadCompletePlan",bundle);
                        Assert(Find<CheckBox>(f,"打怪路线逆向").Checked&&Find<CheckBox>(f,"循环运行").Checked&&((TextBox)Field(f,"keepIds")).Text=="123,456"&&!f.Busy&&factories==0,"Portable plan lost direction/loop or armed input");Assert(!((CheckBox)Field(f,"bindings")).Checked&&!((CheckBox)Field(f,"uiConfirmed")).Checked,"Import retained live confirmation");cases++;
                        Click(f,"检查启动条件");Assert(f.LastPreflight!=null&&!f.LastPreflight.ReadyToArm&&factories==0&&!f.Busy,"Check started or falsely passed without game");CloseReports(f);cases++;
                        string before=((CampaignPlan)Field(f,"plan")).Fingerprint();File.WriteAllText(Path.Combine(dir,"broken.wowplan"),"broken");bool rejected=false;try{Invoke(f,"LoadCompletePlan",Path.Combine(dir,"broken.wowplan"));}catch{rejected=true;}Assert(rejected&&((CampaignPlan)Field(f,"plan")).Fingerprint()==before,"Invalid bundle replaced existing plan");cases++;
                        Click(f,"恢复演示方案");Invoke(f,"Record","PRIOR-RUN-ONLY-MARKER");Click(f,"开始完整离线模拟");
                    }
                    else if(!f.SimulationRunning){Assert(f.SimulationCompleted,"Simulation stopped: "+f.Simulation.Engine.LastReason);Click(f,"导出本次记录");string json=File.ReadAllText(Path.Combine(f.LastReportPath,"campaign-report.json"),Encoding.UTF8);Assert(!json.Contains("PRIOR-RUN-ONLY-MARKER"),"A new report contains previous run events");var data=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(json);var manifest=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(f.LastReportPath,"runtime-evidence.json"),Encoding.UTF8));Assert(Convert.ToInt32(data["schema_version"])==1&&Equals(data["run_id"],manifest["run_id"])&&data.ContainsKey("client_version"),"Feedback report lacks bound identity/client context");cases++;
                        Invoke(f,"InspectFeedback",f.LastReportPath);Assert(f.LastFeedback!=null&&f.LastFeedback.GameValidation=="pending"&&factories==0&&!f.Busy,"Feedback import altered acceptance or created input");CloseReports(f);cases++;exit=0;timer.Stop();f.Close();}
                    if((DateTime.UtcNow-began).TotalSeconds>50)throw new Exception("Readiness UI scenarios timed out");
                }catch(Exception e){error=e.ToString();timer.Stop();f.Close();}
            };
            f.ShowInTaskbar=false;f.StartPosition=FormStartPosition.Manual;f.Location=new Point(-16000,-16000);f.Opacity=0;f.Shown+=delegate{f.Hide();f.Show();timer.Start();};Application.Run(f);
        }
        Write(exit==0,cases,factories,error);return exit;
    }
}
