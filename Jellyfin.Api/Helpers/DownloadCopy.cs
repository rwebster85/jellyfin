using System.IO;

namespace Jellyfin.Api.Helpers
{
    /// <summary>
    /// A transcoded copy ready to serve: either finished on disk, or still being made.
    /// </summary>
    /// <param name="Name">The file name to download it as, after the source rather than the copy's own file.</param>
    /// <param name="Path">The finished copy's path, or <c>null</c> while it is being made.</param>
    /// <param name="Stream">A stream that follows the copy as it grows, or <c>null</c> once it is finished.</param>
    public sealed record DownloadCopy(string Name, string? Path, Stream? Stream);
}
