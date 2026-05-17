// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.2.4 — LTV signing
//
// End-to-end: sign a PDF with /DSS material (certs + CRLs + OCSPs),
// re-open, and confirm the existing DocumentSecurityStore reader picks
// it up and signature.Verify validates the chain without needing
// caller-supplied intermediates.

using System;
using System.IO;
using Chuvadi.Cryptography.Hashing;
using Chuvadi.Cryptography.Ocsp;
using Chuvadi.Cryptography.PathValidation;
using Chuvadi.Cryptography.PublicKey;
using Chuvadi.Cryptography.Revocation;
using Chuvadi.Cryptography.Signing;
using Chuvadi.Cryptography.X509;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Signatures.Dss;
using Chuvadi.Pdf.Signatures.Verification;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Signatures.Tests.Signing;

public sealed class PdfSignerLtvTests
{
    private const string LeafPkcs8Hex =
        "308204be020100300d06092a864886f70d0101010500048204a8308204a40201000282010100d451" +
        "861400c4e8ee65f330dd4dee7cab3a256358c6eedab750a566df9daa0bd85df71267106da241b5fd" +
        "c4a19e7a3c5f44b9f1439346227c9c36e20a9a65ca295901a5a7f3963d71e357e6e255dc5c8ab442" +
        "09dd1c59969d137472acc77e9206aaf907aafbf782c50ec9ced68a01e257af21a5d8210a3e8f5fc2" +
        "eeafb63383ce886349c16c049103c17e6eada7821413f6616e4492e01a76b317c040dab7213f5775" +
        "01d816d54eb964be1a97ade3ff4b3b41ebd8f2c217c04af338571fab985de4a7c764ff20db1d742f" +
        "92617f37d59246e24ff9d39782783e2733de482076093ec888ee35961cf6dc9aac553e32c2c450e2" +
        "26531be35e6b349617fc061c1b1d0203010001028201002fa19c693764f576aa237d3660a9dc8913" +
        "51d8f04d2cbf9f7975a9d707c962bfe710ab2db8f7477be366ab6ab0a16e92d6d9ba20f554ba1ee7" +
        "72be922f621f90d81970fef53c22cbbe7b755e187207c6cc3ab678c6c7e86c93b53f96b868923cf1" +
        "b54d7d93ea4a8987d0495942fbba39fe9d97559dcfed0a002b3c9de9cbb3c94480c89c673c1cb0e7" +
        "1983a167c043e0b9faff037033d44de3e7c51a03cf3fe687d45c5f6fde8f93fb8745d2c129bad34b" +
        "1d0bbed9c0408972d5fdd22a6b8195caae144d2980d672f49f0021c8938dbe6ba335ff2ed5760f16" +
        "b1b6b0613c247e37267dfea045148b98792111d03baf00ed9b6777e8c042681c429efe28452b5702" +
        "818100f49b6ba46ede1df4c4890f307a785379982e0f6b5861b40cfb3cdbb4c2d003c7e94301772a" +
        "d8e00d2ac98e1b58a055c246e51629a0f686e35a77b07f29c4044030ff915432fd99d0c63fd86c3c" +
        "4e3ced89443900c1d1bb39f5c6af3f6d72f3fd5addaf5a32143e74a3acc1df658f5db8885fcf38e8" +
        "e044b91c118440d6b2da7702818100de351bcac1311272a1c82f51ef6e73d32d56b551552e837ef2" +
        "cf2edd8c5bbeea1a16a93e65f7454429645a9517da395b9475c9df62aa66527014295ac80e490e6f" +
        "8001d7c253784162f3d05613c37c093a3969438f9910ee165b142e4de8f74d25e34a6374aa1168dc" +
        "898f9f76e0ecb3757ba068fe54438111c20bb6646f080b0281803ee4d389a5242189b51b14b7bf55" +
        "bf08edd3320dc4dce261d01bb6d6449d1dcbc2338365f3b36df094c6dc6e359c8c5076d022a1b38c" +
        "8fe457779cff256b0c38c120856aa3edc72602439a4f42364fbe37e43dcfef061160f6cc0e757d9b" +
        "e81685cda86fc59ea4ec72c551d83211e9e42fd48ac0b4482e0376af1e55599f054302818100cd51" +
        "ad586fa274354f9fb579b40f0f1ef629c4685e99180fd933ef4def3f66ecd1265743bcdbfa38bd36" +
        "692c9259a5de2513a170b3ae57d59c924494097e89aab90442afa673920e78ec6878e0d2246e324a" +
        "9225472e5c2262adcdbbeb6973f8e92557bb06358ed8a1cec9d2c2f99a3d4834ef4794992fb6b73d" +
        "e1acaaa89d7902818100b3cc7fecec89794e619c274d2c396a1a48f480c42750e18c1eb482df8cf8" +
        "f13f367b238f8ae328b3d2a4dababb89c4837dfdcbc9da6681acc515c2f01b4963b74a95aada4f11" +
        "c494dbaf2ca2c470eb9d9f1de7bba57c3e62fed9c43f6abfd4b0e2f944bd48dcd02312531c07239b" +
        "13cd2859dfb397761f21b77a24a3e17d7c92";

