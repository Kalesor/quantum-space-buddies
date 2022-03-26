﻿using HarmonyLib;
using QSB.EchoesOfTheEye.DreamRafts.Messages;
using QSB.EchoesOfTheEye.DreamRafts.WorldObjects;
using QSB.Messaging;
using QSB.Patches;
using QSB.WorldSync;

namespace QSB.EchoesOfTheEye.DreamRafts.Patches;

public class DreamRaftPatches : QSBPatch
{
	public override QSBPatchTypes Type => QSBPatchTypes.OnClientConnect;

	[HarmonyPrefix]
	[HarmonyPatch(typeof(DreamObjectProjector), nameof(DreamObjectProjector.SetLit))]
	private static void SetLit(DreamObjectProjector __instance,
		bool lit)
	{
		if (Remote)
		{
			return;
		}

		if (__instance._lit == lit)
		{
			return;
		}

		__instance.GetWorldObject<QSBDreamObjectProjector>()
			.SendMessage(new SetLitMessage(lit));
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(DreamRaftProjector), nameof(DreamRaftProjector.RespawnRaft))]
	private static void RespawnRaft(DreamRaftProjector __instance)
	{
		if (Remote)
		{
			return;
		}

		__instance.GetWorldObject<QSBDreamObjectProjector>()
			.SendMessage(new RespawnRaftMessage());
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(DreamRaftProjector), nameof(DreamRaftProjector.ExtinguishImmediately))]
	private static void ExtinguishImmediately(DreamRaftProjector __instance)
	{
		if (Remote)
		{
			return;
		}

		__instance.GetWorldObject<QSBDreamObjectProjector>()
			.SendMessage(new ExtinguishImmediatelyMessage());
	}
}