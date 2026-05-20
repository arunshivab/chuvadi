// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.5 — Chuvadi.Pdf.Encryption tests

using System;
using System.Linq;
using System.Text;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Encryption.Tests;

// ── RC4 ──────────────────────────────────────────────────────────────────

public sealed class Rc4Tests
{
    // RFC 6229 test vector: key "Key", plaintext "Plaintext"
    [Fact]
    public void Rc4_KnownVector_Key3_Plain9()
    {
        byte[] key = Encoding.ASCII.GetBytes("Key");
        byte[] plain = Encoding.ASCII.GetBytes("Plaintext");
        byte[] expected = new byte[] { 0xBB, 0xF3, 0x16, 0xE8, 0xD9, 0x40, 0xAF, 0x0A, 0xD3 };

        byte[] cipher = Rc4.Process(key, plain);
        cipher.Should().Equal(expected);
    }

    [Fact]
    public void Rc4_Symmetric_RoundTrip()
    {
        byte[] key = Encoding.ASCII.GetBytes("secret");
        byte[] data = Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog.");
        byte[] cipher = Rc4.Process(key, data);
        byte[] decrypted = Rc4.Process(key, cipher);
        decrypted.Should().Equal(data);
    }
}

// ── AES ──────────────────────────────────────────────────────────────────

public sealed class AesCryptoTests
{
    [Fact]
    public void Aes128_RoundTrip()
    {
        byte[] key = new byte[16];
        for (int i = 0; i < 16; i++) { key[i] = (byte)i; }

        byte[] plain = Encoding.UTF8.GetBytes("PHI content — must not appear in output");
        byte[] cipher = AesCrypto.Encrypt(key, plain);
        cipher.Length.Should().BeGreaterThan(plain.Length);   // includes IV + padding

        byte[] decrypted = AesCrypto.Decrypt(key, cipher);
        decrypted.Should().Equal(plain);
    }

    [Fact]
    public void Aes256_RoundTrip()
    {
        byte[] key = new byte[32];
        for (int i = 0; i < 32; i++) { key[i] = (byte)(i * 7); }

        byte[] plain = Encoding.UTF8.GetBytes("Larger keys, same wrapping.");
        byte[] cipher = AesCrypto.Encrypt(key, plain);
        byte[] decrypted = AesCrypto.Decrypt(key, cipher);
        decrypted.Should().Equal(plain);
    }

    [Fact]
    public void Aes_WrongKey_Throws()
    {
        byte[] key1 = new byte[16];
        byte[] key2 = new byte[16] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        byte[] cipher = AesCrypto.Encrypt(key1, Encoding.UTF8.GetBytes("hello"));

        Action act = () => AesCrypto.Decrypt(key2, cipher);
        act.Should().Throw<PdfEncryptionException>();
    }

    [Fact]
    public void Aes_TooShortPayload_Throws()
    {
        byte[] key = new byte[16];
        Action act = () => AesCrypto.Decrypt(key, new byte[8]);
        act.Should().Throw<PdfEncryptionException>();
    }
}

// ── Decryptor / Encryptor symmetry ────────────────────────────────────────

public sealed class DecryptorEncryptorTests
{
    [Fact]
    public void Aes128_PerObject_RoundTrip()
    {
        byte[] fileKey = new byte[16];
        for (int i = 0; i < 16; i++) { fileKey[i] = (byte)(i + 1); }

        Encryptor enc = new(fileKey, EncryptionAlgorithm.Aes_128);
        Decryptor dec = new(fileKey, EncryptionAlgorithm.Aes_128);

        byte[] payload = Encoding.UTF8.GetBytes("object 3 generation 0 contents");
        byte[] cipher = enc.Encrypt(payload, objectNumber: 3, generation: 0);
        byte[] back = dec.Decrypt(cipher, objectNumber: 3, generation: 0);

        back.Should().Equal(payload);
    }

    [Fact]
    public void Aes256_RoundTrip()
    {
        byte[] fileKey = Encryptor.GenerateFileKeyAes256();
        Encryptor enc = new(fileKey, EncryptionAlgorithm.Aes_256);
        Decryptor dec = new(fileKey, EncryptionAlgorithm.Aes_256);

        byte[] payload = Encoding.UTF8.GetBytes("AES-256 ignores per-object identifiers");
        byte[] cipher = enc.Encrypt(payload, objectNumber: 42, generation: 0);

        // R=6 uses the file key directly, so any (obj,gen) decrypts equivalently.
        byte[] back = dec.Decrypt(cipher, objectNumber: 0, generation: 0);
        back.Should().Equal(payload);
    }

    [Fact]
    public void Encryptor_Rc4_Refused()
    {
        Action act = () => new Encryptor(new byte[16], EncryptionAlgorithm.Rc4_128);
        act.Should().Throw<PdfEncryptionException>()
           .WithMessage("*only supported for AES*");
    }

    [Fact]
    public void Encryptor_None_Refused()
    {
        Action act = () => new Encryptor(new byte[16], EncryptionAlgorithm.None);
        act.Should().Throw<PdfEncryptionException>();
    }
}

// ── Exception type ────────────────────────────────────────────────────────

public sealed class PdfEncryptionExceptionTests
{
    [Fact]
    public void Default_HasMessage()
    {
        new PdfEncryptionException().Message.Should().NotBeEmpty();
    }

    [Fact]
    public void Message_Preserved()
    {
        new PdfEncryptionException("oops").Message.Should().Be("oops");
    }

    [Fact]
    public void InnerException_Preserved()
    {
        Exception inner = new InvalidOperationException();
        new PdfEncryptionException("wrap", inner).InnerException.Should().BeSameAs(inner);
    }
}

// ── EncryptionAlgorithm enum ──────────────────────────────────────────────

public sealed class EncryptionAlgorithmTests
{
    [Fact]
    public void AllValues_Distinct()
    {
        Enum.GetValues<EncryptionAlgorithm>().Distinct().Should().HaveCount(
            Enum.GetValues<EncryptionAlgorithm>().Length);
    }
}
