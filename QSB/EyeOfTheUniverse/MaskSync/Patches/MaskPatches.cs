﻿using HarmonyLib;
using QSB.Patches;
using QSB.Utility;

namespace QSB.EyeOfTheUniverse.MaskSync.Patches
{
	internal class MaskPatches : QSBPatch
	{
		public override QSBPatchTypes Type => QSBPatchTypes.OnClientConnect;

		[HarmonyPrefix]
		[HarmonyPatch(typeof(EyeShuttleController), nameof(EyeShuttleController.OnLaunchSlotActivated))]
		public static bool DontLaunch(EyeShuttleController __instance)
		{
			if (__instance._isPlayerInside)
			{
				return true;
			}

			MaskManager.Instance.FlickerOutShuttle();
			__instance.enabled = false;

			return false;
		}
	}
}
