﻿using Mirror;
using QSB.HUD;
using QSB.Localization;
using QSB.Menus;
using QSB.Messaging;
using QSB.Utility;

namespace QSB.Player.Messages;

/// <summary>
/// always sent by host
/// </summary>
internal class PlayerKickMessage : QSBMessage<string>
{
	private uint PlayerId;

	public PlayerKickMessage(uint playerId, string reason) : base(reason) =>
		PlayerId = playerId;

	public override void Serialize(NetworkWriter writer)
	{
		base.Serialize(writer);
		writer.Write(PlayerId);
	}

	public override void Deserialize(NetworkReader reader)
	{
		base.Deserialize(reader);
		PlayerId = reader.Read<uint>();
	}

	public override void OnReceiveRemote()
	{
		if (PlayerId != QSBPlayerManager.LocalPlayerId)
		{
			if (QSBPlayerManager.PlayerExists(PlayerId))
			{
				MultiplayerHUDManager.Instance.WriteMessage($"<color=red>{string.Format(QSBLocalization.Current.PlayerWasKicked, QSBPlayerManager.GetPlayer(PlayerId).Name)}</color>");
				return;
			}

			MultiplayerHUDManager.Instance.WriteMessage($"<color=red>{string.Format(QSBLocalization.Current.PlayerWasKicked, PlayerId)}</color>");
			return;
		}

		MultiplayerHUDManager.Instance.WriteMessage($"<color=red>{string.Format(QSBLocalization.Current.KickedFromServer, Data)}</color>");
		MenuManager.Instance.OnKicked(Data);

		NetworkClient.Disconnect();
	}
}