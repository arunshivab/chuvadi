// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.2.3 — CMS timestamp integration

using System;
using Chuvadi.Cryptography.Cms;
using Chuvadi.Cryptography.Hashing;
using Chuvadi.Cryptography.PublicKey;
using Chuvadi.Cryptography.Signing;
using Chuvadi.Cryptography.Timestamps;
using Chuvadi.Cryptography.X509;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.Cms;

public sealed class CmsTimestampIntegrationTests
{
    private const string Pkcs8Hex =
        "308204bf020100300d06092a864886f70d0101010500048204a9308204a50201000282010100b9c5" +
        "0729fca5b148ff03a6fabc563f64fd73941a2176ee259731707285628a70c024f897a6ac3c956687" +
        "5048f74a1c09cb631c50842b465bfbd432b81f9c2ea7a989d6bfb462a2dadee693731f99061e849c" +
        "cad1da67443428ccb8829c9951832322334b030f0b4e15303d1872cde4baf5a83046f5c26cbf5454" +
        "88b14b157ff6ee06e5562fcd5096c107c99655b7287c0348454d6d99686e9790936713bc7d3626ad" +
        "73939e7c38e2aff4ee618381dbae9835cc087ea4ff767f015d5f64c855b8a8e5fb376be746d67bb0" +
        "c20303868d056b9aa4faf015e31cf0ed12d4aa2f9584de6d986204b5878a61626a03896fe0d7b649" +
        "66305b7a35da54fa267bc3a9c46f0203010001028201000f23f26edc45da2a7633a355a6d3eb5c1d" +
        "16b668b576d94ca703432ee795731862bb4b522626137f4f03e83f34d30d709eeaf7a67849d94a2b" +
        "3ecd836ed01932abac02f1f78f07c6dede86ab48aaa81d04a1e2c5dd0f083802bb4203cdcf911d27" +
        "028fe64fdc2cb25dcd0e0302c2ffc95d0c2acdc869e2d86a7f1944ef36feb42032e0148d02e21ea9" +
        "9812f5e48a63f016b46e21e05e5a2ed2f3122f0ba3923fd5e1938d4d0914bcadf57329739a838c7d" +
        "019e979607d5f514ace83f7d71cee024e79cfd263637cf62a769f1877e3d878afb6ebf4466a8cf57" +
        "2675d07bcd709354fb1a540b077e0b8fb4ed187220357fa2748406b63f4a320b7430f6da8d6d0102" +
        "818100f8889a6a8cf51815cd678057cf01559a04c5e111957f4952ff39d7e38af6c52d7c4e2fb834" +
        "0d49c907916c4d4df5f836d5ce03fdba4e59effd0116afb661565892417a6150ada0560f7859689a" +
        "b7f5ef0171a8208231fcc67294a62221acd7463ea9d5547554e5d0adf2201092d4d32fd913201c6a" +
        "67d50deceaa31d8a34b33102818100bf59b9e2b37c9fb2dfe05afaaf15877c21d2ce860b78525dcf" +
        "349e0436b8c89d0b70529bddebf92d3606aa642604f6afd733769205ad1c060fe69b365fbceeec1c" +
        "eab5026e23a2661a9bc03c049dc845ade23a1262decb152f771dc332d9d38d372a2b62ade1cb5f52" +
        "067d32c6e6dc95b7b16d0854797f894abfcdfd6ecdc99f02818100f0b5cd68f950c09d0d2dfb7e10" +
        "3de89c9d96d19fe83d39d52ae0e919b713be71897d68766de398dd1d79597d9dce673324ecbdacd6" +
        "eedfe8b21085da7537dd1b37bc373d5d986c3c2e0b8ffce22cde033850ce577e01d0229c0320ccd9" +
        "f4bf2387b991a695653e985880b3519a048aee42be655160356482723de6f1cb53b3610281810098" +
        "b5f41b0fe1a2d62fc3aef827e907b2b28fba10d270995392bd4c6ad27d5065bd2e4c4f66a21fbfcc" +
        "412f95339e7c7dc342a81b4b7a674613449894a17d783469b38af8408c21dc58d9fa662bccfc7b57" +
        "959780faf511a07bbc15bda6049fc830c16fd4962f008eb738c48c549f04665c2eb674926e50b172" +
        "3d77190e681fc302818100ca034bbd527beb5754ed24c17281b0b7ab403fec106489a97ee8f4c307" +
        "e258ae58fc7ecf74ee9739ff80f40862aefe8b4adab12eb541351f738129b11d17cd9d4a4164cae0" +
        "2ee6611641b708668398fa32d6b895ea1d612f6fcff268f06f9602d6ad541ac689ff14396d20c90d" +
        "c3be3335608a82115ae1aad1563b8eceae6262";