    private const string LeafCertDerHex =
        "308202e2308201caa0030201020203099999300d06092a864886f70d01010b050030283126302406" +
        "035504030c1d43687576616469204c5456205369676e20496e7465726d656469617465301e170d32" +
        "34303130313030303030305a170d3237303130313030303030305a301d311b301906035504030c12" +
        "43687576616469204c5456205369676e657230820122300d06092a864886f70d0101010500038201" +
        "0f003082010a0282010100d451861400c4e8ee65f330dd4dee7cab3a256358c6eedab750a566df9d" +
        "aa0bd85df71267106da241b5fdc4a19e7a3c5f44b9f1439346227c9c36e20a9a65ca295901a5a7f3" +
        "963d71e357e6e255dc5c8ab44209dd1c59969d137472acc77e9206aaf907aafbf782c50ec9ced68a" +
        "01e257af21a5d8210a3e8f5fc2eeafb63383ce886349c16c049103c17e6eada7821413f6616e4492" +
        "e01a76b317c040dab7213f577501d816d54eb964be1a97ade3ff4b3b41ebd8f2c217c04af338571f" +
        "ab985de4a7c764ff20db1d742f92617f37d59246e24ff9d39782783e2733de482076093ec888ee35" +
        "961cf6dc9aac553e32c2c450e226531be35e6b349617fc061c1b1d0203010001a320301e300c0603" +
        "551d130101ff04023000300e0603551d0f0101ff0404030206c0300d06092a864886f70d01010b05" +
        "00038201010055f27e558853806223e45d0992ff0f1b761a1df535cfe374c1e2c29d17a4cb390646" +
        "f3a29cc7dcfee8f7dafcabe2debb97ef9daef1e29299d5ef0824492e3191d830ea117e71858ed5df" +
        "69a5f8daa057beea3337e7f5e9d04ce4e86c78caa3ad4974bf890d3d19586709472e898a03b5f1bb" +
        "9a6db8fdb5ef0ff063336aa5c4902c6a38f0e7e3d0f5daa6b4e9f946019c510f2a3f7c0e0fd60569" +
        "7e28e960467bd1b78e7e5ad3bcc2cc7c08c409473a788529e9255c847a2b0901c07703ff5a9ba8e7" +
        "6fa58f49a51b1416720deb02b0e074193ac8b3a2c3b8a660a5c5866539cec06589002d3edc551a83" +
        "c1c1df3a8b4e1f738218de62e865b83f121a52c43e12";

