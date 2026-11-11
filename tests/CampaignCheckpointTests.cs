using System;
using System.IO;
using System.Text;
using WoWProbe;
class CampaignCheckpointTests
{
    static int passed,failed;static void Assert(bool b){if(!b)throw new Exception("assertion failed");}static void Test(string n,Action a){try{a();passed++;Console.WriteLine("PASS "+n);}catch(Exception e){failed++;Console.WriteLine("FAIL "+n+": "+e.Message);}}
    static void Reject(Action a){try{a();}catch(FormatException){return;}throw new Exception("contradictory checkpoint accepted");}
    static CampaignCheckpoint C(CampaignPlan p,string phase,bool complete,bool interrupted,int index){return new CampaignCheckpoint{PlanFingerprint=p.Fingerprint(),Phase=phase,Completed=complete,Interrupted=interrupted,FarmIndex=index};}
    static int Main(){string file=Path.Combine(Path.GetTempPath(),"wow-checkpoint-"+Guid.NewGuid().ToString("N")+".json");var p=CampaignDemo.Create();
        Test("legal farm recovery vendor stopped and completed states roundtrip",delegate{foreach(var c in new[]{C(p,"Farm",false,false,1),C(p,"Recovery",false,true,1),C(p,"Vendor",false,true,1),C(p,"Stopped",false,false,1),C(p,"Stopped",false,true,1),C(p,"Completed",true,false,p.FarmRoute.Points.Count)}){CampaignCheckpointStore.Save(file,c);var loaded=CampaignCheckpointStore.Load(file,p);Assert(loaded.Phase==c.Phase&&loaded.FarmIndex==c.FarmIndex&&loaded.Interrupted==c.Interrupted);}});
        Test("completed flag and phase must agree in both directions",delegate{Reject(delegate{CampaignCheckpointStore.Save(file,C(p,"Completed",false,false,1));});Reject(delegate{CampaignCheckpointStore.Save(file,C(p,"Farm",true,false,p.FarmRoute.Points.Count));});Reject(delegate{CampaignCheckpointStore.Save(file,C(p,"Stopped",true,false,p.FarmRoute.Points.Count));});});
        Test("farm cannot retain pending interruption and interruptions cannot lose it",delegate{Reject(delegate{CampaignCheckpointStore.Save(file,C(p,"Farm",false,true,1));});Reject(delegate{CampaignCheckpointStore.Save(file,C(p,"Recovery",false,false,1));});Reject(delegate{CampaignCheckpointStore.Save(file,C(p,"Vendor",false,false,1));});});
        Test("load rejects hand-edited contradictory states",delegate{CampaignCheckpointStore.Save(file,C(p,"Farm",false,false,1));string text=File.ReadAllText(file,Encoding.UTF8).Replace("\"Phase\":\"Farm\"","\"Phase\":\"Completed\"");File.WriteAllText(file,text,Encoding.UTF8);Reject(delegate{CampaignCheckpointStore.Load(file,p);});});
        Test("completed index and full plan fingerprint remain bound",delegate{CampaignCheckpointStore.Save(file,C(p,"Completed",true,false,1));Reject(delegate{CampaignCheckpointStore.Load(file,p);});CampaignCheckpointStore.Save(file,C(p,"Farm",false,false,1));var changed=p.Clone();changed.Settings.ProtectedItemIds.Add(123456);Reject(delegate{CampaignCheckpointStore.Load(file,changed);});});
        Test("rejected replacement preserves existing checkpoint bytes",delegate{CampaignCheckpointStore.Save(file,C(p,"Farm",false,false,1));var hash=Convert.ToBase64String(File.ReadAllBytes(file));Reject(delegate{CampaignCheckpointStore.Save(file,C(p,"Vendor",false,false,1));});Assert(hash==Convert.ToBase64String(File.ReadAllBytes(file)));});
        Console.WriteLine("Campaign checkpoint tests: "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
}
