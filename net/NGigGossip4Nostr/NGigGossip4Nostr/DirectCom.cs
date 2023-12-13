﻿using System;
using NBitcoin.Protocol;
using System.Diagnostics;
using NBitcoin.Secp256k1;

namespace NGigGossip4Nostr;

public class DirectMessageEventArgs : EventArgs
{
    public required string EventId;
    public required string SenderPublicKey;
    public required object Frame { get; set; }
    public required bool IsNew { get; set; }
}

public class DirectCom : NostrNode
{
    public DirectCom(ECPrivKey privateKey, int chunkSize) : base(privateKey,chunkSize)
    {

    }

    public DirectCom(NostrNode me, int chunkSize) : base(me, chunkSize)
    {
    }

    public new async Task StartAsync(string[] nostrRelays)
    {
        await base.StartAsync(nostrRelays);
    }

    public event EventHandler<DirectMessageEventArgs> OnDirectMessage;

    public override void OnContactList(string eventId, bool isNew, Dictionary<string, NostrContact> contactList)
    {
    }

    public override void OnHello(string eventId, bool isNew, string senderPublicKeye)
    {
    }

    public async override Task OnMessageAsync(string eventId, bool isNew, string senderPublicKey, object frame)
    {
        OnDirectMessage.Invoke(this, new DirectMessageEventArgs()
        {
            EventId = eventId,
            SenderPublicKey = senderPublicKey,
            Frame = frame,
            IsNew = isNew,
        });
    }
}

