using System.IO;
using System.Threading.Tasks;

namespace Markd.Core.Services
{
    public interface IExportService
    {
        /// <summary>
        /// Create an export package as bytes. If passphrase is provided the package is encrypted (AES-GCM) using a PBKDF2-derived key.
        /// </summary>
        /// <param name="passphrase">Optional passphrase for encryption. If null or empty, plaintext JSON is returned.</param>
        /// <returns>Byte array containing the export package (either plaintext JSON UTF-8 or an encrypted container).</returns>
        Task<byte[]> CreateExportPackageAsync(string? passphrase = null);

        /// <summary>
        /// Create the export model and return the JSON payload as UTF-8 bytes (uncompressed, unencrypted).
        /// </summary>
        Task<byte[]> CreateExportJsonAsync();
    }
}