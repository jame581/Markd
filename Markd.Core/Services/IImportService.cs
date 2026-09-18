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
        /// </summary>
        Task ApplyImportAsync(ExportModel model);
    }
}