    private const string IntCertDerHex =
        "308202e9308201d1a003020102020102300d06092a864886f70d01010b05003020311e301c060355" +
        "04030c1543687576616469204c5456205369676e20526f6f74301e170d3232303130313030303030" +
        "305a170d3332303130313030303030305a30283126302406035504030c1d43687576616469204c54" +
        "56205369676e20496e7465726d65646961746530820122300d06092a864886f70d01010105000382" +
        "010f003082010a0282010100bed87215c71a2c0ff3ea1e364207e38f318e2ddf5fd44cc9ffb71332" +
        "a9f1f19b6c92473b2293692c0e0f25113c87f7c6be7ff190ce5f91b10fc82dc647b01d99d1d9fc7d" +
        "64bea4e5a1aeada4081a681630a502991a49b826bba55d88b44174d9ab2bc27b7e795a5f72f83255" +
        "f37b3f4d39f004b775dcba2a7b5e7007a0145b2b803ccb29c71798ca34aedf8d780c1ba805d3ba95" +
        "fa6daa2bbb406cc05b66f5e5fc59dc456f66739018e6756f9496a8af601880fc9c746f5f7954d7a3" +
        "a92fec375d4d2c8dd8fbfbff0a447c157c36ea23a380d41870c3369f22f36318ac9df0762815b76d" +
        "55cea199e619247bb6c65d61587a9676a86fc7b7ff4e0ca7cd6f79490203010001a3263024301206" +
        "03551d130101ff040830060101ff020100300e0603551d0f0101ff040403020106300d06092a8648" +
        "86f70d01010b050003820101001546132dea08c1d202fdbd2dedce8f5ee336890ddeaf2d05b73871" +
        "12e3f53a08e59888db2bd5ff922f90045c0eb87af24f4996993ae4f225991a98265fee54d3db37c3" +
        "d25f5ac406321ec6446e7f3169a2869c730c46ef29d488670bb8c3879adab18132aa1bdc6c180d39" +
        "2ffaf82767d32e5d99c3d6a8221a92d085b9cf64e278a337d3aa5615333a42c034103e662166b5f1" +
        "6f5f55775a102ed317e5a2851d4587decc5945e5e24424c627290f89d43a7e60c0d338e4f9107f97" +
        "00322a25bcb802880faa095c7486410547a455e1e967251fe09d7ffae16af8aab32ac4e2ee17fbd8" +
        "057f9604f3d16b1360351adda9902d30752fb78da49c248472397404d7";

    private const string RootCertDerHex =
        "308202e1308201c9a003020102020101300d06092a864886f70d01010b05003020311e301c060355" +
        "04030c1543687576616469204c5456205369676e20526f6f74301e170d3230303130313030303030" +
        "305a170d3430303130313030303030305a3020311e301c06035504030c1543687576616469204c54" +
        "56205369676e20526f6f7430820122300d06092a864886f70d01010105000382010f003082010a02" +
        "82010100a26ca3d8970e0e365fb39d069417f4db2b4f8c051dd818d0d5f67190d0577da0fd1e084e" +
        "1050e4b6fe0192599671bbeb9da9f18286b30ae846fb5d20d4f74f87e12e7fa4a22ed54dbc2fb7d2" +
        "2e1048d966b30a33ff0f1e1c4f32c4bea5b1ac5e275bf3014236771cad73916b49a24ffbc84147ea" +
        "15f8d8b59d40660a2cd4afa86f4b1c2574110d9453118ebdb5de1d9c8e92b68a21e49ee0958506e6" +
        "216e9a9c82cac2b184d34b42d6eadd40f5532a9eca28eef3d1802f7af8f4698743969001adf69837" +
        "4e273cbf610aeabef62ff57c57456c98cb0dcbd4cb4c2eb4587d6ce676b15306e71baed3fc3cd832" +
        "a1c03c5ddccf62e7b8799f8e33f5e7ba104d7d470203010001a326302430120603551d130101ff04" +
        "0830060101ff020102300e0603551d0f0101ff040403020106300d06092a864886f70d01010b0500" +
        "03820101004e6df97878614d95cd206ce84c11d893e76ae84878471c23d245bd849c10f0ccb217b1" +
        "18964fe517e7b7ea178213abd2067d1f13796cd6af1fbf2f0b508999e57962caed7266e972e57be3" +
        "0a8bf65f97bff76c09fc748d315e553f105b25433ec6d09bac866bce8bd63b869d2d39943af60bfd" +
        "d5a4b20270ee3e0f49204b2fa586a115f76a4e79570f5a605bec6316e1279964848dbc609873e6b2" +
        "bff48b83be6d0ba0a79f1aa6236b69a27f78f55762db3a9a383e0c05433723df8688b4aaef641a83" +
        "2e090496f09f710b8e98df0b6db5cdb43f2789849cf5f6fff46fe2ac649aacb4d6fd8507d263ef2f" +
        "82ea3e7e9bdff6d070e30dc4f4eb00761b085380f5";