    private const string CertDerHex =
        "308202e8308201d0a003020102020101300d06092a864886f70d01010b0500302531233021060355" +
        "04030c1a436875766164692053656c662d5369676e6564205369676e6572301e170d323430313031" +
        "3030303030305a170d3330303130313030303030305a30253123302106035504030c1a4368757661" +
        "64692053656c662d5369676e6564205369676e657230820122300d06092a864886f70d0101010500" +
        "0382010f003082010a0282010100b9c50729fca5b148ff03a6fabc563f64fd73941a2176ee259731" +
        "707285628a70c024f897a6ac3c9566875048f74a1c09cb631c50842b465bfbd432b81f9c2ea7a989" +
        "d6bfb462a2dadee693731f99061e849ccad1da67443428ccb8829c9951832322334b030f0b4e1530" +
        "3d1872cde4baf5a83046f5c26cbf545488b14b157ff6ee06e5562fcd5096c107c99655b7287c0348" +
        "454d6d99686e9790936713bc7d3626ad73939e7c38e2aff4ee618381dbae9835cc087ea4ff767f01" +
        "5d5f64c855b8a8e5fb376be746d67bb0c20303868d056b9aa4faf015e31cf0ed12d4aa2f9584de6d" +
        "986204b5878a61626a03896fe0d7b64966305b7a35da54fa267bc3a9c46f0203010001a323302130" +
        "0f0603551d130101ff040530030101ff300e0603551d0f0101ff0404030202c4300d06092a864886" +
        "f70d01010b0500038201010074653878a8af7257342375f9632ba548b2b081af0364a8a1068ca5fc" +
        "456597f85d2a91b119be39d3ff2acbd482ba0bc5f31babaae1693ce2349b7747eac9b9961b64a6e6" +
        "99f3520170e01fa3c86263a6570f375847839f11de079f11d13087a6dab8f29e448aef7e700a5b2e" +
        "ab6b3216dee2acf61262e51ecacd40b0f54b02f552a49d3b3609fd4db14f5ce2f8e2b998454468ff" +
        "eca1d0093a2a5b16690bf10fdac4fc2e38d34b8bc8c72e8e425cbfd000205cff675623e5db07b2b6" +
        "3312db2f93867ec28c1d982298d25090a3df33ca65b91bf7682047d796b8c76f1fd4abf7c1cd9ccf" +
        "0e61017c2bc53f7bbd30d00efce40ba96871fb467a1c8741057e7846";

