using System;
namespace WoWProbe
{
    public sealed class FarmSimulator
    {
        private readonly RouteDocument route;
        private readonly BattleProfile profile;
        private readonly BattleTarget monster;
        private int sequence, selection, selected, monsterHealth = 100;
        private double facing, health = 30, mana = 20, targetX, targetY;
        private bool eating, drinking, casting, dead, lootable, skinnable;
        private long skinEnds, readyAt;
        private uint lootOpened, lootClosed, castSuccess, skinSuccess;
        public FarmSimulator(RouteDocument route, BattleProfile profile, bool reverse, bool loop, int startIndex)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            this.profile = profile.Clone(); this.profile.Validate();
            this.route = new Navigator(route, this.profile.Calibration, reverse, loop, startIndex).WorkingRoute;
            foreach (BattleTarget target in this.profile.Targets)
                if (target.Kind == "monster") { monster = target.Clone(); break; }
            if (monster == null) throw new ArgumentException("Virtual farming requires a saved monster target.");
            Waypoint initial = this.route.Points[Math.Max(0, Math.Min(startIndex - 1, this.route.Points.Count - 1))];
            X = initial.X; Y = initial.Y;
            // Place the encounter on the first recorded segment, avoiding an invented off-route approach.
            int toward = Math.Min(this.route.Points.Count - 1, Math.Max(1, startIndex));
            Waypoint next = this.route.Points[toward];
            double dx = next.X - X, dy = next.Y - Y;
            double distance = NavigationMath.Distance(dx, dy, this.profile.Calibration.AxisScale);
            if (distance == 0) { targetX = X; targetY = Y; }
            else { double fraction = Math.Min(1, 0.008 / distance); targetX = X + dx * fraction; targetY = Y + dy * fraction; }
        }
        public long ElapsedMilliseconds { get; private set; }
        public int InputEvents { get { return 0; } }
        public int Kills { get; private set; }
        public int SkillCasts { get; private set; }
        public int Loots { get; private set; }
        public int Skins { get; private set; }
        public int Heals { get; private set; }
        public int Drinks { get; private set; }
        public double X { get; private set; }
        public double Y { get; private set; }
        public bool MissingTargetPosition { get; set; }
        public bool NoSupplies { get; set; }
        public FarmObservation Sample()
        {
            sequence = (sequence + 1) & 65535;
            bool exists = selected != 0;
            bool isMonster = selected == 3;
            string name = selected == 1 ? "Synthetic friendly NPC" : exists ? monster.Name : "";
            uint token = selected == 1 ? 101u : selected == 2 ? 202u : isMonster ? 303u : 0u;
            int ready = ElapsedMilliseconds >= readyAt && !casting ? 15 : 0;
            FarmObservation o = new FarmObservation {
                Sequence = sequence, Fresh = true, KnownCore = true, Health = (int)Math.Floor(health), Power = (int)Math.Floor(mana), PowerType = 0,
                X = X, Y = Y, Facing = facing, MapId = route.MapId, MapMode = route.MapMode, Zone = route.Zone,
                TargetExists = exists, TargetDeadKnown = exists, TargetDead = isMonster && dead, TargetIdentityKnown = exists,
                TargetToken = token, TargetName = name, TargetNameHash = exists ? TargetCatalog.HashName(name) : 0, NameHashKnown = exists,
                AttackabilityKnown = exists, TargetCanAttack = selected == 2 || (isMonster && !dead), TargetIsPlayer = selected == 2,
                TargetHealth = isMonster ? monsterHealth : 100, InCombat = isMonster && !dead && SkillCasts != 0,
                RangeKnown = exists, RangeSlot = profile.RangeSlot, TargetInRange = exists && NavigationMath.Distance(targetX - X, targetY - Y, profile.Calibration.AxisScale) <= 0.0025,
                TargetPositionKnown = exists && !MissingTargetPosition, TargetX = exists && !MissingTargetPosition ? targetX : Double.NaN,
                TargetY = exists && !MissingTargetPosition ? targetY : Double.NaN,
                LootKnown = true, Lootable = isMonster && lootable, SkinKnown = true, Skinnable = isMonster && skinnable,
                CastingKnown = true, Casting = casting, ActionsKnown = true, KnownActionsMask = 15, ReadyActionsMask = ready, UsableActionsMask = mana >= 5 ? 15 : 0,
                LootEventsKnown = true, CastEventsKnown = true, SkinSuccessKnown = true, LootOpened = lootOpened, LootClosed = lootClosed,
                CastSuccess = castSuccess, SkinSuccess = skinSuccess
            };
            return o;
        }
        public void Apply(FarmCommand command)
        {
            if (command == null) throw new ArgumentNullException("command");
            int hold = command.HoldMilliseconds;
            if (command.Action == FarmAction.None || command.Action == FarmAction.Stop || command.Action == FarmAction.Completed)
            { if (hold != 0) throw new ArgumentException("Nonmovement farming commands cannot hold input."); return; }
            if (hold < 60 || hold > 250) throw new ArgumentException("Virtual farming pulse must be 60 to 250 milliseconds.");
            if ((command.RequireAliveTarget || command.RequireDeadTarget) &&
                (selected != 3 || command.ExpectedTargetToken != 303 || (command.RequireAliveTarget && dead) || (command.RequireDeadTarget && !dead)))
                throw new InvalidOperationException("Virtual target guard rejected an outdated farming command.");
            switch (command.Action)
            {
                case FarmAction.Forward:
                case FarmAction.StrafeLeft:
                case FarmAction.StrafeRight:
                case FarmAction.TurnLeft:
                case FarmAction.TurnRight:
                case FarmAction.Jump:
                    Move(command.Action, hold); break;
                case FarmAction.SelectTarget:
                    selection++; selected = selection <= 3 && !dead ? selection : 0; break;
                case FarmAction.Skill1:
                case FarmAction.Skill2:
                case FarmAction.Skill3:
                case FarmAction.Skill4:
                    if (!command.RequireAliveTarget || selected != 3 || dead || casting || ElapsedMilliseconds < readyAt ||
                        NavigationMath.Distance(targetX - X, targetY - Y, profile.Calibration.AxisScale) > 0.0025)
                        throw new InvalidOperationException("Virtual skill requires a live locked monster in range with ready actions.");
                    eating = drinking = false; SkillCasts++; castSuccess++; monsterHealth = Math.Max(0, monsterHealth - 40);
                    health = Math.Max(20, health - 12); mana = Math.Max(0, mana - 8); readyAt = ElapsedMilliseconds + 500;
                    if (monsterHealth == 0) { Kills++; dead = true; lootable = true; skinnable = profile.Skinning; }
                    break;
                case FarmAction.Interact:
                    if (!command.RequireDeadTarget || selected != 3 || !dead || !lootable) throw new InvalidOperationException("Virtual loot requires a confirmed lootable corpse.");
                    // Both events occur inside a single virtual update, exercising instant auto-loot observation.
                    lootOpened++; lootClosed++; lootable = false; Loots++; break;
                case FarmAction.Skin:
                    if (!command.RequireDeadTarget || selected != 3 || !dead || lootable || !skinnable || casting) throw new InvalidOperationException("Virtual skin requires a looted, skinnable locked corpse.");
                    casting = true; skinEnds = ElapsedMilliseconds + profile.SkinWaitMs; break;
                case FarmAction.Heal:
                    if (!NoSupplies) { eating = true; Heals++; } break;
                case FarmAction.Mana:
                    if (!NoSupplies) { drinking = true; Drinks++; } break;
                default: throw new ArgumentException("Unknown virtual farming action.");
            }
            Advance(hold);
        }
        private void Move(FarmAction action, int hold)
        {
            eating = drinking = false;
            NavigationCalibration c = profile.Calibration; double seconds = hold / 1000.0;
            double compass = NavigationMath.Normalize(NavigationMath.Normalize(c.FacingSign * facing) + NavigationMath.Normalize(c.FacingOffset));
            if (action == FarmAction.TurnLeft || action == FarmAction.TurnRight)
            { compass += (action == FarmAction.TurnRight ? 1 : -1) * c.TurnRadiansPerSecond * seconds; facing = NavigationMath.PositiveAngle(c.FacingSign * (NavigationMath.Normalize(compass) - NavigationMath.Normalize(c.FacingOffset))); }
            else if (action != FarmAction.Jump)
            {
                if (action == FarmAction.StrafeLeft) compass -= Math.PI / 2;
                if (action == FarmAction.StrafeRight) compass += Math.PI / 2;
                double amount = c.ForwardUnitsPerSecond * seconds;
                X = Math.Max(0, Math.Min(1, X + Math.Sin(compass) * amount / c.AxisScale)); Y = Math.Max(0, Math.Min(1, Y - Math.Cos(compass) * amount));
            }
        }
        public void Advance(int ms)
        {
            if (ms < 0 || ElapsedMilliseconds > Int64.MaxValue - ms) throw new ArgumentOutOfRangeException("ms");
            ElapsedMilliseconds += ms;
            if (!NoSupplies)
            { if (eating) health = Math.Min(100, health + ms * 0.008); if (drinking) mana = Math.Min(100, mana + ms * 0.01); }
            if (casting && ElapsedMilliseconds >= skinEnds) { casting = false; skinnable = false; skinSuccess++; castSuccess++; Skins++; }
        }
    }
}
