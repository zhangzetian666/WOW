using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace WoWProbe
{
    /// <summary>
    /// Headless blind-grind driver for Forever/secret-value clients.
    /// usage: WoWProbe.exe grind &lt;route.json&gt; &lt;profile.json&gt; [cycles]
    /// Auto-locates the addon color panels, measures turn/forward calibration,
    /// then drives FarmSession blind-grind cycles until stopped (Q + Enter) or death.
    /// </summary>
    public static class GrindCli
    {
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);

        static Stopwatch clock = Stopwatch.StartNew();
        static ProbePacket packet; static CombatPacket combatPacket;
        static Heartbeat heartbeat = new Heartbeat();
        static WindowInfo target; static Rectangle client;
        static int cell = 8; static Point firstCenter; static bool located;
        static long capturedAt = -1; static string logPath;
        static object gate = new object();
        static StreamWriter logFile;
        static long lastShot;

        /// <summary>
        /// Read-only status probe. Locates the addon panels, warms up the heartbeat and
        /// prints one decoded observation without sending a single key or mouse event.
        /// usage: WoWProbe.exe probe &lt;route.json&gt;
        /// </summary>
        public static int Probe(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            if (args.Length < 2) { Console.WriteLine("usage: WoWProbe.exe probe <route.json>"); return 2; }
            target = FindWowWindow();
            if (target == null) { Console.WriteLine("未找到魔兽世界窗口。"); return 1; }
            client = Native.ClientBounds(target);
            Console.WriteLine("窗口客户区: " + client + " / " + target);
            RouteDocument route;
            try { route = RouteStore.Load(args[1]); }
            catch (Exception ex) { Console.WriteLine("路线加载失败: " + ex.Message); return 1; }
            if (!LocatePanels(route)) { Console.WriteLine("未能定位插件色块（请确认插件已加载且地图与路线一致）。"); return 1; }
            for (int i = 0; i < 12; i++) { Snapshot(); Thread.Sleep(150); }
            FarmObservation o = Snapshot();
            Console.WriteLine("=== 只读状态快照（未发送任何按键/鼠标事件）===");
            Console.WriteLine("Fresh=" + o.Fresh + " KnownCore=" + o.KnownCore + " ReadFailures=" + o.ReadFailures + " Sequence=" + o.Sequence);
            Console.WriteLine("PlayerDead=" + o.PlayerDead + " PlayerGhost=" + o.PlayerGhost + " InCombat=" + o.InCombat + " BagFull=" + o.BagFull);
            Console.WriteLine("Health=" + o.Health + " Power=" + o.Power + " PowerType=" + o.PowerType + " TargetHealth=" + o.TargetHealth);
            Console.WriteLine("Pos=(" + o.X.ToString("F5") + "," + o.Y.ToString("F5") + ") Facing=" + o.Facing.ToString("F4") + " MapId=" + o.MapId + " MapMode=" + o.MapMode + " Zone=" + o.Zone);
            Console.WriteLine("TargetExists=" + o.TargetExists + " TargetName=" + o.TargetName + " TargetPositionKnown=" + o.TargetPositionKnown +
                " Target=(" + o.TargetX.ToString("F5") + "," + o.TargetY.ToString("F5") + ") TargetDead=" + o.TargetDead + " TargetCanAttack=" + o.TargetCanAttack);
            Console.WriteLine("RangeKnown=" + o.RangeKnown + " TargetInRange=" + o.TargetInRange + " IncomingEvents: LootOpened=" + o.LootOpened + " CastSuccess=" + o.CastSuccess);
            Console.WriteLine("ActionsKnown=" + o.ActionsKnown + " KnownMask=" + o.KnownActionsMask + " ReadyMask=" + o.ReadyActionsMask + " UsableMask=" + o.UsableActionsMask +
                " CastingKnown=" + o.CastingKnown + " LootKnown=" + o.LootKnown + " SkinKnown=" + o.SkinKnown);
            Console.WriteLine("TargetIdentityKnown=" + o.TargetIdentityKnown + " NameHashKnown=" + o.NameHashKnown + " AttackabilityKnown=" + o.AttackabilityKnown +
                " TargetDeadKnown=" + o.TargetDeadKnown + " PortraitKnown=" + o.PortraitKnown + " LootEventsKnown=" + o.LootEventsKnown + " CastEventsKnown=" + o.CastEventsKnown);
            return 0;
        }

        public static int Run(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { } // WinExe may have no console attached
            if (args.Length < 3)
            {
                Console.WriteLine("usage: WoWProbe.exe grind <route.json> <profile.json> [cycles]");
                return 2;
            }
            string routePath = args[1], profilePath = args[2];
            int cycles = args.Length > 3 ? int.Parse(args[3]) : 999;
            string dir = Path.GetDirectoryName(Path.GetFullPath(routePath));
            logPath = Path.Combine(Path.GetDirectoryName(profilePath) ?? ".", "grind-log.txt");
            logFile = new StreamWriter(logPath, false, new UTF8Encoding(false)) { AutoFlush = true };

            target = FindWowWindow();
            if (target == null) { Log("未找到魔兽世界窗口。"); logFile.Dispose(); return 1; }
            try
            {
                return RunLive(routePath, profilePath, cycles);
            }
            finally
            {
                // Release any cursor clipping and close the log so the machine is left clean for the operator.
                Native.UnclipCursor();
                if (logFile != null) { try { logFile.Dispose(); } catch { } }
            }
        }

        static int RunLive(string routePath, string profilePath, int cycles)
        {
            client = Native.ClientBounds(target);
            Log("窗口: " + target + " 客户区 " + client);
            Activate(target.Handle);

            RouteDocument route;
            BattleProfile baseProfile;
            try
            {
                route = RouteStore.Load(routePath);
                baseProfile = BattleProfileStore.Load(profilePath);
            }
            catch (Exception ex) { Log("配置加载失败: " + ex.Message); return 1; }

            if (!LocatePanels(route)) { Log("未能自动定位插件色块，请确认游戏窗口化和插件已加载。"); return 1; }

            // Keep the physical cursor out of the emergency corner while we drive the game.
            try { Native.MoveMouse(new Point(client.X + client.Width - 40, client.Y + client.Height - 40)); } catch { }

            // Live profile: blind grind on, all live confirmations granted by the CLI operator.
            BattleProfile profile = baseProfile.Clone();
            profile.BlindGrind = true;
            profile.BindingsConfirmed = true;
            profile.AllowApproach = true;
            profile.ApproachPositionConfirmed = true;
            profile.Calibration.Confirmed = true;
            if (profile.Calibration.MapId != route.MapId || profile.Calibration.MapMode != route.MapMode ||
                !String.Equals(profile.Calibration.Zone, route.Zone, StringComparison.Ordinal))
            {
                Log("标定地图与路线不符，改用路线地图信息。");
                profile.Calibration.MapId = route.MapId; profile.Calibration.MapMode = route.MapMode; profile.Calibration.Zone = route.Zone;
            }

            // A dead or ghost character cannot calibrate or farm; revive it first (spirit healer flow).
            if (!WaitForRevival("启动", profile)) return 1;

            bool calibrated = false;
            for (int attempt = 1; attempt <= 5 && !calibrated; attempt++)
            {
                if (attempt > 1) { Log("标定重试第 " + attempt + " 次（等待角色静止）..."); Thread.Sleep(3000); }
                calibrated = MeasureCalibration(profile, route);
            }
            if (!calibrated) { Log("自动标定连续失败。"); return 1; }

            var safety = new InputFailureLatch();
            var backend = new WindowsInputBackend(target, m => Log(m));
            int completed = 0;
            // Kept across patrol rounds: the last mob we actually met is a much better /target
            // name than the profile's catalog entry, and TAB alone found ~1 target per 1000 presses.
            string namedQuarry = profile.Targets.Count > 0 ? profile.Targets[0].Name : "";
            long lastNamedScan = -30000;
            int namedScans = 0;
            for (int cycle = 0; cycle < cycles && !HasKey(); cycle++)
            {
                FarmObservation start = Snapshot();
                if (start.PlayerDead || start.PlayerGhost)
                {
                    if (!WaitForRevival("第 " + (cycle + 1) + " 轮", profile)) break;
                    start = Snapshot();
                }
                bool reverse = (cycle % 2) == 1; // alternate direction so the patrol sweeps back and forth
                int expand = (cycle / 4) % 2; // rarely widen the sweep line; deep extensions reach hostile camps
                RouteDocument workRoute = Expanded(route, expand);
                RouteDocument work = reverse ? workRoute.Reversed() : workRoute.Clone();
                int startIndex = NearestIndex(work, start.X, start.Y);
                Log("=== 第 " + (cycle + 1) + " 轮（" + (reverse ? "反向" : "正向") + "，扩展 " + expand + "）：从路线点 " + startIndex + " 开始，当前位置 " + Pos(start) + " ===");
                var session = new FarmSession(backend, () => clock.ElapsedMilliseconds, Snapshot, Log, safety);
                try { session.Start(work, profile, false, false, startIndex); }
                catch (Exception ex) { Log("启动失败: " + ex.Message); return 1; }
                Activate(target.Handle);
                bool capsLogged = false;
                int capsPoll = 0;
                uint attackToken = 0;
                long lastAutoAttackAt = -100000;
                int lastObservedTargetHealth = -1;
                while (session.IsActive)
                {
                    Thread.Sleep(40);
                    session.Tick();
                    MaybeShot();
                    // Sample once per second: capability bits, real target-damage evidence and
                    // auto-attack. Without a target, FromPackets leaves range/attackability bits
                    // at false defaults, so a no-target probe cannot tell "secret" from "out of range".
                    if ((++capsPoll % 25) == 0)
                    {
                        FarmObservation cap = Snapshot();
                        if (cap.TargetExists)
                        {
                            if (!capsLogged)
                            {
                                Log("接战能力位快照: RangeKnown=" + cap.RangeKnown + " InRange=" + cap.TargetInRange +
                                    " PosKnown=" + cap.TargetPositionKnown + " AttackabilityKnown=" + cap.AttackabilityKnown +
                                    " IdentityKnown=" + cap.TargetIdentityKnown + " DeadKnown=" + cap.TargetDeadKnown +
                                    " ActionsKnown=" + cap.ActionsKnown + " Known=" + cap.KnownActionsMask +
                                    " Ready=" + cap.ReadyActionsMask + " Usable=" + cap.UsableActionsMask +
                                    " LootKnown=" + cap.LootKnown + " InCombat=" + cap.InCombat +
                                    " CanAttack=" + cap.TargetCanAttack + " TargetHealth=" + cap.TargetHealth +
                                    " PlayerHealth=" + cap.Health + " TargetName=" + cap.TargetName +
                                    " NameHashKnown=" + cap.NameHashKnown + " NameHash=" + cap.TargetNameHash +
                                    " Token=" + cap.TargetToken + " Dead=" + cap.TargetDead);
                                capsLogged = true;
                            }
                            if (cap.TargetHealth >= 0 && cap.TargetHealth <= 100 && cap.TargetHealth != lastObservedTargetHealth)
                            {
                                Log("目标血量: " + cap.TargetHealth + "% name=" + cap.TargetName +
                                    " InCombat=" + cap.InCombat + " PlayerHealth=" + cap.Health);
                                lastObservedTargetHealth = cap.TargetHealth;
                            }
                            // /startattack keeps melee auto-attack running while the blind engine
                            // closes the gap. Skills alone need energy and a facing target, and the
                            // client hides range, so two long fights produced swings but no damage.
                            if (cap.TargetToken != 0 && cap.TargetToken != attackToken)
                            {
                                attackToken = cap.TargetToken;
                                // Learn the mob we actually met: TAB found only ~1 target per
                                // thousand presses on this client, while /target <name> worked
                                // reliably during the revival flow. Reuse that name for sparse scans.
                                if (!String.IsNullOrEmpty(cap.TargetName) && cap.TargetName != "灵魂医者")
                                    namedQuarry = cap.TargetName;
                                Log("锁定目标 " + cap.TargetName + "，发送 /startattack 开启自动近战攻击。");
                                Chat(backend, "/startattack");
                                lastAutoAttackAt = clock.ElapsedMilliseconds;
                            }
                            else if (cap.TargetToken != 0 && clock.ElapsedMilliseconds - lastAutoAttackAt >= 12000)
                            {
                                // Slot 1 is the Attack *toggle* so we never press it now, but a
                                // dropped target or a movement state change can still clear
                                // auto-attack. /startattack is idempotent: refreshing is safe.
                                lastAutoAttackAt = clock.ElapsedMilliseconds;
                                Log("刷新自动近战攻击 (/startattack)。");
                                Chat(backend, "/startattack");
                            }
                        }
                        else
                        {
                            attackToken = 0; lastObservedTargetHealth = -1;
                            long nowMs = clock.ElapsedMilliseconds;
                            if (nowMs - lastNamedScan >= 8000)
                            {
                                lastNamedScan = nowMs;
                                namedScans++;
                                // /target <name> reaches any matching mob in the zone, so it
                                // often picked one 40+ yards away and the rogue chased it until
                                // another mob killed it. /targetenemy only picks a nearby threat.
                                // Alternate so both strategies get a chance every 8 seconds.
                                bool byName = (namedScans % 2 == 0) && namedQuarry.Length > 0;
                                string scan = byName ? "/target " + namedQuarry : "/targetenemy";
                                Log("长时间未锁定目标，补扫 (" + (byName ? "按名字" : "最近敌人") + "): " + scan);
                                Chat(backend, scan);
                            }
                        }
                    }
                    if (HasKey())
                    {
                        var key = Console.ReadKey(true).Key;
                        if (key == ConsoleKey.Q) { session.Stop("手动退出"); return 0; }
                        if (key == ConsoleKey.Enter) break;
                    }
                }
                Log("本轮结束: " + session.Status + " / " + session.EngineState + " · " + session.LastReason);
                if (safety.Faulted) { Log("输入安全闩触发，停止。"); break; }
                FarmObservation end = Snapshot();
                if (end.PlayerDead || end.PlayerGhost)
                {
                    if (!WaitForRevival("本轮结束", profile)) { Log("自动复活失败，全部停止。"); break; }
                }
                if (session.Status == "Completed") { completed++; if (completed >= cycles) break; }
                Thread.Sleep(1500);
            }
            Log("打怪循环退出。完成整轮 " + completed + " 次。");
            return 0;
        }

        // ---- automatic revival (spirit healer flow) ----
        // Corpse state: ENTER activates the death dialog's default button (release spirit);
        // ghost state: select the nearest friendly NPC (the spirit healer at the graveyard),
        // walk to it using the panel's target position, press F (interact-with-target hardware key)
        // to open gossip, then /click the first gossip option ("让我复活吧") and the confirmation
        // popup's accept button. The addon's /wowprobe rez command performs the friendly targeting.
        static bool WaitForRevival(string context, BattleProfile profile)
        {
            FarmObservation start = Snapshot();
            if (!start.PlayerDead && !start.PlayerGhost) return true;
            Log(context + "时角色处于死亡/幽魂状态，启动自动复活流程...");
            var backend = new WindowsInputBackend(target, m => Log(m));
            int fullSearches = 0;
            for (int attempt = 1; attempt <= 12 && !HasKey(); attempt++)
            {
                FarmObservation w = Snapshot();
                if (!w.PlayerDead && !w.PlayerGhost && w.Fresh && w.KnownCore) { Log("已复活。"); return true; }
                if (w.PlayerDead && !w.PlayerGhost)
                {
                    Log("尝试释放灵魂（回车触发死亡弹窗默认按钮，/click 兜底）...");
                    Pulse(backend, 0x0D, 60); Thread.Sleep(1500);
                    Chat(backend, "/click StaticPopup1Button1");
                    Thread.Sleep(2200);
                    continue;
                }
                // Ghost: TargetNearestFriend/InteractUnit are hardware-gated even from chat
                // (pcall succeeds but nothing happens). Select the spirit healer by NAME via the
                // /target chat command (Unicode typed with KEYEVENTF_UNICODE), falling back to a
                // real right-click sweep over the screen; the ghost spawns beside the healer.
                w = Snapshot();
                Log("幽魂目标状态: exists=" + w.TargetExists + " name=" + w.TargetName + " pos=" + w.TargetPositionKnown +
                    (w.TargetPositionKnown ? " (" + w.TargetX.ToString("F4") + "," + w.TargetY.ToString("F4") + ")" : "") +
                    "（该客户端幽魂形态下目标坐标通常保密）");
                // Forever/Midnight keeps target map positions secret in ghost form: TargetExists
                // can be true while TargetPositionKnown stays false forever. Requiring a position
                // here deadlocked revival in an endless right-click sweep (2026-11-13 06:37).
                // The spirit healer spawns right beside the ghost, so an existing friendly target
                // is enough to attempt interaction directly.
                bool selected = IsHealerTarget(w);
                if (!selected)
                {
                    Log("幽魂状态：按名字选中灵魂医者（/target 灵魂医者）...");
                    Chat(backend, "/target 灵魂医者");
                    Thread.Sleep(900);
                    w = Snapshot();
                    Log("按名字后目标状态: exists=" + w.TargetExists + " name=" + w.TargetName + " pos=" + w.TargetPositionKnown);
                    selected = IsHealerTarget(w);
                }
                for (int dir = 0; dir < 8 && !selected; dir++)
                {
                    if (dir == 0) Log("按名字未选中，改为右键网格扫描...");
                    else
                    {
                        Pulse(backend, profile.Movement.TurnRight,
                            Math.Max(40, (int)(Math.PI / 4 / Math.Max(0.1, profile.Calibration.TurnRadiansPerSecond) * 1000)));
                        Thread.Sleep(450);
                    }
                    selected = ClickSweep();
                }
                if (!selected)
                {
                    Log("未能选中任何目标，本轮放弃。");
                    Thread.Sleep(1800);
                    continue;
                }
                FarmObservation healer = Snapshot();
                bool positioned = healer.TargetPositionKnown && !Double.IsNaN(healer.TargetX) && !Double.IsNaN(healer.TargetY);
                if (positioned)
                {
                    double stop = attempt >= 5 ? 0.0012 : attempt >= 3 ? 0.0018 : 0.0025;
                    Log(String.Format("灵魂医者坐标 ({0:F4},{1:F4})，走近至 {2}...", healer.TargetX, healer.TargetY, stop));
                    WalkTo(backend, profile, healer.TargetX, healer.TargetY, stop);
                    healer = Snapshot();
                    if (NavigationMathF(healer)) FaceTo(backend, profile, healer.TargetX, healer.TargetY);
                }
                else
                {
                    // Live 2026-11-14 09:45: blind rotate+forward made the ghost DRIFT away
                    // from the graveyard (minimap 34.5,65.7 → 39.9,60.9) while the healer
                    // stayed out of view. The ghost's own position AND the healer range bit
                    // are both readable (probe: Pos/Facing KnownCore, RangeKnown=True,
                    // TargetInRange=False), so search actively instead of guessing.
                    bool closeRange = healer.RangeKnown && healer.TargetInRange;
                    if (closeRange)
                    {
                        Log("灵魂医者已在互动范围内，直接互动。");
                    }
                    else if (healer.KnownCore && !Double.IsNaN(healer.X) && !Double.IsNaN(healer.Y))
                    {
                        // 2026-11-14: the spirit healer is a fixed graveyard fixture. Once its
                        // graveyard point is known (operator-verified from the live minimap),
                        // walk straight there instead of blindly sweeping spokes; the range bit
                        // is checked on every step so an early pass close to the healer stops.
                        if (fullSearches >= 2)
                        {
                            Log("多轮接近尝试均未成功，转入被动等待。");
                            break;
                        }
                        fullSearches++;
                        double gx, gyy;
                        if (GraveyardPoint(healer.MapId, out gx, out gyy))
                        {
                            Log(String.Format("按已知墓地点 ({0:F4},{1:F4}) 直接步行接近...", gx, gyy));
                            WalkTowardsHealer(backend, profile, gx, gyy, 160);
                        }
                        else
                        {
                            SeekHealer(backend, profile, healer);
                        }
                        FarmObservation afterSeek = Snapshot();
                        if (!(afterSeek.RangeKnown && afterSeek.TargetInRange)) continue;
                        Log("接近后医者进入互动范围。");
                    }
                    else
                    {
                        // Position unreadable: fall back to rotating so the healer model
                        // enters the view for the F interact / model click.
                        if (attempt > 1)
                        {
                            Log("自身坐标不可读；旋转 45° 让医者转入视野...");
                            Pulse(backend, profile.Movement.TurnRight,
                                Math.Max(40, (int)(Math.PI / 4 / Math.Max(0.1, profile.Calibration.TurnRadiansPerSecond) * 1000)));
                            Thread.Sleep(450);
                        }
                    }
                }
                Log("与灵魂医者互动（F 键 + 医者模型右键）...");
                Pulse(backend, 0x46, 60); Thread.Sleep(500);
                // The healer's projected position shifts with camera pitch and distance.
                // (+20,+22) was the verified hit on 11-13, but after /reload the model was
                // projected into the LOWER half of the client (its wings filled y=+160..+320),
                // so the centre-only sweep never touched the clickable body. Cover both.
                var offsets = new[] {
                    new Point(20, 22), new Point(0, 200), new Point(0, 260),
                    new Point(-80, 200), new Point(80, 200), new Point(0, 0),
                    new Point(0, -120), new Point(-120, 260), new Point(120, 260),
                    new Point(0, 320) };
                Point hit = offsets[(attempt - 1) % offsets.Length];
                try
                {
                    Native.MoveMouse(new Point(client.X + client.Width / 2 + hit.X, client.Y + client.Height / 2 + hit.Y));
                    Native.SendMouseButton(true, false); Thread.Sleep(80); Native.SendMouseButton(true, true);
                    Log("右键医者模型，中心偏移 (" + hit.X + "," + hit.Y + ")。");
                }
                catch { }
                Thread.Sleep(900);
                // Gossip frame / confirmation popup positions on this 1920x1080, UI scale 1 client.
                Log("选择复活对话（鼠标点选项 + /click 兜底）...");
                LeftClick(new Point(client.X + 76, client.Y + 347)); // 「让我复活吧」
                Thread.Sleep(700);
                Chat(backend, "/click GossipFrameGreetingButton1");
                Thread.Sleep(1200);
                Log("接受复活确认（本客户端需要连续两次接受）...");
                LeftClick(new Point(client.X + 876, client.Y + 344)); // 「接受」
                Thread.Sleep(700);
                LeftClick(new Point(client.X + 876, client.Y + 344));
                Thread.Sleep(900);
                Chat(backend, "/click StaticPopup1Button1");
                Thread.Sleep(500);
                Chat(backend, "/click StaticPopup1Button1");
                Thread.Sleep(2200);
            }
            // passive tail: the operator may still be reviving the character manually
            for (int wait = 0; wait < 60 && !HasKey(); wait++)
            {
                Thread.Sleep(5000);
                FarmObservation w = Snapshot();
                if (!w.PlayerDead && !w.PlayerGhost && w.Fresh && w.KnownCore) { Log("已复活。"); return true; }
            }
            Log("自动复活未成功，等待超时。");
            return false;
        }

        // Operator-verified graveyard spirit-healer points, normalized zone coordinates.
        // Loch Modan (MapId 1432) Thelsamar graveyard, cross-checked from the live minimap
        // against the panel position on 2026-11-14 (minimap y was visually misread as 65.7;
        // probe ground truth is 0.457).
        static bool GraveyardPoint(uint mapId, out double gx, out double gy)
        {
            gx = 0.3452; gy = 0.4568; // Loch Modan (MapId 1432) Thelsamar graveyard, see above.
            return mapId == 1432;
        }

        // Walk toward a known world point while watching the healer range bit on every step.
        // FaceTo re-steers every few steps; when the character stops moving (wall/chat box)
        // a side-step maneuver slides around the obstacle instead of pushing into it.
        static void WalkTowardsHealer(WindowsInputBackend backend, BattleProfile profile, double tx, double ty, int maxSteps)
        {
            double lastX = Double.NaN, lastY = Double.NaN;
            int stuck = 0, slides = 0;
            for (int step = 0; step < maxSteps && !HasKey(); step++)
            {
                FarmObservation o = Snapshot();
                if (o.RangeKnown && o.TargetInRange)
                {
                    Log("步行接近途中：医者进入互动范围（步 " + (step + 1) + "）。");
                    return;
                }
                if (!o.Fresh || !NavigationMathF(o)) { Thread.Sleep(150); continue; }
                double dist = NavigationMath.Distance(tx - o.X, ty - o.Y, profile.Calibration.AxisScale);
                if (dist <= 0.0022)
                {
                    Log("已抵达墓地点附近，医者仍在互动范围外；改用右键网格扫描...");
                    bool selected = ClickSweep();
                    if (!selected) Log("右键扫描未选中目标，转回旋转搜索。");
                    return;
                }
                if (step % 3 == 0) FaceTo(backend, profile, tx, ty);
                Pulse(backend, profile.Movement.Forward, 250);
                Thread.Sleep(150);
                if (o.KnownCore && !Double.IsNaN(o.X))
                {
                    double moved = Double.IsNaN(lastX) ? 1.0 : Math.Sqrt((o.X - lastX) * (o.X - lastX) + (o.Y - lastY) * (o.Y - lastY));
                    lastX = o.X; lastY = o.Y;
                    if (moved < 0.0004)
                    {
                        stuck++;
                        if (stuck >= 3 && slides < 8)
                        {
                            stuck = 0; slides++;
                            Log("接近途中被挡（步 " + (step + 1) + "），侧移绕行 #" + slides + "。");
                            double turn = (slides % 2 == 1 ? 1 : -1) * Math.PI / 3;
                            Pulse(backend, turn > 0 ? profile.Movement.TurnRight : profile.Movement.TurnLeft,
                                Math.Max(40, (int)(Math.Abs(turn) / Math.Max(0.1, profile.Calibration.TurnRadiansPerSecond) * 1000)));
                            Thread.Sleep(300);
                            for (int slide = 0; slide < 3; slide++)
                            {
                                Pulse(backend, profile.Movement.Forward, 250);
                                Thread.Sleep(120);
                            }
                            FaceTo(backend, profile, tx, ty);
                            lastX = Double.NaN; lastY = Double.NaN;
                        }
                    }
                    else stuck = 0;
                }
            }
            Log("步行接近步数用尽。");
        }

        // Radial healer search for the ghost form. The ghost's own position and the healer
        // range bit are readable (verified 2026-11-14 10:18: KnownCore=True, RangeKnown=True,
        // TargetInRange=False while target coordinates stay secret), so walk short spokes in
        // 8 directions and watch TargetInRange; return to the anchor after every miss so the
        // search cannot drift (the earlier blind walk carried the ghost 100+ yards away).
        // Rings at roughly 8/15/25 yards cover the graveyard spawn distance.
        static void SeekHealer(WindowsInputBackend backend, BattleProfile profile, FarmObservation start)
        {
            double ax = start.X, ay = start.Y;
            Log(String.Format("径向搜索灵魂医者：锚点 ({0:F4},{1:F4})...", ax, ay));
            int[] pulsesPerRing = { 4, 8, 14 };
            double lastX = ax, lastY = ay;
            int stuck = 0, resets = 0;
            for (int ring = 0; ring < pulsesPerRing.Length; ring++)
            {
                for (int dir = 0; dir < 8; dir++)
                {
                    if (HasKey()) return;
                    if (dir > 0)
                    {
                        Pulse(backend, profile.Movement.TurnRight,
                            Math.Max(40, (int)(Math.PI / 4 / Math.Max(0.1, profile.Calibration.TurnRadiansPerSecond) * 1000)));
                        Thread.Sleep(400);
                    }
                    for (int step = 0; step < pulsesPerRing[ring]; step++)
                    {
                        Pulse(backend, profile.Movement.Forward, 250);
                        Thread.Sleep(150);
                        // Check after EVERY pulse: a spoke passing near the healer must stop
                        // mid-walk, not only at its far end (the range bit flips back out of
                        // range once the ghost walks past the healer).
                        FarmObservation mid = Snapshot();
                        if (mid.RangeKnown && mid.TargetInRange)
                        {
                            Log("径向搜索命中：医者进入互动范围（环 " + (ring + 1) + "，方向 " + (dir + 1) + "/8，步 " + (step + 1) + "）。");
                            return;
                        }
                        if (mid.TargetPositionKnown && !Double.IsNaN(mid.TargetX) && !Double.IsNaN(mid.TargetY))
                        {
                            Log("径向搜索中目标坐标可读，直接走近。");
                            WalkTo(backend, profile, mid.TargetX, mid.TargetY, 0.0022);
                            return;
                        }
                        // Live 2026-11-14 10:43: a failed chat submit leaves the chat edit box
                        // OPEN, and every forward pulse then types "w" into it instead of
                        // moving — 24 spokes "walked" with zero displacement. The ghost's
                        // position is readable, so detect stuck walking and self-heal: ENTER
                        // submits/closes a stuck chat box (the common case), ESC closes a game
                        // menu when ENTER did not help.
                        if (mid.KnownCore && !Double.IsNaN(mid.X) && !Double.IsNaN(mid.Y))
                        {
                            double moved = Math.Sqrt((mid.X - lastX) * (mid.X - lastX) + (mid.Y - lastY) * (mid.Y - lastY));
                            lastX = mid.X; lastY = mid.Y;
                            if (moved < 0.0004) stuck++; else stuck = 0;
                            if (stuck >= 2 && resets < 4)
                            {
                                stuck = 0; resets++;
                                if (resets % 2 == 1)
                                {
                                    Log("连续无位移，疑似聊天框卡住：发送回车复位。");
                                    Pulse(backend, 0x0D, 60);
                                }
                                else
                                {
                                    Log("回车无效，疑似游戏菜单挡住输入：发送 ESC 关闭。");
                                    Pulse(backend, 0x1B, 60);
                                }
                                Thread.Sleep(500);
                                lastX = Double.NaN; lastY = Double.NaN; // re-baseline after reset
                                FarmObservation rebase = Snapshot();
                                if (rebase.KnownCore && !Double.IsNaN(rebase.X)) { lastX = rebase.X; lastY = rebase.Y; }
                            }
                        }
                    }
                    Thread.Sleep(300);
                    FarmObservation w = Snapshot();
                    if (w.RangeKnown && w.TargetInRange)
                    {
                        Log("径向搜索命中：医者进入互动范围（环 " + (ring + 1) + "，方向 " + (dir + 1) + "/8）。");
                        return;
                    }
                    // Bounded drift: walk back to the anchor before trying the next direction.
                    if (w.KnownCore && !Double.IsNaN(w.X) && !Double.IsNaN(w.Y))
                    {
                        double dx = w.X - ax, dy = w.Y - ay;
                        if (Math.Sqrt(dx * dx + dy * dy) > 0.0008)
                            WalkTo(backend, profile, ax, ay, 0.0008);
                    }
                }
            }
            Log("径向搜索 24 条辐条均未进入互动范围。");
        }

        // A ghost cannot read target map positions on Forever clients, so selection is decided
        // by target identity instead: a readable name must name the spirit healer, otherwise any
        // existing non-attackable (friendly) target is accepted as the healer candidate.
        static bool IsHealerTarget(FarmObservation o)
        {
            if (o == null || !o.TargetExists) return false;
            if (!String.IsNullOrEmpty(o.TargetName))
                return o.TargetName.IndexOf("灵魂医者", StringComparison.Ordinal) >= 0;
            return !o.TargetCanAttack;
        }

        // Right-click across the upper-central screen area (the healer model stands left/above
        // the center from a fresh ghost camera). A hit selects the spirit healer with a real
        // hardware event, immune to the client's tainted-call gating. Returns true when the
        // panels then report an existing friendly target (position may remain secret).
        static bool ClickSweep()
        {
            int cx = client.X + client.Width / 2, cy = client.Y + client.Height / 2;
            var offsets = new[] {
                new Point(0, 0), new Point(-170, 0), new Point(170, 0), new Point(0, -130), new Point(0, 130),
                new Point(-340, -40), new Point(-340, -200), new Point(-500, -120), new Point(-180, -220),
                new Point(180, -220), new Point(340, -120), new Point(-170, 130), new Point(170, 130) };
            foreach (var off in offsets)
            {
                try
                {
                    Native.MoveMouse(new Point(cx + off.X, cy + off.Y));
                    Native.SendMouseButton(true, false); Thread.Sleep(60); Native.SendMouseButton(true, true);
                }
                catch { }
                Thread.Sleep(600);
                FarmObservation w = Snapshot();
                Log("右键 (" + off.X + "," + off.Y + "): exists=" + w.TargetExists + " pos=" + w.TargetPositionKnown +
                    (w.TargetPositionKnown ? " (" + w.TargetX.ToString("F4") + "," + w.TargetY.ToString("F4") + ")" : ""));
                if (IsHealerTarget(w))
                {
                    Log("右键命中：已选中目标" + (w.TargetPositionKnown
                        ? "，坐标 (" + w.TargetX.ToString("F4") + "," + w.TargetY.ToString("F4") + ")"
                        : "（坐标保密）") + "。");
                    return true;
                }
            }
            return false;
        }

        static void LeftClick(Point p)
        {
            try
            {
                Native.MoveMouse(p);
                Native.SendMouseButton(false, false); Thread.Sleep(70); Native.SendMouseButton(false, true);
            }
            catch { }
        }

        // Type one chat slash command: ENTER opens the edit box, per-character keys type it, ENTER submits.
        static void Chat(WindowsInputBackend backend, string text)
        {
            try
            {
                Pulse(backend, 0x0D, 50); Thread.Sleep(180); // open the chat edit box
                Native.SendText(text);
                Thread.Sleep(80);
                Pulse(backend, 0x0D, 50); Thread.Sleep(300); // submit
            }
            catch (Exception ex) { Log("聊天输入失败: " + ex.Message); }
        }

        // Turn the character toward a world point using the live calibration mapping.
        static void FaceTo(WindowsInputBackend backend, BattleProfile p, double tx, double ty)
        {
            for (int step = 0; step < 12; step++)
            {
                FarmObservation o = Snapshot();
                if (!o.Fresh || !NavigationMathF(o)) { Thread.Sleep(120); continue; }
                double bearing = Math.Atan2((tx - o.X) * p.Calibration.AxisScale, -(ty - o.Y));
                double facing = NavigationMath.Normalize(NavigationMath.Normalize(p.Calibration.FacingSign * o.Facing) + NavigationMath.Normalize(p.Calibration.FacingOffset));
                double delta = NavigationMath.Normalize(bearing - facing);
                if (Math.Abs(delta) <= 0.10) return;
                int hold = NavigationMath.Pulse((int)(Math.Abs(delta) / p.Calibration.TurnRadiansPerSecond * 1000), 40, 220);
                Pulse(backend, delta > 0 ? p.Movement.TurnRight : p.Movement.TurnLeft, hold);
                Thread.Sleep(90);
            }
        }

        // Bounded blind walk toward a world point (mirrors the engine's approach pulses).
        static void WalkTo(WindowsInputBackend backend, BattleProfile p, double tx, double ty, double stopDist)
        {
            for (int step = 0; step < 60; step++)
            {
                FarmObservation o = Snapshot();
                if (!o.Fresh || !NavigationMathF(o)) { Thread.Sleep(150); continue; }
                double dist = NavigationMath.Distance(tx - o.X, ty - o.Y, p.Calibration.AxisScale);
                if (dist <= stopDist) { Log("到达目标附近（距离 " + dist.ToString("F4") + "）。"); return; }
                FaceTo(backend, p, tx, ty);
                int hold = NavigationMath.Pulse((int)(Math.Min(dist - stopDist, 0.02) / p.Calibration.ForwardUnitsPerSecond * 1000), 100, 350);
                Pulse(backend, p.Movement.Forward, hold);
                Thread.Sleep(130);
            }
            Log("接近目标超时，按当前距离直接尝试互动。");
        }

        static WindowInfo FindWowWindow()
        {
            int own = Process.GetCurrentProcess().Id;
            foreach (var w in Native.Windows())
                if (w.ProcessId != (uint)own &&
                    !w.ProcessName.Equals("WoWProbe", StringComparison.OrdinalIgnoreCase) &&
                    w.ProcessName.StartsWith("wow", StringComparison.OrdinalIgnoreCase)) return w;
            return null;
        }

        static void Activate(IntPtr h)
        {
            ShowWindow(h, 9);
            Native.SendKey(0x12, false); Native.SendKey(0x12, true); // Alt tap unlocks foreground lock
            SetForegroundWindow(h);
        }

        static string Pos(FarmObservation o)
        {
            return o.X >= 0 && o.X <= 1 ? "(" + o.X.ToString("F4") + "," + o.Y.ToString("F4") + ") 朝向 " + o.Facing.ToString("F3") : "未知";
        }

        static int NearestIndex(RouteDocument route, double x, double y)
        {
            int best = 0; double bestD = double.MaxValue;
            for (int i = 0; i < route.Points.Count; i++)
            {
                double d = Math.Sqrt((route.Points[i].X - x) * (route.Points[i].X - x) + (route.Points[i].Y - y) * (route.Points[i].Y - y));
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        // Extend the recorded route line at both ends so sparse mobs are still found.
        static RouteDocument Expanded(RouteDocument baseRoute, int k)
        {
            var r = baseRoute.Clone();
            var first = r.Points[0]; var last = r.Points[r.Points.Count - 1];
            double dx = last.X - first.X, dy = last.Y - first.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9 || k <= 0) return r;
            dx /= len; dy /= len;
            double step = 0.02 * k;
            var head = new Waypoint { X = Clamp01(first.X - dx * step), Y = Clamp01(first.Y - dy * step), Tolerance = 0.004, WaitMilliseconds = 0, Action = "scan" };
            var tail = new Waypoint { X = Clamp01(last.X + dx * step), Y = Clamp01(last.Y + dy * step), Tolerance = 0.004, WaitMilliseconds = 0, Action = "scan" };
            r.Points.Insert(0, head); r.Points.Add(tail);
            return r;
        }
        static double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

        // ---- panel auto-location ----
        static bool LocatePanels(RouteDocument route)
        {
            // WP1 panel anchors near UIParent TOPLEFT (8,-8) with 1 UI unit = 1 physical pixel.
            // The physical cell size depends on the operator's pick, so sweep plausible sizes
            // and offsets, requiring a fully decodable packet that matches the route's map/zone.
            for (int cs = 14; cs >= 5; cs--)
                for (int dy = 0; dy <= 48; dy += 2)
                    for (int dx = 0; dx <= 48; dx += 2)
                    {
                        var c = new Point(client.X + dx + cs / 2, client.Y + dy + cs / 2);
                        try
                        {
                            Rectangle region = Native.CaptureGeometry(c, cs, client);
                            Rectangle wide = new Rectangle(region.X, region.Y, 68 * cs, region.Height);
                            if (!client.Contains(wide)) continue;
                            using (var image = Native.Capture(wide))
                            {
                                var bytes = PixelCodec.Decode(image, cs / 2, cs / 2, cs);
                                ProbePacket p;
                                try { p = ProbePacket.Decode(bytes); } catch { continue; }
                                if (p.InterfaceVersion == 0 || p.MapId != route.MapId || p.MapMode != route.MapMode ||
                                    !String.Equals(p.Zone, route.Zone, StringComparison.Ordinal)) continue;
                                CombatPacket extra = null;
                                try
                                {
                                    var raw = PixelCodec.Decode(image, 34 * cs + cs / 2, cs / 2, cs);
                                    var cand = CombatPacket.Decode(raw);
                                    if (cand.Sequence == p.Sequence) extra = cand;
                                }
                                catch { }
                                if (extra == null) { Log("WP1 在 (" + c.X + "," + c.Y + ") 格子" + cs + " 可解码，但战斗面板未配对，继续搜索。"); continue; }
                                firstCenter = c; cell = cs; located = true;
                                Log("色块定位成功: 客户区 (" + c.X + "," + c.Y + ") 格子 " + cs + "px 地图 " + p.MapId + " 区域 " + p.Zone + " 接口 " + p.InterfaceVersion);
                                return true;
                            }
                        }
                        catch { }
                    }
            return false;
        }

        // ---- observation plumbing (mirrors ProbeForm.ReadTick) ----
        static FarmObservation Snapshot()
        {
            if (!located) return new FarmObservation();
            try
            {
                Rectangle region = Native.CaptureGeometry(firstCenter, cell, client);
                Rectangle wide = new Rectangle(region.X, region.Y, 68 * cell, region.Height);
                if (!client.Contains(wide)) wide = region;
                long moment = clock.ElapsedMilliseconds;
                using (var image = Native.Capture(wide))
                {
                    var bytes = PixelCodec.Decode(image, cell / 2, cell / 2, cell);
                    var parsed = ProbePacket.Decode(bytes);
                    CombatPacket extra = null;
                    if (image.Width >= 68 * cell)
                    {
                        try
                        {
                            var raw = PixelCodec.Decode(image, 34 * cell + cell / 2, cell / 2, cell);
                            var cand = CombatPacket.Decode(raw);
                            if (cand.Sequence == parsed.Sequence) extra = cand;
                        }
                        catch (FormatException) { } catch (ArgumentException) { }
                    }
                    heartbeat.Observe(parsed, moment);
                    packet = parsed; combatPacket = extra; capturedAt = moment;
                }
            }
            catch (Exception)
            {
                heartbeat.Invalidate(); packet = null; combatPacket = null; capturedAt = -1;
            }
            lock (gate)
            {
                bool fresh = located && capturedAt >= 0 && clock.ElapsedMilliseconds >= capturedAt &&
                    unchecked((ulong)(clock.ElapsedMilliseconds - capturedAt)) < 900UL && heartbeat.IsFresh(clock.ElapsedMilliseconds);
                return FarmObservation.FromPackets(packet, combatPacket, fresh);
            }
        }

        // ---- calibration measurement ----
        static bool MeasureCalibration(BattleProfile profile, RouteDocument route)
        {
            // Warm up: freshness requires two observations with an advancing sequence.
            for (int i = 0; i < 12; i++) { Snapshot(); Thread.Sleep(150); }
            FarmObservation o = Snapshot();
            if (!o.Fresh || !o.KnownCore) { Log("标定前需要新鲜数据。当前 Fresh=" + o.Fresh); return false; }
            if (o.InCombat) { Log("标定时不能处于战斗中，请先脱战。"); return false; }
            if (o.X < 0 || o.X > 1) { Log("坐标不可读，无法标定。"); return false; }
            var backend = new WindowsInputBackend(target, null);
            var samples = new List<double[]>(); // facing, worldAngle
            double turnRate = 0;
            double forwardUnits = -1;
            double traveled = 0; long forwardMs = 0;
            for (int i = 0; i < 6; i++)
            {
                o = Snapshot(); if (!o.Fresh || !NavigationMathF(o)) return false;
                if (o.InCombat) { Log("标定中进入战斗，中止本次尝试。"); return false; }
                double f0 = o.Facing;
                Pulse(backend, profile.Movement.TurnRight, 250); Thread.Sleep(120);
                o = Snapshot(); if (!o.Fresh || !NavigationMathF(o)) return false;
                double f1 = o.Facing;
                double turnDelta = Wrap(f1 - f0); // signed radians reported by addon for a right-turn pulse
                if (i == 0) turnRate = Math.Abs(turnDelta) / 0.25;
                else turnRate = turnRate * 0.5 + 0.5 * (Math.Abs(turnDelta) / 0.25);
                // forward displacement measurement
                double x0 = o.X, y0 = o.Y;
                Pulse(backend, profile.Movement.Forward, 250); forwardMs += 250; Thread.Sleep(120);
                o = Snapshot(); if (!o.Fresh || !NavigationMathF(o)) return false;
                double dx = o.X - x0, dy = o.Y - y0;
                traveled += Math.Sqrt(dx * dx + dy * dy);
                if (Math.Abs(dx) + Math.Abs(dy) > 1e-6)
                {
                    double world = Math.Atan2(dx, -dy); // map-frame angle where 0 = north(-y), matches Navigator bearing
                    double fAfter = o.Facing;
                    samples.Add(new[] { fAfter, world });
                }
            }
            if (samples.Count < 2) { Log("位移样本不足，无法求解朝向映射。"); return false; }
            if (forwardMs > 0 && traveled > 1e-6) forwardUnits = traveled / (forwardMs / 1000.0);
            // fit world = s*facing + offset (mod 2pi); choose s with smaller circular residual
            double bestRes = double.MaxValue, bestOffset = 0; int bestSign = 1;
            foreach (int s in new[] { 1, -1 })
            {
                double[] deltas = new double[samples.Count];
                for (int i = 0; i < samples.Count; i++) deltas[i] = Wrap(samples[i][1] - s * samples[i][0]);
                double cx = 0, sx = 0;
                foreach (double d in deltas) { cx += Math.Cos(d); sx += Math.Sin(d); }
                double off = Math.Atan2(sx, cx);
                double res = 0;
                foreach (double d in deltas) res += Math.Pow(Wrap(d - off), 2);
                if (res < bestRes) { bestRes = res; bestOffset = off; bestSign = s; }
            }
            if (bestRes / samples.Count > 0.35) { Log("朝向拟合残差过大: " + (bestRes / samples.Count).ToString("F3")); return false; }
            profile.Calibration.FacingSign = bestSign;
            profile.Calibration.FacingOffset = Normalize(bestOffset);
            profile.Calibration.TurnRadiansPerSecond = Math.Max(0.1, Math.Min(10, turnRate));
            profile.Calibration.ForwardUnitsPerSecond = forwardUnits > 0 ? Math.Max(0.0001, Math.Min(0.2, forwardUnits)) : 0.01;
            Log(String.Format("标定完成: FacingSign={0} FacingOffset={1:F3} 转速={2:F3} rad/s 移速={3:F5} units/s (样本 {4})",
                bestSign, profile.Calibration.FacingOffset, profile.Calibration.TurnRadiansPerSecond, profile.Calibration.ForwardUnitsPerSecond, samples.Count));
            return true;
        }

        static bool NavigationMathF(FarmObservation o)
        {
            return !Double.IsNaN(o.X) && !Double.IsNaN(o.Y) && !Double.IsNaN(o.Facing) && o.X >= 0 && o.X <= 1 && o.Y >= 0 && o.Y <= 1;
        }

        static void Pulse(WindowsInputBackend backend, ushort key, int ms)
        {
            backend.KeyDown(key); Thread.Sleep(ms); backend.KeyUp(key);
        }

        static double Wrap(double a)
        {
            a %= 2 * Math.PI; if (a >= Math.PI) a -= 2 * Math.PI; if (a < -Math.PI) a += 2 * Math.PI; return a;
        }
        static double Normalize(double a)
        {
            a %= 2 * Math.PI; if (a < 0) a += 2 * Math.PI; return a;
        }

        static void MaybeShot()
        {
            long now = clock.ElapsedMilliseconds;
            if (now - lastShot < 10000) return; lastShot = now;
            try
            {
                Rectangle region = new Rectangle(client.X, client.Y, Math.Min(client.Width, 1600), Math.Min(client.Height, 900));
                using (var img = Native.Capture(region))
                    img.Save(Path.Combine(Path.GetDirectoryName(logPath), "grind-latest.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            catch { }
        }

        static void Log(string text)
        {
            string line = DateTimeOffset.Now.ToString("HH:mm:ss.fff") + " " + text;
            Console.WriteLine(line);
            if (logFile != null) logFile.WriteLine(line);
        }

        static bool HasKey()
        {
            try { return Console.KeyAvailable; } catch { return false; }
        }
    }
}
