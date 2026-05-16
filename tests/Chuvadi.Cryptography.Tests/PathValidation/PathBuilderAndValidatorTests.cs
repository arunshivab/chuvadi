// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for CertificatePathBuilder + CertificatePathValidator

using System;
using System.Collections.Generic;
using Chuvadi.Cryptography.PathValidation;
using Chuvadi.Cryptography.X509;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.PathValidation;

public sealed class PathBuilderAndValidatorTests
{
    private const string RootCertDerHex =
        "308202df308201c7a003020102020101300d06092a864886f70d01010b0500301f311d301b060355" +
        "04030c1443687576616469205465737420526f6f74204341301e170d323030313031303030303030" +
        "5a170d3430303130313030303030305a301f311d301b06035504030c144368757661646920546573" +
        "7420526f6f7420434130820122300d06092a864886f70d01010105000382010f003082010a028201" +
        "0100e1dcd216d969415edd3be7f96afe1d3b552f4c46c13a6b40acfef91518ca52d7b7675219ae44" +
        "5f345efea40b2e3908fb57f2124e0f13affd483d06df812f4554d0d7ceb4d94299d57f5c900cd7d2" +
        "baeea93c86837903eaf21c2ee8f9919c8ebd073e40319815f02f137646308cce206663d6c705373d" +
        "82955658c4fdd82870222964e097a9647d89830f8c11e5ff75fd57769e97281dc484f5c056cd0432" +
        "c5430bca6f4a7d0e2f731c21452b871e5b605d44bb9180b44548f90933293a90387c0d72f5e77a4b" +
        "fa66b77a2602ed76f86e5bdb054ccff0dbcbec41bbb4015fbcfa651d18d8c61aec222e8c57330342" +
        "e0c0d4404a985ac9abe3c9b16c7a549664490203010001a326302430120603551d130101ff040830" +
        "060101ff020102300e0603551d0f0101ff040403020106300d06092a864886f70d01010b05000382" +
        "0101002b6a320016d6e76d87bc39547900e697543226750f655c992887572798ed926134cf7ecd14" +
        "b20489595dac50f6ec1b13694e2d5bb0ed1e5c4ec86c94482dfde7dd543274f4eb62263fc26860de" +
        "14db3f3b78f3847befd14b1cfaeb1f79bf62585b339933e28c9130f0b1550c07529617398bdb324f" +
        "bf19440843624eeacda2b40038dea114eef28fc70f54cd113d4856d57b30f49ec5416f5f2ff9733c" +
        "c36b0aac896be6d2f05a1d18417ff4517a98a8a0d8df748335ad0df403b389a2f514d22f03142946" +
        "e7c2ec8f2276e5e3b17621c85d7b16d3488052c01db0255fa4e8ecbf2f7544fd13d6554cd875f9d1" +
        "cf3606e835cbba275e07e4ba06529b134ef399";

    private const string IntermediateCertDerHex =
        "308202e7308201cfa003020102020102300d06092a864886f70d01010b0500301f311d301b060355" +
        "04030c1443687576616469205465737420526f6f74204341301e170d323230313031303030303030" +
        "5a170d3332303130313030303030305a30273125302306035504030c1c4368757661646920546573" +
        "7420496e7465726d65646961746520434130820122300d06092a864886f70d01010105000382010f" +
        "003082010a028201010090005be9dee6a3fcabfa8bc098434336ddde7f4c9bf1190b98eec128d604" +
        "0b5fcc991dd3b39baa51baf5806508d53e2ee9ba2caa9830692b17ba0a81299e6184da038696696c" +
        "4c719f2be3e30ecc25d94e471ccf55b59b17e4711f88b3f5675c5acac137671dd52a58d786943da0" +
        "da789af4d5548be0b3f1251f94d813fbe3d1b79c9d3150631e031341a8cd2fc4998a9b155e9f6a59" +
        "e0dbd7f72cb252804e48d3488865e3fdfd5f087443d274c9291dbd475f6c943c96b48b5b4f085b68" +
        "8f26171fbdb66a032611fa3ffe2c28f8f7bc2c2f67dfed64575536177798c4d887037b97ccb86c7f" +
        "3f012f5dc3d02477ffcbfb92b0dd9e907e99e839d9a35b1b21b30203010001a32630243012060355" +
        "1d130101ff040830060101ff020100300e0603551d0f0101ff040403020106300d06092a864886f7" +
        "0d01010b05000382010100b343afc4c21dfcd54365e96c282ec92a5303f2f51884e55280a9252684" +
        "523009eb2a8ec4a5e09c1516e4999112e6f2c0779b5cdaa9171e44e0947733bf5e48e3692402b801" +
        "cca1fb52c902092f1d8327964cce1fc13b60d0137db61e2a01cc81979cbad74adb683918dbba3472" +
        "ab140a8f536f6c003e35706fe75a76c21f056c3aac97004be76a45a72f878e6bc380c93266de0975" +
        "20d4cc930a24cd2c3d58991485bedda21640c767a8c5900d1467495fb73ec07ab869d50ef59a4474" +
        "0159a1ab77eb1f27bd83b90351834a6ec6e93732f5aad58102310d0694bc80db13a62e6e96949c92" +
        "44f3788bc85457b42d00912238e29312af9dde724743dde3fcbfe0";

