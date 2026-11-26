using System;
using System.Collections.Generic;

namespace WoWProbe
{
    public enum QuickAction { None, Scan, Turn, Face, Skill, Loot, Skin, CorpseSelect, Food, Drink, BackOff, DetourTurn, DetourForward, Patrol, Defensive, Reacquire, Stop, RouteProbe, ReturnToRoute, CorpseApproach, InterruptSkin, Vanish, StealthEnter, StealthExit, StealthHold, RangedStop, HunterAspect, HunterRevivePet }
    public sealed class QuickCommand
    {
        public bool AttackHostile;public bool MeleeConfirmed;public bool RangedSkill;public bool AllowApproach;public bool InputStarted;
        public bool HunterSkill,HunterDefense;public int HealthAtIssue;
        public bool CastFailuresKnown;public uint CastFailuresAtIssue;
        public QuickAction Action; public ushort Key; public uint Token;public HashSet<uint> IgnoredTargets; public string Reason,SelectName;public int Hold;public bool Report,MouseTurn,RouteMovement,PauseForward,CombatRecovery;public QuickRouteFollower RouteContext;
    }

    // Native target interaction; do not infer character bearing from camera space.
    // A target selection or interaction binding alone does not prove movement.
    public sealed class QuickCombat
    {
        readonly uint map; readonly int mode; readonly string zone;
        readonly QuickRouteFollower route;
        readonly double x,y; readonly int interval;
        uint token,deathBase,lastKilled,creditBase,lockedName; int opening, sequence=-1, deadFrames, scans,corpseReselections,unconfirmedLosses;
        readonly Dictionary<uint,long> skippedTargets=new Dictionary<uint,long>();
        readonly HashSet<uint> inspectedCorpses=new HashSet<uint>();
        string lockedTargetName;
        string openingKeys,loopKeys;readonly QuickSkillRows skillRows;int loopIndex;
        bool openingPending;uint openingCast;int sliceRecovery;uint sliceCast;char retryFinisher;
        bool meleeConfirmed;uint meleeBaseline;
        bool rangedMoving;long nextRangedReport;uint rangedTarget;
        int contactHealth=255;
        long lastDefenseScan=-10000;
        readonly int restBelow;readonly ushort drinkKey;
        readonly bool permitFullBags;readonly bool attackHostile;
        readonly bool hunterMode;readonly QuickHunterState hunterState;
        bool hunterDefending,hunterAnnounced;int hunterIndex,lastHealth=255;
        bool hunterIssued,hunterIssuedDefense,hunterRangeBlocked,hunterErrorSeen;
        int hunterIssuedOpening,hunterIssuedLoop,hunterIssuedIndex;long hunterIssuedAt;uint hunterIssuedToken;
        double hunterBlockedX,hunterBlockedY;
        bool hunterIssuedFailuresKnown;uint hunterIssuedFailures;
        public bool HunterRecoveryPending {get{return hunterMode&&hunterState.AspectPending||hunterState.Pet.Required;}}
        public void HunterInputCompleted(QuickCommand command)
        {
            if(command.Action==QuickAction.HunterRevivePet){hunterState.Pet.InputCompleted(command,last);return;}
            if(command.Action==QuickAction.HunterAspect){hunterState.AspectInputCompleted();return;}
            if(!command.HunterSkill||command.Token!=token)return;
            hunterIssued=true;hunterIssuedDefense=command.HunterDefense;hunterIssuedOpening=opening;hunterIssuedLoop=loopIndex;hunterIssuedIndex=hunterIndex;hunterIssuedAt=last;hunterIssuedToken=token;
            hunterIssuedFailuresKnown=command.CastFailuresKnown;hunterIssuedFailures=command.CastFailuresAtIssue;
            if(command.HunterDefense)hunterIndex++;
            else if(opening<openingKeys.Length)opening++;else loopIndex++;
        }
        public void RestInputCompleted(long now,bool ateFood=false){if(resting){restHoldUntil=now+10000;if(ateFood&&!foodStealth&&foodStealthAllowed)enterStealthPending=true;}}
        public bool IsStealthRecovery {get{return foodStealth||vanishHolding;}}
        public bool IsResting {get{return resting;}}
        public bool HasActiveWork {get{return token!=0||resting||vanishHolding||HunterRecoveryPending;}}
        public static ushort ConfiguredKey(string text){string v=CleanKeys(text,true);if(v.Length>1)throw new FormatException("饮水键只能填一个按键，盗贼可留空。");return (ushort)(v.Length==0?0:v[0]=='='?187:v[0]=='-'?189:v[0]);}
        public static string CleanKeys(string keys,bool allowEmpty)
        {if(keys==null)throw new FormatException("技能序列不能为空值。");var b=new System.Text.StringBuilder();foreach(char c in keys){if(Char.IsWhiteSpace(c))continue;if(!(c>='0'&&c<='9')&&c!='-'&&c!='=')throw new FormatException("技能序列只支持0–9、-、=键，可用空格分隔。");b.Append(c);}if(b.Length>64||!allowEmpty&&b.Length==0)throw new FormatException("循环技能须为1至64个按键，开场可以留空。");return b.ToString();}
        sealed class DeferredCorpse {public uint Token;public QuickCorpse Corpse;public int Loots,Skins;}
        readonly Stack<DeferredCorpse> deferredCorpses=new Stack<DeferredCorpse>();
        long last=-1, nextSkill, nextFace, nextScan, started, lootUntil;
        bool stopped, looted,initialRest=true,resting,food,drink,reacquired;
        long corpseBlockedUntil;bool foodStealth,foodStealthAllowed,enterStealthPending,vanishHolding;
        bool urgentDefenseScan;uint vanishFailedBase;bool vanishLatched;long vanishAt=-1,vanishSafeAt=-1;
        long restHoldUntil,restStarted,nextRestReport,missingCombatTarget=-1,missingTarget=-1,nextDefensive;bool naturalRecovery;
        long foodProgressAt,drinkProgressAt;int restHealth,restPower,foodRetries,drinkRetries;
        double progressX,progressY; uint progressCasts; long progressAt;
        int escapes,escapePhase;
        bool escapeCombat;int combatEscapes;long combatCastAt;uint combatCasts;
        double escapeFacing;long turnDeadline;
        bool patrolPending,searchTurning;double patrolX,patrolY,searchFacing;int blockedWalks,searchEscapes;long searchDeadline,patrolCheckAt;
        readonly bool skinning,rangedCombat;QuickCorpse corpse;int creditedLoots,creditedSkins;
        public int Loots {get;private set;}public int Skins {get;private set;}
        public int Kills { get; private set; }
        public QuickCombat(FarmObservation initial,int interval):this(initial,interval,false){}
        public QuickCombat(FarmObservation initial,int interval,bool skinning):this(initial,interval,skinning,null){}
        public QuickCombat(FarmObservation initial,int interval,bool skinning,QuickRouteFollower route)
            :this(initial,interval,skinning,route,"242223","2"){}
        public QuickCombat(FarmObservation initial,int interval,bool skinning,QuickRouteFollower route,string openingKeys,string loopKeys,int restBelow=80,ushort drinkKey=0,bool permitFullBags=false,string[] skillRows=null,bool rangedCombat=false,bool hunterMode=false,QuickHunterState hunterState=null)
        {
            if(initial==null||!initial.Fresh||!initial.KnownCore)throw new InvalidOperationException("需要连续有效的插件数据。");
            if(interval<100||interval>5000)throw new ArgumentOutOfRangeException("interval");
            this.skillRows=skillRows==null?null:new QuickSkillRows(skillRows);this.openingKeys=CleanKeys(openingKeys,true);this.loopKeys=CleanKeys(loopKeys,false);
            this.rangedCombat=rangedCombat;this.permitFullBags=permitFullBags;this.attackHostile=route!=null&&route.Plan.AttackHostile;
            this.hunterMode=hunterMode||route!=null&&route.Plan.HunterMode;this.hunterState=hunterState??new QuickHunterState();this.hunterState.Observe(initial);lastHealth=initial.Health;
            if(restBelow<30||restBelow>95)throw new ArgumentOutOfRangeException("restBelow");if(drinkKey!=0&&!((drinkKey>=48&&drinkKey<=57)||drinkKey==187||drinkKey==189))throw new ArgumentException("Invalid drink key.");this.restBelow=restBelow;this.drinkKey=drinkKey;
            map=initial.MapId;mode=initial.MapMode;zone=initial.Zone;x=initial.X;y=initial.Y;this.interval=interval;this.skinning=skinning;this.route=route;
        }
        QuickCommand Cmd(QuickAction a,string r,ushort k)
        {return new QuickCommand{Report=skillRows!=null&&r.StartsWith("锁定怪物："),AttackHostile=attackHostile,Action=a,Reason=r,Key=k,Token=token,RouteContext=route,CombatRecovery=escapeCombat&&(a==QuickAction.BackOff||a==QuickAction.DetourTurn||a==QuickAction.DetourForward)};}
        QuickCommand Stop(string reason){stopped=true;return Cmd(QuickAction.Stop,reason,0);}
        public QuickCommand Step(FarmObservation o,long now)
        {
            if(stopped)return Cmd(QuickAction.Stop,"运行已停止。",0);
            if(now<0||now<last)return Stop("运行时钟异常。");last=now;
            if(o==null||!o.Fresh||!o.KnownCore||o.ReadFailures!=0)return Stop("插件状态失效，停止输入。");
            hunterState.Observe(o);
            if(o.PlayerDead||o.PlayerGhost)return Stop("角色死亡或处于灵魂状态。");
            if(o.ChatOpenKnown&&o.ChatOpen)return Stop("聊天输入框打开，停止战斗按键。");
            if(o.BagFull&&!o.InCombat&&!permitFullBags&&!HunterRecoveryPending)return Stop("背包已满。");
            if(o.Health<0||o.Health>100)return Stop("生命值不可读，不能确认恢复完成。");
            if(!MapIdentity.Matches(o.MapId,o.MapMode,o.Zone,map,mode,zone))return Stop("地图发生变化。");
            if(route!=null)
            {
                route.TickNearbySearch(o,now,token==0&&!resting);route.ObserveCombat(o);
                if(!route.CanOperate(o))
                {
                    if(route.TryRejoin(o))return AbandonChase(now,"追怪越过范围边界，放弃当前目标并前往最近巡航点继续。");
                    return Stop("角色超出巡航及追怪范围，当前状态无法确认返回路线。");
                }
            }
            else if(Double.IsNaN(o.X)||Double.IsNaN(o.Y)||Math.Abs(o.X-x)>0.04||Math.Abs(o.Y-y)>0.04)return Stop("离开启动位置四坐标点范围，停止追击。");
            if(token!=0&&!looted&&!o.InCombat&&now-started>=45000)return route!=null?AbandonChase(now,"45秒仍未进入战斗，放弃当前目标并继续巡航。"):Stop("单怪45秒未确认击杀，停止并检查朝向、距离和伤害。");
            if(resting&&(o.FoodCount==0||o.PowerType==0&&drinkKey==0))naturalRecovery=true;
            if(resting&&!naturalRecovery&&!foodStealth&&!o.InCombat&&now-restStarted>=60000)return Stop("60秒内未恢复满，检查食物和饮水绑定。");
            if(sequence==o.Sequence)return Cmd(QuickAction.None,"等待新一帧状态。",0);sequence=o.Sequence;
            if(hunterMode)
            {
                if(!o.InCombat){hunterDefending=hunterAnnounced=false;hunterIndex=0;}
                else if(!hunterDefending&&HealthDropped(o,lastHealth))
                {hunterDefending=true;hunterRangeBlocked=false;hunterIndex=0;openingPending=false;sliceRecovery=0;nextSkill=now;nextFace=now+1500;escapePhase=0;escapeCombat=false;}
                lastHealth=o.Health;
            }
            if(!o.CastingKnown)return Stop("施法状态不可读。");
            if(hunterState.Pet.Required&&(o.Casting||o.Channeling))
            {if(o.InCombat){hunterState.Pet.Interrupted();return Cmd(QuickAction.InterruptSkin,"复活宠物时受到攻击：中断读条，优先反击。",27);}return hunterState.Pet.Step(o,now);}
            if(hunterState.Pet.Waiting){if(o.InCombat){hunterState.Pet.Interrupted();if(o.Casting||o.Channeling)return Cmd(QuickAction.InterruptSkin,"复活宠物时受到攻击：中断读条，优先反击。",27);}else return hunterState.Pet.Step(o,now);}
            if(o.Health>=30&&vanishAt<0)vanishLatched=false;
            if(vanishAt>=0)
            {
                if(!o.InCombat)
                {
                    token=0;looted=false;corpse=null;resting=false;initialRest=true;
                    if(!vanishHolding){vanishHolding=true;return Cmd(QuickAction.StealthHold,"消失脱战后保持潜行，原地等待满血，不吃喝。",0);}
                    if(o.Health<100)return Cmd(QuickAction.None,"等待恢复条件满足，暂不执行尸体动作。",0);
                    vanishAt=vanishSafeAt=-1;vanishHolding=false;return Cmd(QuickAction.StealthExit,"血量100%，按Q退出潜行继续打怪。",81);
                }
                else if(vanishHolding){vanishHolding=false;vanishAt=vanishSafeAt=-1;}
                else if(now-vanishAt<300&&(!o.CastEventsKnown||o.CastFailed==vanishFailedBase)){vanishSafeAt=-1;return Cmd(QuickAction.None,"短暂核验消失是否脱战，保留普通攻击。",0);}
                else if(now-vanishAt<6000){vanishSafeAt=-1;}
                else {vanishAt=vanishSafeAt=-1;}
            }
            if(!hunterMode&&!rangedCombat&&!vanishLatched&&o.InCombat&&o.Health>0&&o.Health<15&&o.PowerType==3&&(route==null||route.Plan.UseRogueVanish))
            {vanishLatched=true;vanishFailedBase=o.CastFailed;vanishAt=now;vanishSafeAt=-1;return Cmd(QuickAction.Vanish,"血量低于15%，优先按6尝试消失。",54);}
            if(urgentDefenseScan){urgentDefenseScan=false;if(o.InCombat)return Cmd(QuickAction.Scan,"剥皮中断后立即选择附近攻击者。",9);}
            bool lockedDead=token!=0&&(HasDeathEvidence(o,token)||o.TargetExists&&o.TargetIdentityKnown&&o.TargetToken==token&&o.TargetDeadKnown&&o.TargetDead);
            if(o.InCombat&&token!=0&&!looted&&!lockedDead&&!o.TargetExists){if(route!=null)route.Interrupt(o);token=0;nextScan=Math.Min(nextScan,now);}
            if(o.InCombat&&(!hunterMode||hunterDefending)&&!looted&&!lockedDead&&o.TargetExists&&o.TargetDeadKnown&&!o.TargetDead&&o.TargetVictimKnown&&!o.TargetTargetsPlayer)
            {
                if(route!=null)route.Interrupt(o);token=0;resting=false;initialRest=true;
                if(now-lastDefenseScan<300)return Cmd(QuickAction.None,"已受攻击，等待切换到攻击本人的怪物。",0);
                lastDefenseScan=now;nextScan=now+300;return Cmd(QuickAction.Scan,"受到攻击：停止原行程，优先切换并核验攻击者。",9);
            }
            if(token==0)
            {
                bool mana=o.PowerType==0;
                if(mana&&(o.Power<0||o.Power>100))return Stop("法力值不可读，不能确认饮水完成。");
                if(!resting&&!o.InCombat&&(o.Health<(HunterRecoveryPending?100:initialRest?99:restBelow)||mana&&o.Power<(HunterRecoveryPending||initialRest?99:restBelow)))
                {resting=true;foodStealthAllowed=!hunterMode&&!rangedCombat&&o.PowerType==3&&(route==null||route.Plan.UseRogueVanish);naturalRecovery=o.FoodCount==0||mana&&drinkKey==0;nextRestReport=now;restStarted=now;restHoldUntil=now+10000;food=drink=false;foodProgressAt=drinkProgressAt=now;restHealth=o.Health;restPower=o.Power;foodRetries=drinkRetries=0;if(route!=null)route.Interrupt(o);}
                if(resting&&o.InCombat){foodStealth=enterStealthPending=false;resting=false;initialRest=true;food=drink=false;if(o.Casting||o.Channeling)return Cmd(QuickAction.InterruptSkin,"吃喝期间受到攻击，停止恢复施法并优先反击。",27);}
                if(resting)
                {
                    if(enterStealthPending){enterStealthPending=false;foodStealth=true;return Cmd(QuickAction.StealthEnter,"进食后按Q潜行，保持恢复。",81);}
                    if(now>=restHoldUntil&&o.Health>=100&&(!mana||o.Power>=99)){resting=false;initialRest=false;if(route!=null)route.BeginNearbySearch(o,now);if(foodStealth){foodStealth=false;return Cmd(QuickAction.StealthExit,"满血后按Q退出潜行。",81);}return Cmd(QuickAction.None,"已观察到血量和适用法力恢复至满。",0);}
                    if(!naturalRecovery&&!foodStealth&&now-restStarted>=60000)return Stop("60秒内未恢复满，检查食物和饮水绑定。");
                    if(o.Health>restHealth)foodProgressAt=now;restHealth=o.Health;
                    if(mana&&o.Power>restPower)drinkProgressAt=now;restPower=o.Power;
                    if(o.FoodCount!=0&&o.Health<100&&(!food||now-foodProgressAt>=10000&&foodRetries<3))
                    {bool retry=food;restHoldUntil=now+10000;food=true;foodProgressAt=now;if(retry)foodRetries++;return Cmd(QuickAction.Food,retry?"吃食物后10秒未见回血，自动重试食物键，无需手动补按。":"血量低于恢复要求，自动按=吃食物。",0xBB);}
                    if(mana&&drinkKey!=0&&o.Power<99&&(!drink||now-drinkProgressAt>=10000&&drinkRetries<3))
                    {bool retry=drink;restHoldUntil=now+10000;drink=true;drinkProgressAt=now;if(retry)drinkRetries++;return Cmd(QuickAction.Drink,retry?"饮水后10秒未见法力恢复，自动重试饮水键。":"法力低于恢复要求，自动按配置饮水键，等待恢复至99%。",drinkKey);}
                    var waiting=Cmd(QuickAction.None,naturalRecovery?"补给不足，保持原地等待自然恢复：生命"+o.Health+"%"+(mana?"，法力"+o.Power+"%":"")+"；恢复后继续打怪。":"吃喝恢复中，至少静坐10秒并等待血条达到100%。",0);
                    if(naturalRecovery&&now>=nextRestReport){waiting.Report=true;nextRestReport=now+30000;}return waiting;
                }
                initialRest=false;
            }
            if(hunterDefending&&(o.Casting||o.Channeling))return Cmd(QuickAction.InterruptSkin,"猎人受袭：中断未完成读条，改为1、2循环。",27);
            if((o.Casting||o.Channeling)&&!(o.InCombat&&looted&&corpse!=null))return Cmd(QuickAction.None,"等待当前施法。",0);
            if(hunterState.Pet.Required&&!o.InCombat&&o.Health==100){var petCommand=hunterState.Pet.Step(o,now);petCommand.RouteContext=route;return petCommand;}
            if(hunterMode&&hunterState.AspectPending&&!o.InCombat&&o.Health==100)
                return Cmd(QuickAction.HunterAspect,"猎人复活恢复满血：按8尝试灵猴守护。",56);
            if(token==0&&!o.InCombat&&deferredCorpses.Count>0)
            {var pending=deferredCorpses.Pop();token=pending.Token;corpse=pending.Corpse;creditedLoots=pending.Loots;creditedSkins=pending.Skins;looted=true;corpse.Resume(o,now);return AdvanceCorpse(o,now);}
            if(now>=corpseBlockedUntil&&token==0&&CanInspectCorpse(o)&&o.TargetExists&&o.TargetIdentityKnown&&o.TargetToken!=0&&!o.TargetIsPlayer&&o.TargetDeadKnown&&o.TargetDead&&(o.LootKnown&&o.Lootable||skinning&&o.SkinKnown&&o.Skinnable)&&!inspectedCorpses.Contains(o.TargetToken)&&(route==null||route.TargetAllowed(o)))
            {if(route!=null)route.Interrupt(o);token=o.TargetToken;inspectedCorpses.Add(token);looted=true;started=now;corpse=new QuickCorpse(token,skinning,o,now,true,o.TargetName,route!=null&&route.Plan.SkinWithInteract,route!=null&&route.Plan.NoChatCombat,route==null?"F8":route.Plan.LastTargetKey);creditedLoots=creditedSkins=0;return AdvanceCorpse(o,now);}
            if(token!=0)
            {
                if(looted&&corpse!=null)return AdvanceCorpse(o,now);
                if(HasDeathEvidence(o,token))return ConfirmDeath(o,now);
                if(o.TargetExists&&o.TargetIdentityKnown&&o.TargetToken==token&&o.TargetDeadKnown&&o.TargetDead)return ConfirmDeath(o,now);
                if(route!=null&&!o.InCombat&&o.TargetToken==token&&Contested(o))return AbandonChase(now,"目标已被其他人占有或正在战斗，跳过并继续寻怪。");
                bool chaseStalled=now-progressAt>=12000&&(o.X-progressX)*(o.X-progressX)+(o.Y-progressY)*(o.Y-progressY)<.00000009;
                if(route!=null&&!o.InCombat&&(!route.TargetAllowed(o)||chaseStalled||route.PursuitDistance(o)>route.Plan.CombatRadius-.001))return AbandonChase(now,chaseStalled?"接近持续无位移，放弃当前目标并继续巡航。":"到达配置的追怪范围边界，返回巡航路线。");
                if(route!=null&&o.InCombat&&!route.TargetAllowed(o))return Stop("战斗目标已离开配置的追怪范围，停止继续追击。");
                if(!o.TargetExists||!o.TargetIdentityKnown||o.TargetToken!=token)
                {
                    if(o.InCombat&&o.TargetExists&&o.TargetDeadKnown&&o.TargetDead)
                    {token=0;missingTarget=-1;nextScan=now+300;return Cmd(QuickAction.Scan,"战斗中切到了其他尸体，立即重新选择攻击者，不计击杀。",9);}
                    if(!o.InCombat&&o.TargetExists&&o.TargetIdentityKnown&&o.TargetToken==lastKilled&&o.TargetDeadKnown&&o.TargetDead&&o.CastSuccess==progressCasts&&corpseReselections<3)
                    {token=0;corpseReselections++;nextScan=now+400;return Cmd(QuickAction.None,"交互切回刚确认的尸体，重新选怪；不增加击杀。",0);}
                    if(missingTarget<0)missingTarget=now;
                    if(!o.TargetExists&&!reacquired){reacquired=true;return Cmd(QuickAction.Reacquire,"目标消失：重选上一目标核实死亡，不推定击杀。",0);}
                    if(now-missingTarget<2500)return Cmd(QuickAction.None,"目标变化，暂停输入等待死亡事件；不推定击杀。",0);
                    unconfirmedLosses++;
                    if(route!=null&&!o.InCombat)return AbandonChase(now,"目标丢失且未确认击杀，返回原路段继续寻怪。");
                    token=0;nextScan=now;return Cmd(QuickAction.None,"目标丢失未计击杀，重新核验附近怪物。",0);
                }
                missingTarget=-1;
                if(o.TargetIsPlayer||!o.AttackabilityKnown||!o.TargetDeadKnown)return Stop("怪物身份或死亡状态不可验证。");
                if(o.TargetDead)
                {
                    return ConfirmDeath(o,now);
                }
                deadFrames=0;
                if(!o.TargetCanAttack)return Stop("目标不再可攻击。");
                if(hunterMode)return HunterCombat(o,now);
                if(!rangedCombat&&o.InCombat&&o.PowerType==3&&o.Health<45&&now>=nextDefensive)
                {nextDefensive=now+120000;return Cmd(QuickAction.Defensive,"血量低于45%，尝试9闪避；继续当前战斗。",57);}
                if(o.MeleeEventsKnown&&o.MeleeHitToken==token&&o.MeleeHits!=meleeBaseline)meleeConfirmed=true;
                if(meleeConfirmed&&escapeCombat){escapePhase=0;escapeCombat=false;combatEscapes=0;}
                // Some clients register combat-log events without delivering them.
                // This is a separate defensive contact fallback, not a claimed player swing.
                if(!meleeConfirmed&&o.InCombat&&o.TargetVictimKnown&&o.TargetTargetsPlayer&&o.Health<contactHealth&&o.Health<=100&&contactHealth<=100)
                {meleeConfirmed=true;combatCastAt=now;escapePhase=0;escapeCombat=false;combatEscapes=0;var contact=Cmd(QuickAction.None,"普攻事件未返回；当前怪物正在攻击本人且已观察到实际掉血，启用技能设置中的按键。",0);contact.Report=true;return contact;}
                if(o.CastEventsKnown&&o.CastSuccess!=combatCasts){combatCasts=o.CastSuccess;combatCastAt=now;combatEscapes=0;}
                int configuredMask=0;foreach(char configured in openingKeys+loopKeys)configuredMask|=ActionBit(configured);
                if(configuredMask!=0&&(o.KnownActionsMask&configuredMask)==configuredMask&&(o.ReadyActionsMask&o.UsableActionsMask&configuredMask)==0)combatCastAt=now;
                if(escapePhase!=0)
                {
                    if(o.InCombat&&!escapeCombat||!o.InCombat&&escapeCombat){escapePhase=0;escapeCombat=false;progressAt=now;}
                    else if(escapePhase==1){escapePhase=2;escapeFacing=o.Facing;turnDeadline=now+3000;return Cmd(QuickAction.DetourTurn,"脱困：按住鼠标右键横拖，改变角色方向。",(ushort)(escapes%2==1?39:37));}
                    else
                    {
                        if(!MouseLookInput.TurnObserved(escapeFacing,o.Facing))
                            return now<turnDeadline?Cmd(QuickAction.None,"等待确认角色实际转向，暂不前进。",0):Stop("右键转向后未观察到角色朝向变化，停止前进。");
                        escapePhase=0;progressAt=now;nextFace=now+500;var forward=Cmd(QuickAction.DetourForward,"脱困：已确认转向，沿新方向短距离移动，再重新面向目标。",QuickMovement.Forward);escapeCombat=false;return forward;
                    }
                }
                // The configured rogue loop needs energy. Waiting for a resource
                // is not evidence of a blocked approach or a wrong facing.
                if(!rangedCombat&&!meleeConfirmed&&o.InCombat&&o.CastEventsKnown&&now-combatCastAt>=3500&&(o.PowerType!=3||o.Power>100||o.Power>=45))
                {
                    if(combatEscapes>=3){combatEscapes=0;combatCastAt=now+10000;}
                    else{combatEscapes++;combatCastAt=now;escapeCombat=true;escapePhase=1;return Cmd(QuickAction.BackOff,"战斗中持续无成功施法：后退换向并试走，绕开遮挡后重新面向攻击者。",83);}
                }
                double moved=(o.X-progressX)*(o.X-progressX)+(o.Y-progressY)*(o.Y-progressY);
                if(moved>=0.00000009||o.CastEventsKnown&&o.CastSuccess!=progressCasts)
                {progressX=o.X;progressY=o.Y;progressCasts=o.CastSuccess;progressAt=now;}
                if(!rangedCombat&&!o.InCombat&&now-progressAt>=4500)
                {
                    if(!o.TargetPositionKnown)return route!=null?AbandonChase(now,"交互后没有实际接近，返回已录路线继续寻找近处目标。"):Stop("交互后没有实际接近，请核对本角色移动设置。");
                    if(escapes>=2)return route!=null?AbandonChase(now,"两次脱困仍未接近目标，返回路线寻找下一只。"):Stop("连续两次换向仍无法接近怪物，停止以免持续顶住障碍。");
                    escapes++;escapePhase=1;progressAt=now;return Cmd(QuickAction.BackOff,"位置持续不变且未进入战斗：取消直走，后退后换向。",83);
                }
                if(!o.InCombat&&now-started>=45000)return Stop("单怪45秒未确认击杀，停止并检查朝向、距离和伤害。");
                if(rangedTarget!=token){rangedTarget=token;rangedMoving=false;nextRangedReport=0;}
                int rangedBit=rangedCombat?FirstRangedAction(o):0;
                bool rangedReady=rangedBit!=0&&(o.ActionRangeKnownMask&o.ActionsInRangeMask&rangedBit)!=0;
                bool closeContact=meleeConfirmed||CloseContact(o);
                bool approach=rangedCombat&&rangedBit!=0&&!rangedReady&&!closeContact;
                if(approach&&!rangedMoving)nextFace=now;
                if(rangedMoving&&!approach){rangedMoving=false;nextFace=long.MaxValue;return Cmd(QuickAction.RangedStop,"已进入技能距离，停止接近后攻击。",0);}
                if(now>=nextFace)
                {nextFace=rangedCombat&&!approach?long.MaxValue:now+1500;var face=Cmd(QuickAction.Face,rangedCombat?(approach?"远程目标尚未进入有效射程，尝试F交互并核对实际位移。":"远程：F校正朝向，停稳后执行可用技能。"):meleeConfirmed?"已接触目标，F校正近战朝向。":"F：与当前目标交互，核对实际朝向和接近情况。",0x46);face.MeleeConfirmed=meleeConfirmed;face.AllowApproach=approach;rangedMoving=approach;return face;}
                if(approach&&(o.ActionRangeKnownMask&rangedBit)!=0)return Cmd(QuickAction.None,"继续接近远程目标，等待进入有效射程。",0);
                if(o.MeleeEventsKnown&&o.MeleeHitToken==token&&o.MeleeHits!=meleeBaseline)meleeConfirmed=true;
                if(sliceRecovery==2&&o.CastEventsKnown&&o.CastSuccess!=sliceCast){sliceRecovery=3;}
                if(sliceRecovery==1&&o.CastEventsKnown&&o.CastSuccess!=sliceCast)sliceRecovery=0;
                if(openingPending&&o.CastEventsKnown&&o.CastSuccess!=openingCast){opening++;openingPending=false;}
                if(!rangedCombat&&!meleeConfirmed&&(o.MeleeEventsKnown||!o.RangeKnown||!o.TargetInRange))return Cmd(QuickAction.None,"保持普通攻击并接近，等待本角色首次普攻击中当前怪物后开始技能。",0);
                if(now>=nextSkill)
                {
                    nextSkill=now+interval;
                    if(rangedCombat)
                    {
                        int attempts=openingKeys.Length+loopKeys.Length;
                        while(attempts-->0)
                        {
                            bool inOpening=opening<openingKeys.Length;char candidate=inOpening?openingKeys[opening]:loopKeys[(loopIndex++)%loopKeys.Length];
                            ushort candidateKey=(ushort)(candidate=='='?187:candidate=='-'?189:candidate);
                            if(!RangedKeyAllowed(candidateKey,o,meleeConfirmed)){if(inOpening){opening++;openingPending=false;}continue;}
                            if(inOpening){openingPending=o.CastEventsKnown;openingCast=o.CastSuccess;if(!o.CastEventsKnown)opening++;}
                            var rangedCommand=Cmd(QuickAction.Skill,"远程技能键 "+candidate+"：射程、可用状态及自动射击状态核验后尝试。",candidateKey);rangedCommand.RangedSkill=true;rangedCommand.MeleeConfirmed=meleeConfirmed;return rangedCommand;
                        }
                        var wait=Cmd(QuickAction.None,"当前技能行没有需要再次按下的可用技能：保持已开启的自动射击；不可用时检查弹药、装备或技能条件。",0);if(now>=nextRangedReport){wait.Report=true;nextRangedReport=now+5000;}return wait;
                    }
                    char digit;
                    if(sliceRecovery==1){sliceRecovery=2;openingPending=false;}
                    bool rebuilding=sliceRecovery==2;
                    if(rebuilding){digit='2';sliceCast=o.CastSuccess;}
                    else if(sliceRecovery==3){digit=retryFinisher;if(opening<openingKeys.Length&&openingKeys[opening]==retryFinisher){openingPending=o.CastEventsKnown;openingCast=o.CastSuccess;}}
                    else if(opening<openingKeys.Length){digit=openingKeys[opening];if(o.CastEventsKnown){openingPending=true;openingCast=o.CastSuccess;}else opening++;}
                    else digit=loopKeys[(loopIndex++)%loopKeys.Length];
                    if(!rangedCombat&&(digit=='4'||digit=='5')&&o.CastEventsKnown){retryFinisher=digit;sliceRecovery=1;sliceCast=o.CastSuccess;}
                    ushort key=(ushort)(digit=='='?187:digit=='-'?189:digit);
                    var skill=Cmd(QuickAction.Skill,rebuilding?(retryFinisher=='5'?"破甲未确认成功，先补2邪恶攻击，确认成功后再试5。":"切割未确认成功，先补2邪恶攻击，确认成功后再试4。"):"尝试技能键 "+(char)key+"，不冒充施法成功。",key);skill.MeleeConfirmed=meleeConfirmed;skill.RangedSkill=rangedCombat;return skill;
                }
                return Cmd(QuickAction.None,"等待下一拍技能。",0);
            }
            if(ShouldAcquire(o,now))
            {if(route!=null)route.Interrupt(o);token=o.TargetToken;hunterRangeBlocked=hunterIssued=false;hunterErrorSeen=o.RangeErrorActive;if(skillRows!=null){skillRows.Choose();this.openingKeys="";this.loopKeys=skillRows.Keys;}lockedTargetName=o.TargetName;deathBase=o.TargetDeaths;creditBase=o.KillCredits;lockedName=o.NameHashKnown?o.TargetNameHash:0;missingTarget=missingCombatTarget=-1;reacquired=false;meleeConfirmed=false;contactHealth=o.Health;meleeBaseline=o.MeleeHits;openingPending=false;sliceRecovery=0;opening=loopIndex=deadFrames=escapes=escapePhase=combatEscapes=0;escapeCombat=false;combatCastAt=now;combatCasts=o.CastSuccess;patrolPending=searchTurning=false;blockedWalks=searchEscapes=0;looted=false;started=nextFace=nextSkill=progressAt=now;progressX=o.X;progressY=o.Y;progressCasts=o.CastSuccess;scans=0;return Cmd(QuickAction.None,"锁定怪物："+o.TargetName+(skillRows==null?"":"；随机技能行"+(skillRows.Selected+1)+"="+skillRows.Keys),0);}
            if(route!=null&&!o.InCombat)
            {
                if((route.SearchingNearby||!route.Returning)&&now>=nextScan){nextScan=now+1200;return Cmd(QuickAction.Scan,"移动中扫描附近怪物；附近无目标再前往巡航点。",9);}
                var movement=route.Step(o,now);movement.AttackHostile=attackHostile;movement.IgnoredTargets=new HashSet<uint>();foreach(var skip in skippedTargets)if(now<skip.Value)movement.IgnoredTargets.Add(skip.Key);return movement;
            }
            if(!o.InCombat&&searchTurning)
            {
                if(!MouseLookInput.TurnObserved(searchFacing,o.Facing))return now<searchDeadline?Cmd(QuickAction.None,"等待寻怪脱困转向确认，不前进。",0):Stop("寻怪脱困未确认实际转向，停止。");
                searchTurning=false;blockedWalks=0;
            }
            if(!o.InCombat&&patrolPending&&now>=patrolCheckAt)
            {
                patrolPending=false;double moved=(o.X-patrolX)*(o.X-patrolX)+(o.Y-patrolY)*(o.Y-patrolY);
                if(moved>=.00000009){blockedWalks=searchEscapes=0;}
                else if(++blockedWalks>=2)
                {
                    if(++searchEscapes>2)return Stop("寻怪前进持续受阻，停止检查障碍。");
                    searchTurning=true;searchFacing=o.Facing;searchDeadline=now+3000;
                    var turn=Cmd(QuickAction.Turn,"寻怪两次前进无位移：右键换向脱困，不按固定次数转圈。",(ushort)(searchEscapes%2==1?39:37));turn.MouseTurn=true;return turn;
                }
            }
            if(o.InCombat)
            {
                if(missingCombatTarget<0)missingCombatTarget=now;
                if(now-missingCombatTarget>=6000)return Stop("受到攻击后6秒仍未取得可验证的怪物目标。");
                nextScan=Math.Min(nextScan,now+200);
            }
            else missingCombatTarget=-1;
            if(now<nextScan)return Cmd(QuickAction.None,"等待附近目标扫描。",0);
            nextScan=now+1500;
            if(++scans>80)return Stop("附近巡航扫描两分钟仍未发现怪物，停止。");
            if(!o.InCombat&&(Math.Abs(o.X-x)>.025||Math.Abs(o.Y-y)>.025))
            {
                double desired=Math.Atan2(-(x-o.X)*1.5,-(y-o.Y));double delta=NavigationMath.Normalize(desired-o.Facing);
                if(Math.Abs(delta)>.15){var turn=Cmd(QuickAction.Turn,"附近巡航：转向启动位置附近。",(ushort)(delta>0?37:39));turn.Hold=Math.Max(40,Math.Min(260,(int)(Math.Abs(delta)/5.85*1000)));return turn;}
                var back=Cmd(QuickAction.Patrol,"持续巡航：行进中返回中心区域。",QuickMovement.Forward);back.Hold=800;patrolPending=true;patrolCheckAt=now+800;patrolX=o.X;patrolY=o.Y;return back;
            }
            if(!o.InCombat&&scans%2==0){nextScan=now+300;var walk=Cmd(QuickAction.Patrol,"持续前进并扫描；发现怪物后接近攻击。",QuickMovement.Forward);walk.Hold=1000;patrolPending=true;patrolCheckAt=now+1000;patrolX=o.X;patrolY=o.Y;return walk;}
            return Cmd(QuickAction.Scan,"Tab：扫描附近怪物。",9);
        }
        public bool HasDeathEvidence(FarmObservation o,uint expected)
        {return expected!=0&&expected==token&&o!=null&&o.Fresh&&(o.DeathEventsKnown&&o.DeathToken==expected&&o.TargetDeaths!=deathBase||o.KillCreditsKnown&&lockedName!=0&&o.KillCreditNameHash==lockedName&&o.KillCredits!=creditBase);}
        static bool HealthDropped(FarmObservation o,int previous)
        {return o.InCombat&&previous>=0&&previous<=100&&o.Health>=0&&o.Health<=100&&o.Health<previous;}
        QuickCommand HunterCombat(FarmObservation o,long now)
        {
            if(!o.RangeErrorActive)hunterErrorSeen=false;
            if(o.RangeErrorActive&&!hunterErrorSeen)
            {
                hunterErrorSeen=true;
                if(hunterIssued&&hunterIssuedToken==token&&now-hunterIssuedAt<=2500&&(!hunterDefending||hunterIssuedDefense))
                {
                    opening=hunterIssuedOpening;loopIndex=hunterIssuedLoop;hunterIndex=hunterIssuedIndex;
                    hunterRangeBlocked=true;hunterBlockedX=o.X;hunterBlockedY=o.Y;nextFace=now;nextSkill=now;
                    hunterIssued=false;
                }
            }
            if(hunterIssued&&hunterIssuedFailuresKnown&&o.CastEventsKnown&&o.CastFailed!=hunterIssuedFailures&&hunterIssuedToken==token&&now-hunterIssuedAt<=2500&&(!hunterDefending||hunterIssuedDefense))
            {
                opening=hunterIssuedOpening;loopIndex=hunterIssuedLoop;hunterIndex=hunterIssuedIndex;hunterIssued=false;nextSkill=now+500;
                var failed=Cmd(QuickAction.None,"猎人技能施放失败：保留刚才的按键，等待冷却和射程后重试，不跳过后续技能。",0);failed.Report=true;return failed;
            }
            if(hunterDefending&&!hunterAnnounced)
            {hunterAnnounced=true;nextFace=now+1500;var note=Cmd(QuickAction.None,"猎人检测到战斗中生命下降：放弃本轮剩余技能行，改为1、2循环，脱战后新怪恢复技能设置。",0);note.Report=true;return note;}
            char digit=hunterDefending?"12"[hunterIndex%2]:opening<openingKeys.Length?openingKeys[opening]:loopKeys[loopIndex%loopKeys.Length];
            ushort key=(ushort)(digit=='='?187:digit=='-'?189:digit);int bit=ActionBit(key);
            bool rangeKnown=bit!=0&&(o.ActionRangeKnownMask&bit)!=0;
            if(hunterRangeBlocked&&!o.RangeErrorActive&&rangeKnown&&(o.ActionsInRangeMask&bit)!=0&&
                (o.X-hunterBlockedX)*(o.X-hunterBlockedX)+(o.Y-hunterBlockedY)*(o.Y-hunterBlockedY)>1e-8)hunterRangeBlocked=false;
            if(!hunterRangeBlocked&&!hunterDefending&&(digit=='5'||digit=='6'||digit=='3')&&!rangeKnown)
            {var wait=Cmd(QuickAction.None,"猎人技能"+digit+"射程暂不可读，等待插件刷新；未推进技能顺序。",0);if(now>=nextRangedReport){wait.Report=true;nextRangedReport=now+5000;}return wait;}
            bool approach=hunterRangeBlocked||rangeKnown&&(o.ActionsInRangeMask&bit)==0;
            if(approach&&!rangedMoving)nextFace=now;
            if(rangedMoving&&!approach){rangedMoving=false;return Cmd(QuickAction.RangedStop,"猎人当前技能已进入射程，停止接近再施放。",0);}
            if(now>=nextFace)
            {nextFace=now+1500;rangedMoving=approach;var face=Cmd(QuickAction.Face,approach?(hunterRangeBlocked?"收到游戏距离太远反馈，停止重复施法：":"")+"猎人技能"+digit+"距离不足，先F接近目标，保留当前技能等待进入射程。":"猎人：校正目标朝向后继续技能。",70);face.AllowApproach=approach;return face;}
            if(approach)return Cmd(QuickAction.None,"等待进入当前猎人技能的射程。",0);
            if(now<nextSkill)return Cmd(QuickAction.None,"等待猎人技能间隔。",0);
            if((o.RepeatKnownMask&o.RepeatingMask&bit)!=0)
            {if(hunterDefending)hunterIndex++;else if(opening<openingKeys.Length)opening++;else loopIndex++;nextSkill=now+interval;return Cmd(QuickAction.None,"当前自动射击已开启，保留效果并继续下一按键。",0);}
            if(!RangedKeyAllowed(key,o,true))
            {var wait=Cmd(QuickAction.None,"猎人技能"+digit+"冷却中或暂不可用：等待就绪，保留当前技能顺序。",0);if(now>=nextRangedReport){wait.Report=true;nextRangedReport=now+3000;}return wait;}
            nextSkill=now+interval;var skill=Cmd(QuickAction.Skill,"猎人"+(hunterDefending?"受袭循环":"技能顺序")+"：按"+digit+"。",key);
            skill.HunterSkill=true;skill.HunterDefense=hunterDefending;skill.HealthAtIssue=o.Health;skill.CastFailuresKnown=o.CastEventsKnown;skill.CastFailuresAtIssue=o.CastFailed;return skill;
        }
        static int ActionBit(ushort key){return key>=49&&key<=55?1<<(key-49):0;}
        int FirstRangedAction(FarmObservation o){foreach(char key in openingKeys+loopKeys){int bit=ActionBit(key);if((o.RangedActionsMask&bit)!=0)return bit;}return 0;}
        static bool CloseContact(FarmObservation o){return (o.MeleeActionsMask&o.ActionRangeKnownMask&o.ActionsInRangeMask)!=0;}
        static bool RangedKeyAllowed(ushort key,FarmObservation o,bool contact,bool inputStarted=false)
        {
            int bit=ActionBit(key);if(bit==0)return true;
            if((o.ActionRangeKnownMask&bit)!=0&&(o.ActionsInRangeMask&bit)==0)return false;
            if(!inputStarted&&(o.RepeatKnownMask&o.RepeatingMask&bit)!=0)return false;
            if((o.MeleeActionsMask&bit)!=0&&!contact&&!CloseContact(o))return false;
            return inputStarted||(o.KnownActionsMask&bit)==0||(o.UsableActionsMask&o.ReadyActionsMask&bit)!=0;
        }
        bool CanAcquire(FarmObservation o){return CanAcquire(o,attackHostile);}
        static bool CanAcquire(FarmObservation o,bool includeHostile)
        {return GrindRecoveryPolicy.CanAttack(o)&&(o.InCombat&&(!o.TargetVictimKnown||o.TargetTargetsPlayer)||(o.TargetReaction==4||includeHostile&&o.TargetReaction>=1&&o.TargetReaction<=3)&&!Contested(o));}
        public static bool Contested(FarmObservation o){return o!=null&&!(o.InCombat&&(!o.TargetVictimKnown||o.TargetTargetsPlayer))&&(o.TargetClaimKnown&&o.TargetClaimedByOther||o.TargetCombatKnown&&o.TargetInCombat&&(!o.TargetVictimKnown||!o.TargetTargetsPlayer));}
        public bool ShouldAcquire(FarmObservation o,long now)
        {long until;return CanAcquire(o)&&(o.InCombat||!skippedTargets.TryGetValue(o.TargetToken,out until)||now>=until)&&(route==null||route.MayAcquire(o));}
        public bool CanInspectCorpse(FarmObservation o)
        {return token==0&&!resting&&o!=null&&!o.InCombat&&!o.BagFull&&o.Health>=80&&(route==null||!route.Returning&&route.OnRoute(o));}
        QuickCommand AbandonChase(long now,string reason)
        {
            var expired=new List<uint>();foreach(var skip in skippedTargets)if(now>=skip.Value)expired.Add(skip.Key);foreach(uint key in expired)skippedTargets.Remove(key);
            if(token!=0)skippedTargets[token]=now+30000;
            if(skippedTargets.Count>64){uint oldest=0;long until=long.MaxValue;foreach(var skip in skippedTargets)if(skip.Value<until){oldest=skip.Key;until=skip.Value;}skippedTargets.Remove(oldest);}
            token=0;corpse=null;looted=false;escapePhase=0;escapeCombat=false;missingTarget=-1;nextScan=now;route.Resume();return Cmd(QuickAction.ReturnToRoute,reason,0);
        }
        QuickCommand ConfirmDeath(FarmObservation o,long now)
        {
            if(++deadFrames<2)return Cmd(QuickAction.None,"确认第二帧死亡证据。",0);
            if(!looted)
            {
                Kills++;lastKilled=token;corpseReselections=unconfirmedLosses=0;looted=true;lootUntil=now+1600;
                corpse=new QuickCorpse(token,skinning,o,now,false,lockedTargetName,route!=null&&route.Plan.SkinWithInteract,route!=null&&route.Plan.NoChatCombat,route==null?"F8":route.Plan.LastTargetKey);creditedLoots=creditedSkins=0;
            }
            return AdvanceCorpse(o,now);
        }
        QuickCommand AdvanceCorpse(FarmObservation o,long now)
        {
            if(now<corpseBlockedUntil){inspectedCorpses.Add(token);token=0;corpse=null;looted=false;nextScan=now;return Cmd(QuickAction.None,"等待恢复条件满足，暂不执行尸体动作。",0);}
            var c=corpse.Step(o,now,deferredCorpses.Count>0);Loots+=corpse.Loots-creditedLoots;Skins+=corpse.Skins-creditedSkins;creditedLoots=corpse.Loots;creditedSkins=corpse.Skins;
            if(corpse.Interrupted)
            {
                if(deferredCorpses.Count>=8)return Stop("待处理尸体超过8具，停止检查持续攻击来源。");
                corpseBlockedUntil=now+60000;inspectedCorpses.Add(token);deferredCorpses.Clear();token=0;corpse=null;looted=false;nextScan=now;urgentDefenseScan=true;c.Report=true;
                if(!(o.Casting||o.Channeling)){urgentDefenseScan=false;c.Action=QuickAction.Scan;c.Key=9;c.Reason="拾取剥皮受攻击，立即选择附近攻击者，脱战后恢复尸体处理。";}
                if(o.Casting||o.Channeling){c.Action=QuickAction.InterruptSkin;c.Key=27;c.Reason="剥皮期间受到攻击：取消本次施法，先处理攻击者。";}
            }
            else if(corpse.Done){inspectedCorpses.Add(token);token=0;corpse=null;nextScan=now;if(route!=null)route.BeginNearbySearch(o,now);}
            c.RouteContext=route;return c;
        }
        public static bool Matches(QuickCommand c,FarmObservation o)
        {return Matches(c,o,false);}
        public static bool MatchesChatCommand(QuickCommand c,FarmObservation o)
        {return c!=null&&(c.Action==QuickAction.CorpseSelect||c.Action==QuickAction.Reacquire||c.Action==QuickAction.Scan||c.Action==QuickAction.ReturnToRoute)&&o!=null&&o.ChatOpenKnown&&Matches(c,o,true);}
        static bool Matches(QuickCommand c,FarmObservation o,bool ownedChat)
        {
            if(o==null||!o.Fresh||!o.KnownCore||o.ReadFailures!=0||o.PlayerDead||o.PlayerGhost)return false;
            if(c.RouteContext!=null&&!c.RouteContext.CanOperate(o))return false;
            if(!ownedChat&&o.ChatOpenKnown&&o.ChatOpen)return false;
            if(c.Action==QuickAction.HunterRevivePet)return !o.InCombat&&o.Health==100&&o.CastingKnown&&(c.InputStarted||!o.Casting&&!o.Channeling&&o.PetStateKnown&&(!o.PetExists||o.PetDead));
            if(c.Action==QuickAction.HunterAspect)return !o.InCombat&&o.Health==100&&o.CastingKnown&&!o.Casting&&!o.Channeling;
            if(c.HunterSkill)
            {
                if(o.RangeErrorActive&&!c.HunterDefense&&!c.InputStarted)return false;
                if(c.HunterDefense&&!o.InCombat)return false;
                if(!c.HunterDefense&&HealthDropped(o,c.HealthAtIssue))return false;
                if(!RangedKeyAllowed(c.Key,o,true,c.InputStarted))return false;
            }
            if(c.Action==QuickAction.StealthEnter||c.Action==QuickAction.StealthHold)return !o.InCombat;
            if(c.Action==QuickAction.StealthExit)return !o.InCombat&&o.Health==100;
            if(c.Action==QuickAction.Vanish)return o.InCombat&&o.Health>0&&o.Health<15&&o.PowerType==3;
            if(c.Action==QuickAction.Skill&&!c.HunterSkill&&!c.RangedSkill&&!c.MeleeConfirmed&&(!o.RangeKnown||!o.TargetInRange))return false;
            if(c.Action==QuickAction.Skill&&c.RangedSkill&&!RangedKeyAllowed(c.Key,o,c.MeleeConfirmed,c.InputStarted))return false;
            if(!o.InCombat&&Contested(o)&&(c.Action==QuickAction.Face||c.Action==QuickAction.Skill||c.Action==QuickAction.RouteProbe))return false;
            if(c.Action==QuickAction.InterruptSkin)return o.InCombat&&(o.Casting||o.Channeling);
            if(c.RouteMovement)return !o.InCombat&&!(CanAcquire(o,c.AttackHostile)&&c.RouteContext.MayAcquire(o)&&(c.IgnoredTargets==null||!c.IgnoredTargets.Contains(o.TargetToken)));
            if(c.Action==QuickAction.ReturnToRoute)return c.RouteContext!=null&&c.RouteContext.Returning&&!o.InCombat;
            if(c.Action==QuickAction.RouteProbe)return c.RouteContext!=null&&!c.RouteContext.Returning&&!o.InCombat&&CanAcquire(o,c.AttackHostile)&&o.TargetToken==c.Token&&!o.TargetPositionKnown;
            if(c.RouteContext!=null&&(c.Action==QuickAction.Face||c.Action==QuickAction.Skill)&&!c.RouteContext.TargetAllowed(o))return false;
            if(c.Action==QuickAction.Scan)return true;
            if(c.Action==QuickAction.CorpseSelect)return !o.InCombat;
            if(c.Action==QuickAction.Reacquire)return !o.TargetExists||o.TargetIdentityKnown&&o.TargetToken==c.Token&&!o.TargetIsPlayer;
            if(!ownedChat&&o.ChatOpenKnown&&o.ChatOpen)return false;
            if(c.Action==QuickAction.Patrol)return !o.InCombat&&!CanAcquire(o,c.AttackHostile);
            if(c.Action==QuickAction.Turn)return !o.InCombat&&!CanAcquire(o,c.AttackHostile);
            if(c.Action==QuickAction.Food||c.Action==QuickAction.Drink)return !o.InCombat;
            if(!o.TargetExists||!o.TargetIdentityKnown||c.Token==0||o.TargetToken!=c.Token||o.TargetIsPlayer||!o.AttackabilityKnown||!o.TargetDeadKnown)return false;
            if(c.Action==QuickAction.CorpseApproach)return o.TargetDead&&!o.InCombat&&(o.LootKnown&&o.Lootable||o.SkinKnown&&o.Skinnable);
            if(c.Action==QuickAction.BackOff||c.Action==QuickAction.DetourTurn||c.Action==QuickAction.DetourForward)return (c.CombatRecovery?o.InCombat:!o.InCombat)&&!o.TargetDead&&o.TargetCanAttack;
            if(c.Action==QuickAction.Skin)return o.TargetDead&&!o.InCombat&&o.SkinKnown&&o.Skinnable&&(c.Key==70?o.InteractBindingKnown&&o.InteractBindingCorrect:o.SkinBindingKnown&&o.SkinBindingCorrect);
            return c.Action==QuickAction.Loot?o.TargetDead&&!o.InCombat:!o.TargetDead&&o.TargetCanAttack;
        }
    }
}
