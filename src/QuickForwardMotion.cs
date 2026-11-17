using System;
namespace WoWProbe
{
    // Holds only our W across scanning/steering ticks. The host still checks its
    // normal foreground, map, heartbeat and cancellation guard every loop.
    public sealed class QuickForwardMotion
    {
        readonly IInputBackend backend;readonly Action ensure;readonly InputFailureLatch safety;
        readonly Func<FarmObservation,bool> ignoreNeutralTarget;
        public bool Held {get;private set;}
        public QuickForwardMotion(IInputBackend backend,Action ensure,InputFailureLatch safety,Func<FarmObservation,bool> ignoreNeutralTarget=null)
        {this.backend=backend;this.ensure=ensure;this.safety=safety;this.ignoreNeutralTarget=ignoreNeutralTarget;}
        public void Start()
        {
            ensure();if(Held)return;
            if(backend.IsKeyAlreadyDown(87))throw new InvalidOperationException("用户正在按W，不能接管持续前进。");
            ensure();Held=true;
            try{backend.KeyDown(87);}catch(Exception ex){safety.Fail("持续前进按键失败："+ex.Message);Stop();throw;}
        }
        public void Observe(FarmObservation o)
        {
            if(!Held)return;
            if(!backend.IsTargetForeground()||backend.IsEmergencyPressed()||!CanMove(o,ignoreNeutralTarget!=null&&ignoreNeutralTarget(o)))Stop();
        }
        public static bool CanMove(FarmObservation o,bool allowNeutralTarget=false)
        {
            return o!=null&&o.Fresh&&o.KnownCore&&o.ReadFailures==0&&!o.PlayerDead&&!o.PlayerGhost&&!o.InCombat&&!o.BagFull&&
                o.Health>=80&&o.Health<=100&&o.ChatOpenKnown&&!o.ChatOpen&&o.CastingKnown&&!o.Casting&&!o.Channeling&&
                (allowNeutralTarget||!(GrindRecoveryPolicy.CanAttack(o)&&o.TargetReaction==4));
        }
        public void Stop()
        {
            if(!Held)return;Held=false;
            try{backend.KeyUp(87);}catch(Exception ex){safety.Fail("持续前进释放失败："+ex.Message);throw;}
        }
    }
}
