﻿using OWML.Common;
using OWML.ModHelper;
using QSB.TimeSync;
using UnityEngine;
using UnityEngine.Networking;

namespace QSB
{
    public class QSB : ModBehaviour
    {
        public static IModHelper Helper;
        public static string DefaultServerIP;

        private void Awake()
        {
            Application.runInBackground = true;
        }

        private void Start()
        {
            Helper = ModHelper;

            gameObject.AddComponent<DebugLog>();
            gameObject.AddComponent<QSBNetworkManager>();
            gameObject.AddComponent<NetworkManagerHUD>();
        }

        public override void Configure(IModConfig config)
        {
            DefaultServerIP = config.GetSettingsValue<string>("defaultServerIP");
        }
    }
}
