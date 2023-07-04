﻿using System;
using NGigGossip4Nostr;
using System.Diagnostics;
using NBitcoin.Protocol;
using NBitcoin.Secp256k1;
using System.Numerics;
using System.Reflection;
using System.Buffers.Text;
using System.Threading.Channels;
using System.Net.NetworkInformation;
using System.Text;

public class GigGossipNode : NamedEntity, IHodlInvoiceIssuer,IHodlInvoicePayer
{

    protected Certificate certificate;
    protected ECPrivKey _privateKey;
    protected PaymentChannel paymentChannel;
    protected int priceAmountForRouting;
    protected TimeSpan broadcastConditionsTimeout;
    protected string broadcastConditionsPowScheme;
    protected int broadcastConditionsPowComplexity;
    protected TimeSpan timestampTolerance;
    protected TimeSpan invoicePaymentTimeout;
    protected Settler settler;
    protected Dictionary<string, GigGossipNode> _knownHosts;
    protected Dictionary<Guid, BroadcastPayload> _broadcastPayloadsByAskId;
    protected Dictionary<Guid, POWBroadcastConditionsFrame> _myPowBrCondByAskId;
    protected Dictionary<Guid, int> _alreadyBroadcastedRequestPayloadIds;
    protected Dictionary<Guid, Dictionary<ECXOnlyPubKey, List<Tuple<ReplyPayload, HodlInvoice>>>> replyPayloads;
    protected Dictionary<Guid, HodlInvoice> nextNetworkInvoiceToPay;
    protected Dictionary<Guid, ReplyPayload> replyPayloadsByHodlInvoiceId;

    public GigGossipNode(string name) : base(name)
    {

    }

    protected void Init(Certificate certificate, ECPrivKey privateKey, PaymentChannel paymentChannel,
                           int priceAmountForRouting, TimeSpan broadcastConditionsTimeout, string broadcastConditionsPowScheme,
                           int broadcastConditionsPowComplexity, TimeSpan timestampTolerance, TimeSpan invoicePaymentTimeout,
                           Settler settler)
    {
        this.certificate = certificate;
        this._privateKey = privateKey;
        this.paymentChannel = paymentChannel;
        this.priceAmountForRouting = priceAmountForRouting;
        this.broadcastConditionsTimeout = broadcastConditionsTimeout;
        this.broadcastConditionsPowScheme = broadcastConditionsPowScheme;
        this.broadcastConditionsPowComplexity = broadcastConditionsPowComplexity;
        this.timestampTolerance = timestampTolerance;
        this.invoicePaymentTimeout = invoicePaymentTimeout;
        this.settler = settler;

        this._knownHosts = new();
        this._broadcastPayloadsByAskId = new();
        this._myPowBrCondByAskId = new();
        this._alreadyBroadcastedRequestPayloadIds = new();
        this.replyPayloads = new();
        this.nextNetworkInvoiceToPay = new();
        this.replyPayloadsByHodlInvoiceId = new();
    }

    public void OnHodlInvoiceSettled(HodlInvoice invoice)
    {
        var message = (byte[]) Crypto.SymmetricDecrypt(invoice.Preimage, replyPayloadsByHodlInvoiceId[invoice.Id].EncryptedReplyMessage);
        Trace.TraceInformation(Encoding.Default.GetString(message));
    }

    public bool OnHodlInvoiceAccepting(HodlInvoice invoice)
    {
        if (nextNetworkInvoiceToPay.ContainsKey(invoice.Id))
            paymentChannel.PayHodlInvoice(nextNetworkInvoiceToPay[invoice.Id]);
        return true;
    }

    public void ConnectTo(GigGossipNode other)
    {
        if (other.Name == this.Name)
            throw new Exception("Cannot connect node to itself");
        this._knownHosts[other.Name] = other;
        other._knownHosts[this.Name] = this;
    }

    public virtual bool AcceptTopic(AbstractTopic topic)
    {
        return false;
    }

