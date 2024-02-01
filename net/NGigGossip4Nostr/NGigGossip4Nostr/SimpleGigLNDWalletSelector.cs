﻿using System;
using CryptoToolkit;
using GigGossipSettlerAPIClient;
using NBitcoin.Secp256k1;
using GigLNDWalletAPIClient;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace NGigGossip4Nostr;

public interface ISettlerSelector : ICertificationAuthorityAccessor
{
    GigGossipSettlerAPIClient.swaggerClient GetSettlerClient(Uri ServiceUri);
}

public class SimpleSettlerSelector : ISettlerSelector
{
    ConcurrentDictionary<Uri, GigGossipSettlerAPIClient.swaggerClient> swaggerClients = new();
    ConcurrentDictionary<Guid, bool> revokedCertificates = new();

    Func<HttpClient> _httpClientFactory;

    public SimpleSettlerSelector(Func<HttpClient> httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ECXOnlyPubKey> GetPubKeyAsync(Uri serviceUri)
    {
        return SettlerAPIResult.Get<string>(await GetSettlerClient(serviceUri).GetCaPublicKeyAsync()).AsECXOnlyPubKey();
    }

    public GigGossipSettlerAPIClient.swaggerClient GetSettlerClient(Uri serviceUri)
    {
        return swaggerClients.GetOrAdd(serviceUri, (serviceUri) => new GigGossipSettlerAPIClient.swaggerClient(serviceUri.AbsoluteUri, _httpClientFactory()));
    }

    public async Task<bool> IsRevokedAsync(Uri serviceUri, Guid id)
    {
        return await revokedCertificates.GetOrAddAsync(id, async (id) => SettlerAPIResult.Get<bool>(await GetSettlerClient(serviceUri).IsCertificateRevokedAsync(id.ToString())));
    }
}