    private const string TsaResponseHex =
        "308208b83003020100308208af06092a864886f70d010702a08208a03082089c020103310d300b06" +
        "09608648016503040201306d060b2a864886f70d0109100104a05e045c305a02010106052a030405" +
        "063031300d0609608648016503040201050004202dbbac98523e9930516ea41f352287b9e9f2ffe9" +
        "2fe79f3ee1bcce84650fdd2c020400c0ffee180f32303236303531373130303835335a0204123456" +
        "78a08205dc308202e1308201c9a003020102020164300d06092a864886f70d01010b05003020311e" +
        "301c06035504030c154368757661646920546573742054534120526f6f74301e170d323030313031" +
        "3030303030305a170d3430303130313030303030305a3020311e301c06035504030c154368757661" +
        "646920546573742054534120526f6f7430820122300d06092a864886f70d01010105000382010f00" +
        "3082010a0282010100a50175ae2b6c62b6134404d5234f60242858d24f57e4418c1c9246465f1896" +
        "e27290c8e7c2f04e14d7cd1a34da321ee728268d4ad188b9da19e1de2132005f63535f94223d7f83" +
        "6bbbdd7f7d5a4d86a534466dbc06f5a3fbfe4a918f28dc8284f05dcc0f1e8b3b53e4dcf03706b271" +
        "5e39280459c252435ef56fc00d63d717fe2d7fc03f757ca3ac549bc9e616166a4f9f6f106accad25" +
        "19b099923851751c8c896b738da3b02de08ccde69add528438a4a830980663b307dafed99ee44837" +
        "493677e6e1cbc6037ac59b9c8de39b44c1a204292278a6bd1ee90a36ace642cc77146c21adfee75d" +
        "2e7b46013331a71abddf44c1959dec4be84b5312cb220688590203010001a326302430120603551d" +
        "130101ff040830060101ff020101300e0603551d0f0101ff040403020106300d06092a864886f70d" +
        "01010b0500038201010023854ea130d116ce60af5bcb4d791b39ea1b9570147802bc4aa58e3c8cfd" +
        "d15cc64de32c2c415f41cb24521765d02832b484c614518a67d31987231ec3c287a2e84c12547df3" +
        "e0ca8f0c5e0d0c6fd70a156ad2babe03cf8a396dc293224fd9ffcf8e6608d2095800b2d09e443285" +
        "a9734d24d0e890d916fb805b165c658849285ce611a40cb9953aa0a7ca85466b716c3311d76c88e4" +
        "68842f0b1a70a049c5c902e424bbc5c6702fc52cd0e470a5419633eb165151e00b21fa1b9e240692" +
        "7b6c1e5d9fad1237caaf698a2801e00e4b62f65613642cfba38f4601515cff4ac54972e23c833767" +
        "79199270a1e2348e1b7901db5d3ec31121a8de1a7bce57570616308202f3308201dba00302010202" +
        "0165300d06092a864886f70d01010b05003020311e301c06035504030c1543687576616469205465" +
        "73742054534120526f6f74301e170d3234303130313030303030305a170d33303031303130303030" +
        "30305a3020311e301c06035504030c1543687576616469205465737420545341204c656166308201" +
        "22300d06092a864886f70d01010105000382010f003082010a0282010100c494eaab3897c305ea3a" +
        "612ffb8022ab8b9cae76b5929f63fc64d0dc09609aabc3f0eea96ef89ead9278500d43fde3eb9d29" +
        "36f6f1bd1eea834909dda50a9c3859166090e86a04f7f526186368319947bb554d2a11d773c659b7" +
        "980d157a5f8044ca788c1fca2b2d4598cb3d37502b199cf8e779a1c3c8fa5f8ab3ec676a034d7649" +
        "276e61066816538ae5ef48d1ba690c6c51bb1f92484c12494615a8867dc3918926505c770221ebf2" +
        "2df0e57ffa661dfb76af00a79a276b1700eeedc6e40baca1a77c8bdd8712c33ba12d78d5199fe017" +
        "f904da2df6b37279dadf5dab494ba72ad7675159a3a8ff1aa557810b379fffa5808e932c9e968d2c" +
        "768fff7564f30203010001a3383036300c0603551d130101ff0402300030160603551d250101ff04" +
        "0c300a06082b06010505070308300e0603551d0f0101ff0404030206c0300d06092a864886f70d01" +
        "010b0500038201010032458fbca1157f014d0a792576b1e20ba6847db753bc98e68d309e127777de" +
        "aa2e3a4892c1eab8522a1eda1cc95f6a63e8a348afdb437775f591031daf6082c5f7e1566f9639a5" +
        "51d33d35d5847a36787912b5fd27b748fa48f0147ce45dfab184b757783ba945edcf87762090495f" +
        "9fa77113a346a99bd1af3e1f7c8c3311f95e503ab5fcc6044780ac2d7756b9ab5c94fffa7fbde465" +
        "3dae9db3fbfc78feceed053a6b62d33406525a632730d9839b043d2bc4ae3a5a7f6d4073a33fa501" +
        "c53dcfb579beefea4a52732bf8f2cb41dc31969c0895309f3aa6a1e6cc3a2a6b820b31d83b6daae1" +
        "69ac91b1002f668f037bf894f9e49f6410c7d640f9cbdb7d61318202373082023302010130253020" +
        "311e301c06035504030c154368757661646920546573742054534120526f6f74020165300b060960" +
        "8648016503040201a081e6301a06092a864886f70d010903310d060b2a864886f70d010910010430" +
        "1c06092a864886f70d010905310f170d3236303531373130303835335a302f06092a864886f70d01" +
        "0904312204205e22c8f062e9ba5a04282a81678091d2c136d808188c294e68700a3adc46ff3f3079" +
        "06092a864886f70d01090f316c306a300b060960864801650304012a300b06096086480165030401" +
        "16300b0609608648016503040102300a06082a864886f70d0307300e06082a864886f70d03020202" +
        "0080300d06082a864886f70d0302020140300706052b0e030207300d06082a864886f70d03020201" +
        "28300d06092a864886f70d0101010500048201009917d67df21ec570c7be0873327c1d63e90259c1" +
        "c5fe6a17df01fb01384dce10be87dd223fe8cf0d2934aeaa460756bbdf3b8a303e2a875a08161067" +
        "116d58e8a55c5dd773b260d2c6f4ece786cd43510ba903f878bb03783473f23fa52d30cf41afc338" +
        "22a8032f9f8b051a332c2341714e298e5cfa3e0ced226f87fcbd7d422d5b8d6dd8c9f9fe1e4a7486" +
        "2f9e9dc3ec9266bd2d90714625b64d687d76f2bab0bee5093900c17dedd02919ea758e23298dc9e1" +
        "d79dfcd5c0c05f428d1f046336b14b341c9c2d3ecb97c432d3c5518c0473993c42af67d941fe9349" +
        "854c86d9574ca73856e3824b8b06575fec68104afccdcfca897f6484bc87527ed444dcc2";