    public void IncrementBroadcasted(Guid payloadId)
    {
        if (!_alreadyBroadcastedRequestPayloadIds.ContainsKey(payloadId))
            _alreadyBroadcastedRequestPayloadIds[payloadId] = 0;
        _alreadyBroadcastedRequestPayloadIds[payloadId] += 1;
    }

    public bool CanIncrementBroadcast(Guid payloadId)
    {
        if (!_alreadyBroadcastedRequestPayloadIds.ContainsKey(payloadId))
            return true;
        return _alreadyBroadcastedRequestPayloadIds[payloadId] <= 2;
    }

    public void Broadcast(RequestPayload requestPayload,
                          string? originatorPeerName = null,
                          OnionRoute? backwardOnion = null)
    {
        if (!this.AcceptTopic(requestPayload.Topic))
        {
            return;
        }

        this.IncrementBroadcasted(requestPayload.PayloadId);

        if (!this.CanIncrementBroadcast(requestPayload.PayloadId))
        {
            Trace.TraceInformation("already broadcasted");
            return;
        }

        foreach (KeyValuePair<string, GigGossipNode> peer in _knownHosts)
        {
            if (peer.Key == originatorPeerName)
                continue;

            AskForBroadcastFrame askForBroadcastFrame = new AskForBroadcastFrame()
            {
                SignedRequestPayload = requestPayload,
                AskId = Guid.NewGuid()
            };

            BroadcastPayload broadcastPayload = new BroadcastPayload()
            {
                SignedRequestPayload = requestPayload,
                BackwardOnion = (backwardOnion ?? new OnionRoute()).Grow(new OnionLayer(this.Name),
                this._privateKey,
                peer.Value.certificate.PublicKey),
                Timestamp = null
            };

            this._broadcastPayloadsByAskId[askForBroadcastFrame.AskId] = broadcastPayload;
            this.NewMessage(peer.Value, askForBroadcastFrame);
        }
    }

    public void OnAskForBroadcastFrame(GigGossipNode peer, AskForBroadcastFrame askForBroadcastFrame)
    {
        if (!CanIncrementBroadcast(askForBroadcastFrame.SignedRequestPayload.PayloadId))
        {
            Trace.TraceInformation("already broadcasted, don't ask");
            return;
        }
        POWBroadcastConditionsFrame powBroadcastConditionsFrame = new POWBroadcastConditionsFrame()
        {
            AskId = askForBroadcastFrame.AskId,
            ValidTill = DateTime.Now.Add(this.broadcastConditionsTimeout),
            WorkRequest = new WorkRequest()
            {
                PowScheme = this.broadcastConditionsPowScheme,
                PowTarget = ProofOfWork.PowTargetFromComplexity(this.broadcastConditionsPowScheme, this.broadcastConditionsPowComplexity)
            },
            TimestampTolerance = this.timestampTolerance
        };

        _myPowBrCondByAskId[powBroadcastConditionsFrame.AskId] = powBroadcastConditionsFrame;
        NewMessage(peer, powBroadcastConditionsFrame);
    }

    public void OnPOWBroadcastConditionsFrame(GigGossipNode peer, POWBroadcastConditionsFrame powBroadcastConditionsFrame)
    {
        if (DateTime.Now <= powBroadcastConditionsFrame.ValidTill)
        {
            if (_broadcastPayloadsByAskId.ContainsKey(powBroadcastConditionsFrame.AskId))
            {
                BroadcastPayload broadcastPayload = _broadcastPayloadsByAskId[powBroadcastConditionsFrame.AskId];
                broadcastPayload.SetTimestamp(DateTime.Now);
                var pow = powBroadcastConditionsFrame.WorkRequest.ComputeProof(broadcastPayload);    // This will depend on your computeProof method implementation
                POWBroadcastFrame powBroadcastFrame = new POWBroadcastFrame()
                {
                    AskId = powBroadcastConditionsFrame.AskId,
                    BroadcastPayload = broadcastPayload,
                    ProofOfWork = pow
                };
                NewMessage(peer, powBroadcastFrame);
            }
        }
    }

