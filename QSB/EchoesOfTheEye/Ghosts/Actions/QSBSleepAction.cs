﻿using QSB.Utility;

namespace QSB.EchoesOfTheEye.Ghosts.Actions;

public class QSBSleepAction : QSBGhostAction
{
	private SleepAction.WakeState _state;

	public override GhostAction.Name GetName()
		=> GhostAction.Name.Sleep;

	public override float CalculateUtility()
		=> !_data.hasWokenUp
			? 100f
			: -100f;

	public override bool IsInterruptible()
		=> false;

	protected override void OnEnterAction()
		=> EnterSleepState();

	protected override void OnExitAction() { }

	public override bool Update_Action()
	{
		if (_state == SleepAction.WakeState.Sleeping)
		{
			if (_data.hasWokenUp || _data.IsIlluminatedByAnyPlayer)
			{
				DebugLog.DebugWrite($"{_brain.AttachedObject._name} : Who dares awaken me?");
				_state = SleepAction.WakeState.Awake;
				_effects.PlayDefaultAnimation();
			}
		}
		else if (_state is not SleepAction.WakeState.WakingUp and SleepAction.WakeState.Awake)
		{
			return false;
		}

		return true;
	}

	private void EnterSleepState()
	{
		_controller.SetLanternConcealed(true, true);
		_effects.PlaySleepAnimation();
		_state = SleepAction.WakeState.Sleeping;
	}

	private enum WakeState
	{
		Sleeping,
		Awake
	}
}