    private const string LeafCertDerHex =
        "308202d8308201c0a003020102020103300d06092a864886f70d01010b0500302731253023060355" +
        "04030c1c43687576616469205465737420496e7465726d656469617465204341301e170d32343031" +
        "30313030303030305a170d3237303130313030303030305a30163114301206035504030c0b546573" +
        "74205369676e657230820122300d06092a864886f70d01010105000382010f003082010a02820101" +
        "0094865177d89e0b9b166998cf3c4ae8de082658269181e3021b6e6d520b9d9226723e1d39d3395a" +
        "cda7d1f220fd6c03752e41d563dd519af4d6e518bb3c87a3ee02d81b5cefa2911163b4623cf78d1b" +
        "f8faf254c85f5aa9a512f51dc372d32d436769882e240a830fe22dbf48e96ad7e18494155f34fcb8" +
        "7fdc1aecc34f85e5e923dd1d420fc3f9092b9b441829eddec0aa592d23bbb5b4562437d718690dc8" +
        "2aebc0e031ab67e6553c0721da6c84a90c3833aa7777273fed0f523ef2ba63753efa35ad721b6acb" +
        "103405a2e265ff73159815c59a1561c04e85664d3e05db19e39fbb046650aa973bcd2b9735eb4916" +
        "cdddfe00c9aa19b19c5bd052780d9f59090203010001a320301e300c0603551d130101ff04023000" +
        "300e0603551d0f0101ff0404030206c0300d06092a864886f70d01010b05000382010100741d5e55" +
        "8b20e25780a0f5416b1b73749aee61f4b23a5781b9da5fe475e5578338e96c2b7ad91aad29a6040a" +
        "5f53cc66c85a36fdde003f46800db1964fa5a9d9a8eac69ece3068709337d19f269b87d02c72f44e" +
        "1e197bfb9159ed114b1b3022523a26a205c1a84b78e0859113a35f52ee7d1815fcd4b9e9c8339408" +
        "b3e0904dead67970052196d6d1fac5d473e376f22fa6e62b7604581428608e7dc755933019d16673" +
        "a7df2863e34a47abe48a0de5033f67000b4e87ff3b7625955c7e1d0aa2c8816b8da4ff1b7d947c99" +
        "deb47bf4f96f83fb7964571ce7d89204b31ea68e2d36b7b9b85bd8a29309644e4c3c58e7048c1c77" +
        "0d43b23c9b1124d36fabf20f";

    private const string OtherRootCertDerHex =
        "308202d6308201bea003020102020163300d06092a864886f70d01010b0500301c311a3018060355" +
        "04030c11556e72656c6174656420526f6f74204341301e170d3230303130313030303030305a170d" +
        "3430303130313030303030305a301c311a301806035504030c11556e72656c6174656420526f6f74" +
        "20434130820122300d06092a864886f70d01010105000382010f003082010a0282010100d20c67d2" +
        "4c5498180278662dc25ac0019274e84cc7236502ff31dfc89356ec8ea10ead86f1408e5b6d395a49" +
        "bbec5102dffadf569604cd7b6658b267fc1d200c0fac230e3c5a16de42ead3d9c7bb0f3944939244" +
        "4ccf0763643ce09ba1725a039fbd103bbe7b1c63eff9019e4975d34e1822930ea77addcb15e01dc9" +
        "5eff465ff0a379c3eb215048b6e1ca045f0b38ae3f16d5894eca996cc888137bc34dbbcc1e7b66f2" +
        "170b4666eebce2d8bf242eabf2b17b8cdc6ca5ce614d2e99d28ccb1386e62711babe877481918be7" +
        "bf2dcf95a850c0282f20bd231721f7fece2b789891157f30c89b284883b9568d718e61ce80b46802" +
        "6f81d6224f8de1e3c02ca9af0203010001a3233021300f0603551d130101ff040530030101ff300e" +
        "0603551d0f0101ff040403020106300d06092a864886f70d01010b05000382010100478db7411d00" +
        "a7fb9511c23a1f80968b23704eedb94d4ddad2b0f126a676e82626b1f5cf5fa5aefb116fdbcf67ae" +
        "beebb949f40c160049d75b742e1b807340e01862630ec84d3099a3f32f8a3a9b404a307d69b0a249" +
        "3b18c962a68c4e406e72ad1e7097d72d59077391bd898d18f920ab983dc9bcd6361f2b14805a5658" +
        "516e0c48459f4d2eee4e09723ac3097195c9c1d8cb46ea545183601321a30d4faf345faa47777631" +
        "c17313a4de482ded81ddcdf94d421c6b45c43896f39926432c83af96694bf291db3d62252f2b77af" +
        "bf5c85ca7e614ba8f435e4aeddc80327ee3fc2db52181d3bf42cfd29fb3abd7efd9d8fd1e8e7455f" +
        "afa14d4f9031a4edad18";

