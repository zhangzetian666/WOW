namespace WoWProbe
{
    // Shared by combat engines and retries while the client window is open.
    public sealed class QuickHunterState
    {
        public bool AspectPending {get;private set;}
        public void Observe(FarmObservation o)
        {if(o!=null&&o.Fresh&&o.KnownCore&&o.ReadFailures==0&&(o.PlayerDead||o.PlayerGhost))AspectPending=true;}
        public void AspectInputCompleted(){AspectPending=false;}
    }
}
