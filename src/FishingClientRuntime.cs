using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
namespace WoWProbe
{
    public sealed class FishingClientRuntime:IDisposable
    {
        readonly FishingClientSettings settings;readonly Action<string> log;readonly Stopwatch clock=Stopwatch.StartNew();readonly string directory;
        readonly object cacheGate=new object();FishingObservation cached;volatile bool stopping;Thread worker;
        readonly FishingFocusRecovery focusRecovery=new FishingFocusRecovery();long lastLootNotice=-10000;
        readonly FishingAuxiliaryCycle auxiliary=new FishingAuxiliaryCycle();FishingPeriodicKeyClock periodicKey;
        readonly FishingStayClock stay=new FishingStayClock();readonly InputFailureLatch fishingSafety=new InputFailureLatch();bool focusOnStart=true;long focusResumeAt;
        QuickRecovery recovery;FishingDeathCheckpoint death;string DeathPath{get{return Path.Combine(directory,"fishing-death-checkpoint.json");}}
        FishingNativeHost host;FishingSession session;FishingSoundDetector sound;FishingSplashDetector splash;WindowsFishingAudioCapture audio;
        FishingActivityClock activity;FishingSpotPicker picker;FishingPoint point;FishingPhrases phrases;readonly FishingWhisperPolicy replies=new FishingWhisperPolicy();
        Navigator travel;QuickVendorEngine vendor;FarmObservation forwardAnchor;int forwardHold;double measuredTravelSpeed=.04;long arrived=-1,castStarted=-1,nextFrame,lastAudible=-100000,lastNotice=-100000;int countedCasts,countedReels,countedCatches,lastSpokenLap;
        string lastLog="";int seenSessionCasts,seenSessionReels,seenSessionCatches;bool bagSuppressed;long lastSignalReport=-1,lastSignalNotice=-10000;
        public volatile bool Running;public string Status="未开始";public int Casts{get{return countedCasts;}}public int Reels{get{return countedReels;}}public int Catches{get{return countedCatches;}}public int VendorVisits{get;private set;}public int Laps{get{return picker==null?0:picker.CompletedLaps;}}
        public FishingClientRuntime(FishingClientSettings settings,string directory,Action<string> log){this.settings=settings.Clone();this.directory=directory;this.log=log;}
        public bool WorkerAlive{get{return worker!=null&&worker.IsAlive;}}public volatile bool Retryable;
        public void Start(bool focus=true){if(worker!=null)throw new InvalidOperationException("钓鱼已启动。");settings.Validate(true);focusOnStart=focus;worker=new Thread(Run){IsBackground=true,Name="Independent fishing client"};worker.SetApartmentState(ApartmentState.MTA);Running=true;worker.Start();}
        public void Stop(){stopping=true;Retryable=false;if(session!=null)session.Stop("用户停止钓鱼。");}
        public void Dispose(){Stop();if(worker!=null&&worker!=Thread.CurrentThread)worker.Join(5000);}
        void Report(string text){Status=text;if(text!=lastLog||clock.ElapsedMilliseconds-lastNotice>30000){lastLog=text;lastNotice=clock.ElapsedMilliseconds;log(text);}}
        void Run()
        {
            bool owner=false;Mutex exclusive=null;
            try{
                exclusive=new Mutex(true,"WoWProbe-QuickCombat",out owner);if(!owner)throw new InvalidOperationException("请先关闭自动打怪客户端，避免同时控制游戏。");
                host=new FishingNativeHost(settings.Fishing.Calibration,()=>stopping,()=>clock.ElapsedMilliseconds,Report);if(focusOnStart)host.Focus();else if(!host.Foreground)throw new InvalidOperationException("自动重试等待游戏回到前台，不切换其他窗口。");
                var first=host.Read();ValidateWorld(first);activity=new FishingActivityClock(settings,clock.ElapsedMilliseconds);periodicKey=new FishingPeriodicKeyClock(settings,clock.ElapsedMilliseconds);picker=new FishingSpotPicker(settings);point=picker.Start(first.Farm);stay.Select(settings.StayInterval());
                splash=new FishingSplashDetector(settings.SplashSensitivity);if(settings.ChatAtLap)phrases=new FishingPhrases(Path.Combine(directory,"assets","fishing","chat-phrases.txt"));
                if(settings.UseAudio){sound=new FishingSoundDetector(Path.Combine(directory,"assets","fishing"));audio=new WindowsFishingAudioCapture((samples,rate,end,broken)=>{if(broken){sound.Reset();return;}double energy=0;foreach(float v in samples)energy+=v*v;if(samples.Length>0&&energy/samples.Length>.00000001)Interlocked.Exchange(ref lastAudible,end);sound.Push(samples,rate,end);},()=>clock.ElapsedMilliseconds);try{audio.Start();}catch(Exception e){Report("声音监听不可用，使用水花图像："+e.Message);}}
                var social=settings.RequiresSocialFeedback()?host.Social(first):new FishingSocialObservation();if(settings.RequiresSocialFeedback()&&!social.Known)throw new InvalidOperationException("自动聊天需要配套插件反馈，请在游戏输入 /reload；定时B和定时技能不需要此扩展反馈。");replies.Discard(social.WhisperCount);
                Report("钓鱼准备完成；F12急停，进入钓点后倒计时开始抛竿。");
                if(settings.TimedKeyEnabled)Report("定时键"+settings.TimedKey+"已启用，本轮约"+((periodicKey.NextDue-clock.ElapsedMilliseconds)/60000.0).ToString("F2")+"分钟后执行；当前杆结束后按键并留足10秒。");
                var checkpoint=Path.Combine(directory,"fishing-vendor-checkpoint.json");
                if(!first.Farm.PlayerDead&&!first.Farm.PlayerGhost){if(File.Exists(DeathPath))File.Delete(DeathPath);if(File.Exists(checkpoint)){var cp=QuickVendorCheckpoint.Load(checkpoint);vendor=new QuickVendorEngine(cp,host.Read());Report("继续未完成的钓鱼售卖路线。");}else BeginTravel(first.Farm,-1);}
                while(!stopping)
                {
                    try{
                    host.CheckCancelled();
                    if(!host.Foreground){SuspendInput("游戏窗口失去前台");host.Idle(200);continue;}
                    if(!host.ModifiersReleased){SuspendInput("检测到Alt/Ctrl/Shift仍按住");host.Idle(200);continue;}
                    var o=host.Read();if(!host.Foreground){SuspendInput("读取画面期间游戏失去前台");continue;}ValidateWorld(o);long now=clock.ElapsedMilliseconds;
                    if(o.Farm.ChatOpen){CancelSession("用户打开了聊天框。");focusRecovery.Suspend();Report("等待用户关闭聊天框。");host.Idle(200);continue;}
                    if(focusRecovery.Waiting){if(now<focusResumeAt||!focusRecovery.ResumeReady(now,host.Foreground,host.ModifiersReleased,!o.Farm.ChatOpen)){host.Idle(100);continue;}
                        if(!o.Farm.PlayerDead&&!o.Farm.PlayerGhost){if(vendor!=null)vendor=new QuickVendorEngine(vendor.Checkpoint,o);else if(travel!=null)BeginTravel(o.Farm,picker.Segment);}
                        Report("游戏已回到前台且按键已松开，自动恢复钓鱼任务。");}
                    if(settings.AutoCorpse&&(recovery!=null||o.Farm.PlayerDead||o.Farm.PlayerGhost)){TickRecovery(o,now,checkpoint);host.Idle(60);continue;}
                    if(auxiliary.Active){TickAuxiliary(o,now);host.Idle(50);continue;}
                        if(activity.LogoutDue(now)&&FishingCycleBoundary.IsIdle(session==null?null:session.EngineStatus,session!=null&&session.IsHolding)&&!o.Farm.Casting&&!o.Farm.Channeling)
                        {
                            CancelSession("本轮在线时长达到随机下线点。");activity.LogoutRequested();Report("已结束当前杆，开始登出。");
                            var vendorProgress=vendor==null?null:vendor.Checkpoint;
                            new FishingLoginRunner(host,activity,()=>clock.ElapsedMilliseconds,()=>stopping,Report).Run();if(stopping)break;
                            GrindCli.OpenTelemetry();var online=host.Read();ValidateWorld(online);if(vendorProgress!=null){vendor=new QuickVendorEngine(vendorProgress,online);Report("重新上线，继续未完成的商人路线。");}else BeginTravel(online.Farm,picker.Segment);continue;
                        }
                    if(vendor!=null){TickVendor(o,now,checkpoint);host.Idle(50);continue;}
                    if(!o.Farm.BagFull&&o.FreeSlotsKnown&&o.FreeSlots>0)bagSuppressed=false;
                    if(!bagSuppressed&&(o.Farm.BagFull||o.FreeSlotsKnown&&o.FreeSlots==0))
                    {
                        if(!settings.Service.Vendor.Enabled)throw new InvalidOperationException("背包已满；请录制并启用自动售卖路线。");
                        CancelSession("背包满，前往售卖。");if(o.Farm.Casting||o.Farm.Channeling)host.Pulse(27);host.Idle(200);o=host.Read();
                        vendor=new QuickVendorEngine(settings.Service,o,false,true,false,false,true);QuickVendorCheckpoint.Save(checkpoint,vendor.Checkpoint);Report("背包满，沿录制路线前往NPC。");continue;
                    }
                    if(travel!=null){if(o.Farm.Casting||o.Farm.Channeling)Report("等待当前读条结束再移动到钓点。");else TickTravel(o,now);host.Idle(50);continue;}
                    bool idle=FishingCycleBoundary.IsIdle(session==null?null:session.EngineStatus,session!=null&&session.IsHolding);
                    if(idle&&!o.Farm.Casting&&!o.Farm.Channeling)
                    {
                        var info=settings.RequiresSocialFeedback()?host.Social(o):new FishingSocialObservation();
                        if(settings.WhisperReply&&info.Known){string answer=replies.Pending(info.WhisperCount,info.SenderHash);if(answer!=null){CancelSession("回复新私信。");host.Chat(answer,true,info.SenderHash,info.WhisperCount);replies.Sent(info.WhisperCount,info.SenderHash);continue;}}
                        if(settings.ChatAtLap&&picker.CompletedLaps>lastSpokenLap){CancelSession("全部钓点连线轮换完成，发送圈末交流。");host.Chat(phrases.Next(),false);lastSpokenLap=picker.CompletedLaps;continue;}
                        if(periodicKey.Due(now)){CancelSession("定时技能已到期，当前杆已结束。");auxiliary.Start(FishingAuxiliaryKind.TimedKey,(ushort)(48+settings.TimedKey));Report("准备定时按键"+settings.TimedKey+"，按下后保留10秒施法时间。");continue;}
                        if(activity.BagDue(now)){CancelSession("执行到期背包查看。");auxiliary.Start(FishingAuxiliaryKind.Bag,66,(long)Math.Round(settings.BagOpenSeconds*1000));Report("准备按B查看背包，保持"+settings.BagOpenSeconds.ToString("0.0")+"秒后再按B。");continue;}
                        if(settings.RandomSpots&&arrived>=0&&(stay.Due(now)||settings.ChangeAfterCatch&&seenSessionCatches>0))
                        {CancelSession("本钓点驻留结束，沿连线换点。");int previous=picker.Segment;point=picker.Next();stay.Select(settings.StayInterval());BeginTravel(o.Farm,previous);continue;}
                    }
                    if(session==null)
                    {
                        if(o.Farm.Casting||o.Farm.Channeling){Report("等待当前施法完成，再恢复钓鱼。");host.Idle(100);continue;}
                        if(!AlignToWater(o)){host.Idle(50);continue;}
                        PrepareSession(o,now);
                    }
                    UpdateObservation(o,now);session.Tick();
                    // Release a pressed fishing/facing key on time before doing expensive captures.
                    while(session.IsHolding&&!stopping){host.Idle(5);session.Tick();}
                    CountSession();
                    if(!session.IsActive){string reason=session.LastReason;CancelSession(reason);throw new InvalidOperationException(reason);}
                    var command=session.LastCommand;if(command!=null&&command.Action==FishingAction.None)Report(command.Reason);
                    host.Idle(60);
                    }catch(InvalidOperationException ex){if(stopping||host.InputFaulted||fishingSafety.Faulted)throw;if(!FishingFocusRecovery.Transient(ex.Message)){if(recovery==null&&settings.AutoCorpse&&DeathObserved()){CancelSession("检测到死亡，切入跑尸。");continue;}throw;}SuspendInput(ex.Message.Contains("modifier")||ex.Message.Contains("修饰键")?"输入检查发现Alt/Ctrl/Shift按住":"输入检查发现游戏失去前台");host.Idle(100);}
                }
            }
            catch(OperationCanceledException){Report("钓鱼已停止。");}
            catch(Exception e){Retryable=!stopping&&!fishingSafety.Faulted&&(host==null||!host.InputFaulted)&&e.Message.IndexOf("emergency",StringComparison.OrdinalIgnoreCase)<0;Report("钓鱼暂停："+e.Message);}
            finally{if(session!=null)session.Stop("钓鱼退出，释放按键。");if(fishingSafety.Faulted||stopping)Retryable=false;if(audio!=null)audio.Dispose();if(owner&&exclusive!=null)exclusive.ReleaseMutex();if(exclusive!=null)exclusive.Dispose();Running=false;}
        }
        void SuspendInput(string cause)
        {CancelSession(cause+"，释放本轮按键。");if(!focusRecovery.Waiting)focusResumeAt=clock.ElapsedMilliseconds+(settings.AutoRetry?settings.RetrySeconds*1000L:0);focusRecovery.Suspend();Report("等待恢复："+cause+"；任务仍保留。"+(settings.AutoRetry?"暂停至少"+settings.RetrySeconds+"秒后，":"")+"游戏回到前台且松开修饰键后自动继续；停止/F12才结束任务。");}
        void TickAuxiliary(OperationObservation o,long now)
        {
            bool casting=o.Farm.Casting||o.Farm.Channeling;ushort key=auxiliary.KeyDue(now,casting);
            if(key!=0){host.AuxiliaryPulse(key,()=>{auxiliary.InputSent(clock.ElapsedMilliseconds);Report("定时动作已按"+(key==66?"B":((char)key).ToString())+(auxiliary.Kind==FishingAuxiliaryKind.TimedKey?"，等待至少10秒且读条结束。":"。"));});return;}
            if(!auxiliary.Finished(now,casting))return;
            if(auxiliary.Kind==FishingAuxiliaryKind.Bag){activity.BagClosed(now);Report("B两次切换完成，重新计算下一次背包间隔。");}
            else{periodicKey.Completed(now);Report("定时技能等待完成；下一次约"+((periodicKey.NextDue-now)/60000.0).ToString("F2")+"分钟后执行。");}
            auxiliary.Clear();
        }
        bool AlignToWater(OperationObservation o)
        {
            var f=o.Farm;if(NavigationMath.Distance(f.X-point.X,f.Y-point.Y,settings.Fishing.Calibration.AxisScale)>FishingTravelPolicy.ArrivalTolerance){BeginTravel(f,picker.Segment);return false;}
            var command=FishingArrivalFacing.Next(f,point,settings.Fishing.Calibration,settings.Fishing.Movement);
            if(command.Action==FishingAction.None){Report(command.Reason);return true;}
            Report(command.Reason);host.Pulse(command.Key,command.HoldMilliseconds,x=>!x.Farm.Casting&&!x.Farm.Channeling);return false;
        }
        void ValidateWorld(OperationObservation o)
        {
            var f=o==null?null:o.Farm;var c=settings.Fishing.Calibration;
            if(f==null||!o.Fresh||!f.Fresh||!f.KnownCore||f.ReadFailures!=0)throw new InvalidOperationException("游戏状态暂不可读，请露出游戏左上角状态面板。");
            if(!MapIdentity.Matches(f.MapId,f.MapMode,f.Zone,c.MapId,c.MapMode,c.Zone))throw new InvalidOperationException("当前地图与保存钓点不同。");
            if(settings.AutoCorpse&&(f.PlayerDead||f.PlayerGhost||recovery!=null))return;
            if(f.InteractBindingKnown&&!f.InteractBindingCorrect)throw new InvalidOperationException(GrindInputGuard.InteractBindingError);
            if(f.InCombat||f.PlayerDead||f.PlayerGhost)throw new InvalidOperationException("当前处于战斗、死亡或幽魂状态，请恢复到安全钓点。");
        }
        bool DeathObserved(){try{var o=host.Read();return o.Fresh&&o.Farm.Fresh&&o.Farm.KnownCore&&(o.Farm.PlayerDead||o.Farm.PlayerGhost);}catch{return false;}}
        void TickRecovery(OperationObservation o,long now,string vendorPath)
        {
            if(recovery!=null&&!o.Farm.PlayerDead&&!o.Farm.PlayerGhost&&o.Farm.InCombat){Report("复活后仍在战斗，等待脱战再返回钓点。");return;}
            if(recovery==null){CancelSession("死亡/幽魂：暂停钓鱼并记录死亡点。");auxiliary.Clear();travel=null;vendor=null;var route=settings.CorpseRoute;
                death=File.Exists(DeathPath)?FishingDeathCheckpoint.Load(DeathPath):null;
                if(o.Farm.PlayerDead&&!o.Farm.PlayerGhost||death==null||!death.Matches(route,o.Farm)||o.CorpseKnown&&NavigationMath.Distance(death.X-o.CorpseX,death.Y-o.CorpseY,settings.Fishing.Calibration.AxisScale)>.005)death=FishingDeathCheckpoint.From(route,o);
                death.Save(DeathPath);recovery=new QuickRecovery(route,settings.Fishing.Calibration,death.Death(),true,true,death.EndReached);
                Report("自动跑尸：死亡点("+(death.X*100).ToString("F3")+", "+(death.Y*100).ToString("F3")+")，先沿录制路线到终点，再去死亡点复活。");}
            var command=recovery.Step(o,now);if(recovery.RecordedEndReached&&!death.EndReached){death.EndReached=true;death.Save(DeathPath);Report("已到跑尸路线终点，接下来前往本轮死亡点。");}
            if(command.Action==QuickRecoveryAction.Stop)throw new InvalidOperationException(command.Reason);
            if(command.Action==QuickRecoveryAction.Complete){recovery=null;death=null;if(File.Exists(DeathPath))File.Delete(DeathPath);point=picker.Start(o.Farm);stay.Select(settings.StayInterval());
                if(File.Exists(vendorPath))vendor=new QuickVendorEngine(QuickVendorCheckpoint.Load(vendorPath),o);else BeginTravel(o.Farm,picker.Segment);
                Report("已由连续存活状态确认复活，返回钓点后继续钓鱼。");return;}
            Report(command.Reason);if(command.Action==QuickRecoveryAction.Key||command.Action==QuickRecoveryAction.Click)host.RecoveryInput(command,settings.Fishing.Movement);
        }
        void BeginTravel(FarmObservation from,int previous)
        {
            forwardAnchor=null;var route=picker.Travel(from,point,previous);travel=route.Points.Count<2?null:new Navigator(route,settings.Fishing.Calibration,false,false,1,NavigationPurpose.VendorReturn){ForwardPulseLimit=1500};
            if(travel==null){arrived=clock.ElapsedMilliseconds;stay.Arrive(arrived);}else{arrived=-1;stay.Depart();}
            Report((settings.RandomSpots?"随机钓点：第"+(picker.Segment+1)+"条连线":"固定钓点"+(picker.Segment+1))+"，目标("+(point.X*100).ToString("F2")+", "+(point.Y*100).ToString("F2")+")"+(settings.RandomSpots?"；本点随机驻留"+(stay.DurationMs/60000.0).ToString("F2")+"分钟，到点开始计时。":"；固定驻留。"));
        }
        void TickTravel(OperationObservation o,long now)
        {
            var f=o.Farm;
            if(forwardAnchor!=null&&f.Sequence!=forwardAnchor.Sequence){double moved=NavigationMath.Distance(f.X-forwardAnchor.X,f.Y-forwardAnchor.Y,settings.Fishing.Calibration.AxisScale);if(moved>.0001&&forwardHold>0)measuredTravelSpeed=Math.Max(measuredTravelSpeed,Math.Min(NavigationCalibration.MaximumForwardSpeed,moved*1000/forwardHold*1.15));forwardAnchor=null;}
            var action=travel.Step(new NavigationSample{Fresh=f.Fresh,KnownCore=f.KnownCore,ReadFailures=f.ReadFailures,Sequence=f.Sequence,MapId=f.MapId,MapMode=f.MapMode,Zone=f.Zone,X=f.X,Y=f.Y,Facing=f.Facing,Dead=f.PlayerDead,Ghost=f.PlayerGhost,Combat=f.InCombat},now);
            if(action.Action==NavigationAction.Stop)throw new InvalidOperationException(action.Reason);
            if(action.Action==NavigationAction.Completed){travel=null;arrived=now;stay.Arrive(now);Report("已到达钓点并停稳，恢复面水朝向后按抛竿键。");return;}
            if(action.Action==NavigationAction.None)return;
            var target=travel.WorkingRoute.Points[action.TargetIndex];var cal=settings.Fishing.Calibration;double distance=NavigationMath.Distance(target.X-f.X,target.Y-f.Y,cal.AxisScale);
            int hold=action.HoldMilliseconds;Func<FarmObservation,bool> release=null;
            if(action.Action==NavigationAction.Forward){hold=FishingTravelPolicy.ForwardHold(distance,target.Tolerance,Math.Max(cal.ForwardUnitsPerSecond,measuredTravelSpeed));release=current=>NavigationMath.Distance(target.X-current.X,target.Y-current.Y,cal.AxisScale)<=target.Tolerance||NavigationMath.Distance(target.X-current.X,target.Y-current.Y,cal.AxisScale)>distance+.001;}
            else if(action.Action==NavigationAction.TurnLeft||action.Action==NavigationAction.TurnRight){double bearing=Math.Atan2((target.X-f.X)*cal.AxisScale,-(target.Y-f.Y));double delta=NavigationMath.Normalize(bearing-NavigationMath.Normalize(cal.FacingSign*f.Facing+cal.FacingOffset));hold=FishingTravelPolicy.FacingHold(delta,cal.TurnRadiansPerSecond);}
            Report(String.Format(System.Globalization.CultureInfo.InvariantCulture,"接近钓点：{0} {1}ms，当前位置({2:F3},{3:F3}) → 路点({4:F3},{5:F3})，距离{6:F3}坐标点。",action.Action,hold,f.X*100,f.Y*100,target.X*100,target.Y*100,distance*100));
            if(action.Action==NavigationAction.Forward){forwardAnchor=f;forwardHold=hold;}
            host.Pulse(settings.Fishing.Movement.For(action.Action),hold,x=>!x.Farm.Casting&&!x.Farm.Channeling,release);
        }
        void PrepareSession(OperationObservation o,long now)
        {
            var plan=settings.At(point);plan.Settings.BindingsConfirmed=plan.Settings.AutoLootConfirmed=plan.Settings.AudioConfirmed=true;plan.Calibration.Confirmed=true;
            castStarted=-1;nextFrame=0;lastSignalReport=-1;lastSignalNotice=lastLootNotice=-10000;splash.Reset();if(sound!=null)sound.Reset();seenSessionCasts=seenSessionReels=seenSessionCatches=0;UpdateObservation(o,now);
            session=new FishingSession(host.Backend,()=>clock.ElapsedMilliseconds,()=>{lock(cacheGate)return cached==null?null:cached.Clone();},message=>{if(message.StartsWith("Fishing pulse"))Report(message);},fishingSafety);session.Start(plan);
        }
        void UpdateObservation(OperationObservation o,long now)
        {
            if(settings.UseVisual&&now>=nextFrame){using(var image=host.Capture())splash.Push(image,settings.VisualRegion,now,castStarted,session!=null&&(session.EngineStatus=="WaitBite"||session.EngineStatus=="ReelDelay")&&(o.Farm.Casting||o.Farm.Channeling));nextFrame=now+80;}
            var s=sound==null?null:sound.Snapshot();bool audioReady=audio!=null&&audio.Running&&now-audio.LastPacketMs<1000&&now-Interlocked.Read(ref lastAudible)<5000;
            long bite=audioReady&&s!=null?s.LastBiteAtMs:-1;double confidence=audioReady&&s!=null?s.BiteConfidence:0;
            if(settings.UseVisual&&splash.LastBiteAtMs>bite){bite=splash.LastBiteAtMs;confidence=splash.Confidence;}
            if(session!=null&&session.EngineStatus=="WaitBite"){
                if(bite>=0&&bite!=lastSignalReport){lastSignalReport=bite;Report((settings.UseVisual&&bite==splash.LastBiteAtMs?"视觉鱼讯："+splash.Evidence:"声音鱼讯")+"；进入收竿延迟核验。");}
                else if(now-lastSignalNotice>=5000){lastSignalNotice=now;Report("鱼讯监测：声音"+(audioReady?"有信号":"无有效信号")+"；"+(settings.UseVisual?"图像"+(splash.Ready?"可用":"未就绪")+"，浮标"+(splash.TrackingBobber?"已跟踪":"未锁定，检测局部水花")+String.Format(System.Globalization.CultureInfo.InvariantCulture,"，水花{0:F3}/阈值{1:F3}，最大下沉{2:F1}/阈值{3:F1}，读条{4}",splash.LocalWhiteChange,splash.Threshold,splash.PeakDrop,splash.DropThreshold,o.Farm.Casting||o.Farm.Channeling):"图像未启用"));}
            }
            if(session!=null&&session.EngineStatus=="LootWait"&&now-lastLootNotice>=3000){lastLootNotice=now;Report("收竿核对：读条="+(o.Farm.Casting||o.Farm.Channeling)+"，拾取窗口="+(!o.LootWindowKnown?"未知":o.LootWindowOpen?"打开":"关闭")+"，拾取事件="+o.Farm.LootOpened+"/"+o.Farm.LootClosed+"；本杆结束后按抛竿前随机等待继续。");}
            var observation=new FishingObservation{Game=o,CapturedAtMs=now,AudioReady=audioReady,VisualReady=settings.UseVisual&&splash.Ready,LastBiteAtMs=bite,BiteConfidence=confidence,LastBagVoiceAtMs=s==null?-1:s.LastBagVoiceAtMs,BagVoiceConfidence=s==null?0:s.BagVoiceConfidence};
            lock(cacheGate)cached=observation;
        }
        void CountSession()
        {if(session==null)return;if(session.Casts>seenSessionCasts){countedCasts+=session.Casts-seenSessionCasts;seenSessionCasts=session.Casts;castStarted=clock.ElapsedMilliseconds;}if(session.Reels>seenSessionReels){countedReels+=session.Reels-seenSessionReels;seenSessionReels=session.Reels;}if(session.ConfirmedCatches>seenSessionCatches){countedCatches+=session.ConfirmedCatches-seenSessionCatches;seenSessionCatches=session.ConfirmedCatches;Report("已由拾取反馈确认收获，累计"+countedCatches+"次。");}}
        void CancelSession(string reason){if(session==null)return;CountSession();session.Stop(reason);session=null;castStarted=-1;}
        void TickVendor(OperationObservation o,long now,string checkpoint)
        {
            var command=vendor.Step(o,now);Report(command.Reason);
            if(command.Action==QuickVendorAction.Stop)throw new InvalidOperationException(command.Reason);
            if(command.Action==QuickVendorAction.Complete){VendorVisits++;bagSuppressed=vendor.NoSaleObserved;vendor=null;if(File.Exists(checkpoint))File.Delete(checkpoint);BeginTravel(o.Farm,picker.Segment);return;}
            if(command.Action==QuickVendorAction.None)return;
            Func<OperationObservation,bool> context=x=>vendor.Matches(command,x,command.Action==QuickVendorAction.Select);
            if(command.Action==QuickVendorAction.Select)host.Chat(command.Text,false,0,0,true);
            else if(command.Action==QuickVendorAction.Goods||command.Action==QuickVendorAction.Repair)host.Click(command.Region,context);
            else host.Pulse(command.Key,command.Hold,context);
            vendor.ConfirmStealthCommand(command);QuickVendorCheckpoint.Save(checkpoint,vendor.Checkpoint);
        }
    }
}
