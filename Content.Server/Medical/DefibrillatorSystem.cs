using Content.Server._Impstation.Traits;
using Content.Server.Atmos.Rotting;
using Content.Server.Chat.Systems;
using Content.Server.DoAfter;
using Content.Server.Electrocution;
using Content.Server.EUI;
using Content.Server.Ghost;
using Content.Server.Popups;
using Content.Server.PowerCell;
using Content.Server.Traits.Assorted;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Medical;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.PowerCell;
using Content.Shared.Timing;
using Content.Shared.Toggleable;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Content.Shared.Whitelist;

namespace Content.Server.Medical;

/// <summary>
/// This handles interactions and logic relating to <see cref="DefibrillatorComponent"/>
/// </summary>
public sealed class DefibrillatorSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chatManager = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly ElectrocutionSystem _electrocution = default!;
    [Dependency] private readonly EuiManager _euiManager = default!;
    [Dependency] private readonly ItemToggleSystem _toggle = default!;
    [Dependency] private readonly RottingSystem _rotting = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly PowerCellSystem _powerCell = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly UseDelaySystem _useDelay = default!;
    {
        SubscribeLocalEvent<DefibrillatorComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<DefibrillatorComponent, DefibrillatorZapDoAfterEvent>(OnDoAfter);
    }

    private void OnAfterInteract(EntityUid uid, DefibrillatorComponent component, AfterInteractEvent args)
    {
        if (args.Handled || args.Target is not { } target)
            return;

        args.Handled = TryStartZap(uid, target, args.User, component);
    }

    private void OnDoAfter(EntityUid uid, DefibrillatorComponent component, DefibrillatorZapDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        if (args.Target is not { } target)
            return;

        if (!CanZap(uid, target, args.User, component))
            return;

        args.Handled = true;
        Zap(uid, target, args.User, component);
    }

    /// <summary>
    ///     Checks if you can actually defib a target.
    /// </summary>
    /// <param name="uid">Uid of the defib</param>
    /// <param name="target">Uid of the target getting defibbed</param>
    /// <param name="user">Uid of the entity using the defibrillator</param>
    /// <param name="component">Defib component</param>
    /// <param name="targetCanBeAlive">
    ///     If true, the target can be alive. If false, the function will check if the target is alive and will return false if they are.
    /// </param>
    /// <returns>
    ///     Returns true if the target is valid to be defibed, false otherwise.
    /// </returns>
    public bool CanZap(EntityUid uid, EntityUid target, EntityUid? user = null, DefibrillatorComponent? component = null, bool targetCanBeAlive = false)
    {
        if (!Resolve(uid, ref component))
            return false;

        if (!_toggle.IsActivated(uid) && !component.IgnoreToggle)
        {
            if (user != null)
                _popup.PopupEntity(Loc.GetString("defibrillator-not-on"), uid, user.Value);
            return false;
        }

        if (!TryComp(uid, out UseDelayComponent? useDelay) || _useDelay.IsDelayed((uid, useDelay), component.DelayId))
            return false;

        if (!TryComp<MobStateComponent>(target, out var mobState))
            return false;

        if (!_powerCell.HasActivatableCharge(uid, user: user) && !component.IgnorePowerCell)
            return false;

        if (!targetCanBeAlive && _mobState.IsAlive(target, mobState))
            return false;

        if (!targetCanBeAlive && !component.CanDefibCrit && _mobState.IsCritical(target, mobState))
            return false;

        // imp
        return _whitelist.IsWhitelistPassOrNull(component.Whitelist, target);
    }

    /// <summary>
    ///     Tries to start defibrillating the target. If the target is valid, will start the defib do-after.
    /// </summary>
    /// <param name="uid">Uid of the defib</param>
    /// <param name="target">Uid of the target getting defibbed</param>
    /// <param name="user">Uid of the entity using the defibrillator</param>
    /// <param name="component">Defib component</param>
    /// <returns>
    ///     Returns true if the defibrillation do-after started, otherwise false.
    /// </returns>
    public bool TryStartZap(EntityUid uid, EntityUid target, EntityUid user, DefibrillatorComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return false;

        if (!CanZap(uid, target, user, component))
            return false;

        if (component.SkipDoAfter)
        {
            Zap(uid, target, user, component);
            return false;
        }

        if (component.PlayChargeSound)
            _audio.PlayPvs(component.ChargeSound, uid);
        return _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, TimeSpan.FromSeconds(component.DoAfterDuration), new DefibrillatorZapDoAfterEvent(),
            uid, target, uid)
        {
            NeedHand = true,
            BreakOnMove = !component.AllowDoAfterMovement
        });
    }

    /// <summary>
    ///     Tries to defibrillate the target with the given defibrillator.
    /// </summary>
    public void Zap(EntityUid uid, EntityUid target, EntityUid user, DefibrillatorComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        if (!component.IgnorePowerCell)
        {
            if (!_powerCell.TryUseActivatableCharge(uid, user: user))
                return;
        }

        var selfEvent = new SelfBeforeDefibrillatorZapsEvent(user, uid, target);
        RaiseLocalEvent(user, selfEvent);

        target = selfEvent.DefibTarget;

        // Ensure thet new target is still valid.
        if (selfEvent.Cancelled || !CanZap(uid, target, user, component, true))
            return;

        var targetEvent = new TargetBeforeDefibrillatorZapsEvent(user, uid, target);
        RaiseLocalEvent(target, targetEvent);

        target = targetEvent.DefibTarget;

        if (targetEvent.Cancelled || !CanZap(uid, target, user, component, true))
            return;

        if (!TryComp<MobStateComponent>(target, out var mob) ||
            !TryComp<MobThresholdsComponent>(target, out var thresholds))
            return;

        if (component.PlayZapSound)
            _audio.PlayPvs(component.ZapSound, uid);
        _electrocution.TryDoElectrocution(target,
            null,
            component.ZapDamage,
            TimeSpan.FromSeconds(component.WritheDuration),
            true,
            ignoreInsulation: true);
        component.NextZapTime = _timing.CurTime + component.ZapDelay;
        _appearance.SetData(uid, DefibrillatorVisuals.Ready, false);

        ICommonSession? session = null;

        var dead = true;
        if (_rotting.IsRotten(target))
        {
            if (component.ShowMessages)
                return;
                _chatManager.TrySendInGameICMessage(uid,
                    Loc.GetString("defibrillator-rotten"),
                    InGameICChatType.Speak,
                    true);
            return;
        }

        if (HasComp<UnrevivableComponent>(target))
        {
            if (!component.ShowMessages)
                _chatManager.TrySendInGameICMessage(uid,
                Loc.GetString("defibrillator-unrevivable"),
                InGameICChatType.Speak,
                true);
            return;
        }

        if (HasComp<RandomUnrevivableComponent>(target))
        {
            var dnrComponent = Comp<RandomUnrevivableComponent>(target);

            if (dnrComponent.Chance < _random.NextDouble())
            {
                if (component.ShowMessages)
                    _chatManager.TrySendInGameICMessage(uid, Loc.GetString("defibrillator-unrevivable"), InGameICChatType.Speak, true);
                    dnrComponent.Chance = 0f;
                    AddComp<UnrevivableComponent>(target);
                return;
            }
            else
            {
                dnrComponent.Chance -= 0.1f;
            }
        }

        if (_mobState.IsDead(target, mob))
            _damageable.TryChangeDamage(target, component.ZapHeal, true, origin: uid);

        if (_mobThreshold.TryGetThresholdForState(target, MobState.Dead, out var threshold) &&
            TryComp<DamageableComponent>(target, out var damageableComponent) &&
            damageableComponent.TotalDamage < threshold)
        {
            if (component.AllowSkipCrit &&
            (!_mobThreshold.TryGetThresholdForState(target, MobState.Critical, out var critThreshold) ||
            damageableComponent.TotalDamage < critThreshold))
            {
                _mobState.ChangeMobState(target, MobState.Alive, mob, uid);
                dead = false;
            } else {
                _mobState.ChangeMobState(target, MobState.Critical, mob, uid);
                dead = false;
            }
        }

        if (_mind.TryGetMind(target, out _, out var mind) &&
            mind.Session is { } playerSession)
        {
            session = playerSession;
            // notify them they're being revived.
            if (mind.CurrentEntity != target)
            {
                _euiManager.OpenEui(new ReturnToBodyEui(mind, _mind), session);
            }
        }
        else
        {
            if (component.ShowMessages)
                _chatManager.TrySendInGameICMessage(uid,
                    Loc.GetString("defibrillator-no-mind"),
                    InGameICChatType.Speak,
                    true);

            if (dead || session == null)
            {
                if (component.PlayFailureSound)
                    _audio.PlayPvs(component.FailureSound, uid);
            } else {
                if (component.PlaySuccessSound)
                    _audio.PlayPvs(component.SuccessSound, uid);
            }

            // if we don't have enough power left for another shot, turn it off
            if (!component.IgnorePowerCell)
            {
                if (!_powerCell.HasActivatableCharge(uid) && !component.IgnoreToggle)
                    _toggle.TryDeactivate(uid);
            }

            // TODO clean up this clown show above
            var ev = new TargetDefibrillatedEvent(user, (uid, component));
            RaiseLocalEvent(target, ref ev);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<DefibrillatorComponent>();
        while (query.MoveNext(out var uid, out var defib))
        {
            if (defib.NextZapTime == null || _timing.CurTime < defib.NextZapTime)
                continue;

            if(defib.PlayReadySound)
                _audio.PlayPvs(defib.ReadySound, uid);
            _appearance.SetData(uid, DefibrillatorVisuals.Ready, true);
            defib.NextZapTime = null;
        }
    }
}
