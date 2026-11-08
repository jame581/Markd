using System.Threading.Tasks;

namespace Markd.Core.Services
{
    public interface IImportService
    {
        /// <summary>
        /// Parse an import package (plaintext JSON or encrypted container, format v2, see <see cref="MarkdPackage"/>) and return the deserialized ExportModel.
        /// Throws on decryption or validation failure.
        /// </summary>
        Task<ExportModel> ParseImportPackageAsync(byte[] package, string? passphrase = null);

        /// <summary>
        /// Apply an import model to the local database inside a transaction. Creates a timestamped DB backup before applying.
        /// Without sync state the backup replaces the data; with sync state it only adds (see <see cref="ImportOutcome"/>).
        /// </summary>
        Task<ImportOutcome> ApplyImportAsync(ExportModel model);
    }

    /// <summary>
    /// What an import did. <paramref name="Additive"/> is true on a device with sync state. The counts are events:
    /// added (unknown here), revived (deleted here after the backup) and skipped (already here, by SyncId or matched
    /// on content). A replace reports every event it wrote as added.
    /// </summary>
    public sealed record ImportOutcome(bool Additive, int Added, int Revived, int Skipped);
}