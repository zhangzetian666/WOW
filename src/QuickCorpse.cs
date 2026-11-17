using System;
namespace WoWProbe
{
    // One confirmed corpse only. Selection, loot and skinning have separate proof.
    public sealed class QuickCorpse
    {
        readonly uint token;readonly bool skinning;readonly long started;
        uint opened,closed,skinBase,failedBase;long lootAt,skinAt,skinSelectAt;bool selected,lootIssued,skinIssued,skinSelected;
        readonly bool approach;bool approached;
        public bool Done {get;private set;}
        public int Loots {get;private set;}public int Skins {get;private set;}
        public QuickCorpse(uint token,bool skinning,FarmObservation initial,long now)
            :this(token,skinning,initial,now,false){}
        public QuickCorpse(uint token,bool skinning,FarmObservation initial,long now,bool approach)
        {if(token==0||initial==null)throw new ArgumentException("A verified corpse identity is required.");this.token=token;this.skinning=skinning;this.approach=approach;started=now;opened=initial.LootOpened;closed=initial.LootClosed;}
        QuickCommand C(QuickAction action,string reason,ushort key){return new QuickCommand{Action=action,Token=token,Reason=reason,Key=key};}
        QuickCommand Finish(string reason){Done=true;var c=C(QuickAction.None,reason,0);c.Report=true;return c;}
        public QuickCommand Step(FarmObservation o,long now)
        {
            if(Done)return C(QuickAction.None,"尸体处理已结束。",0);
            if(o==null||!o.Fresh||!o.KnownCore||o.ReadFailures!=0||o.PlayerDead||o.PlayerGhost)return Finish("尸体状态失效，取消拾取/剥皮。");
            if(o.InCombat)
            {
                return Finish("仍在战斗，先处理附近攻击者；本具尸体不计拾取或剥皮成功。");
            }
            if(now<started||now-started>20000)return Finish("尸体处理超时，继续巡航；不计未确认的拾取/剥皮。");
            bool same=o.TargetExists&&o.TargetIdentityKnown&&o.TargetToken==token&&!o.TargetIsPlayer&&o.TargetDeadKnown&&o.TargetDead;
            if(approach&&same&&!approached){approached=true;return C(QuickAction.CorpseApproach,"发现路边可剥皮尸体，先按F接近，不计为本轮击杀。",70);}
            if(approach&&now-started<1500)return C(QuickAction.None,"等待接近路边尸体。",0);
            if(skinIssued)
            {
                if(o.CastEventsKnown&&o.CastFailed!=failedBase)return Finish("剥皮施法失败，跳过本具尸体。");
                if(o.SkinSuccessKnown&&o.SkinSuccess!=skinBase&&now-skinAt>=1500){Skins=1;return Finish("已收到剥皮成功事件，继续下一只。");}
                if(now-skinAt>=8000)return Finish("未收到剥皮成功事件，跳过本具尸体。");
                return C(QuickAction.None,"等待剥皮施法及成功事件。",0);
            }
            bool lootConfirmed=lootIssued&&o.LootEventsKnown&&o.LootOpened!=opened&&o.LootClosed!=closed;
            if(lootConfirmed&&Loots==0)Loots=1;
            if(lootIssued&&!lootConfirmed)
            {
                if(now-lootAt>=5000)return Finish("拾取未确认，不执行剥皮；继续下一只。");
                return C(QuickAction.None,"等待拾取窗口开启和关闭后再剥皮。",0);
            }
            if(!same)
            {
                if(lootConfirmed&&!skinning)return Finish("拾取窗口开关事件已确认；尸体目标已清除，继续下一只。");
                if(lootConfirmed&&skinning)
                {
                    if(!skinSelected){skinSelected=true;skinSelectAt=now;return C(QuickAction.CorpseSelect,"拾取完成后重选同一尸体，核对剥皮条件。",0);}
                    if(now-skinSelectAt>=2500)return Finish("拾取已确认，但无法重选同一尸体；不计剥皮成功。");
                    return C(QuickAction.None,"等待拾取后的尸体身份。",0);
                }
                if(!selected){selected=true;return C(QuickAction.CorpseSelect,"重选上一目标，核对已击杀尸体后再拾取。",0);}
                if(now-started>=3000)return Finish("无法重新取得同一尸体，跳过拾取/剥皮，不冒充成功。");
                return C(QuickAction.None,"等待同一尸体重新出现。",0);
            }
            if(o.Casting||o.Channeling)return C(QuickAction.None,"等待当前施法结束。",0);
            bool empty=o.LootKnown&&!o.Lootable;
            if(lootConfirmed||empty)
            {
                if(lootConfirmed&&now-lootAt<600)return C(QuickAction.None,"拾取已确认，等待动作结束后剥皮。",0);
                if(!skinning)return Finish(lootConfirmed?"拾取已确认，继续下一只。":"尸体已无可拾取物，继续下一只。");
                if(!o.SkinKnown)return Finish("尸体的可剥皮状态不可读，跳过剥皮。");
                if(!o.Skinnable)return Finish("该尸体不可剥皮，继续下一只。");
                if(!o.SkinBindingKnown||!o.SkinBindingCorrect||!o.SkinSuccessKnown||!o.CastEventsKnown)return Finish("剥皮键7或剥皮成功事件未就绪，跳过剥皮。");
                skinIssued=true;skinAt=now;skinBase=o.SkinSuccess;failedBase=o.CastFailed;
                return C(QuickAction.Skin,"同一尸体已拾取完且可剥皮，按7剥皮一次。",55);
            }
            if(!lootIssued){lootIssued=true;lootAt=now;return C(QuickAction.Loot,"对同一已死亡怪物按F拾取，等待窗口事件确认。",70);}
            if(now-lootAt>=5000)return Finish("拾取未确认，不执行剥皮；继续下一只。");
            return C(QuickAction.None,"等待拾取窗口开启和关闭。",0);
        }
    }
}
