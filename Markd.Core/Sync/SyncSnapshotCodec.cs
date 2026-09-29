using System.Security.Cryptography;
using System.Text;
using Markd.Core.Services;

namespace Markd.Core.Sync
{
    public sealed class SnapshotFormatException(string message, Exception? inner = null) : InvalidOperationException(message, inner);

    public sealed class SnapshotTooNewException(string schemaVersion)
        : InvalidOperationException($"Snapshot schema {schemaVersion} is newer than this app understands.");

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

        private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        };
    }
}
