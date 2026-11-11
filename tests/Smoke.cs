using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace WoWProbe
{
    public static class Smoke
    {
        [STAThread]
        public static int Main(string[] args)
        {
            Native.SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            if(args.Length>0&&args[0]=="--route-ui-preview")return RoutePreview();
            return args.Length>0 && args[0]=="--ui-preview"?Preview():Run();
        }
        public static int RoutePreview()
        {
            string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output");Directory.CreateDirectory(directory);
            using(var form=new RouteForm(delegate{return new RouteTelemetry();},delegate{return null;},null,null))
            using(var timer=new System.Windows.Forms.Timer{Interval=500})
            {
                timer.Tick+=delegate{timer.Stop();using(var image=Native.Capture(form.Bounds))image.Save(Path.Combine(directory,"route-ui.png"),System.Drawing.Imaging.ImageFormat.Png);form.Close();};
                form.Shown+=delegate{form.Hide();form.Show();form.Activate();form.Refresh();timer.Start();};Application.Run(form);
            }
            return 0;
        }
        public static int Preview()
        {
            string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output");Directory.CreateDirectory(directory);
            using(var form=new ProbeForm())
            using(var timer=new System.Windows.Forms.Timer {Interval=500})
            {
                timer.Tick+=delegate {
                    timer.Stop();
                    using(var image=Native.Capture(form.Bounds)) {image.Save(Path.Combine(directory,"tool-ui.png"),System.Drawing.Imaging.ImageFormat.Png);}
                    form.Close();
                };
                form.Shown+=delegate{form.Hide();form.Show();form.Activate();form.Refresh();timer.Start();};Application.Run(form);
            }
            return 0;
        }
        static void Put16(byte[] b,int i,int n){b[i]=(byte)n;b[i+1]=(byte)(n>>8);}
        static void Put32(byte[] b,int i,int n){for(int k=0;k<4;k++)b[i+k]=(byte)(n>>(8*k));}
        public static byte[] Fixture()
        {
            var b=new byte[128];b[0]=87;b[1]=80;b[2]=49;b[3]=1;b[4]=1;b[6]=8;b[7]=127;
            b[8]=80;b[9]=70;b[10]=60;b[11]=0;Put16(b,12,32767);Put16(b,14,16384);Put16(b,16,0);Put16(b,18,16);
            Put32(b,20,141);Put16(b,24,65535);Put16(b,26,65535);Put32(b,28,12340);Put32(b,32,30300);Put32(b,36,1000);
            b[40]=4;b[41]=10;Encoding.UTF8.GetBytes("TEST").CopyTo(b,42);Encoding.UTF8.GetBytes("SIMULATION").CopyTo(b,74);
            Encoding.UTF8.GetBytes("3.3.5").CopyTo(b,106);b[122]=2;b[123]=5;
            b[125]=255;
            int a=0,c=0;for(int i=0;i<126;i++){a=(a+b[i])%255;c=(c+a)%255;}b[126]=(byte)a;b[127]=(byte)c;
            return b;
        }
        public static int Run()
        {
            int exit=1;string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-output");Directory.CreateDirectory(directory);
            using(var image=PixelCodec.Encode(Fixture(),8))
            using(var form=new Form {Text="本地模拟信号验证 · 不操作游戏",ClientSize=new Size(370,180),StartPosition=FormStartPosition.CenterScreen,TopMost=true,BackColor=Color.White})
            using(var timer=new System.Windows.Forms.Timer {Interval=600})
            {
                form.Paint+=delegate(object sender,PaintEventArgs e){e.Graphics.DrawImageUnscaled(image,16,16);e.Graphics.DrawString("SIMULATION ONLY - no game / no input",SystemFonts.DefaultFont,Brushes.Black,16,130);};
                timer.Tick+=delegate {
                    timer.Stop();
                    try
                    {
                        Point center=form.PointToScreen(new Point(20,20));
                        var bounds=Native.CaptureGeometry(center,8,SystemInformation.VirtualScreen);
                        using(var screen=Native.Capture(bounds))
                        {
                            screen.Save(Path.Combine(directory,"capture-diagnostic.png"),System.Drawing.Imaging.ImageFormat.Png);
                            var bytes=PixelCodec.Decode(screen,4,4,8);var packet=ProbePacket.Decode(bytes);
                            if(packet.TargetName!="TEST" || packet.Zone!="SIMULATION" || packet.FreeSlots!=16 || packet.Health!=80)throw new Exception("Actual screen capture did not match known fixture");
                            screen.Save(Path.Combine(directory,"simulated-overlay.png"),System.Drawing.Imaging.ImageFormat.Png);
                            var report=new {kind="local_simulation_not_game_validation",screen_capture=true,packet_decoded=true,input_emitted=false,telemetry=ReportData.From(packet)};
                            File.WriteAllText(Path.Combine(directory,"smoke-test.json"),new JavaScriptSerializer().Serialize(report),new UTF8Encoding(true));
                        }
                        exit=0;
                        string oldError=Path.Combine(directory,"smoke-error.txt");if(File.Exists(oldError))File.Delete(oldError);
                    }
                    catch(Exception ex){File.WriteAllText(Path.Combine(directory,"smoke-error.txt"),ex.ToString());}
                    form.Close();
                };
                // The launcher deliberately hides its startup window. Re-show this owned
                // test surface before testing desktop capture; never sample a hidden form.
                form.Shown+=delegate{form.Hide();form.Show();form.Activate();form.Refresh();timer.Start();};Application.Run(form);
            }
            return exit;
        }
    }
}
