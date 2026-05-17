// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for OCSP decoder and signature verifier

using System;
using System.Numerics;
using Chuvadi.Cryptography.Ocsp;
using Chuvadi.Cryptography.Revocation;
using Chuvadi.Cryptography.X509;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.Ocsp;

public sealed class OcspResponseTests
{
    private const string IntermediateCertDerHex =
        "308202db308201c3a003020102020102300d06092a864886f70d01010b0500301931173015060355" +
        "04030c0e4f435350205465737420526f6f74301e170d3232303130313030303030305a170d333230" +
        "3130313030303030305a3021311f301d06035504030c164f435350205465737420496e7465726d65" +
        "646961746530820122300d06092a864886f70d01010105000382010f003082010a0282010100dd18" +
        "c02d3aebe1258389cf7c7af9746b159e3c1f3017f107c6c88da029753f9b5927d862d61b23972658" +
        "dcfd625097c08b96f5595c732a6eaf2c123953d1eaaf15de704b30c42aaa0ed1b47c4bf58dfd03f4" +
        "8ce6aa7eed0c158b4a2e07618fc4ed93faf6af45670b0f844287bb1662f1444f250ff1f158473b14" +
        "f664bb0947598bb424ed18c5dae04057b960d06415cd88c7da25e472803d73be37017a0f1c5d00c1" +
        "42e60d2c9ea90673ce4d3a7bcd57643b00ec1c3f9bdae4d194d75ac87296b5694714db5e02e4b077" +
        "9289951f7bc04b0e7ee6f26a77c658f7c6a00ccd4def392613cb6e631206f5177cbc46e7febf2dff" +
        "7bfdff35175939ebed3bad3380570203010001a326302430120603551d130101ff040830060101ff" +
        "020100300e0603551d0f0101ff040403020186300d06092a864886f70d01010b0500038201010046" +
        "b82adcb9d445248ec3345a3f7744963ecc4ba63c28eae813fdedc521df9ea6110601cf47a2e4b8e1" +
        "bb6f6d6ab34602f77c84d4adac561ef3e6ba4fcb7820b32cd74bc11847260c829bf34c8969cd85bd" +
        "8d73f724e35166aa3fd8376d5d03f89e61fbfe0bab7afce65f7b9985a28564c761485238f29bb29d" +
        "28688fbb944afa7ffdc291a9773a8d9f8ccc3dbbb3d6c354358d118ad4ca62d61e4c4873e8bfbee5" +
        "c6467f907d0ed4d3e490a00843bdf36c591575eb19ead6b214ca44c2839c8dd8274e3ccdc9c4c11d" +
        "6e0d4806d86d67a738b1505fa3a0a7c7b90fac90bc5aafb7914961f6c87e6043dd895516adf5e8f4" +
        "265b3d3cc3bf63f77d6ec77b5a6821";

    private const string LeafCertDerHex =
        "308202d7308201bfa003020102020142300d06092a864886f70d01010b05003021311f301d060355" +
        "04030c164f435350205465737420496e7465726d656469617465301e170d32343031303130303030" +
        "30305a170d3237303130313030303030305a301b3119301706035504030c104f4353502054657374" +
        "205369676e657230820122300d06092a864886f70d01010105000382010f003082010a0282010100" +
        "c12825affd818591aaa5f11fdcb5504f90506086c65fdd65cd64d135c1312278669f665b346f769d" +
        "7ea93233ac178879de7c3e2ca80279ed57606edadb8ad5fc8ec55b4b96369c963f5d0217d5bc447b" +
        "52fd358d0b32a1006ac587320abe69f88f5a0e3416f12bdab665293241157b321dab7c1c93a8df88" +
        "5440547509151949d7890f39379be9b44819085bf96ef5558f0b34be7c12e02d8ac380b0cec0d4c7" +
        "2547dd360e66f9824adfaa9448519d824cfd86f958ed8f95fffae40f666e6b874bd520cd1c542bcd" +
        "f43f9579000699200a8229ce6907dfb09cd54609d312a9a48d03a9b4e7d5a8f3e0b27c3870b0e205" +
        "5ec726849f7d62084f3fc8ebe95e13bf0203010001a320301e300c0603551d130101ff0402300030" +
        "0e0603551d0f0101ff0404030206c0300d06092a864886f70d01010b0500038201010088b7128498" +
        "deee19e8d9e0546037a25e05eacf0ac2c751c618eba8cb4759d775dd061ea197c049073f88295ada" +
        "d6e682e6a6a2a7ffc1d1f730213021072b82f3ac54d57acba802f258425d2c21e712ac168cbd703f" +
        "75f29288f8afeab0a718964779b6f4ad73809cf46783edb30d6b5bcd611f059e62ad0f9d2afa8888" +
        "dbb8298f65a1b029687ee64c1baba660fba2e4202fa41692e2f21b6f6db8b60df55418bb8c825f28" +
        "fda42ace2c7316251322f19686e837d11505ee2f0a680de9ea4be0e9359d3e725717a1eb11e6eb82" +
        "08f2809b96cde9293cbf9dc8d6378a84bc8e729a15b4bfe5126975acd0c18786a0bde7a0e437165b" +
        "e4d4cb90e7d090a25c1927";