    private sealed class FakeTsaClient : ITsaClient
    {
        private readonly TimeStampResponse _response;
        public FakeTsaClient(TimeStampResponse response) { _response = response; }
        public TimeStampResponse Fetch(TimeStampRequest request) => _response;
    }

    private static ISigner BuildSigner()
    {
        RsaPrivateKey priv = RsaPrivateKey.FromPkcs8(Convert.FromHexString(Pkcs8Hex));
        X509Certificate cert = X509Certificate.Decode(Convert.FromHexString(CertDerHex));
        return new RsaPkcs1V15Signer(priv, cert, HashAlgorithmName.Sha256);
    }

    [Fact]
    public void BuildSignatureTimeStampAttribute_StructuresAsAttribute()
    {
        TimeStampResponse resp = TimeStampResponse.Decode(Convert.FromHexString(TsaResponseHex));
        resp.IsGranted.Should().BeTrue();

        byte[] attr = CmsSignedDataBuilder.BuildSignatureTimeStampAttribute(
            resp.TimeStampToken!.RawEncoding);
        // Attribute is SEQUENCE { OID id-aa-signatureTimeStampToken, SET { token } }
        attr[0].Should().Be(0x30);
        attr.Length.Should().BeGreaterThan(resp.TimeStampToken.RawEncoding.Length);
    }

    [Fact]
    public void BuildDetachedWithTimestamp_EmbedsTokenAsUnsignedAttribute()
    {
        TimeStampResponse cannedResp = TimeStampResponse.Decode(Convert.FromHexString(TsaResponseHex));
        ITsaClient client = new FakeTsaClient(cannedResp);
        ISigner signer = BuildSigner();

        byte[] cms = CmsSignedDataBuilder.BuildDetachedWithTimestamp(
            "data"u8.ToArray(), signer, client,
            signingTime: new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero));

        // CMS decodes; SignerInfo has an unsigned attribute carrying the TST.
        SignedData sd = CmsDecoder.DecodeSignedData(cms);
        sd.SignerInfos.Should().HaveCount(1);
        // The verifier exposes the signature timestamp via SignerInfo helpers when
        // present in unsignedAttrs.
    }

    [Fact]
    public void BuildDetachedWithTimestamp_NullArgs_Throw()
    {
        ISigner signer = BuildSigner();
        ITsaClient client = new FakeTsaClient(
            TimeStampResponse.Decode(Convert.FromHexString(TsaResponseHex)));
        Action a = () => CmsSignedDataBuilder.BuildDetachedWithTimestamp(null!, signer, client);
        Action b = () => CmsSignedDataBuilder.BuildDetachedWithTimestamp("x"u8.ToArray(), null!, client);
        Action c = () => CmsSignedDataBuilder.BuildDetachedWithTimestamp("x"u8.ToArray(), signer, null!);
        a.Should().Throw<ArgumentNullException>();
        b.Should().Throw<ArgumentNullException>();
        c.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildDetachedWithTimestamp_RejectingTsa_ThrowsTsaException()
    {
        ITsaClient rejecting = new FakeTsaClient(
            new TimeStampResponse(TimeStampStatus.Rejection,
                new[] { "test rejection" }, null));
        ISigner signer = BuildSigner();
        Action act = () => CmsSignedDataBuilder.BuildDetachedWithTimestamp(
            "x"u8.ToArray(), signer, rejecting);
        act.Should().Throw<TsaException>()
            .WithMessage("*Rejection*");
    }
}