    private const string CrlDerHex =
        "30820180306a020101300d06092a864886f70d01010b050030283126302406035504030c1d436875" +
        "76616469204c5456205369676e20496e7465726d656469617465170d323530353031303030303030" +
        "5a170d3235303830313030303030305aa00e300c300a0603551d140403020101300d06092a864886" +
        "f70d01010b050003820101009b2f43b51cc60b24f311d93439c6ce93810cf6c66b474fad36e03005" +
        "a88c63e526e4b9a73eef9665dfd26681ce6e9def6fbb40515cbd1fd76e40fac45438e525c702cfa9" +
        "fef89d96bf6a8da3afc3082e2a488c2d30caa06ad1011c86f7ac7766376c75c7fafb791c8659cf2c" +
        "119a47cdf1a629262aab1d75128f3e2fe9e13fb64f4d11dd69a923d81e6301d7555125741fbca5b0" +
        "a173c21d74770e6bf08894bcdd5a9dd26b14876acf707e330f271733eb9d4534e062daa5083f2e3c" +
        "602d4d42fd31e38c9b0bba1b136c9297a8be77fcbf46f20312702e6240afea4c6eb5a150e6c0f855" +
        "40b0f598232059d35cb22f8eb683372a6feaa243ad40120ddd8d87f2";

    private const string OcspDerHex =
        "308204cf0a0100a08204c8308204c406092b0601050507300101048204b5308204b13081a5a12a30" +
        "283126302406035504030c1d43687576616469204c5456205369676e20496e7465726d6564696174" +
        "65180f32303236303531373130333230315a30663064303c300906052b0e03021a0500041494549d" +
        "e64e29bdd4ee96b9f7b5f97fc9d6bec71504148acf67fdc4cefebda40c0afbac15e0c300ff7a6c02" +
        "030999998000180f32303235303531353030303030305aa011180f32303235303631353030303030" +
        "305a300d06092a864886f70d01010b0500038201010078eb3eed8f76c8ec882dbd47a87298210724" +
        "70f59986b7e1292fab8a04da6330d30c7b11c8796fd0878f1fb652e2937d6b2e1ad5de81e9786f14" +
        "3b06d32533c540a7f500332e863374218dad02e5d87076fdb2176f9ad61945b1465ef09ec2f95e01" +
        "52def75e4a2ae22d98e1e4294109d3a30d7935bd3c539159245ea6aae9e7bbbc51ee4ca32971ae37" +
        "af50f5cc252d5519430e6713cfeb71bd7758640f716924af970c5c1bd6d823ad610b2fdc9660a65d" +
        "593cd73ed0044af442fdcbfd99d5c879b5035f40d3013d4a23806f99e9d4117a396875c01c365e1a" +
        "9e8031ae8f3423f76005909df64de8783bbd37827f4125c46002e537f5410ef8b722f6fdfcc8a082" +
        "02f1308202ed308202e9308201d1a003020102020102300d06092a864886f70d01010b0500302031" +
        "1e301c06035504030c1543687576616469204c5456205369676e20526f6f74301e170d3232303130" +
        "313030303030305a170d3332303130313030303030305a30283126302406035504030c1d43687576" +
        "616469204c5456205369676e20496e7465726d65646961746530820122300d06092a864886f70d01" +
        "010105000382010f003082010a0282010100bed87215c71a2c0ff3ea1e364207e38f318e2ddf5fd4" +
        "4cc9ffb71332a9f1f19b6c92473b2293692c0e0f25113c87f7c6be7ff190ce5f91b10fc82dc647b0" +
        "1d99d1d9fc7d64bea4e5a1aeada4081a681630a502991a49b826bba55d88b44174d9ab2bc27b7e79" +
        "5a5f72f83255f37b3f4d39f004b775dcba2a7b5e7007a0145b2b803ccb29c71798ca34aedf8d780c" +
        "1ba805d3ba95fa6daa2bbb406cc05b66f5e5fc59dc456f66739018e6756f9496a8af601880fc9c74" +
        "6f5f7954d7a3a92fec375d4d2c8dd8fbfbff0a447c157c36ea23a380d41870c3369f22f36318ac9d" +
        "f0762815b76d55cea199e619247bb6c65d61587a9676a86fc7b7ff4e0ca7cd6f79490203010001a3" +
        "26302430120603551d130101ff040830060101ff020100300e0603551d0f0101ff04040302010630" +
        "0d06092a864886f70d01010b050003820101001546132dea08c1d202fdbd2dedce8f5ee336890dde" +
        "af2d05b7387112e3f53a08e59888db2bd5ff922f90045c0eb87af24f4996993ae4f225991a98265f" +
        "ee54d3db37c3d25f5ac406321ec6446e7f3169a2869c730c46ef29d488670bb8c3879adab18132aa" +
        "1bdc6c180d392ffaf82767d32e5d99c3d6a8221a92d085b9cf64e278a337d3aa5615333a42c03410" +
        "3e662166b5f16f5f55775a102ed317e5a2851d4587decc5945e5e24424c627290f89d43a7e60c0d3" +
        "38e4f9107f9700322a25bcb802880faa095c7486410547a455e1e967251fe09d7ffae16af8aab32a" +
        "c4e2ee17fbd8057f9604f3d16b1360351adda9902d30752fb78da49c248472397404d7";

