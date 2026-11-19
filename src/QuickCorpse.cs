using System;
namespace WoWProbe
{
    // One confirmed corpse only. Selection, loot and skinning have separate proof.
    public sealed class QuickCorpse
    {
        readonly uint token;readonly bool skinning;long started;readonly string targetName;
        uint opened,closed,skinBase,failedBase;long lootAt,skinAt;bool selected,lootIssued,skinIssued,skinSelected;
        readonly bool approach;bool approached;
        long selectionAt;int selectionAttempts;
        long combatSince=-1,settledSince=-1;bool settled;int lastHealth;
        public bool Interrupted {get;private set;}
        public bool Done {get;private set;}
        public int Loots {get;private set;}public int Skins {get;private set;}
        public QuickCorpse(uint token,bool skinning,FarmObservation initial,long now)
            :this(token,skinning,initial,now,false){}
        public QuickCorpse(uint token,bool skinning,FarmObservation initial,long now,bool approach)
            :this(token,skinning,initial,now,approach,initial==null?"":initial.TargetName){}
        public QuickCorpse(uint token,bool skinning,FarmObservation initial,long now,bool approach,string targetName)
        {if(token==0||initial==null)throw new ArgumentException("A verified corpse identity is required.");this.token=token;this.skinning=skinning;this.approach=approach;this.targetName=targetName;started=now;settledSince=initial.InCombat?-1:now;lastHealth=initial.Health;opened=initial.LootOpened;closed=initial.LootClosed;}
        QuickCommand C(QuickAction action,string reason,ushort key){return new QuickCommand{Action=action,Token=token,Reason=reason,Key=key,SelectName=action==QuickAction.CorpseSelect&&resumed?targetName:null};}
        bool resumed;
        public void Resume(FarmObservation o,long now)
        {Interrupted=false;resumed=true;started=now;combatSince=-1;settled=false;settledSince=o.InCombat?-1:now;lastHealth=o.Health;opened=o.LootOpened;closed=o.LootClosed;selected=lootIssued=skinIssued=skinSelected=approached=false;selectionAttempts=0;}
        QuickCommand Select(long now,bool skin)
        {
            if(selectionAttempts==0){selectionAt=now;selectionAttempts=resumed?2:1;return C(QuickAction.CorpseSelect,skin?"拾取完成后重选同一尸体，核对剥皮条件。":"重选上一目标，核对已击杀尸体后再拾取。",0);}
            if(selectionAttempts==1&&now-selectionAt>=1000&&!String.IsNullOrWhiteSpace(targetName)){selectionAttempts=2;selectionAt=now;var c=C(QuickAction.CorpseSelect,"上一目标重选未成功，按完整名字重选并核对原尸体身份。",0);c.SelectName=targetName;return c;}
            if(now-selectionAt>=3000)return Finish(skin?"拾取已确认，但无法重选原尸体；不计剥皮成功。":"无法重新取得原尸体，跳过拾取/剥皮，不冒充成功。");
            return C(QuickAction.None,"等待原尸体重新选中，不继续寻怪。",0);
        }
        QuickCommand Finish(string reason){Done=true;var c=C(QuickAction.None,reason,0);c.Report=true;return c;}
        public QuickCommand Step(FarmObservation o,long now)
        {
            if(Done)return C(QuickAction.None,"尸体处理已结束。",0);
            if(o==null||!o.Fresh||!o.KnownCore||o.ReadFailures!=0||o.PlayerDead||o.PlayerGhost)return Finish("尸体状态失效，取消拾取/剥皮。");
            if(o.InCombat)
            {
                settled=false;settledSince=-1;if(combatSince<0)combatSince=now;
                bool attacker=o.TargetExists&&o.TargetIdentityKnown&&o.TargetToken!=token&&!o.TargetIsPlayer&&o.TargetDeadKnown&&!o.TargetDead&&o.AttackabilityKnown&&o.TargetCanAttack;
                bool damage=lastHealth>=0&&lastHealth<=100&&o.Health>=0&&o.Health<lastHealth;lastHealth=o.Health;
                if(!lootIssued&&!skinIssued&&!approached&&!attacker&&!damage&&now-combatSince<2500)return C(QuickAction.None,"击杀后等待战斗标记清除，保留尸体，不继续巡航。",0);
                Interrupted=true;return C(QuickAction.None,"确认新攻击或持续战斗，暂存尸体，先处理攻击者。",0);
            }
            combatSince=-1;lastHealth=o.Health;
            if(!settled){if(settledSince<0)settledSince=now;if(now-settledSince<500)return C(QuickAction.None,"已确认尸体，等待脱战稳定0.5秒后拾取。",0);settled=true;}
            if(now<started||now-started>20000)return Finish("尸体处理超时，继续巡航；不计未确认的拾取/剥皮。");
            bool same=o.TargetExists&&o.TargetIdentityKnown&&o.TargetToken==token&&!o.TargetIsPlayer&&o.TargetDeadKnown&&o.TargetDead;
            if(approach&&same&&!approached){approached=true;return C(QuickAction.CorpseApproach,"发现路边可拾取或剥皮尸体，先按F接近，不计为本轮击杀。",70);}
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
                    if(!skinSelected){skinSelected=true;selectionAttempts=0;}return Select(now,true);
                }
                if(!selected){selected=true;selectionAttempts=0;}return Select(now,false);
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
                return C(QuickAction.Skin,"同一尸体已拾取完且可剥皮，直接按快捷键7剥皮，不输入聊天命令。",55);
            }
            if(!lootIssued){lootIssued=true;lootAt=now;return C(QuickAction.Loot,"对同一已死亡怪物按F拾取，等待窗口事件确认。",70);}
            if(now-lootAt>=5000)return Finish("拾取未确认，不执行剥皮；继续下一只。");
            return C(QuickAction.None,"等待拾取窗口开启和关闭。",0);
        }
    }
}
