namespace WoWProbe
{
    // Shared by combat engines and retries while the client window is open.
    public sealed class QuickHunterState
    {
        public readonly QuickPetRecovery Pet;
        public QuickHunterState(System.Func<long> clock=null){Pet=new QuickPetRecovery(clock);}
        public bool AspectPending {get;private set;}
        public void Observe(FarmObservation o)
        {if(o==null||!o.Fresh||!o.KnownCore||o.ReadFailures!=0)return;if(o.PlayerDead||o.PlayerGhost){AspectPending=true;Pet.PlayerDied();}else Pet.Observe(o);}
        public void AspectInputCompleted(){AspectPending=false;}
    }

    public sealed class QuickPetRecovery
    {
        bool enabled,pending,attempted,sawCast,petKeySent,reviveWithCommand,afterPlayerDeath;
        bool ignoreManaUntilCombat,waitForLivingPet;int fixedStage;long fixedSentAt;
        readonly System.Func<long> clock;
        ushort reviveKey,summonKey;long issuedAt,nextTry,nextReport;uint failedBase;bool failuresKnown;
        public bool Required {get{return enabled&&pending;}}
        public bool Waiting {get{return Required&&attempted;}}
        public bool PriorityRequired {get{return Required&&!afterPlayerDeath;}}
        public bool PostDeathRequired {get{return Required&&afterPlayerDeath;}}
        public bool PostDeathStarted {get{return PostDeathRequired&&fixedStage>0;}}
        public bool IgnoreManaRest {get{return PostDeathRequired||ignoreManaUntilCombat;}}
        public QuickPetRecovery(System.Func<long> clock=null)
        {var watch=System.Diagnostics.Stopwatch.StartNew();this.clock=clock??(()=>watch.ElapsedMilliseconds);}
        public void Configure(bool enable,string binding,string summonBinding="E",bool useCommand=false)
        {enabled=enable;reviveWithCommand=useCommand;reviveKey=enable&&!useCommand?QuickCombatHotkeys.Key(binding,"复活宠物"): (ushort)0;summonKey=enable?QuickCombatHotkeys.Key(summonBinding,"召唤宠物"):(ushort)0;attempted=sawCast=false;nextTry=nextReport=0;}
        public void PlayerDied(){pending=afterPlayerDeath=true;attempted=sawCast=petKeySent=ignoreManaUntilCombat=waitForLivingPet=false;fixedStage=0;nextTry=nextReport=0;}
        public void Observe(FarmObservation o)
        {
            if(o.InCombat)ignoreManaUntilCombat=false;
            if(waitForLivingPet){if(o.PetStateKnown&&o.PetExists&&!o.PetDead)waitForLivingPet=false;return;}
            if(enabled&&!pending&&o.PetStateKnown&&o.PetDead){pending=true;afterPlayerDeath=attempted=sawCast=petKeySent=false;nextTry=nextReport=0;}
        }
        public void Interrupted(){attempted=sawCast=false;nextTry=0;}
        public void InputCompleted(QuickCommand c,long now)
        {
            if(c.FixedPostDeath)
            {
                if(!PostDeathRequired)return;
                if(fixedStage==0&&c.Action==QuickAction.HunterSummonPet){fixedStage=1;fixedSentAt=clock();}
                else if(fixedStage==1&&c.Action==QuickAction.HunterRevivePet){fixedStage=2;fixedSentAt=clock();}
                else if(fixedStage==2&&c.Action==QuickAction.HunterAspect){pending=false;ignoreManaUntilCombat=waitForLivingPet=true;}
                return;
            }
            attempted=petKeySent=true;sawCast=false;issuedAt=now;failedBase=c.CastFailuresAtIssue;failuresKnown=c.CastFailuresKnown;
        }
        QuickCommand PostDeathStep(FarmObservation o,long now)
        {
            long elapsed=clock()-fixedSentAt;
            if(fixedStage==1&&elapsed<1500)return Wait("复活后固定流程：E已发送，短暂等待召唤公共冷却。",now);
            if(fixedStage==2&&elapsed<11000)return Wait("复活后固定流程：Q已发送，原地保留11秒复活宠物施法时间。",now);
            if(o.Casting||o.Channeling)return Wait("复活后固定流程：等待当前读条结束。",now);
            return new QuickCommand{FixedPostDeath=true,Action=fixedStage==0?QuickAction.HunterSummonPet:fixedStage==1?QuickAction.HunterRevivePet:QuickAction.HunterAspect,Key=(ushort)(fixedStage==0?69:fixedStage==1?81:56),Reason=fixedStage==0?"复活后血量已满：固定按E召唤宠物，不等待蓝量。":fixedStage==1?"复活后固定按Q复活宠物，接下来原地等待11秒。":"复活宠物等待已结束：固定按8施放雄鹰守护，随后继续循环打怪。"};
        }
        QuickCommand Wait(string reason,long now,bool report=false)
        {bool log=report||now>=nextReport;if(log)nextReport=now+5000;return new QuickCommand{Action=QuickAction.None,Reason=reason,Report=log};}
        public QuickCommand Step(FarmObservation o,long now)
        {
            if(!Required)return null;
            if(PostDeathRequired)return PostDeathStep(o,now);
            if(o.Casting||o.Channeling){if(attempted)sawCast=true;return Wait("恢复宠物：等待当前读条结束，保持原地。",now);}
            if((petKeySent||!afterPlayerDeath)&&o.PetStateKnown&&o.PetExists&&!o.PetDead)
            {pending=attempted=false;return Wait("已确认宠物存活，宠物恢复流程完成，继续原流程。",now,true);}
            if(attempted)
            {
                if(failuresKnown&&o.CastEventsKnown&&o.CastFailed!=failedBase||sawCast||now-issuedAt>=3000)
                {attempted=sawCast=false;nextTry=now+2000;return Wait("尚未确认宠物存活，等待2秒后按实际状态选择召唤或复活。",now,true);}
                return Wait("宠物技能按键已发送，等待读条及宠物存活反馈。",now);
            }
            if(!o.PetStateKnown)return Wait("宠物状态暂不可读，保持原地等待插件数据。",now);
            if(now<nextTry)return Wait("等待宠物技能重试间隔。",now);
            string prefix=afterPlayerDeath?"吃喝已结束，":"循环中优先恢复宠物，";
            return new QuickCommand{Action=o.PetDead?QuickAction.HunterRevivePet:QuickAction.HunterSummonPet,PriorityPetRecovery=PriorityRequired,Key=o.PetDead?reviveKey:summonKey,UsePetReviveChat=o.PetDead&&reviveWithCommand,Reason=prefix+(o.PetDead?(reviveWithCommand?"宝宝死亡：输入/cast 复活宠物，等待确认存活。":"宝宝死亡：按"+((char)reviveKey)+"复活宠物，等待确认存活。"):"宝宝未确认死亡：施放召唤宠物，等待确认出现并存活。"),CastFailuresKnown=o.CastEventsKnown,CastFailuresAtIssue=o.CastFailed};
        }
    }
}
