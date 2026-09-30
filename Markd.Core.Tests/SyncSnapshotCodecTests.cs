using System.Text;
using Markd.Core.Services;
using Markd.Core.Sync;
using Xunit;
using static Markd.Core.Tests.Snap;

namespace Markd.Core.Tests;

public class SyncSnapshotCodecTests
{
    private const int Fast = MarkdPackage.MinIterations;

    [Fact]
    public void Round_trip_keeps_records_and_tombstones()
    {
        var model = Model(c: [Cat(1, "c")], o: [Occ(2, "o", cat: 1), Occ(3, "gone", deleted: true)], m: [Ms(4, 2)]);
        var bytes = SyncSnapshotCodec.Encode(model, "correct horse", Fast);

        Assert.True(MarkdPackage.IsEncrypted(bytes));
        var back = SyncSnapshotCodec.Decode(bytes, "correct horse");

        Assert.Equal(SyncContent.Fingerprint(model), SyncContent.Fingerprint(back));
        Assert.Contains(back.Occasions, o => o.DeletedAt != null);
    }

    [Fact]
    public void Wrong_passphrase_surfaces_as_WrongPassword()
    {
        var bytes = SyncSnapshotCodec.Encode(Model(), "right", Fast);
        var ex = Assert.Throws<MarkdPackageException>(() => SyncSnapshotCodec.Decode(bytes, "wrong"));
        Assert.Equal(PackageError.WrongPassword, ex.Error);
    }

    [Fact]
    public void Plain_json_and_garbage_are_a_format_error()
    {
        Assert.Throws<SnapshotFormatException>(() => SyncSnapshotCodec.Decode(Encoding.UTF8.GetBytes("{}"), "x"));
        Assert.Throws<SnapshotFormatException>(() => SyncSnapshotCodec.Decode([1, 2, 3], "x"));
        Assert.Throws<SnapshotFormatException>(() => SyncSnapshotCodec.Decode([], "x"));
    }

    [Fact]
    public void A_newer_schema_is_reported_as_too_new()
    {
        var model = Model();
        model.SchemaVersion = "3";
        var bytes = SyncSnapshotCodec.Encode(model, "p", Fast);
        Assert.Throws<SnapshotTooNewException>(() => SyncSnapshotCodec.Decode(bytes, "p"));
    }

    /// <summary>Header layout (MarkdPackage): "MARKD", format version byte, KDF id byte, iterations (uint32 LE).</summary>
    internal static byte[] PackageWithPatchedHeader(string field, uint? value = null)
    {
        var bytes = SyncSnapshotCodec.Encode(Model(), "p", Fast);
        switch (field)
        {
            case "version": bytes[5] = (byte)(value ?? 3); break;
            case "kdf": bytes[6] = (byte)(value ?? 2); break;
            case "iterations": System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(7, 4), value ?? MarkdPackage.MaxIterations + 1); break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }
        return bytes;
    }

    [Theory]
    [InlineData("version", 0u)]
    [InlineData("version", 1u)]
    [InlineData("kdf", 0u)]
    [InlineData("iterations", 5u)]
    public void An_older_or_invalid_package_header_is_a_format_error(string field, uint value)
    {
        var bytes = PackageWithPatchedHeader(field, value);
        Assert.Throws<SnapshotFormatException>(() => SyncSnapshotCodec.Decode(bytes, "p"));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("kdf")]
    [InlineData("iterations")]
    public void A_newer_package_format_is_reported_as_too_new(string field)
    {
        var bytes = PackageWithPatchedHeader(field);
        Assert.Throws<SnapshotTooNewException>(() => SyncSnapshotCodec.Decode(bytes, "p"));
    }

    [Fact]
    public void A_truncated_package_is_a_format_error()
    {
        var bytes = SyncSnapshotCodec.Encode(Model(), "p", Fast);
        Assert.Throws<SnapshotFormatException>(() => SyncSnapshotCodec.Decode(bytes[..(MarkdPackage.HeaderSize - 1)], "p"));
    }

    [Fact]
    public void Schema_1_is_not_a_sync_snapshot()
    {
        var model = Model();
        model.SchemaVersion = "1";
        var bytes = SyncSnapshotCodec.Encode(model, "p", Fast);
        Assert.Throws<SnapshotFormatException>(() => SyncSnapshotCodec.Decode(bytes, "p"));
    }

    [Fact]
    public void Each_upload_uses_a_fresh_salt_and_nonce()
    {
        var model = Model();
        var a = SyncSnapshotCodec.Encode(model, "p", Fast);
        var b = SyncSnapshotCodec.Encode(model, "p", Fast);
        Assert.NotEqual(a, b);
    }
}
