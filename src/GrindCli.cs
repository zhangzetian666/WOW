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
        static GrindInputGuard input;
        static FarmSession activeSession;

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
            int cycles = 999;
            if (args.Length > 3 && (!Int32.TryParse(args[3], out cycles) || cycles < 1 || cycles > 10000))
            { Console.WriteLine("cycles must be between 1 and 10000."); return 2; }
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
                if (activeSession != null && activeSession.IsActive) activeSession.Stop("CLI exit; release owned input.");
                activeSession = null; input = null;
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
                Log("技能按键方案：开场一次=" + (baseProfile.SkillOpeningSequence.Length == 0 ? "（无）" : baseProfile.SkillOpeningSequence) +
                    "，之后循环=" + baseProfile.SkillSequence + "，尝试间隔=" + baseProfile.SkillIntervalMs + "ms。发送按键不代表技能已成功施放。");
                if ((baseProfile.SkillOpeningSequence + baseProfile.SkillSequence).Contains("1"))
                    Log("盲打模式会跳过1号普攻开关；普通攻击由/startattack维持，职业技能按配置的2至4键尝试。");
            }
            catch (Exception ex) { Log("配置加载失败: " + ex.Message); return 1; }

            if (!LocatePanels(route)) { Log("未能自动定位插件色块，请确认游戏窗口化和插件已加载。"); return 1; }

            // Establish sequence freshness before auxiliary input. The emergency corner
            // remains an operator stop signal; never move the cursor out of it automatically.
            for (int i = 0; i < 12; i++) { Snapshot(); Pause(150); }
            var safety = new InputFailureLatch();
            var backend = new WindowsInputBackend(target, m => Log(m));
            input = new GrindInputGuard(backend, Snapshot, () => clock.ElapsedMilliseconds,
                ms => Thread.Sleep(ms), safety, route.MapId, route.MapMode, route.Zone);
            input.Ensure();

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
                if (attempt > 1) { Log("标定重试第 " + attempt + " 次（等待角色静止）..."); Pause(3000); }
                calibrated = MeasureCalibration(profile, route);
            }
            if (!calibrated) { Log("自动标定连续失败。"); return 1; }

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
                activeSession = session;
                try { session.Start(work, profile, false, false, startIndex); }
                catch (Exception ex) { Log("启动失败: " + ex.Message); return 1; }
                bool capsLogged = false;
                int capsPoll = 0;
                uint attackToken = 0;
                long lastAutoAttackAt = -100000;
                int lastObservedTargetHealth = -1;
                while (session.IsActive)
                {
                    Pause(40);
                    session.Tick();
                    if (!session.IsActive) break;
                    input.Ensure();
                    MaybeShot();
                    // Sample once per second: capability bits, real target-damage evidence and
                    // auto-attack. Without a target, FromPackets leaves range/attackability bits
                    // at false defaults, so a no-target probe cannot tell "secret" from "out of range".
                    if ((++capsPoll % 25) == 0 && !session.IsHolding && session.Status != "Countdown")
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
                            if (GrindRecoveryPolicy.CanAttack(cap) && cap.TargetToken != attackToken)
                            {
                                attackToken = cap.TargetToken;
                                // Learn the mob we actually met: TAB found only ~1 target per
                                // thousand presses on this client, while /target <name> worked
                                // reliably during the revival flow. Reuse that name for sparse scans.
                                if (!String.IsNullOrEmpty(cap.TargetName) && cap.TargetName != "灵魂医者")
                                    namedQuarry = cap.TargetName;
                                Log("锁定目标 " + cap.TargetName + "，发送 /startattack 开启自动近战攻击。");
                                ChatAttack(backend, cap.TargetToken);
                                lastAutoAttackAt = clock.ElapsedMilliseconds;
                            }
                            else if (GrindRecoveryPolicy.CanAttack(cap) && clock.ElapsedMilliseconds - lastAutoAttackAt >= 12000)
                            {
                                // Slot 1 is the Attack *toggle* so we never press it now, but a
                                // dropped target or a movement state change can still clear
                                // auto-attack. /startattack is idempotent: refreshing is safe.
                                lastAutoAttackAt = clock.ElapsedMilliseconds;
                                Log("刷新自动近战攻击 (/startattack)。");
                                ChatAttack(backend, cap.TargetToken);
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
                        if (key == ConsoleKey.Enter) { session.Stop("CLI stopped by operator."); break; }
                    }
                }
                Log("本轮结束: " + session.Status + " / " + session.EngineState + " · " + session.LastReason);
                if (safety.Faulted) { Log("输入安全闩触发，停止。"); break; }
                FarmObservation end = Snapshot();
                if (GrindRecoveryPolicy.ShouldRevive(session.Status, session.LastReason, end))
                {
                    if (!WaitForRevival("本轮结束", profile)) { Log("自动复活失败，全部停止。"); break; }
                }
                else if (session.Status != "Completed") { Log("本轮已停止，不自动重新布防。"); break; }
                if (session.Status == "Completed") { completed++; if (completed >= cycles) break; }
                Pause(1500);
            }
            Log("打怪循环退出。完成整轮 " + completed + " 次。");
            return completed >= cycles ? 0 : 1;
        }

        // ---- automatic revival (spirit healer flow) ----
        // Corpse state: ENTER activates the death dialog's default button (release spirit);
        // Ghost state requires the spirit healer's verified identity and readable target position.
        // Approach must prove arrival before the recorded interaction layout is used.
        // The addon's /wowprobe rez command is read-only diagnostics and never selects a target.
        static bool WaitForRevival(string context, BattleProfile profile)
        {
            FarmObservation start = Snapshot();
            if (!start.PlayerDead && !start.PlayerGhost) return true;
            Log(context + "时角色处于死亡/幽魂状态，启动自动复活流程...");
            var backend = new WindowsInputBackend(target, m => Log(m));
            for (int attempt = 1; attempt <= 12 && !HasKey(); attempt++)
            {
                FarmObservation w = Snapshot();
                if (!w.PlayerDead && !w.PlayerGhost && w.Fresh && w.KnownCore) { Log("已复活。"); return true; }
                if (w.PlayerDead && !w.PlayerGhost)
                {
                    Log("尝试释放灵魂（回车触发死亡弹窗默认按钮，/click 兜底）...");
                    Pulse(backend, 0x0D, 60); Pause(1500);
                    Chat(backend, "/click StaticPopup1Button1");
                    Pause(2200);
                    continue;
                }
                // Select the healer by name, then verify full identity and readable coordinates.
                // Missing position never authorizes a screen sweep or guessed graveyard movement.
                w = Snapshot();
                Log("幽魂目标状态: exists=" + w.TargetExists + " name=" + w.TargetName + " pos=" + w.TargetPositionKnown +
                    (w.TargetPositionKnown ? " (" + w.TargetX.ToString("F4") + "," + w.TargetY.ToString("F4") + ")" : "") +
                    "（该客户端幽魂形态下目标坐标通常保密）");
                // A friendly target alone cannot authorize healer interaction. Secret coordinates
                // require manual revival; skill range is not evidence of interaction distance.
                bool selected = IsHealerTarget(w);
                if (!selected)
                {
                    Log("幽魂状态：按名字选中灵魂医者（/target 灵魂医者）...");
                    Chat(backend, "/target 灵魂医者");
                    Pause(900);
                    w = Snapshot();
                    Log("按名字后目标状态: exists=" + w.TargetExists + " name=" + w.TargetName + " pos=" + w.TargetPositionKnown);
                    selected = IsHealerTarget(w);
                }
                if (!selected)
                {
                    Log("未能选中任何目标，本轮放弃。");
                    Pause(1800);
                    continue;
                }
                FarmObservation healer = Snapshot();
                bool positioned = GrindRecoveryPolicy.CanApproachHealer(healer);
                if (positioned)
                {
                    double stop = attempt >= 5 ? 0.0012 : attempt >= 3 ? 0.0018 : 0.0025;
                    Log(String.Format("灵魂医者坐标 ({0:F4},{1:F4})，走近至 {2}...", healer.TargetX, healer.TargetY, stop));
                    if (!WalkTo(backend, profile, healer.TargetToken, healer.TargetX, healer.TargetY, stop))
                    { Log("Healer arrival was not verified. Stop automatic revival and revive manually."); return false; }
                }
                else
                {
                    Log("Healer position is unavailable. Skill range cannot prove interaction distance. Stop automatic revival and revive manually.");
                    return false;
                }
                var arrivedHealer = Snapshot();
                if (!GrindRecoveryPolicy.CanApproachHealer(arrivedHealer) || arrivedHealer.TargetToken != healer.TargetToken ||
                    arrivedHealer.TargetX != healer.TargetX || arrivedHealer.TargetY != healer.TargetY || client.Width != 1920 || client.Height != 1080)
                { Log("Healer identity or the recorded 1920x1080 interaction layout is unavailable."); return false; }

                Log("与灵魂医者互动（F 键 + 医者模型右键）...");
                Pulse(backend, 0x46, 60); Pause(500);
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
                    Click(new Point(client.X + client.Width / 2 + hit.X, client.Y + client.Height / 2 + hit.Y), true);
                    Log("右键医者模型，中心偏移 (" + hit.X + "," + hit.Y + ")。");
                }
                catch { throw; }
                Pause(900);
                // Gossip frame / confirmation popup positions on this 1920x1080, UI scale 1 client.
                Log("选择复活对话（鼠标点选项 + /click 兜底）...");
                LeftClick(new Point(client.X + 76, client.Y + 347)); // 「让我复活吧」
                Pause(700);
                Chat(backend, "/click GossipFrameGreetingButton1");
                Pause(1200);
                Log("接受复活确认（本客户端需要连续两次接受）...");
                LeftClick(new Point(client.X + 876, client.Y + 344)); // 「接受」
                Pause(700);
                LeftClick(new Point(client.X + 876, client.Y + 344));
                Pause(900);
                Chat(backend, "/click StaticPopup1Button1");
                Pause(500);
                Chat(backend, "/click StaticPopup1Button1");
                Pause(2200);
            }
            // passive tail: the operator may still be reviving the character manually
            for (int wait = 0; wait < 60 && !HasKey(); wait++)
            {
                Pause(5000);
                FarmObservation w = Snapshot();
                if (!w.PlayerDead && !w.PlayerGhost && w.Fresh && w.KnownCore) { Log("已复活。"); return true; }
            }
            Log("自动复活未成功，等待超时。");
            return false;
        }

        static bool IsHealerTarget(FarmObservation o)
        { return GrindRecoveryPolicy.IsHealer(o); }

        static void LeftClick(Point p)
        {
            Click(p, false);
        }
        static void Click(Point p, bool right)
        { RequireInput().Click(p, right, point => Native.ClientBounds(target) == client && client.Contains(point), Native.MoveMouse, Native.SendMouseButton); }
        static GrindInputGuard RequireInput()
        { if (input == null) throw new InvalidOperationException("CLI input is not armed."); return input; }

        // Type one chat slash command: ENTER opens the edit box, per-character keys type it, ENTER submits.
        static void ChatAttack(WindowsInputBackend backend, uint token)
        { ChatChecked(backend, "/startattack", () => { var o=Snapshot(); return GrindRecoveryPolicy.CanAttack(o)&&o.TargetToken==token; }); }
        static void Chat(WindowsInputBackend backend, string text)
        { ChatChecked(backend, text, null); }
        static void CheckChatContext(Func<bool> context)
        { RequireInput().Ensure(); if(context!=null&&!context())throw new InvalidOperationException("CLI chat target context changed."); }
        static void ChatChecked(WindowsInputBackend backend, string text, Func<bool> context)
        {
            try
            {
                CheckChatContext(context);
                Pulse(backend, 0x0D, 50); Pause(180); // open the chat edit box
                RequireInput().TypeText(text, ch => { CheckChatContext(context); Native.SendChar(ch); });
                Pause(80);
                CheckChatContext(context);
                Pulse(backend, 0x0D, 50); Pause(300); // submit
            }
            catch (Exception ex) { Log("聊天输入失败: " + ex.Message); throw; }
        }

        // Turn the character toward a world point using the live calibration mapping.
        static void FaceTo(WindowsInputBackend backend, BattleProfile p, double tx, double ty)
        {
            for (int step = 0; step < 12; step++)
            {
                FarmObservation o = Snapshot();
                if (!o.Fresh || !NavigationMathF(o)) { Pause(120); continue; }
                double bearing = Math.Atan2((tx - o.X) * p.Calibration.AxisScale, -(ty - o.Y));
                double facing = NavigationMath.Normalize(NavigationMath.Normalize(p.Calibration.FacingSign * o.Facing) + NavigationMath.Normalize(p.Calibration.FacingOffset));
                double delta = NavigationMath.Normalize(bearing - facing);
                if (Math.Abs(delta) <= 0.10) return;
                int hold = NavigationMath.Pulse((int)(Math.Abs(delta) / p.Calibration.TurnRadiansPerSecond * 1000), 40, 220);
                Pulse(backend, delta > 0 ? p.Movement.TurnRight : p.Movement.TurnLeft, hold);
                Pause(90);
            }
        }

        // The pure approach planner preserves the same healer context and reports actual arrival.
        static bool WalkTo(WindowsInputBackend backend, BattleProfile p, uint token, double tx, double ty, double stopDist)
        {
            var approach = new GrindHealerApproach(Snapshot, (key, ms) => Pulse(backend, key, ms), Pause);
            bool arrived = approach.Run(p, token, tx, ty, stopDist, 60);
            Log(approach.LastReason);
            return arrived;
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
            for (int i = 0; i < 12; i++) { Snapshot(); Pause(150); }
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
                Pulse(backend, profile.Movement.TurnRight, 250); Pause(120);
                o = Snapshot(); if (!o.Fresh || !NavigationMathF(o)) return false;
                double f1 = o.Facing;
                double turnDelta = Wrap(f1 - f0); // signed radians reported by addon for a right-turn pulse
                if (i == 0) turnRate = Math.Abs(turnDelta) / 0.25;
                else turnRate = turnRate * 0.5 + 0.5 * (Math.Abs(turnDelta) / 0.25);
                // forward displacement measurement
                double x0 = o.X, y0 = o.Y;
                Pulse(backend, profile.Movement.Forward, 250); forwardMs += 250; Pause(120);
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

        static void Pause(int milliseconds)
        { if (input == null) Thread.Sleep(milliseconds); else input.Wait(milliseconds); }

        static void Pulse(WindowsInputBackend backend, ushort key, int ms)
        {
            RequireInput().Pulse(key, ms);
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
