using System;
namespace WoWProbe
{
    public static class QuickCombatHotkeys
    {
        public static ushort Key(string text)
        {
            string s=(text??"").Trim().ToUpperInvariant();int n;
            if(s.Length==1&&((s[0]>='0'&&s[0]<='9')||(s[0]>='A'&&s[0]<='Z')))return s[0];
            if(s.Length>1&&s[0]=='F'&&Int32.TryParse(s.Substring(1),out n)&&n>=1&&n<=11)return (ushort)(111+n);
            throw new FormatException("快捷键请填写单个字母、数字或F1–F11；F12保留急停。");
        }
        public static ushort ActionKey(QuickAction action,QuickRoutePlan plan)
        {
            if(action==QuickAction.CorpseSelect||action==QuickAction.Reacquire)return Key(plan.LastTargetKey);
            if(action==QuickAction.ReturnToRoute)return Key(plan.StopAttackKey);
            if(action==QuickAction.Scan)return 9;
            if(action==QuickAction.Loot||action==QuickAction.CorpseApproach)return 70;
            if(action==QuickAction.Skin)return (ushort)(plan.SkinWithInteract?70:55);
            throw new ArgumentException("Unsupported hotkey action.");
        }
    }
}