    private static readonly DateTimeOffset GoodTime
        = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static (ISigner Signer, X509Certificate Leaf, X509Certificate Int, X509Certificate Root,
                    CertificateList Crl, OcspResponse Ocsp)
    BuildFixture()
    {
        RsaPrivateKey priv = RsaPrivateKey.FromPkcs8(Convert.FromHexString(LeafPkcs8Hex));
        X509Certificate leaf = X509Certificate.Decode(Convert.FromHexString(LeafCertDerHex));
        X509Certificate inter = X509Certificate.Decode(Convert.FromHexString(IntCertDerHex));
        X509Certificate root = X509Certificate.Decode(Convert.FromHexString(RootCertDerHex));
        CertificateList crl = CertificateList.Decode(Convert.FromHexString(CrlDerHex));
        OcspResponse ocsp = OcspResponse.Decode(Convert.FromHexString(OcspDerHex));
        return (new RsaPkcs1V15Signer(priv, leaf, HashAlgorithmName.Sha256), leaf, inter, root, crl, ocsp);
    }

    [Fact]
    public void Sign_WithDssMaterial_EmbedsDssDictionary()
    {
        (ISigner signer, _, X509Certificate inter, X509Certificate root, CertificateList crl, OcspResponse ocsp)
            = BuildFixture();

        byte[] signedBytes = SignFreshPdf(signer, new Chuvadi.Pdf.Signatures.Signing.PdfSigningOptions
        {
            SigningTime = GoodTime,
            ExtraCertificates = new[] { inter },
            LtvOptions = new Chuvadi.Pdf.Signatures.Signing.LtvOptions
            {
                Certificates = new[] { inter, root },
                Crls = new[] { crl },
                OcspResponses = new[] { ocsp },
            },
        });

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(signedBytes), leaveOpen: false);
        DocumentSecurityStore? dss = doc.GetDocumentSecurityStore();
        dss.Should().NotBeNull();
        dss!.Certificates.Should().HaveCount(2);
        dss.Crls.Should().HaveCount(1);
        dss.OcspResponses.Should().HaveCount(1);
        dss.Vri.Should().BeEmpty();
    }

    [Fact]
    public void Sign_WithDssMaterial_VerifiesWithoutCallerSuppliedIntermediates()
    {
        (ISigner signer, _, X509Certificate inter, X509Certificate root, CertificateList crl, OcspResponse ocsp)
            = BuildFixture();
        TrustStore trust = new();
        trust.Add(root);

        // The signer's leaf cert is in the CMS; intermediate is in /DSS. Verifier
        // must auto-extract from DSS — caller does not supply ExtraIntermediates.
        byte[] signedBytes = SignFreshPdf(signer, new Chuvadi.Pdf.Signatures.Signing.PdfSigningOptions
        {
            SigningTime = GoodTime,
            ExtraCertificates = new[] { inter },
            LtvOptions = new Chuvadi.Pdf.Signatures.Signing.LtvOptions
            {
                Certificates = new[] { inter, root },
                Crls = new[] { crl },
                OcspResponses = new[] { ocsp },
            },
        });

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(signedBytes), leaveOpen: false);
        SignatureVerificationResult r = doc.Signatures()[0].Verify(doc, new()
        {
            TrustStore = trust,
            ValidationTime = GoodTime,
        });
        r.IsValid.Should().BeTrue($"verification failed: {r.Message}");
        r.TrustValidated.Should().BeTrue();
    }

    [Fact]
    public void Sign_WithDssMaterial_TamperingDssStreamBreaksIntegrity()
    {
        (ISigner signer, _, X509Certificate inter, X509Certificate root, _, _) = BuildFixture();
        TrustStore trust = new();
        trust.Add(root);

        byte[] signedBytes = SignFreshPdf(signer, new Chuvadi.Pdf.Signatures.Signing.PdfSigningOptions
        {
            SigningTime = GoodTime,
            ExtraCertificates = new[] { inter },
            LtvOptions = new Chuvadi.Pdf.Signatures.Signing.LtvOptions
            {
                Certificates = new[] { inter, root },
            },
        });

        // Flip a byte inside a stream body
        byte[] tampered = (byte[])signedBytes.Clone();
        byte[] needle = "stream\n"u8.ToArray();
        int p = -1;
        for (int i = 0; i < signedBytes.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (signedBytes[i + j] != needle[j]) { match = false; break; }
            }
            if (match) { p = i + needle.Length + 5; break; }
        }
        p.Should().BeGreaterThan(-1, "the test setup must find a stream marker");
        tampered[p] ^= 0x42;

        using PdfDocument doc = PdfDocument.Open(new MemoryStream(tampered), leaveOpen: false);
        SignatureVerificationResult r = doc.Signatures()[0].Verify(doc, new()
        {
            TrustStore = trust,
            ValidationTime = GoodTime,
        });
        r.Status.Should().Be(SignatureVerificationStatus.DigestMismatch);
    }

    [Fact]
    public void Sign_IncludeVri_ThrowsNotSupported()
    {
        (ISigner signer, _, X509Certificate inter, X509Certificate root, _, _) = BuildFixture();
        Action act = () => SignFreshPdf(signer, new Chuvadi.Pdf.Signatures.Signing.PdfSigningOptions
        {
            SigningTime = GoodTime,
            LtvOptions = new Chuvadi.Pdf.Signatures.Signing.LtvOptions
            {
                Certificates = new[] { inter, root },
                IncludeVri = true,
            },
        });
        act.Should().Throw<NotSupportedException>()
            .WithMessage("*VRI*");
    }

    [Fact]
    public void Sign_LtvOptionsWithoutMaterial_IsNoOp()
    {
        // An LtvOptions with no certs/CRLs/OCSPs shouldn't change the document.
        (ISigner signer, _, _, _, _, _) = BuildFixture();
        byte[] noLtv = SignFreshPdf(signer, new Chuvadi.Pdf.Signatures.Signing.PdfSigningOptions
        {
            SigningTime = GoodTime,
        });
        byte[] emptyLtv = SignFreshPdf(signer, new Chuvadi.Pdf.Signatures.Signing.PdfSigningOptions
        {
            SigningTime = GoodTime,
            LtvOptions = new Chuvadi.Pdf.Signatures.Signing.LtvOptions(),
        });
        // Both should have NO /DSS dictionary.
        using PdfDocument doc1 = PdfDocument.Open(new MemoryStream(noLtv), leaveOpen: false);
        using PdfDocument doc2 = PdfDocument.Open(new MemoryStream(emptyLtv), leaveOpen: false);
        doc1.GetDocumentSecurityStore().Should().BeNull();
        doc2.GetDocumentSecurityStore().Should().BeNull();
    }

    [Fact]
    public void LtvOptions_HasMaterial_TrueWhenAnyKindPopulated()
    {
        new Chuvadi.Pdf.Signatures.Signing.LtvOptions().HasMaterial.Should().BeFalse();
        (_, X509Certificate leaf, _, _, _, _) = BuildFixture();
        new Chuvadi.Pdf.Signatures.Signing.LtvOptions { Certificates = new[] { leaf } }
            .HasMaterial.Should().BeTrue();
    }

    private static byte[] SignFreshPdf(ISigner signer, Chuvadi.Pdf.Signatures.Signing.PdfSigningOptions options)
    {
        using PdfDocument doc = PdfDocument.Open(new MemoryStream(BuildMinimalPdf()), leaveOpen: false);
        return Chuvadi.Pdf.Signatures.Signing.PdfSigner.Sign(doc, signer, options);
    }

    private static byte[] BuildMinimalPdf()
    {
        PdfObjectId catalogId = new(1, 0);
        PdfObjectId pagesId = new(2, 0);

        PdfDictionary catalog = new();
        catalog.Set(PdfName.Type, PdfName.Catalog);
        catalog.Set(PdfName.Pages, new PdfReference(pagesId));

        PdfDictionary pages = new();
        pages.Set(PdfName.Type, PdfName.Pages);
        pages.Set(PdfName.Kids, new PdfArray());
        pages.Set(PdfName.Count, 0);

        PdfIndirectObject[] objects =
        {
            new(catalogId, catalog),
            new(pagesId, pages),
        };

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new();
        PdfWriter.Write(ms, objects, trailer);
        return ms.ToArray();
    }
}
