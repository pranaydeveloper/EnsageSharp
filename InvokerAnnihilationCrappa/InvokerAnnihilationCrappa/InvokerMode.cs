using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Ensage;
using Ensage.Common.Extensions;
using Ensage.Common.Objects.UtilityObjects;
using Ensage.SDK.Helpers;
using Ensage.SDK.Input;
using Ensage.SDK.Orbwalker;
using Ensage.SDK.Orbwalker.Modes;
using log4net;
using PlaySharp.Toolkit.Logging;
using AbilityId = Ensage.Common.Enums.AbilityId;

namespace InvokerAnnihilationCrappa
{
    public class InvokerMode : KeyPressOrbwalkingModeAsync
    {
        private static readonly ILog Log = AssemblyLogs.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private Hero _target;
        private MultiSleeper sleeper;

        public InvokerMode(Key key, Lazy<IOrbwalkerManager> orbwalkerManager, Lazy<IInputManager> input,
            Invoker me) : base(orbwalkerManager.Value, input.Value, key)
        {
            Me = me;
            sleeper = new MultiSleeper();
        }

        public Invoker Me { get; set; }

        public void Load()
        {
            UpdateManager.Subscribe(TargetUpdater, 100);
            UpdateManager.Subscribe(IndicatorUpdater);
        }

        private void IndicatorUpdater()
        {
            if (_target != null)
                Me.ParticleManager.Value.DrawDangerLine(Owner, "Invo_line", _target.Position);
        }

        public void Unload()
        {
            UpdateManager.Unsubscribe(TargetUpdater);
        }
        private void TargetUpdater()
        {
            if (!CanExecute && _target != null)
            {
                if (!Me.TargetManager.Value.IsActive)
                {
                    Me.TargetManager.Value.Activate();
                    Log.Debug("re Enable _targetManager");
                }
                _target = null;
                Me.ParticleManager.Value.Remove("Invo_target");
                Me.ParticleManager.Value.Remove("Invo_line");

                /*var target=Me.TargetManager.Value.Active.GetTargets().FirstOrDefault();
                if (target != null)
                {
                    Game.PrintMessage($"Angle: {UnitExtensions.FindRotationAngle(Owner,target.Position)}");
                }*/
            }
        }
        public void UpdateConfig(Config config)
        {
            Me.Config = config;
        }

        public override async Task ExecuteAsync(CancellationToken token)
        {
            if (Me.Config.Prepare.Enable && Game.IsKeyDown(0x11))
                return;
            if (_target == null || !_target.IsValid || !_target.IsAlive)
            {
                if (!Me.TargetManager.Value.IsActive)
                    Me.TargetManager.Value.Activate();
                _target = Me.TargetManager.Value.Active.GetTargets().FirstOrDefault() as Hero;
                if (_target != null)
                {
                    Log.Debug("target detected");
                    var currentCombo = Me.Config.ComboPanel.Combos.First(x => x.Id == Me.SelectedCombo);
                    currentCombo.CurrentAbility = 0;
                    if (Me.TargetManager.Value.IsActive)
                        Me.TargetManager.Value.Deactivate();
                }
                else
                {
                    Log.Debug("Cant find target");
                }
            }
            
            if (_target != null)
            {
                var currentCombo = Me.Config.ComboPanel.Combos.First(x => x.Id == Me.SelectedCombo);
                if (currentCombo.CurrentAbility < currentCombo.AbilityCount)
                {
                    var currentAbility = currentCombo.AbilityInfos[currentCombo.CurrentAbility];
                    if (currentAbility.Ability.AbilityState == AbilityState.Ready)
                    {
                        if (!currentAbility.Ability.CanBeCasted())
                        {
                            await Me.Invoke(currentAbility);
                        }
                        else if (currentAbility.Ability.CanHit(_target)
                                 /*Ensage.SDK.Extensions.EntityExtensions.Distance2D(Me.Owner, _target) <=
                                 currentAbility.Ability.CastRange*/||
                                 currentAbility.Ability.GetAbilityId() == AbilityId.invoker_ice_wall)
                        {
                            var casted = await currentAbility.UseAbility(_target, token);
                            if (casted)
                            {
                                Log.Error($"using: [{currentCombo.CurrentAbility}]" + currentAbility.Ability.Name);
                                if (currentCombo.CurrentAbility + 1 >= currentCombo.AbilityCount)
                                    currentCombo.CurrentAbility = 0;
                                else
                                    currentCombo.CurrentAbility++;
                            }
                            else
                            {
                                Log.Error($"not casted: [{currentCombo.CurrentAbility}]" + currentAbility.Ability.Name);
                            }
                        }
                    }
                    else
                    {
                        Log.Error($"skip ability cuz cd: [{currentCombo.CurrentAbility}]" + currentAbility.Ability.Name);
                        if (currentCombo.CurrentAbility + 1 >= currentCombo.AbilityCount)
                            currentCombo.CurrentAbility = 0;
                        else
                            currentCombo.CurrentAbility++;
                    }
                }
                else
                {
                    Log.Error($"end of combo. Set current ability stage to 0");
                    currentCombo.CurrentAbility = 0;
                }
                if (_target.IsAttackImmune() || _target.IsInvul())
                    Orbwalker.Move(Game.MousePosition);
                else
                {
                    Orbwalker.OrbwalkTo(_target);
                    var others =
                        EntityManager<Unit>.Entities.Where(
                            x => x.IsAlive && x.IsControllable && x.Team == Owner.Team && !x.Equals(Owner));
                    foreach (var other in others)
                    {
                        if (other.IsAttacking())
                            continue;
                        other.Attack(_target);
                    }
                    if (currentCombo.CurrentAbility > 1)
                    {
                        if (Me.Shiva != null && Me.Shiva.CanBeCasted && Me.Owner.Distance2D(_target) <= Me.Shiva.Radius)
                        {
                            Me.Shiva.UseAbility();
                        }
                        if (Me.Hex != null && Me.Hex.CanBeCasted && Me.Hex.CanHit(_target))
                        {
                            float duration;
                            if (!_target.IsStunned(out duration) || duration <= 0.15f)
                                Me.Hex.UseAbility(_target);
                        }
                    }
                    else
                    {
                        if (Me.Blink != null && Me.Blink.CanBeCasted && Me.Blink.CanHit(_target))
                            Me.Blink.UseAbility(_target.Position);
                    }
                }
                Me.ParticleManager.Value.DrawRange(_target, "Invo_target", 125, SharpDX.Color.YellowGreen);
            }
            else
            {
                Orbwalker.Move(Game.MousePosition);
            }
        }
    }
}