    private const string OcspGoodDerHex =
        "308201d10a0100a08201ca308201c606092b0601050507300101048201b7308201b330819ca12330" +
        "21311f301d06035504030c164f435350205465737420496e7465726d656469617465180f32303236" +
        "303531363231343431365a30643062303a300906052b0e03021a050004146bf04ec34bc97ce92d24" +
        "88a41e5fac49c88bb3e904141a67e77bfd07f4600db6f68aee87e70348b182720201428000180f32" +
        "303235303530313030303030305aa011180f32303235303830313030303030305a300d06092a8648" +
        "86f70d01010b050003820101001121627f6782e8fd4150ae3c2c5d79d4b07f30f97d5c4819d6f58f" +
        "37a8765b49ee8c46f936df245549856d75fa98cdc03a81b2b4007a2a705452934ec2b4745785608a" +
        "714ec7d3fd029770e581dcd41dbd85d8ad19768893e85113c1fee2a2c3a3e27dece3478ef1ba02df" +
        "6240a8de981e74de693fd2e782c13d591a3148a0b89262aa9abb0934f25ce9a1002cf2e582d03323" +
        "2f12c1809969afe285af574c9a1cc8857ab5ab486d2defcdc70d4b2043281c00989a76bc61309868" +
        "11362653bfc5af35c0df599b843388cda752f36f9efdf327139641b2211af37a931747f6d8d2a6f1" +
        "1a0fc52dfdeda27ca44e9a30f142a1ecd16f6c437ab09f6eaec0582d21";

    private const string OcspRevokedDerHex =
        "308201e70a0100a08201e0308201dc06092b0601050507300101048201cd308201c93081b2a12330" +
        "21311f301d06035504030c164f435350205465737420496e7465726d656469617465180f32303236" +
        "303531363231343431365a307a3078303a300906052b0e03021a050004146bf04ec34bc97ce92d24" +
        "88a41e5fac49c88bb3e904141a67e77bfd07f4600db6f68aee87e70348b18272020142a116180f32" +
        "303235303431353030303030305aa0030a0101180f32303235303530313030303030305aa011180f" +
        "32303235303830313030303030305a300d06092a864886f70d01010b050003820101009586f2f1c5" +
        "eed155121b779cd11664f52745927b3b95a123abf78d25dcc239e0cc3133d9a1048d536bad7a0c7a" +
        "0eb64acab05b4345d9322b131f86395784fa9d00f37e79f721cb311bfe7fe20383c8b50008df942f" +
        "1fba176c507a3c6f9dfa3d5facb312f8d3dbc61f4c3e73490b89f5ba98cb29ecb2f2f92987b12e43" +
        "8b172a4c09b1dfa80bae6a525be0859cb9b7a1decc274e7a51dab6475b74fd77cdfb490975d7a2b1" +
        "6197cdeadef87c887c354597f44262447b6137cf1da92bca0777619a047daf6688f38de87e7ff129" +
        "be46b795d81e1281f16e7d1429a59fa6357d7e11de4798cebd03e60e5ed952d530a5b78f3ce1830c" +
        "4e25ed6a2b51b55de8a0d9";

    private const int LeafSerial = 66;

    private static OcspResponse DecodeGood() => OcspResponse.Decode(Convert.FromHexString(OcspGoodDerHex));
    private static OcspResponse DecodeRevoked() => OcspResponse.Decode(Convert.FromHexString(OcspRevokedDerHex));
    private static X509Certificate Intermediate() => X509Certificate.Decode(Convert.FromHexString(IntermediateCertDerHex));
    private static X509Certificate Leaf() => X509Certificate.Decode(Convert.FromHexString(LeafCertDerHex));

    [Fact]
    public void Decode_Good_Succeeds()
    {
        OcspResponse r = DecodeGood();
        r.Status.Should().Be(OcspResponseStatus.Successful);
        r.BasicResponse.Should().NotBeNull();
    }

    [Fact]
    public void Decode_Good_HasResponderByName()
    {
        BasicOcspResponse b = DecodeGood().BasicResponse!;
        b.ResponderId.IsByName.Should().BeTrue();
    }

    [Fact]
    public void Decode_Good_HasSingleEntryWithLeafSerial()
    {
        BasicOcspResponse b = DecodeGood().BasicResponse!;
        b.Responses.Should().HaveCount(1);
        b.Responses[0].CertId.SerialNumber.Should().Be(new BigInteger(LeafSerial));
        b.Responses[0].Status.IsGood.Should().BeTrue();
    }

    [Fact]
    public void Decode_Revoked_PopulatesRevocationFields()
    {
        SingleResponse sr = DecodeRevoked().BasicResponse!.Responses[0];
        sr.Status.IsRevoked.Should().BeTrue();
        sr.Status.RevocationTime.Should().NotBeNull();
        sr.Status.RevocationReason.Should().Be(CrlReason.KeyCompromise);
    }

    [Fact]
    public void SignatureVerifier_GoodResponse_AgainstCorrectIssuer_ReturnsResponder()
    {
        BasicOcspResponse b = DecodeGood().BasicResponse!;
        X509Certificate? responder = OcspResponseSignatureVerifier
            .VerifyAndIdentifyResponder(b, Intermediate());
        responder.Should().NotBeNull();
    }

    [Fact]
    public void SignatureVerifier_RevokedResponse_AgainstCorrectIssuer_ReturnsResponder()
    {
        BasicOcspResponse b = DecodeRevoked().BasicResponse!;
        X509Certificate? responder = OcspResponseSignatureVerifier
            .VerifyAndIdentifyResponder(b, Intermediate());
        responder.Should().NotBeNull();
    }

    [Fact]
    public void SignatureVerifier_WrongIssuer_ReturnsNull()
    {
        BasicOcspResponse b = DecodeGood().BasicResponse!;
        // Leaf is not a valid issuer for the OCSP response — Subject DN mismatch.
        X509Certificate? responder = OcspResponseSignatureVerifier
            .VerifyAndIdentifyResponder(b, Leaf());
        responder.Should().BeNull();
    }
}
