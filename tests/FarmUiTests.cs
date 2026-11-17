using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using WoWProbe;

class FarmUiTests
{
    sealed class Fake:IInputBackend
    {public int Events;public bool IsTargetForeground(){return true;}public bool IsEmergencyPressed(){return false;}public bool IsKeyAlreadyDown(ushort k){return false;}public void KeyDown(ushort k){Events++;}public void KeyUp(ushort k){Events++;}}
    static T Find<T>(Control root,string text) where T:Control
    {foreach(Control c in root.Controls){var match=c as T;if(match!=null&&c.Text==text)return match;var nested=Find<T>(c,text);if(nested!=null)return nested;}return null;}
    static void Click(FarmForm f,string text){var b=Find<Button>(f,text);if(b==null)throw new Exception("Missing UI button "+text);b.PerformClick();}
    static void Check(FarmForm f,string text,bool value){Find<CheckBox>(f,text).Checked=value;}
    static T Field<T>(FarmForm f,string name){var field=typeof(FarmForm).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance);if(field==null)throw new Exception("Missing battle control "+name);return (T)field.GetValue(f);}
    static object Call(FarmForm f,string name,params object[] args)
    {var method=typeof(FarmForm).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance);if(method==null)throw new Exception("Missing battle action "+name);try{return method.Invoke(f,args);}catch(TargetInvocationException ex){throw ex.InnerException??ex;}}
    static string Opening(BattleProfile p){var field=typeof(BattleProfile).GetField("SkillOpeningSequence");if(field==null)throw new Exception("Opening sequence cannot be saved in a battle profile");return (string)field.GetValue(p);}
    static void Confirm(FarmForm f,bool value){foreach(var name in new[]{"bindings","calibrated","positionConfirmed"})Field<CheckBox>(f,name).Checked=value;}
    static void NoConfirmations(FarmForm f){foreach(var name in new[]{"bindings","calibrated","positionConfirmed"})if(Field<CheckBox>(f,name).Checked)throw new Exception("Changed battle settings retained confirmation "+name);}
    static void RejectPreset(FarmForm f)
    {try{Call(f,"ApplyRoguePreset");}catch(InvalidOperationException){return;}throw new Exception("Unconfirmed rogue finisher was applied");}
    static void RogueControls(FarmForm f,ref int scenarios)
    {
        var interval=Field<NumericUpDown>(f,"interval");
        if(interval.Minimum!=500)throw new Exception("Battle controls still reject the requested 500ms skill interval");
        interval.Value=500;if(((BattleProfile)Call(f,"Settings",false)).SkillIntervalMs!=500)throw new Exception("500ms did not reach battle settings");
        interval.Value=750;if(((BattleProfile)Call(f,"Settings",false)).SkillIntervalMs!=750)throw new Exception("750ms did not reach battle settings");scenarios++;

        var opening=Field<TextBox>(f,"opening");var sequence=Field<TextBox>(f,"sequence");
        opening.Text="242223";sequence.Text="2";var applied=(BattleProfile)Call(f,"Settings",false);
        if(Opening(applied)!="242223"||applied.SkillSequence!="2")throw new Exception("Opening and repeat sequence were not kept separately");
        string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output");Directory.CreateDirectory(directory);string path=Path.Combine(directory,"rogue-ui-"+Guid.NewGuid().ToString("N")+".json");
        try{BattleProfileStore.Save(path,applied);var loaded=BattleProfileStore.Load(path);typeof(FarmForm).GetField("profile",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(f,loaded);Confirm(f,true);Call(f,"Populate");
            if(opening.Text!="242223"||sequence.Text!="2"||interval.Value!=750)throw new Exception("Reloading a battle profile lost opening/repeat controls");NoConfirmations(f);}
        finally{if(File.Exists(path))File.Delete(path);}scenarios++;

        var finisher=Field<ComboBox>(f,"rogueFinisher");
        finisher.SelectedIndex=0;string before=opening.Text+"/"+sequence.Text+"/"+interval.Value;RejectPreset(f);
        if(before!=opening.Text+"/"+sequence.Text+"/"+interval.Value)throw new Exception("Missing finisher changed the battle settings");scenarios++;
        finisher.SelectedIndex=1;RejectPreset(f);if(before!=opening.Text+"/"+sequence.Text+"/"+interval.Value)throw new Exception("Attack toggle was accepted as the finisher");scenarios++;
        foreach(int key in new[]{2,4}){finisher.SelectedIndex=key;RejectPreset(f);if(before!=opening.Text+"/"+sequence.Text+"/"+interval.Value)throw new Exception("A repeated rogue ability key was accepted as the finisher");}scenarios++;

        finisher.SelectedIndex=3;Confirm(f,true);Click(f,"应用盗贼方案");applied=(BattleProfile)Call(f,"Settings",false);
        if(Opening(applied)!="242223"||applied.SkillSequence!="2"||applied.SkillIntervalMs!=750||applied.BlindGrind)throw new Exception("Explicit rogue preset has the wrong opening, repeat, interval or mode");NoConfirmations(f);scenarios++;
        using(var image=new System.Drawing.Bitmap(f.Width,f.Height)){f.DrawToBitmap(image,new System.Drawing.Rectangle(System.Drawing.Point.Empty,f.Size));image.Save(Path.Combine(directory,"farm-rogue-ui.png"),System.Drawing.Imaging.ImageFormat.Png);}
        File.WriteAllText(Path.Combine(directory,"farm-rogue-ui-preview.txt"),"owned_form_render; layout preview only, not desktop capture");

        Confirm(f,true);Click(f,"开始离线战斗模拟");if(!f.Busy)throw new Exception("Opening-edit stop check had no running simulation");
        opening.Text="242224";if(f.Busy)throw new Exception("Changing an opening left the previous run active");NoConfirmations(f);scenarios++;
        Click(f,"恢复演示配置");
    }
    static Dictionary<string,object> Report()
    {
        var directories=new DirectoryInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"reports")).GetDirectories("farm-*");Array.Sort(directories,delegate(DirectoryInfo a,DirectoryInfo b){return a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc);});
        return (Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(directories[directories.Length-1].FullName,"farm-report.json"),Encoding.UTF8));
    }
    [STAThread] static int Main(){try{return Run();}catch(Exception ex){var path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output");Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,"farm-ui-tests.json"),new JavaScriptSerializer().Serialize(new{passed=false,error=ex.ToString()}),new UTF8Encoding(true));return 1;}}
    static int Run()
    {
        Native.SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        int exit=1,factory=0,stage=0,scenarios=0;long time=0;string error=null;var fake=new Fake();var single=new InputGuard(fake,delegate{return time;},delegate{return true;});single.Schedule(0x57,5000,120);var began=DateTime.UtcNow;
        using(var f=new FarmForm(delegate{return new FarmObservation();},delegate{factory++;throw new Exception("Offline mode requested real input");},null,delegate{single.Stop("Switching mode");},null,new InputFailureLatch()))
        using(var t=new System.Windows.Forms.Timer{Interval=50})
        {
            t.Tick+=delegate{
                try{
                    if(stage==0){
                        RogueControls(f,ref scenarios);
                        var low=FarmForm.DemoProfile();low.CriticalHealth=1;low.HealBelow=2;low.ManaBelow=1;low.RecoverTo=3;low.Validate();typeof(FarmForm).GetField("profile",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(f,low);typeof(FarmForm).GetMethod("Populate",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(f,null);
                        var applied=(BattleProfile)typeof(FarmForm).GetMethod("Settings",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(f,new object[]{false});if(applied.HealBelow!=2||applied.RecoverTo!=3)throw new Exception("Valid low recovery thresholds cannot populate imported controls");scenarios++;
                        Click(f,"恢复演示配置");Click(f,"开始离线战斗模拟");time=6000;single.Tick();if(single.Pending||single.Active||fake.Events!=0||!f.Busy)throw new Exception("Simulation failed to cancel previous input or publish busy state");
                        Click(f,"停止");Click(f,"导出本次记录");var stopped=(Dictionary<string,object>)Report()["simulation"];if(!Equals(stopped["status"],"Stopped")||!Equals(stopped["reason"],"用户停止")||f.Busy)throw new Exception("Manual stop was not reported as stopped with its reason");scenarios++;
                        Click(f,"恢复演示配置");Click(f,"开始离线战斗模拟");stage=1;
                    }
                    else if(stage==1&&f.SimulationCompleted){
                        var sim=f.Simulation;if(sim.Kills!=1||sim.SkillCasts!=3||sim.Loots!=1||sim.Skins!=1||sim.Heals==0||sim.Drinks==0||sim.InputEvents!=0)throw new Exception("Incomplete combat/recovery/loot/skin UI flow");
                        Click(f,"导出本次记录");var r=Report();var sr=(Dictionary<string,object>)r["simulation"];if(!Equals(r["game_validation"],"pending")||Convert.ToInt32(sr["native_input_events"])!=0||!Convert.ToBoolean(sr["completed"]))throw new Exception("UI report mislabels game validation/input");scenarios++;
                        Click(f,"恢复演示配置");if(f.SimulationCompleted||f.Busy||f.Simulation!=null)throw new Exception("Replacing config retained stale run context");Check(f,"模拟缺少目标坐标",true);Click(f,"开始离线战斗模拟");stage=2;
                    }
                    else if(stage==2&&!f.SimulationRunning){
                        if(f.SimulationCompleted||f.Simulation.Kills!=0)throw new Exception("Missing target position proceeded to kill");Click(f,"导出本次记录");var r=Report();var sr=(Dictionary<string,object>)r["simulation"];if(!Equals(sr["status"],"Stopped"))throw new Exception("Missing position was not reported stopped");scenarios++;
                        Click(f,"恢复演示配置");Check(f,"模拟缺少目标坐标",false);Check(f,"模拟没有补给",true);Click(f,"开始离线战斗模拟");stage=3;
                    }
                    else if(stage==3&&!f.SimulationRunning){
                        if(f.SimulationCompleted||f.Simulation.Kills!=0)throw new Exception("No supplies continued the encounter");scenarios++;
                        Click(f,"恢复演示配置");Check(f,"模拟没有补给",false);Check(f,"逆向",true);Click(f,"开始离线战斗模拟");stage=4;
                    }
                    else if(stage==4&&f.SimulationCompleted){scenarios++;if(factory!=0||fake.Events!=0)throw new Exception("Offline UI emitted native input");exit=0;t.Stop();f.Close();}
                    else if(stage==4&&!f.SimulationRunning){f.Export();throw new Exception("Reverse UI scenario stopped; inspect latest farm report");}
                    if((DateTime.UtcNow-began).TotalSeconds>90)throw new Exception("Farm UI scenarios timed out at "+stage);
                }catch(Exception ex){error=ex.ToString();f.Export();t.Stop();single.Stop("Cleanup");f.Close();}
            };
            f.ShowInTaskbar=false;f.StartPosition=FormStartPosition.Manual;f.Location=new System.Drawing.Point(-16000,-16000);f.Opacity=0;
            f.Shown+=delegate{f.Hide();f.Show();t.Start();};Application.Run(f);
        }
        string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output");Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,"farm-ui-tests.json"),new JavaScriptSerializer().Serialize(new{passed=exit==0,scenarios=scenarios,input_events=fake.Events,native_backend_requests=factory,error=error}),new UTF8Encoding(true));return exit;
    }
}
