using System.Buffers.Binary;
using System.Text;
using Markd.Core.Services;
using Xunit;

namespace Markd.Core.Tests;

public class MarkdPackageTests
{
    private const string Password = "correct horse";
    private static readonly byte[] Plain = Encoding.UTF8.GetBytes("""{"schemaVersion":"1"}""");

    // The minimum count keeps the suite fast; the format is identical.
    private static byte[] Encrypt() => MarkdPackage.Encrypt(Plain, Password, MarkdPackage.MinIterations);

    [Fact]
    public void RoundTrip()
    {
        var package = Encrypt();

        Assert.True(MarkdPackage.IsEncrypted(package));
        Assert.Equal(Plain, MarkdPackage.Decrypt(package, Password));
    }

    [Fact]
    public void Layout_MatchesSpec()
    {
        var package = Encrypt();

        Assert.Equal("MARKD"u8.ToArray(), package[..5]);
        Assert.Equal(2, package[5]);
        Assert.Equal(1, package[6]);
        Assert.Equal((uint)MarkdPackage.MinIterations, BinaryPrimitives.ReadUInt32LittleEndian(package.AsSpan(7, 4)));
        Assert.Equal(MarkdPackage.HeaderSize + Plain.Length + 16, package.Length);
    }

    [Fact]
    public void DefaultIterations_Is600k() =>
        Assert.Equal(600_000u, BinaryPrimitives.ReadUInt32LittleEndian(MarkdPackage.Encrypt(Plain, Password).AsSpan(7, 4)));

    [Fact]
    public void SameInput_ProducesDifferentPackages() => Assert.NotEqual(Encrypt(), Encrypt());

    [Fact]
    public void PlainJson_IsNotEncrypted() => Assert.False(MarkdPackage.IsEncrypted(Plain));

    [Fact]
    public void WrongPassword_Throws()
    {
        var ex = Assert.Throws<MarkdPackageException>(() => MarkdPackage.Decrypt(Encrypt(), "wrong password"));
        Assert.Equal(PackageError.WrongPassword, ex.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingPassword_Throws(string? password)
    {
        var ex = Assert.Throws<MarkdPackageException>(() => MarkdPackage.Decrypt(Encrypt(), password));
        Assert.Equal(PackageError.PasswordRequired, ex.Error);
    }

    [Fact]
    public void AnyFlippedByte_AfterMagic_IsRejected()
    {
        var original = Encrypt();
        for (var i = 5; i < original.Length; i++)
        {
            var tampered = (byte[])original.Clone();
            tampered[i] ^= 0x01;
            Assert.Throws<MarkdPackageException>(() => MarkdPackage.Decrypt(tampered, Password));
        }
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(38)]
    [InlineData(39)]
    [InlineData(54)]
    public void Truncated_Throws(int length)
    {
        var ex = Assert.Throws<MarkdPackageException>(() => MarkdPackage.Decrypt(Encrypt()[..length], Password));
        Assert.Equal(PackageError.Truncated, ex.Error);
    }

    [Fact]
    public void UnknownVersion_Throws()
    {
        var package = Encrypt();
        package[5] = 3;
        Assert.Equal(PackageError.UnsupportedVersion, Assert.Throws<MarkdPackageException>(() => MarkdPackage.Decrypt(package, Password)).Error);
    }

    [Fact]
    public void UnknownKdf_Throws()
    {
        var package = Encrypt();
        package[6] = 9;
        Assert.Equal(PackageError.UnsupportedKdf, Assert.Throws<MarkdPackageException>(() => MarkdPackage.Decrypt(package, Password)).Error);
    }

    [Theory]
    [InlineData(99_999u)]
    [InlineData(2_000_001u)]
    public void OutOfRangeIterations_Throw(uint iterations)
    {
        var package = Encrypt();
        BinaryPrimitives.WriteUInt32LittleEndian(package.AsSpan(7, 4), iterations);
        Assert.Equal(PackageError.InvalidIterations, Assert.Throws<MarkdPackageException>(() => MarkdPackage.Decrypt(package, Password)).Error);
    }

    [Fact]
    public void Encrypt_RejectsOutOfRangeIterations() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => MarkdPackage.Encrypt(Plain, Password, 1_000));
}
