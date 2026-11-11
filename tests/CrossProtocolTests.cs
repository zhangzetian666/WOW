using System;
using System.IO;
using WoWProbe;
class CrossProtocolTests
{
    static int Main(string[] args)
    {
        try {
            var p=ProbePacket.Decode(File.ReadAllBytes(args[0]));
            if(p.ClientVersion!="3.3.5" || p.Build!=12340 || p.InterfaceVersion!=30300 || p.TargetName!="Target" || p.Zone!="Zone" || p.Health!=0 || p.Power!=30 || p.PowerType!=1 || p.FreeSlots!=0 || p.MapId!=42)
                throw new Exception("Lua fixture changed or does not match independently specified expected state");
            if(Math.Abs(p.X-0.250007629627369)>1e-9 || Math.Abs(p.Y-0.750007629627369)>1e-9 || Math.Abs(p.Facing-Math.PI)>1e-9)
                throw new Exception("Coordinate or angle mismatch across Lua and C#");
            Console.WriteLine("PASS real Lua51 packet decoded by C#; fields, UTF8, checksum, coordinates and non-mana resource agree.");return 0;
        } catch(Exception ex) {Console.WriteLine("FAIL cross-language protocol: "+ex);return 1;}
    }
}