    public virtual Tuple<byte[]?, int> AcceptBroadcast(RequestPayload signedRequestPayload)
    {
        return new Tuple<byte[]?, int>(null, 0);
    }

    public void OnPOWBroadcastFrame(GigGossipNode peer, POWBroadcastFrame powBroadcastFrame)
    {
        if (!_myPowBrCondByAskId.ContainsKey(powBroadcastFrame.AskId))
            return;

        var myPowBroadcastConditionFrame = _myPowBrCondByAskId[powBroadcastFrame.AskId];

        if (powBroadcastFrame.ProofOfWork.PowScheme != myPowBroadcastConditionFrame.WorkRequest.PowScheme)
            return;

        if (powBroadcastFrame.ProofOfWork.PowTarget != myPowBroadcastConditionFrame.WorkRequest.PowTarget)
            return;

        if (powBroadcastFrame.BroadcastPayload.Timestamp > DateTime.Now)
            return;

        if (powBroadcastFrame.BroadcastPayload.Timestamp + myPowBroadcastConditionFrame.TimestampTolerance < DateTime.Now)
            return;

        if (!powBroadcastFrame.Verify())
            return;

        var messageAndFeeTuple = this.AcceptBroadcast(powBroadcastFrame.BroadcastPayload.SignedRequestPayload);
        var message = messageAndFeeTuple.Item1;
        var fee = messageAndFeeTuple.Item2;

        if (message != null)
        {
            var invoiceIdAndOnAcceptedTuple = this.settler.GenerateReplyPaymentTrust();
            var invoiceId = invoiceIdAndOnAcceptedTuple.Item1;
            var replyPaymentHash = invoiceIdAndOnAcceptedTuple.Item2;

            var replyInvoice = this.paymentChannel.CreateHodlInvoice(this.Name,peer.Name,settler.Name,
                fee, replyPaymentHash, DateTime.MaxValue, invoiceId);

            var messageAndNetworkInvoiceTuple = this.settler.GenerateSettlementTrust(this.Name,
                peer.Name,
                message,
                replyInvoice,
                powBroadcastFrame.BroadcastPayload.SignedRequestPayload,
                this.certificate);

            var signedSettlementPromise = messageAndNetworkInvoiceTuple.Item1;
            var networkInvoice = messageAndNetworkInvoiceTuple.Item2;
            var encryptedReplyPayload = messageAndNetworkInvoiceTuple.Item3;

            var responseFrame = new ReplyFrame()
            {
                EncryptedReplyPayload = encryptedReplyPayload,
                SignedSettlementPromise = signedSettlementPromise,
                ForwardOnion = powBroadcastFrame.BroadcastPayload.BackwardOnion,
                NetworkInvoice = networkInvoice
            };

            this.OnResponseFrame(peer, responseFrame, newResponse: true);
        }
        else
        {
            this.Broadcast(
                requestPayload: powBroadcastFrame.BroadcastPayload.SignedRequestPayload,
                originatorPeerName: peer.Name,
                backwardOnion: powBroadcastFrame.BroadcastPayload.BackwardOnion);
        }
    }

