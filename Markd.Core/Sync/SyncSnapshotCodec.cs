using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Markd.Core.Services;

namespace Markd.Core.Sync
{
    public sealed class SnapshotFormatException(string message, Exception? inner = null) : InvalidOperationException(message, inner);

    public sealed class SnapshotTooNewException : InvalidOperationException
    {
        public SnapshotTooNewException(string schemaVersion)
            : base($"Snapshot schema {schemaVersion} is newer than this app understands.") { }

        private SnapshotTooNewException(string message, Exception inner) : base(message, inner) { }

        /// <summary>The package header (format version, KDF or its parameters) is newer than this app understands.</summary>
        public static SnapshotTooNewException PackageFormat(Exception inner) =>
            new("The remote package format is newer than this app understands.", inner);
    }

    public static class SyncSnapshotCodec
    {
        public static byte[] Encode(ExportModel model, string passphrase, int iterations = MarkdPackage.DefaultIterations)
        {
            var plain = Encoding.UTF8.GetBytes(ExportModelJson.Serialize(model, indented: false));
            try
            {
                return MarkdPackage.Encrypt(plain, passphrase, iterations);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }

        public static ExportModel Decode(byte[] data, string passphrase)
        {
            if (data is null || data.Length == 0 || !MarkdPackage.IsEncrypted(data))
                throw new SnapshotFormatException("The remote file is not a Markd package.");

            byte[] plain;
            try
            {
                plain = MarkdPackage.Decrypt(data, passphrase);
            }
            catch (MarkdPackageException ex) when (IsNewerHeader(ex.Error, data))
            {
                // A later app may raise the format, change the KDF or raise the iteration count: ask for an update,
                // never steer the user toward replacing the newer app's data.
                throw SnapshotTooNewException.PackageFormat(ex);
            }
            catch (MarkdPackageException ex) when (ex.Error is PackageError.Truncated or PackageError.UnsupportedVersion
                                                    or PackageError.UnsupportedKdf or PackageError.InvalidIterations)
            {
                throw new SnapshotFormatException("The remote file is damaged or from an unknown format.", ex);
            }

            try
            {
                var json = Encoding.UTF8.GetString(plain);
                ExportModel? model;
                try
                {
                    model = System.Text.Json.JsonSerializer.Deserialize<ExportModel>(json, JsonOptions);
                }
                catch (System.Text.Json.JsonException ex)
                {
                    throw new SnapshotFormatException("The remote snapshot is unreadable.", ex);
                }

                if (model is null || string.IsNullOrWhiteSpace(model.SchemaVersion))
                    throw new SnapshotFormatException("The remote snapshot has no schema version.");

                if (model.SchemaVersion != ExportModel.CurrentSchemaVersion)
                {
                    if (int.TryParse(model.SchemaVersion, out var version) && version > 2)
                        throw new SnapshotTooNewException(model.SchemaVersion);
                    throw new SnapshotFormatException($"Unsupported snapshot schema {model.SchemaVersion}.");
                }

                try
                {
                    ExportModelJson.Validate(model);
                }
                catch (InvalidOperationException ex)
                {
                    throw new SnapshotFormatException(ex.Message, ex);
                }

                return model;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }

        // Header layout (MarkdPackage): "MARKD" magic, format version byte, KDF id byte, iterations (uint32 LE).
        private const int VersionOffset = 5;
        private const int KdfOffset = 6;
        private const int IterationsOffset = 7;

        /// <summary>
        /// Only a header value above what this app writes can come from a later app. Older or invalid values are
        /// damage, which the user can fix by replacing the cloud data.
        /// </summary>
        private static bool IsNewerHeader(PackageError error, byte[] data) => error switch
        {
            PackageError.UnsupportedVersion => data.Length > VersionOffset && data[VersionOffset] > MarkdPackage.FormatVersion,
            PackageError.UnsupportedKdf => data.Length > KdfOffset && data[KdfOffset] > MarkdPackage.KdfPbkdf2Sha256,
            PackageError.InvalidIterations => data.Length >= IterationsOffset + 4
                && BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(IterationsOffset, 4)) > MarkdPackage.MaxIterations,
            _ => false,
        };

        private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        };
    }
}
