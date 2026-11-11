using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using WoWProbe;

internal static class CampaignStartFailureTests
{
    const string Blocker="owned startup persistence failure fixture";
    static readonly BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    static int passed,failed,factories,inputs;
    static string errors="";
    sealed class FakeBackend:ICampaignInputBackend
    {
        public bool IsTargetForeground(){return true;}
        public bool IsEmergencyPressed(){return false;}
        public bool IsKeyAlreadyDown(ushort key){return false;}
        public void KeyDown(ushort key){inputs++;}
        public void KeyUp(ushort key){inputs++;}
        public Rectangle ClientBounds(){return new Rectangle(0,0,1920,1080);}
        public bool IsMouseAlreadyDown(bool right){return false;}
        public void MoveMouse(Point point){inputs++;}
        public void MouseDown(bool right){inputs++;}
        public void MouseUp(bool right){inputs++;}
    }
    static object Field(CampaignForm form,string name){return typeof(CampaignForm).GetField(name,Private).GetValue(form);}
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    static void Test(string name,Action test)
    {
        try{test();passed++;Console.WriteLine("PASS "+name);}
        catch(Exception ex){failed++;errors+=name+": "+ex+Environment.NewLine;Console.WriteLine("FAIL "+name+": "+ex.Message);}
    }
    static CampaignForm Form(Func<ICampaignInputBackend> factory)
    {
        var o=CampaignFixtures.Sample();
        var form=new CampaignForm(delegate{return o;},factory,null,null,null,null,new InputFailureLatch());
        form.ShowInTaskbar=false;form.Opacity=0;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-16000,-16000);form.Show();
        foreach(string field in new[]{"bindings","calibrated","uiConfirmed","resumeConfirmed","positionConfirmed"})((CheckBox)Field(form,field)).Checked=true;
        return form;
    }
    static Exception StartFailure(CampaignForm form)
    {
        try{typeof(CampaignForm).GetMethod("StartLive",Private).Invoke(form,null);}
        catch(TargetInvocationException ex){return ex.InnerException;}
        throw new Exception("StartLive unexpectedly succeeded.");
    }
    static void CleanFailure(CampaignForm form)
    {
        var session=(CampaignSession)Field(form,"session");
        Check(session==null||!session.IsActive,"Failed startup left a live countdown armed.");
        Check(Field(form,"session")==null,"Failed startup retained a published session.");
        Check(Field(form,"livePlan")==null&&Field(form,"runPlan")==null,"Failed startup retained its run plan.");
        Check(Field(form,"journal")==null,"Failed startup retained its journal.");
        Check(!form.Busy,"Failed startup still reports active work.");
    }
    static string OwnedBase()
    {
        string path=Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        string name=Path.GetFileName(path);const string prefix="journal-failure-";Guid id;
        if(Path.GetFileName(Path.GetDirectoryName(path))!="test-output"||!name.StartsWith(prefix,StringComparison.Ordinal)||!Guid.TryParseExact(name.Substring(prefix.Length),"N",out id))
            throw new InvalidOperationException("Compile this test into a new test-output/journal-failure-{GUID} directory.");
        return path;
    }
    [STAThread]static int Main(string[] args)
    {
        if(args.Length==1&&args[0]=="--child")return RunChild();
        string output=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output"),report=Path.Combine(output,"campaign-start-failure.json");Directory.CreateDirectory(output);
        try
        {
            string childDirectory=Path.Combine(output,"journal-failure-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(childDirectory);
            string child=Path.Combine(childDirectory,"CampaignStartFailureTests.exe");File.Copy(Assembly.GetExecutingAssembly().Location,child);
            var start=new ProcessStartInfo(child,"--child"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
            using(var process=Process.Start(start))
            {
                if(!process.WaitForExit(15000)){process.Kill();throw new Exception("Isolated startup failure checks timed out.");}
                File.Copy(Path.Combine(childDirectory,"journal-start-failure.json"),report,true);Console.WriteLine(File.ReadAllText(report));return process.ExitCode;
            }
        }
        catch(Exception ex){File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{passed=false,error=ex.ToString(),input_events=inputs}),new UTF8Encoding(true));Console.WriteLine(ex);return 1;}
    }
    static int RunChild()
    {
        string root=OwnedBase(),runtime=Path.Combine(root,"runtime");
        Check(!File.Exists(runtime)&&!Directory.Exists(runtime),"The isolated runtime fixture already exists.");
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        File.WriteAllText(runtime,Blocker,new UTF8Encoding(false));
        Test("journal creation failure cannot arm a session or request a backend",delegate{
            using(var form=Form(delegate{factories++;return new FakeBackend();}))
            {
                try{var error=StartFailure(form);Check(error is IOException,"Expected the owned runtime file to prevent journal creation.");CleanFailure(form);Check(factories==0&&inputs==0,"Persistence failure requested a backend or emitted input.");}
                finally{form.StopWorkspace("startup failure test cleanup");form.Close();}
            }
        });
        Check(File.ReadAllText(runtime)==Blocker,"The owned failure fixture changed unexpectedly.");File.Delete(runtime);Directory.CreateDirectory(runtime);
        Test("backend factory failure disposes the prepared journal and clears the session",delegate{
            int before=factories;
            using(var form=Form(delegate{factories++;throw new InvalidOperationException("controlled backend factory failure");}))
            {
                try{var error=StartFailure(form);Check(error is InvalidOperationException&&error.Message=="controlled backend factory failure","Unexpected factory exception.");CleanFailure(form);Check(factories==before+1&&inputs==0,"Factory failure emitted input or retried creation.");var logs=Directory.GetFiles(runtime,"run-*.jsonl");Check(logs.Length==1,"The journal was not prepared before requesting the backend.");using(var file=new FileStream(logs[0],FileMode.Open,FileAccess.ReadWrite,FileShare.None))Check(file.Length>0,"Prepared journal header is missing.");}
                finally{form.StopWorkspace("startup failure test cleanup");form.Close();}
            }
        });
        File.WriteAllText(Path.Combine(root,"journal-start-failure.json"),new JavaScriptSerializer().Serialize(new{passed=failed==0,tests_passed=passed,tests_failed=failed,backend_factory_requests=factories,input_events=inputs,game_validation="pending",error=errors}),new UTF8Encoding(true));
        Console.WriteLine("Campaign startup failure tests: "+passed+" passed, "+failed+" failed. Fake backend only; no native input.");return failed==0?0:1;
    }
}
