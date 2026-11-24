using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace WoWProbe
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Native.SetProcessDPIAware();
            if(args.Length>0&&args[0]=="move"){try{return GrindCli.Move(args);}catch(Exception ex){Console.WriteLine(ex.Message);return 1;}}
            if(args.Length>0&&args[0]=="look"){try{return GrindCli.Look(args);}catch(Exception ex){Console.WriteLine(ex.Message);return 1;}}
            if(args.Length==0||args[0]=="quick"||args[0]=="quick-start"||args[0]=="quick-setup")
            {
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                bool owner;using(var mutex=new Mutex(true,"WoWProbe-QuickCombat",out owner))
                {if(!owner)return 1;try{Application.Run(new QuickGrindForm(args.Length==0||args[0]!="quick-setup"));}finally{mutex.ReleaseMutex();}}return 0;
            }
            if (args.Length > 0 && args[0] == "grind") { try { return GrindCli.Run(args); } catch (Exception ex) { Console.WriteLine("grind failure: " + ex); return 1; } }
            if (args.Length > 0 && args[0] == "probe") { try { return GrindCli.Probe(args); } catch (Exception ex) { Console.WriteLine("probe failure: " + ex); return 1; } }
            Console.Error.WriteLine("Unknown command. Use quick, quick-start, quick-setup, probe, move or look.");
            return 2;
        }
    }
}
