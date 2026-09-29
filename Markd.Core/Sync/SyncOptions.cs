using Markd.Core.Services;

namespace Markd.Core.Sync
{
    public sealed class SyncOptions
    {
        /// <summary>Tests lower this to MarkdPackage.MinIterations to keep the suite fast.</summary>
        public int KdfIterations { get; set; } = MarkdPackage.DefaultIterations;

        /// <summary>Upload attempts before giving up when another device keeps writing first.</summary>
        public int MaxUploadAttempts { get; set; } = 3;
    }
}
