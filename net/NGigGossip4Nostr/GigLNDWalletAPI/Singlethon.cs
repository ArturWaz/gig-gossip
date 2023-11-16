﻿using System;
using LNDWallet;

namespace GigLNDWalletAPI;

public static class Singlethon
{
    public static LNDWalletManager LNDWalletManager = null;

    public static HubDicStore<string> PaymentHashes4ConnectionId = new();
    public static HubDicStore<string> InvoiceHashes4ConnectionId = new();

}
