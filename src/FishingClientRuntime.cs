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
        FishingNativeHost host;FishingSession session;FishingSoundDetector sound;FishingSplashDetector splash;WindowsFishingAudioCapture audio;
        FishingActivityClock activity;FishingSpotPicker picker;FishingPoint point;FishingPhrases phrases;readonly FishingWhisperPolicy replies=new FishingWhisperPolicy();
        Navigator travel;QuickVendorEngine vendor;FarmObservation forwardAnchor;int forwardHold;double measuredTravelSpeed=.04;long arrived=-1,castStarted=-1,nextFrame,lastAudible=-100000,lastNotice=-100000;int countedCasts,countedReels,countedCatches,lastSpokenLap;
        string lastLog="";int seenSessionCasts,seenSessionReels,seenSessionCatches;bool bagSuppressed;
        public volatile bool Running;public string Status="未开始";public int Casts{get{return countedCasts;}}public int Reels{get{return countedReels;}}public int Catches{get{return countedCatches;}}public int VendorVisits{get;private set;}public int Laps{get{return picker==null?0:picker.CompletedLaps;}}
        public FishingClientRuntime(FishingClientSettings settings,string directory,Action<string> log){this.settings=settings.Clone();this.directory=directory;this.log=log;}
        public void Start(){if(worker!=null)throw new InvalidOperationException("钓鱼已启动。");settings.Validate(true);worker=new Thread(Run){IsBackground=true,Name="Independent fishing client"};worker.SetApartmentState(ApartmentState.MTA);Running=true;worker.Start();}
        public void Stop(){stopping=true;if(session!=null)session.Stop("用户停止钓鱼。");}
        public void Dispose(){Stop();if(worker!=null&&worker!=Thread.CurrentThread)worker.Join(5000);}
        void Report(string text){Status=text;if(text!=lastLog||clock.ElapsedMilliseconds-lastNotice>30000){lastLog=text;lastNotice=clock.ElapsedMilliseconds;log(text);}}
        void Run()
        {
            bool owner=false;Mutex exclusive=null;
            try{
                exclusive=new Mutex(true,"WoWProbe-QuickCombat",out owner);if(!owner)throw new InvalidOperationException("请先关闭自动打怪客户端，避免同时控制游戏。");
                host=new FishingNativeHost(settings.Fishing.Calibration,()=>stopping,()=>clock.ElapsedMilliseconds,Report);host.Focus();
                var first=host.Read();ValidateWorld(first);activity=new FishingActivityClock(settings,clock.ElapsedMilliseconds);picker=new FishingSpotPicker(settings);point=picker.Start(first.Farm);
                splash=new FishingSplashDetector(settings.SplashSensitivity);if(settings.ChatAtLap)phrases=new FishingPhrases(Path.Combine(directory,"assets","fishing","chat-phrases.txt"));
                if(settings.UseAudio){sound=new FishingSoundDetector(Path.Combine(directory,"assets","fishing"));audio=new WindowsFishingAudioCapture((samples,rate,end,broken)=>{if(broken){sound.Reset();return;}double energy=0;foreach(float v in samples)energy+=v*v;if(samples.Length>0&&energy/samples.Length>.00000001)Interlocked.Exchange(ref lastAudible,end);sound.Push(samples,rate,end);},()=>clock.ElapsedMilliseconds);try{audio.Start();}catch(Exception e){Report("声音监听不可用，使用水花图像："+e.Message);}}
                var social=host.Social(first);if((settings.ChatAtLap||settings.WhisperReply||settings.PeekBags)&&!social.Known)throw new InvalidOperationException("请安装配套钓鱼插件并在游戏输入 /reload，以启用聊天及背包状态反馈。");replies.Discard(social.WhisperCount);
                Report("钓鱼准备完成；F12急停，进入钓点后倒计时开始抛竿。");
                var checkpoint=Path.Combine(directory,"fishing-vendor-checkpoint.json");
                if(File.Exists(checkpoint)){var cp=QuickVendorCheckpoint.Load(checkpoint);vendor=new QuickVendorEngine(cp,host.Read());Report("继续未完成的钓鱼售卖路线。");}
                else BeginTravel(first.Farm,-1);
                while(!stopping)
                {
                    host.CheckCancelled();
                    if(!host.Foreground){CancelSession("游戏失去前台，暂停钓鱼输入。");Report("等待游戏回到前台。");host.Idle(200);continue;}
                    var o=host.Read();ValidateWorld(o);long now=clock.ElapsedMilliseconds;
                    if(o.Farm.ChatOpen){CancelSession("用户打开了聊天框。");Report("等待用户关闭聊天框。");host.Idle(200);continue;}
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
                    if(travel!=null){TickTravel(o,now);host.Idle(50);continue;}
                    bool idle=FishingCycleBoundary.IsIdle(session==null?null:session.EngineStatus,session!=null&&session.IsHolding);
                    if(idle&&!o.Farm.Casting&&!o.Farm.Channeling)
                    {
                        var info=host.Social(o);
                        if(settings.WhisperReply&&info.Known){string answer=replies.Pending(info.WhisperCount,info.SenderHash);if(answer!=null){CancelSession("回复新私信。");host.Chat(answer,true,info.SenderHash,info.WhisperCount);replies.Sent(info.WhisperCount,info.SenderHash);continue;}}
                        if(settings.ChatAtLap&&picker.CompletedLaps>lastSpokenLap){CancelSession("三段轮换完成，发送圈末交流。");host.Chat(phrases.Next(),false);lastSpokenLap=picker.CompletedLaps;continue;}
                        if(activity.BagDue(now)){CancelSession("执行到期背包查看。");host.PeekBag();activity.BagClosed(clock.ElapsedMilliseconds);continue;}
                        if(settings.RandomSpots&&arrived>=0&&(now-arrived>=settings.StayMinutes*60000||settings.ChangeAfterCatch&&seenSessionCatches>0))
                        {CancelSession("本钓点驻留结束，沿连线换点。");int previous=picker.Segment;point=picker.Next();BeginTravel(o.Farm,previous);continue;}
                    }
                    if(session==null)
                    {
                        if(o.Farm.Casting||o.Farm.Channeling){Report("等待当前施法完成，再恢复钓鱼。");host.Idle(100);continue;}
                        PrepareSession(o,now);
                    }
                    UpdateObservation(o,now);session.Tick();
                    // Release a pressed fishing/facing key on time before doing expensive captures.
                    while(session.IsHolding&&!stopping){host.Idle(5);session.Tick();}
                    CountSession();
                    if(!session.IsActive){string reason=session.LastReason;CancelSession(reason);throw new InvalidOperationException(reason);}
                    var command=session.LastCommand;if(command!=null&&command.Action==FishingAction.None)Report(command.Reason);
                    host.Idle(60);
                }
            }
            catch(OperationCanceledException){Report("钓鱼已停止。");}
            catch(Exception e){Report("钓鱼暂停："+e.Message);}
            finally{if(session!=null)session.Stop("钓鱼退出，释放按键。");if(audio!=null)audio.Dispose();if(owner&&exclusive!=null)exclusive.ReleaseMutex();if(exclusive!=null)exclusive.Dispose();Running=false;}
        }
        void ValidateWorld(OperationObservation o)
        {
            var f=o==null?null:o.Farm;var c=settings.Fishing.Calibration;
            if(f==null||!o.Fresh||!f.Fresh||!f.KnownCore||f.ReadFailures!=0)throw new InvalidOperationException("游戏状态暂不可读，请露出游戏左上角状态面板。");
            if(!MapIdentity.Matches(f.MapId,f.MapMode,f.Zone,c.MapId,c.MapMode,c.Zone))throw new InvalidOperationException("当前地图与保存钓点不同。");
            if(f.InteractBindingKnown&&!f.InteractBindingCorrect)throw new InvalidOperationException(GrindInputGuard.InteractBindingError);
            if(f.InCombat||f.PlayerDead||f.PlayerGhost)throw new InvalidOperationException("当前处于战斗、死亡或幽魂状态，请恢复到安全钓点。");
        }
        void BeginTravel(FarmObservation from,int previous)
        {
            forwardAnchor=null;var route=picker.Travel(from,point,previous);travel=route.Points.Count<2?null:new Navigator(route,settings.Fishing.Calibration,false,false,1,NavigationPurpose.VendorReturn){ForwardPulseLimit=1500};
            if(travel==null)arrived=clock.ElapsedMilliseconds;else arrived=-1;
            Report((settings.RandomSpots?"随机钓点：第"+(picker.Segment+1)+"条连线":"固定钓点"+(char)('A'+picker.Segment))+"，目标("+(point.X*100).ToString("F2")+", "+(point.Y*100).ToString("F2")+")；驻留"+settings.StayMinutes+"分钟。");
        }
        void TickTravel(OperationObservation o,long now)
        {
            var f=o.Farm;
            if(forwardAnchor!=null&&f.Sequence!=forwardAnchor.Sequence){double moved=NavigationMath.Distance(f.X-forwardAnchor.X,f.Y-forwardAnchor.Y,settings.Fishing.Calibration.AxisScale);if(moved>.0001&&forwardHold>0)measuredTravelSpeed=Math.Max(measuredTravelSpeed,Math.Min(.2,moved*1000/forwardHold*1.15));forwardAnchor=null;}
            var action=travel.Step(new NavigationSample{Fresh=f.Fresh,KnownCore=f.KnownCore,ReadFailures=f.ReadFailures,Sequence=f.Sequence,MapId=f.MapId,MapMode=f.MapMode,Zone=f.Zone,X=f.X,Y=f.Y,Facing=f.Facing,Dead=f.PlayerDead,Ghost=f.PlayerGhost,Combat=f.InCombat},now);
            if(action.Action==NavigationAction.Stop)throw new InvalidOperationException(action.Reason);
            if(action.Action==NavigationAction.Completed){travel=null;arrived=now;Report("已到达钓点并停稳，恢复面水朝向后按抛竿键。");return;}
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
            castStarted=-1;nextFrame=0;splash.Reset();if(sound!=null)sound.Reset();seenSessionCasts=seenSessionReels=seenSessionCatches=0;UpdateObservation(o,now);
            session=new FishingSession(host.Backend,()=>clock.ElapsedMilliseconds,()=>{lock(cacheGate)return cached==null?null:cached.Clone();},message=>{if(message.StartsWith("Fishing pulse"))Report(message);},new InputFailureLatch());session.Start(plan);
        }
        void UpdateObservation(OperationObservation o,long now)
        {
            if(settings.UseVisual&&now>=nextFrame){using(var image=host.Capture())splash.Push(image,settings.VisualRegion,now,castStarted,session!=null&&(session.EngineStatus=="WaitBite"||session.EngineStatus=="ReelDelay"));nextFrame=now+80;}
            var s=sound==null?null:sound.Snapshot();bool audioReady=audio!=null&&audio.Running&&now-audio.LastPacketMs<1000&&now-Interlocked.Read(ref lastAudible)<5000;
            long bite=audioReady&&s!=null?s.LastBiteAtMs:-1;double confidence=audioReady&&s!=null?s.BiteConfidence:0;
            if(settings.UseVisual&&splash.LastBiteAtMs>bite){bite=splash.LastBiteAtMs;confidence=splash.Confidence;}
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
