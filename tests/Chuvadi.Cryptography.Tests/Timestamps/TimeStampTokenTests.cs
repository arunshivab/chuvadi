// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for RFC 3161 TimeStampToken

using System;
using System.Numerics;
using Chuvadi.Cryptography.Timestamps;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.Timestamps;

public sealed class TimeStampTokenTests
{
    private const string TokenDerHex =
        "308205b506092a864886f70d010702a08205a6308205a2020103310d300b06096086480165030402" +
        "013067060b2a864886f70d0109100104a0580456305402010106052a030405063031300d06096086" +
        "48016503040201050004209329f20ae9ae87b03ccc18351e6a03497a705c88264f7df7754b238174" +
        "f0d81202041234abcd180f32303235303630313132303030305aa08202ed308202e9308201d1a003" +
        "020102020163300d06092a864886f70d01010b0500301b3119301706035504030c10436875766164" +
        "69205465737420545341301e170d3230303130313030303030305a170d3430303130313030303030" +
        "305a301b3119301706035504030c104368757661646920546573742054534130820122300d06092a" +
        "864886f70d01010105000382010f003082010a0282010100b1468cb0cb32fa64bf1eaf5f485e2fdf" +
        "6de3b6378011bc09ce5b1b947e461f6a1dc60395209a29734c4af9b870c963319f7c6f66fdac9ccd" +
        "75a9ec8414d169ffa2608305dd9a54bf82d58667de1a78b6dc610dc1a550fe74f057432ff685c757" +
        "520c4ac8b9aaafe0d0274e5cc4f2e7b7788fe4484929b8d3db15b2d825115ad653a9d14e5b328807" +
        "0d954f33cc0ecce15dc5161b7f032c22621fe45c0202bb3fdf9fa5983bd7bc1517f25f2eb1260eb7" +
        "6c310639c8545d1d871718a4c370337a74a9248646030d7d910c393e18aa63324ad8bf44a76e9e5a" +
        "9ede414c1317bb40712c7805479a50f0566af5532ef5e8fde9ecbb9826c8df2a6a23c54d250b1781" +
        "0203010001a3383036300c0603551d130101ff0402300030160603551d250101ff040c300a06082b" +
        "06010505070308300e0603551d0f0101ff0404030206c0300d06092a864886f70d01010b05000382" +
        "010100462df1621544cdd2ae693643f448ff79b6da37c58bf711501fa4f2f7b267ff5e005929f732" +
        "0e9afdec5bf288f2517692e21f498e33d905fcfa21dd35fb4ffaa214387f5dc1676b7132321c5d66" +
        "e915029ed514ea4f5953004ba6c76b82a7eedeaebd0cc5d6f9cb3255efa5545751e695133ebd307b" +
        "1bcc777b9ea88e542bdeb5900df202a27effd4f97853dbf822312dd2b214bf378dc850c7e80f6ac6" +
        "b4fcda775623bad9fb30f8a55cd042bc4f87efa3527b6c569d52b6d57ba7cd638e40f4e3456ee3a0" +
        "d713a84d343c800c186b0b2eda8020e577f4d0e1f2200d640f222c13aee3e2dba48ae94adb06884b" +
        "05d81fdbfd3a60c0c64ad8f639d8cd9ea7dd79318202323082022e0201013020301b311930170603" +
        "5504030c1043687576616469205465737420545341020163300b0609608648016503040201a081e6" +
        "301a06092a864886f70d010903310d060b2a864886f70d0109100104301c06092a864886f70d0109" +
        "05310f170d3236303531373035313133335a302f06092a864886f70d0109043122042064154ea5ba" +
        "4e4438f58c97a97d6f46c842c1566606541a8f004dac1fdb0129f7307906092a864886f70d01090f" +
        "316c306a300b060960864801650304012a300b0609608648016503040116300b0609608648016503" +
        "040102300a06082a864886f70d0307300e06082a864886f70d030202020080300d06082a864886f7" +
        "0d0302020140300706052b0e030207300d06082a864886f70d0302020128300d06092a864886f70d" +
        "010101050004820100387ab448d870a0ad970b58c7091372ca0ab981dc3f8f3f6d3cdcd1b59aeda2" +
        "fc04b6d6933ff46c790f175843fb90cab1afde2f6c94d9a641befc573bf97415272fd8ea7f2612dc" +
        "647c0e12f878ce7d803b5460b53c8ca80b4b129539781c7e04be84f348ca34e1519d47f5746c3442" +
        "41d4358f782f132bcd4f69c164f7ccb029e58f04819b313c2f6b9a7c66153792b4d65251cdecd51c" +
        "514fd36c8ecffba67bb8feeea11997090b08770a4ba5d7a90a1caec88d10410107dcfcda5298e734" +
        "f475878a4b933c83072745b5531fef783de4096c90d45e4b3c868d82ce721375f12e9050a6361068" +
        "818e7aa532d829a1b8d41c94a2d659d48b7e1ec42e0dc62dfb";

    private const string FakeSignatureBytesHex =
        "66616b652d7064662d7369676e65722d7369676e61747572652d62797465732d746f2d62652d7469" +
        "6d657374616d706564";

    private const long ExpectedSerial = 305441741;

    private static TimeStampToken LoadToken()
        => TimeStampToken.Decode(Convert.FromHexString(TokenDerHex));

    [Fact]
    public void Decode_RealToken_Succeeds()
    {
        TimeStampToken t = LoadToken();
        t.Should().NotBeNull();
    }

    [Fact]
    public void TstInfo_Version_Is1()
    {
        TimeStampToken t = LoadToken();
        t.TstInfo.Version.Should().Be(1);
    }

    [Fact]
    public void TstInfo_SerialNumber_Matches()
    {
        TimeStampToken t = LoadToken();
        t.TstInfo.SerialNumber.Should().Be(new BigInteger(ExpectedSerial));
    }

    [Fact]
    public void TstInfo_GenTime_Is2025June1Noon()
    {
        TimeStampToken t = LoadToken();
        DateTimeOffset expected = new(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);
        Math.Abs((t.TstInfo.GenTime - expected).TotalSeconds).Should().BeLessThan(1);
    }

    [Fact]
    public void TstInfo_MessageImprint_Has32ByteSha256Hash()
    {
        TimeStampToken t = LoadToken();
        t.TstInfo.MessageImprint.HashedMessage.Length.Should().Be(32);
    }

    [Fact]
    public void Verifier_CorrectBytes_ReturnsValid()
    {
        TimeStampToken t = LoadToken();
        byte[] sig = Convert.FromHexString(FakeSignatureBytesHex);
        TimeStampVerificationResult r = TimeStampTokenVerifier.Verify(t, sig);
        r.IsValid.Should().BeTrue();
        r.SignerCertificate.Should().NotBeNull();
        r.Timestamp.Should().NotBeNull();
    }

    [Fact]
    public void Verifier_WrongBytes_ReturnsMessageImprintMismatch()
    {
        TimeStampToken t = LoadToken();
        TimeStampVerificationResult r = TimeStampTokenVerifier.Verify(t, new byte[] { 1, 2, 3 });
        r.Status.Should().Be(TimeStampVerificationStatus.MessageImprintMismatch);
    }
}