    private static X509Certificate Cert(string hex)
        => X509Certificate.Decode(Convert.FromHexString(hex));

    private static readonly DateTimeOffset GoodTime
        = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BuildPaths_FindsLeaf_To_Root_Via_Intermediate()
    {
        X509Certificate leaf = Cert(LeafCertDerHex);
        X509Certificate intermediate = Cert(IntermediateCertDerHex);
        X509Certificate root = Cert(RootCertDerHex);

        TrustStore store = new();
        store.Add(root);

        IReadOnlyList<CertificatePath> paths = CertificatePathBuilder.BuildPaths(
            leaf, new[] { intermediate }, store);

        paths.Should().HaveCount(1);
        paths[0].Length.Should().Be(2);
        paths[0].Leaf.Subject.CommonName.Should().Be("Test Signer");
    }

    [Fact]
    public void BuildPaths_NoIntermediates_ReturnsEmpty()
    {
        X509Certificate leaf = Cert(LeafCertDerHex);
        X509Certificate root = Cert(RootCertDerHex);
        TrustStore store = new();
        store.Add(root);

        IReadOnlyList<CertificatePath> paths = CertificatePathBuilder.BuildPaths(
            leaf, Array.Empty<X509Certificate>(), store);

        paths.Should().BeEmpty();
    }

    [Fact]
    public void BuildPaths_UnrelatedTrustStore_ReturnsEmpty()
    {
        X509Certificate leaf = Cert(LeafCertDerHex);
        X509Certificate intermediate = Cert(IntermediateCertDerHex);
        X509Certificate other = Cert(OtherRootCertDerHex);
        TrustStore store = new();
        store.Add(other);

        IReadOnlyList<CertificatePath> paths = CertificatePathBuilder.BuildPaths(
            leaf, new[] { intermediate }, store);

        paths.Should().BeEmpty();
    }

    [Fact]
    public void ValidatePath_KnownGoodChain_ReturnsValid()
    {
        X509Certificate leaf = Cert(LeafCertDerHex);
        X509Certificate intermediate = Cert(IntermediateCertDerHex);
        X509Certificate root = Cert(RootCertDerHex);
        TrustStore store = new();
        store.Add(root);

        IReadOnlyList<CertificatePath> paths = CertificatePathBuilder.BuildPaths(
            leaf, new[] { intermediate }, store);

        CertificatePathValidationResult result = CertificatePathValidator.Validate(paths, GoodTime);
        result.IsValid.Should().BeTrue();
        result.ValidatedPath.Should().NotBeNull();
        result.ValidatedPath!.Length.Should().Be(2);
    }

    [Fact]
    public void ValidatePath_ExpiredLeaf_ReturnsExpired()
    {
        X509Certificate leaf = Cert(LeafCertDerHex);
        X509Certificate intermediate = Cert(IntermediateCertDerHex);
        X509Certificate root = Cert(RootCertDerHex);
        TrustStore store = new();
        store.Add(root);

        IReadOnlyList<CertificatePath> paths = CertificatePathBuilder.BuildPaths(
            leaf, new[] { intermediate }, store);

        DateTimeOffset afterExpiry = new(2028, 1, 1, 0, 0, 0, TimeSpan.Zero);
        CertificatePathValidationResult result = CertificatePathValidator.Validate(paths, afterExpiry);
        result.IsValid.Should().BeFalse();
        result.Status.Should().Be(CertificatePathValidationStatus.CertificateExpired);
    }

    [Fact]
    public void Validate_EmptyPathList_ReturnsNoPathFound()
    {
        CertificatePathValidationResult result = CertificatePathValidator.Validate(
            Array.Empty<CertificatePath>(), GoodTime);
        result.Status.Should().Be(CertificatePathValidationStatus.NoPathFound);
    }
}