    public void OnResponseFrame(GigGossipNode peer, ReplyFrame responseFrame, bool newResponse = false)
    {
        if (responseFrame.ForwardOnion.IsEmpty())
        {
            if (responseFrame.SignedSettlementPromise.NetworkPaymentHash != responseFrame.NetworkInvoice.PaymentHash)
            {
                Trace.TraceError("reply payload has different network_payment_hash than network_invoice");
                return;
            }

            ReplyPayload replyPayload = responseFrame.DecryptAndVerify(_privateKey, responseFrame.SignedSettlementPromise.SettlerCertificate.PublicKey);
            if (replyPayload == null)
            {
                Trace.TraceError("reply payload mismatch");
                return;
            }
            var payloadId = replyPayload.SignedRequestPayload.PayloadId;
            if (!replyPayloads.ContainsKey(payloadId))
            {
                replyPayloads[payloadId] = new();
            }
            var replierId = replyPayload.ReplierCertificate.PublicKey;
            if (!replyPayloads[payloadId].ContainsKey(replierId))
            {
                replyPayloads[payloadId][replierId] = new();
            }

            replyPayloads[payloadId][replierId].Add(new Tuple<ReplyPayload, HodlInvoice>(replyPayload, responseFrame.NetworkInvoice));
            replyPayloadsByHodlInvoiceId[responseFrame.NetworkInvoice.Id] = replyPayload;
            Trace.TraceInformation("reply payload frame collected");
        }
        else
        {
            var topLayer = responseFrame.ForwardOnion.Peel(_privateKey, peer.certificate.PublicKey);
            if (_knownHosts.ContainsKey(topLayer.PeerName))
            {
                if (!responseFrame.SignedSettlementPromise.VerifyAll(responseFrame.EncryptedReplyPayload))
                {
                    return;
                }
                if (responseFrame.SignedSettlementPromise.NetworkPaymentHash != responseFrame.NetworkInvoice.PaymentHash)
                {
                    return;
                }
                if (!newResponse)
                {
                    var nextNetworkInvoice = responseFrame.NetworkInvoice;
                    var networkInvoice = paymentChannel.CreateHodlInvoice(
                        this.Name,
                        peer.Name,
                        settler.Name,
                        responseFrame.NetworkInvoice.Amount + this.priceAmountForRouting,
                        responseFrame.NetworkInvoice.PaymentHash,
                        DateTime.MaxValue, Guid.NewGuid());
                    this.nextNetworkInvoiceToPay[networkInvoice.Id] = nextNetworkInvoice;
                    responseFrame = responseFrame.DeepCopy();
                    responseFrame.NetworkInvoice = networkInvoice;
                }
                NewMessage(_knownHosts[topLayer.PeerName], responseFrame);
            }
        }
    }

    public List<List<Tuple<ReplyPayload, HodlInvoice>>> GetResponses(Guid payloadId)
    {
        if (!replyPayloads.ContainsKey(payloadId))
        {
            Trace.TraceError("topic has no responses");
            return new();
        }
        return replyPayloads[payloadId].Values.ToList();
    }

    public void PayAndReadResponse(ReplyPayload replyPayload, HodlInvoice networkInvoice)
    {
        var payloadId = replyPayload.SignedRequestPayload.PayloadId;
        if (!replyPayloads.ContainsKey(payloadId))
        {
            Trace.TraceError("topic has no responses");
            return;
        }

        if (!replyPayloads[payloadId].ContainsKey(replyPayload.ReplierCertificate.PublicKey))
        {
            Trace.TraceError("replier has not responded for this topic");
            return;
        }

        Trace.TraceInformation("paying and reading");

        paymentChannel.PayHodlInvoice(networkInvoice);
    }

    public void NewMessage(GigGossipNode targetNode, object frame)
    {
        targetNode.OnMessage(this, frame);
    }

    public void OnMessage(GigGossipNode senderNode, object frame)
    {
        if (frame is AskForBroadcastFrame)
        {
            OnAskForBroadcastFrame(senderNode, (AskForBroadcastFrame)frame);
        }
        else if (frame is POWBroadcastConditionsFrame)
        {
            OnPOWBroadcastConditionsFrame(senderNode, (POWBroadcastConditionsFrame)frame);
        }
        else if (frame is POWBroadcastFrame)
        {
            OnPOWBroadcastFrame(senderNode, (POWBroadcastFrame)frame);
        }
        else if (frame is ReplyFrame)
        {
            OnResponseFrame(senderNode, (ReplyFrame)frame);
        }
        else
        {
            Trace.TraceError("unknown request: ", senderNode, frame);
        }
    }

}