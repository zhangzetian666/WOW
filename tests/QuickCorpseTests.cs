using System;
using WoWProbe;
class QuickCorpseTests
{
    static int passed,failed;static void Check(bool b){if(!b)throw new Exception("Unexpected corpse behavior");}
    static FarmObservation O(){return new FarmObservation{Fresh=true,KnownCore=true,Health=100,TargetExists=true,TargetIdentityKnown=true,TargetToken=7,TargetDeadKnown=true,TargetDead=true,AttackabilityKnown=true,LootKnown=true,Lootable=true,LootEventsKnown=true,SkinKnown=true,Skinnable=true,SkinBindingKnown=true,SkinBindingCorrect=true,SkinSuccessKnown=true,CastEventsKnown=true,CastingKnown=true};}
    static void Test(string name,Action body){try{body();passed++;Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex.Message);}}
    static int Main()
    {
        Test("loot open and close precede one skin action and a dedicated success event",()=>{var o=O();var c=new QuickCorpse(7,true,o,0);Check(c.Step(o,0).Action==QuickAction.Loot);o.LootOpened=o.LootClosed=1;o.Lootable=false;Check(c.Step(o,700).Action==QuickAction.Skin&&c.Loots==1);o.SkinSuccess=1;o.TargetExists=false;Check(c.Step(o,1000).Action==QuickAction.None&&!c.Done);c.Step(o,2300);Check(c.Done&&c.Skins==1);c.Step(o,2500);Check(c.Skins==1);});
        Test("lootability dropping before window events never races a skin key",()=>{var o=O();var c=new QuickCorpse(7,true,o,0);Check(c.Step(o,0).Action==QuickAction.Loot);o.Lootable=false;Check(c.Step(o,170).Action==QuickAction.None&&c.Skins==0);o.LootOpened=o.LootClosed=1;Check(c.Step(o,400).Action==QuickAction.None);Check(c.Step(o,700).Action==QuickAction.Skin);});
        Test("lost corpse is reselected once and another dead token cannot authorize loot",()=>{var o=O();o.TargetExists=false;var c=new QuickCorpse(7,true,o,0);Check(c.Step(o,0).Action==QuickAction.CorpseSelect);o.TargetExists=true;o.TargetToken=8;Check(c.Step(o,1000).Action==QuickAction.None);c.Step(o,3100);Check(c.Done&&c.Loots==0&&c.Skins==0);});
        Test("combat stale data or player death cancels corpse actions",()=>{for(int i=0;i<3;i++){var o=O();var c=new QuickCorpse(7,true,o,0);if(i==0)o.InCombat=true;if(i==1)o.Fresh=false;if(i==2)o.PlayerDead=true;Check(c.Step(o,2600).Action==QuickAction.None&&c.Done&&c.Skins==0);}});
        Test("post kill combat immediately yields to attackers before looting",()=>{var o=O();o.InCombat=true;var c=new QuickCorpse(7,true,o,0);Check(c.Step(o,100).Action==QuickAction.None&&c.Done&&c.Loots==0&&c.Skins==0);});
        Test("unconfirmed loot times out without skinning or success credit",()=>{var o=O();var c=new QuickCorpse(7,true,o,0);Check(c.Step(o,0).Action==QuickAction.Loot);c.Step(o,5100);Check(c.Done&&c.Loots==0&&c.Skins==0);});
        Test("non skinnable unknown skin and wrong bindings do not emit skin keys",()=>{for(int i=0;i<3;i++){var o=O();o.Lootable=false;if(i==0)o.Skinnable=false;if(i==1)o.SkinKnown=false;if(i==2)o.SkinBindingCorrect=false;var c=new QuickCorpse(7,true,o,0);Check(c.Step(o,0).Action==QuickAction.None&&c.Done&&c.Skins==0);}});
        Test("failed skinning or elapsed cast deadline never becomes success",()=>{for(int i=0;i<2;i++){var o=O();o.Lootable=false;var c=new QuickCorpse(7,true,o,0);Check(c.Step(o,0).Action==QuickAction.Skin);if(i==0)o.CastFailed=1;c.Step(o,i==0?100:8100);Check(c.Done&&c.Skins==0);}});
        Test("cleared target after loot is reselected separately for optional skinning",()=>{var o=O();var c=new QuickCorpse(7,true,o,0);c.Step(o,0);o.LootOpened=o.LootClosed=1;o.TargetExists=false;Check(c.Step(o,500).Action==QuickAction.CorpseSelect&&c.Loots==1);o.TargetExists=true;o.Lootable=false;Check(c.Step(o,1000).Action==QuickAction.Skin);});
        Console.WriteLine("Quick corpse tests: "+passed+" passed, "+failed+" failed. Simulated observations only.");return failed==0?0:1;
    }
}
