﻿using OWML.Common;
using QSB.Player;
using QSB.Utility;
using QSB.WorldSync;
using UnityEngine;
using UnityEngine.UI;

namespace QSB.QuantumSync.WorldObjects
{
	internal class QSBSocketedQuantumObject : QSBQuantumObject<SocketedQuantumObject>
	{
		public Text DebugBoxText;

		public override void Init()
		{
			base.Init();
			if (QSBCore.ShowQuantumDebugBoxes)
			{
				DebugBoxText = DebugBoxManager.CreateBox(AttachedObject.transform, 0, $"Socketed\r\nid:{ObjectId}").GetComponent<Text>();
			}
		}

		public override void OnRemoval()
		{
			base.OnRemoval();
			if (DebugBoxText != null)
			{
				Object.Destroy(DebugBoxText.gameObject);
			}
		}

		public void MoveToSocket(uint playerId, int socketId, Quaternion localRotation)
		{
			var qsbSocket = QSBWorldSync.GetWorldFromId<QSBQuantumSocket>(socketId);
			if (qsbSocket == null)
			{
				DebugLog.ToConsole($"Couldn't find socket id {socketId}", MessageType.Error);
				return;
			}

			var socket = qsbSocket.AttachedObject;
			if (socket == null)
			{
				DebugLog.ToConsole($"QSBSocket id {socketId} has no attached socket.", MessageType.Error);
				return;
			}

			var wasEntangled = AttachedObject.IsPlayerEntangled();
			var component = Locator.GetPlayerTransform().GetComponent<OWRigidbody>();
			var location = new RelativeLocationData(Locator.GetPlayerTransform().GetComponent<OWRigidbody>(), AttachedObject.transform);

			AttachedObject.MoveToSocket(socket);

			if (wasEntangled)
			{
				component.MoveToRelativeLocation(location, AttachedObject.transform);
			}

			if (QuantumManager.Shrine != AttachedObject)
			{
				AttachedObject.transform.localRotation = localRotation;
			}
			else
			{
				var playerToShrine = QSBPlayerManager.GetPlayer(playerId).Body.transform.position - AttachedObject.transform.position;
				var projectOnPlace = Vector3.ProjectOnPlane(playerToShrine, AttachedObject.transform.up);
				var angle = OWMath.Angle(AttachedObject.transform.forward, projectOnPlace, AttachedObject.transform.up);
				angle = OWMath.RoundToNearestMultiple(angle, 120f);
				AttachedObject.transform.rotation = Quaternion.AngleAxis(angle, AttachedObject.transform.up) * AttachedObject.transform.rotation;
			}
		}
	}
}