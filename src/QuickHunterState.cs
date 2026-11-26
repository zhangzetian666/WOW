namespace WoWProbe
{
    // Shared by combat engines and retries while the client window is open.
    public sealed class QuickHunterState
    {
        public readonly QuickPetRecovery Pet=new QuickPetRecovery();
        public bool AspectPending {get;private set;}
        public void Observe(FarmObservation o)
        {if(o!=null&&o.Fresh&&o.KnownCore&&o.ReadFailures==0&&(o.PlayerDead||o.PlayerGhost)){AspectPending=true;Pet.PlayerDied();}}
        public void AspectInputCompleted(){AspectPending=false;}
    }

    public sealed class QuickPetRecovery
    {
        bool enabled,pending,attempted,sawCast;
        ushort key;long issuedAt,nextTry,nextReport;uint failedBase;bool failuresKnown;
        public bool Required {get{return enabled&&pending;}}
        public bool Waiting {get{return Required&&attempted;}}
        public void Configure(bool enable,string binding)
        {enabled=enable;key=enable?QuickCombatHotkeys.Key(binding,"复活宠物"): (ushort)0;attempted=sawCast=false;nextTry=nextReport=0;}
        public void PlayerDied(){pending=true;attempted=sawCast=false;nextTry=nextReport=0;}
        public void Interrupted(){attempted=sawCast=false;nextTry=0;}
        public void InputCompleted(QuickCommand c,long now)
        {attempted=true;sawCast=false;issuedAt=now;failedBase=c.CastFailuresAtIssue;failuresKnown=c.CastFailuresKnown;}
        QuickCommand Wait(string reason,long now,bool report=false)
        {bool log=report||now>=nextReport;if(log)nextReport=now+5000;return new QuickCommand{Action=QuickAction.None,Reason=reason,Report=log};}
        public QuickCommand Step(FarmObservation o,long now)
        {
            if(!Required)return null;
            if(o.Casting||o.Channeling){if(attempted)sawCast=true;return Wait("复活宠物：等待当前读条结束，保持原地。",now);}
            if(o.PetStateKnown&&o.PetExists&&!o.PetDead)
            {pending=attempted=false;return Wait("已确认宠物存活，复活后恢复流程完成。",now,true);}
            if(attempted)
            {
                if(failuresKnown&&o.CastEventsKnown&&o.CastFailed!=failedBase||sawCast||now-issuedAt>=3000)
                {attempted=sawCast=false;nextTry=now+2000;return Wait("尚未确认宠物存活，等待2秒后重新核验并尝试复活。",now,true);}
                return Wait("复活宠物按键已发送，等待读条及宠物存活反馈。",now);
            }
            if(!o.PetStateKnown)return Wait("宠物状态暂不可读，保持原地等待插件数据。",now);
            if(now<nextTry)return Wait("等待复活宠物重试间隔。",now);
            return new QuickCommand{Action=QuickAction.HunterRevivePet,Key=key,Reason="复活后吃喝已结束：按配置快捷键复活宠物，确认存活后再巡航。",CastFailuresKnown=o.CastEventsKnown,CastFailuresAtIssue=o.CastFailed};
        }
    }
}
