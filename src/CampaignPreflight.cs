using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace WoWProbe
{
    public sealed class PreflightCheck
    {
        public string Code,Label,Detail,State;
    }
    public sealed class PreflightReport
    {
        public readonly List<PreflightCheck> Checks=new List<PreflightCheck>();
        public bool ReadyToArm{get{if(Checks.Count==0)return false;foreach(var c in Checks)if(c.State=="block")return false;return true;}}
        public string ToMarkdown()
        {
            var text=new StringBuilder("# 完整方案启动前检查\n\ngame_validation: pending\n\n");text.Append(ReadyToArm?"当前可核验的准备条件已满足。":"当前存在阻断项，请按下表处理。离线模拟仍可单独使用。");
            text.Append("\n\n| 项目 | 状态 | 说明 |\n| --- | --- | --- |\n");foreach(var c in Checks)text.Append("| ").Append(Escape(c.Label)).Append(" | ").Append(c.State=="pass"?"已满足":c.State=="block"?"未满足":"运行时检查").Append(" | ").Append(Escape(c.Detail)).Append(" |\n");
            return text.Append("\n自检不启动任何键鼠输入；准备条件满足、模拟完成均不代表实机验收通过。运行时仍会持续校验窗口、状态和每次动作。\n").ToString();
        }
        internal static string Escape(string s)
        {
            if(s==null)return "";var b=new StringBuilder();foreach(char c in s){if(c<32||c==127){b.Append(' ');continue;}if("\\|[]()`*_<>".IndexOf(c)>=0)b.Append('\\');b.Append(c);}return b.ToString();
        }
    }
    public static class CampaignPreflight
    {
        public static PreflightReport Evaluate(CampaignPlan plan,OperationObservation observation,bool inputFaulted,string avatarDirectory)
        {
            var r=new PreflightReport();Add(r,"input_lock","输入故障锁",!inputFaulted,"未发现尚未处理的输入故障。","曾发生输入发送/释放故障；手动检查并松开按键和鼠标后重启工具。");
            CampaignPlan p=null;try{if(plan==null)throw new FormatException("尚未载入完整方案。");p=plan.Clone();p.Validate();Add(r,"plan","计划与三类路线",true,"路线角色、地图、连接点、方向和可逆性有效。","");}catch(Exception ex){p=null;Add(r,"plan","计划与三类路线",false,"",ex.Message);}
            var o=observation;bool data=o!=null&&o.Fresh&&o.Farm!=null&&o.Farm.Fresh&&o.Farm.KnownCore&&o.Farm.ReadFailures==0&&o.Sequence==o.Farm.Sequence;
            Add(r,"data","同步状态数据",data,"三面板同序号、状态有效且无读取错误。","请更新插件、选择游戏窗口、定位色块并等待持续更新。");
            if(p==null||p.Settings==null||p.Battle==null||p.FarmRoute==null)return r;
            var s=p.Settings;var b=p.Battle;Add(r,"bindings","全部按键确认",s.BindingsConfirmed&&b.BindingsConfirmed,"已人工确认移动、技能、NPC、背包及补给键。","请在游戏中逐项核对并勾选按键确认。");
            Add(r,"calibration","当前地图标定",b.Calibration!=null&&b.Calibration.Confirmed,"已人工核对当前地图的标定。","导入实测标定并勾选当前地图标定确认。");
            Add(r,"layout_confirmation","界面位置确认",s.UiConfirmed,"已人工核对弹窗、背包和出售页。","请核对按钮及格子位置，并勾选界面确认。");
            Add(r,"resume","允许中断后续跑",s.AutoResumeConfirmed,"已允许中断处理完成后回原路线。","请明确勾选允许处理完成后继续原打怪路线。");
            if(b.AllowApproach)Add(r,"approach_confirmation","接近所需坐标确认",b.ApproachPositionConfirmed,"已人工验证目标坐标来源；每次仍检查实时数据。","启用接近时必须实际验证目标坐标能力并勾选确认。");
            if(s.RequirePortraitMatch)
            {
                string error=null;try{foreach(var target in b.Targets)if(target.Kind=="monster")using(var image=PortraitMatcher.LoadReference(avatarDirectory,target)){} }catch(Exception ex){error=ex.Message;}
                Add(r,"portraits","已保存头像参考",error==null,"所有保存怪物的参考图均可读取，实际相似度运行时检查。","参考图缺失或无效："+error);
            }else Pending(r,"portraits","头像辅助","未启用；完整名称与实时目标身份仍始终必需。");
            if(o==null||o.Farm==null)return r;var f=o.Farm;
            Add(r,"map","当前地图",o.MapId==p.FarmRoute.MapId&&f.MapId==o.MapId&&f.MapMode==p.FarmRoute.MapMode&&String.Equals(f.Zone,p.FarmRoute.Zone,StringComparison.Ordinal),"与方案绑定的地图、模式和区域一致。","当前地图与路线不一致；不要直接套用合成演示坐标。");
            Add(r,"geometry","客户区尺寸",o.GeometryKnown&&o.LayoutToken!=0&&o.ClientWidth==s.ClientWidth&&o.ClientHeight==s.ClientHeight,"布局可读且尺寸与已核对设置一致。","读取当前客户区尺寸，重新定位并核对窗口布局。");
            Add(r,"position","当前坐标与朝向",NavigationMath.InRange(f.X,0,1)&&NavigationMath.InRange(f.Y,0,1)&&NavigationMath.InRange(f.Facing,0,Math.PI*2),"坐标和朝向可读。","坐标或朝向未知、超范围，请先完成只读验证。");
            Add(r,"health","角色血量",f.Health>=0&&f.Health<=100&&(f.PlayerDead||f.PlayerGhost||f.Health>b.CriticalHealth),"当前血量状态可由对应流程处理。","血量未知或活着时低于紧急停止阈值，请先人工处理。");
            Add(r,"combat","当前战斗",!f.InCombat,"当前没有意外战斗。","请先脱战；不能在未锁定本会话怪物时启动。");
            if(!f.PlayerDead&&!f.PlayerGhost)Add(r,"power","资源与施法状态",f.CastingKnown&&f.PowerType>=0&&f.PowerType<255&&(f.PowerType!=0||(f.Power>=0&&f.Power<=100))&&!f.Casting&&!f.Channeling,"当前资源类型、必要法力和施法状态有效。","资源或施法状态未知，或当前仍在施法/引导。");
            Add(r,"cursor","鼠标指针状态",o.CursorKnown&&o.CursorEmpty,"指针未携带物品。","先放回指针上的物品；状态未知时不能自动输入。");
            Add(r,"popup","阻挡弹窗",o.ConfirmationKnown&&!o.ConfirmationVisible,"没有需要人工确认的弹窗。","处理确认弹窗；弹窗状态未知时先检查默认界面适配。");
            if(o.IncomingResurrectionKnown&&o.IncomingResurrection||s.EnableRecovery&&(f.PlayerDead||f.PlayerGhost))Add(r,"resurrection_invite","他人复活邀请",o.IncomingResurrectionKnown&&!o.IncomingResurrection,"没有待人工处理的他人复活邀请。","先人工处理复活邀请或验证对应只读能力。");else Pending(r,"resurrection_invite","他人复活邀请","进入死亡恢复后再次验证，不能自动接受邀请。");
            Add(r,"capacity","背包空位",o.FreeSlotsKnown&&o.FreeSlots>=0,"空位可读；包满由已启用的商人流程处理。","背包空位未知，请先核对插件能力。");
            if(o.FreeSlotsKnown&&o.FreeSlots==0&&!s.EnableVendor)Add(r,"vendor_enabled","包满处理",false,"","当前已包满且售卖未启用，请人工清理或配置商人流程。");
            if(f.PlayerGhost)
            {
                Add(r,"recovery_enabled","幽灵恢复流程",s.EnableRecovery,"已启用死亡恢复。","当前为幽灵，需要配置死亡恢复路线。");
                bool corpse=o.CorpseKnown&&NavigationMath.InRange(o.CorpseX,0,1)&&NavigationMath.InRange(o.CorpseY,0,1);Add(r,"corpse","实际尸体坐标",corpse,"使用客户端提供的实际尸体位置。","缺少实际尸体坐标，不能以死亡坐标替代。");
                if(corpse&&s.EnableRecovery)try{TravelPlanner.ToCorpse(p,o,p.StartIndex);Add(r,"route_connection","墓地与尸体路线",true,"当前幽灵可接入记录的跑尸路线。","");}catch(Exception ex){Add(r,"route_connection","墓地与尸体路线",false,"",ex.Message);}
                if(o.ResurrectVisible)Add(r,"resurrect","当前复活按钮",o.ResurrectKnown&&o.ResurrectEnabled&&o.RecoveryDelayKnown&&o.RecoveryDelayMs==0&&Region(o,o.ResurrectRegion),"当前复活按钮和冷却有效，仍需运行时复核。","按钮未知、不可用或恢复冷却尚未结束。");else Pending(r,"resurrect","复活按钮与冷却","到尸体附近后核验真实按钮、可用状态和零冷却。");
            }
            else if(f.PlayerDead)
            {
                Add(r,"recovery_enabled","死亡恢复流程",s.EnableRecovery,"已启用死亡恢复。","当前死亡且恢复未启用。");
                Add(r,"release","当前释放灵魂按钮",o.ReleaseKnown&&o.ReleaseVisible&&o.ReleaseEnabled&&Region(o,o.ReleaseRegion),"已知、可见且可用；运行时还需不同帧死亡确认。","先验证默认释放弹窗及按钮位置，不能使用猜测坐标。");
            }
            else
            {
                var working=p.FarmReverse?p.FarmRoute.Reversed():p.FarmRoute;var anchor=working.Points[Math.Max(0,p.StartIndex-1)];Add(r,"route_connection","打怪起点/断点连接",NavigationMath.Distance(f.X-anchor.X,f.Y-anchor.Y,b.Calibration.AxisScale)<=s.JoinTolerance,"当前位于所选起点或断点的允许范围内。","先人工沿可通行路径移动到起点/断点附近。");
                Pending(r,"release","释放与复活按钮","当前活着，不要求死亡弹窗预先显示；事件发生后再验证。");
            }
            var saved=TargetCatalog.Match(b,f);if(saved!=null)
            {
                int required=0;foreach(char skill in b.SkillSequence)required|=1<<(skill-'1');
                bool known=f.ActionsKnown&&(f.KnownActionsMask&required)==required;
                if(!known)Add(r,"target_actions","当前配置技能",false,"","已选中保存的怪物，但配置技能槽位的数据未知；请核对技能栏和采样。");
                else if((f.ReadyActionsMask&required)!=required||(f.UsableActionsMask&required)!=required)Pending(r,"target_actions","当前配置技能","技能槽位已知；冷却或暂不可用的技能将在运行时逐次等待并复核。");
                else Add(r,"target_actions","当前配置技能",true,"配置技能槽位均已知，当前可用；执行前仍逐次复核。","");
                bool range=f.RangeKnown&&f.RangeSlot==b.RangeSlot;Add(r,"target_range","当前怪物射程",range,"当前测距槽位与战斗配置一致。","选中的怪物射程未知或测距槽位不一致。");
                if(range&&!f.TargetInRange)Add(r,"target_position","当前怪物接近依据",b.AllowApproach&&f.TargetPositionKnown&&NavigationMath.InRange(f.TargetX,0,1)&&NavigationMath.InRange(f.TargetY,0,1)&&NavigationMath.Distance(f.TargetX-f.X,f.TargetY-f.Y,b.Calibration.AxisScale)<=.02,"可按当前可靠坐标进行有限接近。","目标不在范围内；缺少可靠坐标、超出距离或接近被关闭。");
            }else Pending(r,"target_range","目标与射程","尚未选中可攻击的保存怪物，选中后再检查身份、射程和动作条。");
            if(s.EnableVendor)Pending(r,"inventory","商店与物品格","到保存NPC处后再验证商店、出售页、物品信息及可见格子；不要求现在打开全部界面。");
            if(saved==null)Pending(r,"target_actions","配置技能","选中可攻击的保存怪物后再检查配置技能槽位；无需提前选择未来目标。");
            return r;
        }
        static bool Region(OperationObservation o,Rectangle r){return r.X>=0&&r.Y>=0&&r.Width>0&&r.Height>0&&r.Width<=512&&r.Height<=512&&(long)r.X+r.Width<=o.ClientWidth&&(long)r.Y+r.Height<=o.ClientHeight&&!r.IntersectsWith(new Rectangle(8,8,800,96));}
        static void Add(PreflightReport r,string code,string label,bool pass,string ok,string bad){r.Checks.Add(new PreflightCheck{Code=code,Label=label,State=pass?"pass":"block",Detail=pass?ok:bad});}
        static void Pending(PreflightReport r,string code,string label,string detail){r.Checks.Add(new PreflightCheck{Code=code,Label=label,State="pending",Detail=detail});}
    }
}
