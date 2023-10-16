﻿using System;
namespace GigMobile.Services
{
    public static class GigGossipNodeConfig
    {
        public const string GigWalletOpenApi = "http://localhost:7101/";
        public const string DatabaseFile = "GigGossip.db3";
        public const int Fanout = 2;
        public static string[] NostrRelays = new string[] { "ws://localhost:6969" };
        public static Uri SettlerOpenApi = new Uri("http://localhost:7189/");
        public const long PriceAmountForRouting = 1000;
        public const int BroadcastConditionsTimeoutMs = 1000000;
        public const string BroadcastConditionsPowScheme = "sha256";
        public const int BroadcastConditionsPowComplexity = 0;
        public const int TimestampToleranceMs = 1000000;
        public const int InvoicePaymentTimeoutSec = 1000;
        public const int ChunkSize = 2048;
}
}

