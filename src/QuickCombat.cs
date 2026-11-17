using System;

namespace WoWProbe
{
    public enum QuickAction { None, Scan, Turn, Face, Skill, Loot, Skin, CorpseSelect, Food, Drink, BackOff, DetourTurn, DetourForward, Patrol, Defensive, Reacquire, Stop, RouteProbe }
    public sealed class QuickCommand
    {
        public QuickAction Action; public ushort Key; public uint Token; public string Reason;public int Hold;public bool Report,MouseTurn,RouteMovement;public QuickRouteFollower RouteContext;
    }

    // Nearby combat only. The game's confirmed F/click-to-move binding supplies
    // facing and approach; there is no guessed target position or blind forward sweep.
    public sealed class QuickCombat
    {
        readonly uint map; readonly int mode; readonly string zone;
        readonly QuickRouteFollower route;
        readonly double x,y; readonly int interval;
        uint token,deathBase,lastKilled,creditBase,lockedName; int opening, sequence=-1, deadFrames, scans,corpseReselections,unconfirmedLosses;
        long last=-1, nextSkill, nextFace, nextScan, started, lootUntil;
        bool stopped, looted,initialRest=true,resting,food,drink,reacquired;
        long restStarted,missingCombatTarget=-1,missingTarget=-1,nextDefensive;
        double progressX,progressY; uint progressCasts; long progressAt;
        int escapes,escapePhase;
        double escapeFacing;long turnDeadline;
        bool patrolPending,searchTurning;double patrolX,patrolY,searchFacing;int blockedWalks,searchEscapes;long searchDeadline,patrolCheckAt;
        readonly bool skinning;QuickCorpse corpse;int creditedLoots,creditedSkins;
        public int Loots {get;private set;}public int Skins {get;private set;}
        public int Kills { get; private set; }
        public QuickCombat(FarmObservation initial,int interval):this(initial,interval,false){}
        public QuickCombat(FarmObservation initial,int interval,bool skinning):this(initial,interval,skinning,null){}
        public QuickCombat(FarmObservation initial,int interval,bool skinning,QuickRouteFollower route)
        {
            if(initial==null||!initial.Fresh||!initial.KnownCore)throw new InvalidOperationException("需要连续有效的插件数据。");
            if(interval!=500&&interval!=750)throw new ArgumentOutOfRangeException("interval");
            map=initial.MapId;mode=initial.MapMode;zone=initial.Zone;x=initial.X;y=initial.Y;this.interval=interval;this.skinning=skinning;this.route=route;
        }
        QuickCommand Cmd(QuickAction a,string r,ushort k)
        {return new QuickCommand{Action=a,Reason=r,Key=k,Token=token,RouteContext=route};}
        QuickCommand Stop(string reason){stopped=true;return Cmd(QuickAction.Stop,reason,0);}
        public QuickCommand Step(FarmObservation o,long now)
        {
            if(stopped)return Cmd(QuickAction.Stop,"运行已停止。",0);
            if(now<0||now<last)return Stop("运行时钟异常。");last=now;
            if(o==null||!o.Fresh||!o.KnownCore||o.ReadFailures!=0)return Stop("插件状态失效，停止输入。");
            if(o.PlayerDead||o.PlayerGhost)return Stop("角色死亡或处于灵魂状态。");
            if(o.ChatOpenKnown&&o.ChatOpen)return Stop("聊天输入框打开，停止战斗按键。");
            if(o.BagFull)return Stop("背包已满。");
            if(o.Health<0||o.Health>100)return Stop("生命值不可读，不能确认恢复完成。");
            if(o.MapId!=map||o.MapMode!=mode||o.Zone!=zone)return Stop("地图发生变化。");
            if(route!=null){if(!route.OnRoute(o))return Stop("角色离开指定路线范围，停止追击；请放回路线后继续。");}
            else if(Double.IsNaN(o.X)||Double.IsNaN(o.Y)||Math.Abs(o.X-x)>0.04||Math.Abs(o.Y-y)>0.04)return Stop("离开启动位置四坐标点范围，停止追击。");
            if(token!=0&&!looted&&now-started>=45000)return Stop("单怪45秒未确认击杀，停止并检查朝向、距离和伤害。");
            if(resting&&!o.InCombat&&now-restStarted>=60000)return Stop("60秒内未恢复满，检查食物和饮水绑定。");
            if(sequence==o.Sequence)return Cmd(QuickAction.None,"等待新一帧状态。",0);sequence=o.Sequence;
            if(!o.CastingKnown)return Stop("施法状态不可读。");
            if(token==0)
            {
                if(!resting&&!o.InCombat&&o.FoodCount==0)return Stop("食物栏位已耗尽，补充食物后再继续打怪。");
                bool mana=o.PowerType==0;
                if(mana&&(o.Power<0||o.Power>100))return Stop("法力值不可读，不能确认饮水完成。");
                if(!resting&&!o.InCombat&&(o.Health<(initialRest?99:80)||mana&&o.Power<(initialRest?99:80)))
                {resting=true;restStarted=now;food=drink=false;if(route!=null)route.Interrupt(o);}
                if(resting&&o.InCombat){resting=false;initialRest=true;food=drink=false;}
                if(resting)
                {
                    if(o.Health>=99&&(!mana||o.Power>=99)){resting=false;initialRest=false;return Cmd(QuickAction.None,"已观察到血量和适用法力恢复至满。",0);}
                    if(now-restStarted>=60000)return Stop("60秒内未恢复满，检查食物和饮水绑定。");
                    if(!food&&o.Health<99){food=true;return Cmd(QuickAction.Food,"血量低于恢复要求，按=吃食物。",0xBB);}
                    if(mana&&!drink&&o.Power<99)return Stop("附近打怪入口仅配置了盗贼；法力职业请先配置饮水按键。");
                    return Cmd(QuickAction.None,"吃喝恢复中，等待血条达到99%。",0);
                }
                initialRest=false;
            }
            if(o.Casting||o.Channeling)return Cmd(QuickAction.None,"等待当前施法。",0);
            if(token!=0)
            {
                if(looted&&corpse!=null)return AdvanceCorpse(o,now);
                if(HasDeathEvidence(o,token))return ConfirmDeath(o,now);
                if(!o.TargetExists||!o.TargetIdentityKnown||o.TargetToken!=token)
                {
                    if(!o.InCombat&&o.TargetExists&&o.TargetIdentityKnown&&o.TargetToken==lastKilled&&o.TargetDeadKnown&&o.TargetDead&&o.CastSuccess==progressCasts&&corpseReselections<3)
                    {token=0;corpseReselections++;nextScan=now+400;return Cmd(QuickAction.None,"交互切回刚确认的尸体，重新选怪；不增加击杀。",0);}
                    if(missingTarget<0)missingTarget=now;
                    if(!o.TargetExists&&!reacquired){reacquired=true;return Cmd(QuickAction.Reacquire,"目标消失：重选上一目标核实死亡，不推定击杀。",0);}
                    if(now-missingTarget<2500)return Cmd(QuickAction.None,"目标变化，暂停输入等待死亡事件；不推定击杀。",0);
                    if(++unconfirmedLosses>3)return Stop("连续四次丢失目标且无死亡证据，停止检查目标读取。");
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
                if(o.InCombat&&o.PowerType==3&&o.Health<45&&now>=nextDefensive)
                {nextDefensive=now+120000;return Cmd(QuickAction.Defensive,"血量低于45%，尝试9闪避；继续当前战斗。",57);}
                if(escapePhase!=0)
                {
                    if(o.InCombat){escapePhase=0;progressAt=now;}
                    else if(escapePhase==1){escapePhase=2;escapeFacing=o.Facing;turnDeadline=now+3000;return Cmd(QuickAction.DetourTurn,"脱困：按住鼠标右键横拖，改变角色方向。",(ushort)(escapes%2==1?39:37));}
                    else
                    {
                        if(!MouseLookInput.TurnObserved(escapeFacing,o.Facing))
                            return now<turnDeadline?Cmd(QuickAction.None,"等待确认角色实际转向，暂不前进。",0):Stop("右键转向后未观察到角色朝向变化，停止前进。");
                        escapePhase=0;progressAt=now;nextFace=now+500;return Cmd(QuickAction.DetourForward,"脱困：已确认转向，沿新方向短距离移动，再重新面向目标。",87);
                    }
                }
                double moved=(o.X-progressX)*(o.X-progressX)+(o.Y-progressY)*(o.Y-progressY);
                if(moved>=0.00000009||o.CastEventsKnown&&o.CastSuccess!=progressCasts)
                {progressX=o.X;progressY=o.Y;progressCasts=o.CastSuccess;progressAt=now;}
                if(!o.InCombat&&now-progressAt>=4500)
                {
                    if(escapes>=2)return Stop("连续两次换向仍无法接近怪物，停止以免持续顶住障碍。");
                    escapes++;escapePhase=1;progressAt=now;return Cmd(QuickAction.BackOff,"位置持续不变且未进入战斗：取消直走，后退后换向。",83);
                }
                if(now-started>=45000)return Stop("单怪45秒未确认击杀，停止并检查朝向、距离和伤害。");
                if(now>=nextFace)
                {nextFace=now+1500;return Cmd(QuickAction.Face,"F：让游戏面向并接近锁定怪物。",0x46);}
                if(now>=nextSkill)
                {
                    nextSkill=now+interval;string keys="242223";
                    ushort key=(ushort)(opening<keys.Length?keys[opening++]: '2');
                    return Cmd(QuickAction.Skill,"尝试技能键 "+(char)key+"，不冒充施法成功。",key);
                }
                return Cmd(QuickAction.None,"等待下一拍技能。",0);
            }
            if(CanAcquire(o)&&(route==null||route.MayAcquire(o)))
            {if(route!=null)route.Interrupt(o);token=o.TargetToken;deathBase=o.TargetDeaths;creditBase=o.KillCredits;lockedName=o.NameHashKnown?o.TargetNameHash:0;missingTarget=missingCombatTarget=-1;reacquired=false;opening=deadFrames=escapes=escapePhase=0;patrolPending=searchTurning=false;blockedWalks=searchEscapes=0;looted=false;started=nextFace=nextSkill=progressAt=now;progressX=o.X;progressY=o.Y;progressCasts=o.CastSuccess;scans=0;return Cmd(QuickAction.None,"锁定怪物："+o.TargetName,0);}
            if(route!=null&&!o.InCombat)
            {
                if(route.ProbeDue(o,now)){var probe=Cmd(QuickAction.RouteProbe,"沿路线走近黄名目标并尝试技能；不离线追击未知位置目标。",50);probe.Token=o.TargetToken;return probe;}
                if(now>=nextScan){nextScan=now+1200;return Cmd(QuickAction.Scan,"沿指定路线扫描附近怪物。",9);}
                return route.Step(o,now);
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
                var back=Cmd(QuickAction.Patrol,"持续巡航：行进中返回中心区域。",87);back.Hold=800;patrolPending=true;patrolCheckAt=now+800;patrolX=o.X;patrolY=o.Y;return back;
            }
            if(!o.InCombat&&scans%2==0){nextScan=now+300;var walk=Cmd(QuickAction.Patrol,"持续前进并扫描；发现怪物后接近攻击。",87);walk.Hold=1000;patrolPending=true;patrolCheckAt=now+1000;patrolX=o.X;patrolY=o.Y;return walk;}
            return Cmd(QuickAction.Scan,"Tab：扫描附近怪物。",9);
        }
        public bool HasDeathEvidence(FarmObservation o,uint expected)
        {return expected!=0&&expected==token&&o!=null&&o.Fresh&&(o.DeathEventsKnown&&o.DeathToken==expected&&o.TargetDeaths!=deathBase||o.KillCreditsKnown&&lockedName!=0&&o.KillCreditNameHash==lockedName&&o.KillCredits!=creditBase);}
        static bool CanAcquire(FarmObservation o)
        {return GrindRecoveryPolicy.CanAttack(o)&&(o.InCombat||o.TargetReaction==4);}
        QuickCommand ConfirmDeath(FarmObservation o,long now)
        {
            if(++deadFrames<2)return Cmd(QuickAction.None,"确认第二帧死亡证据。",0);
            if(!looted)
            {
                Kills++;lastKilled=token;corpseReselections=unconfirmedLosses=0;looted=true;lootUntil=now+1600;
                corpse=new QuickCorpse(token,skinning,o,now);creditedLoots=creditedSkins=0;
            }
            return AdvanceCorpse(o,now);
        }
        QuickCommand AdvanceCorpse(FarmObservation o,long now)
        {
            var c=corpse.Step(o,now);Loots+=corpse.Loots-creditedLoots;Skins+=corpse.Skins-creditedSkins;creditedLoots=corpse.Loots;creditedSkins=corpse.Skins;
            if(corpse.Done){token=0;corpse=null;nextScan=now+400;}
            c.RouteContext=route;return c;
        }
        public static bool Matches(QuickCommand c,FarmObservation o)
        {
            if(o==null||!o.Fresh||!o.KnownCore||o.ReadFailures!=0||o.PlayerDead||o.PlayerGhost)return false;
            if(c.RouteContext!=null&&!c.RouteContext.OnRoute(o))return false;
            if(o.ChatOpenKnown&&o.ChatOpen)return false;
            if(c.RouteMovement)return !o.InCombat&&!(CanAcquire(o)&&c.RouteContext.MayAcquire(o));
            if(c.Action==QuickAction.RouteProbe)return c.RouteContext!=null&&!c.RouteContext.Returning&&!o.InCombat&&CanAcquire(o)&&o.TargetToken==c.Token&&!o.TargetPositionKnown;
            if(c.RouteContext!=null&&(c.Action==QuickAction.Face||c.Action==QuickAction.Skill)&&!c.RouteContext.TargetAllowed(o))return false;
            if(c.Action==QuickAction.Scan)return true;
            if(c.Action==QuickAction.CorpseSelect)return !o.InCombat;
            if(c.Action==QuickAction.Reacquire)return !o.TargetExists||o.TargetIdentityKnown&&o.TargetToken==c.Token&&!o.TargetIsPlayer;
            if(o.ChatOpenKnown&&o.ChatOpen)return false;
            if(c.Action==QuickAction.Patrol)return !o.InCombat&&!CanAcquire(o);
            if(c.Action==QuickAction.Turn)return !o.InCombat&&!CanAcquire(o);
            if(c.Action==QuickAction.Food||c.Action==QuickAction.Drink)return !o.InCombat;
            if(!o.TargetExists||!o.TargetIdentityKnown||c.Token==0||o.TargetToken!=c.Token||o.TargetIsPlayer||!o.AttackabilityKnown||!o.TargetDeadKnown)return false;
            if(c.Action==QuickAction.BackOff||c.Action==QuickAction.DetourTurn||c.Action==QuickAction.DetourForward)return !o.InCombat&&!o.TargetDead&&o.TargetCanAttack;
            if(c.Action==QuickAction.Skin)return o.TargetDead&&!o.InCombat&&o.SkinKnown&&o.Skinnable&&o.SkinBindingKnown&&o.SkinBindingCorrect;
            return c.Action==QuickAction.Loot?o.TargetDead&&!o.InCombat:!o.TargetDead&&o.TargetCanAttack;
        }
    }
}
