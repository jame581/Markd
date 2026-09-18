using System.Buffers.Binary;
using System.Security.Cryptography;
using Markd.Core.Localization;

namespace Markd.Core.Services;

public enum PackageError { PasswordRequired, WrongPassword, Truncated, UnsupportedVersion, UnsupportedKdf, InvalidIterations }

public sealed class MarkdPackageException(PackageError error, string message, Exception? inner = null)
    : InvalidOperationException(message, inner)
{
    public PackageError Error { get; } = error;
}

/// <summary>
/// Password-protected export, format v2:
/// "MARKD" | version 2 | KDF id 1 (PBKDF2-HMAC-SHA256) | iterations uint32 LE | salt 16 | nonce 12 | ciphertext | tag 16.
/// The whole header is AES-GCM associated data, so any change to it fails authentication.
/// </summary>
public static class MarkdPackage
{
    public const string EncryptedExtension = ".markd";
    public const int DefaultIterations = 600_000;
    public const int MinIterations = 100_000;
    public const int MaxIterations = 2_000_000;

    private const byte FormatVersion = 2;
    private const byte KdfPbkdf2Sha256 = 1;
    private const int MagicSize = 5;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int IterationsOffset = MagicSize + 2;
    private const int SaltOffset = IterationsOffset + 4;
    private const int NonceOffset = SaltOffset + SaltSize;
    public const int HeaderSize = NonceOffset + NonceSize;

    private static ReadOnlySpan<byte> Magic => "MARKD"u8;

    public static bool IsEncrypted(ReadOnlySpan<byte> data) => data.StartsWith(Magic);

    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, string password, int iterations = DefaultIterations)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, MinIterations);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(iterations, MaxIterations);

        var output = new byte[HeaderSize + plaintext.Length + TagSize];
        var header = output.AsSpan(0, HeaderSize);
        Magic.CopyTo(header);
        header[MagicSize] = FormatVersion;
        header[MagicSize + 1] = KdfPbkdf2Sha256;
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(IterationsOffset, 4), (uint)iterations);
        RandomNumberGenerator.Fill(header.Slice(SaltOffset, SaltSize));
        RandomNumberGenerator.Fill(header.Slice(NonceOffset, NonceSize));

        var key = DeriveKey(password, header.Slice(SaltOffset, SaltSize), iterations);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(
                header.Slice(NonceOffset, NonceSize),
                plaintext,
                output.AsSpan(HeaderSize, plaintext.Length),
                output.AsSpan(HeaderSize + plaintext.Length, TagSize),
                header);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        return output;
    }

    public static byte[] Decrypt(ReadOnlySpan<byte> package, string? password)
    {
        if (!IsEncrypted(package))
            throw new ArgumentException("Not an encrypted Markd package.", nameof(package));
        if (package.Length <= MagicSize)
            throw Fail(PackageError.Truncated, Strings.Package_Truncated);
        if (package[MagicSize] != FormatVersion)
            throw Fail(PackageError.UnsupportedVersion, Strings.Package_UnsupportedVersion);
        if (package.Length < HeaderSize + TagSize)
            throw Fail(PackageError.Truncated, Strings.Package_Truncated);
        if (package[MagicSize + 1] != KdfPbkdf2Sha256)
            throw Fail(PackageError.UnsupportedKdf, Strings.Package_UnsupportedKdf);

        var iterations = BinaryPrimitives.ReadUInt32LittleEndian(package.Slice(IterationsOffset, 4));
        if (iterations is < MinIterations or > MaxIterations)
            throw Fail(PackageError.InvalidIterations, Strings.Package_InvalidIterations);
        if (string.IsNullOrEmpty(password))
            throw Fail(PackageError.PasswordRequired, Strings.Package_PasswordRequired);

        var header = package[..HeaderSize];
        var ciphertext = package[HeaderSize..^TagSize];
        var plaintext = new byte[ciphertext.Length];
        var key = DeriveKey(password, header.Slice(SaltOffset, SaltSize), (int)iterations);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(header.Slice(NonceOffset, NonceSize), ciphertext, package[^TagSize..], plaintext, header);
            return plaintext;
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw Fail(PackageError.WrongPassword, Strings.Package_WrongPassword, ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] DeriveKey(string password, ReadOnlySpan<byte> salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, KeySize);

    private static MarkdPackageException Fail(PackageError error, string message, Exception? inner = null) =>
        new(error, message, inner);
}
