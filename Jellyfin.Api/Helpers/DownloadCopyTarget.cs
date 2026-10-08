using System.Collections.Generic;
using MediaBrowser.Model.Configuration;

namespace Jellyfin.Api.Helpers
{
    /// <summary>
    /// Where a user's transcoded copy of an item belongs, whether or not it has been made yet.
    /// </summary>
    /// <param name="Tier">The tier the copy is made at.</param>
    /// <param name="Locations">The locations copies are kept in, in order of preference.</param>
    /// <param name="RelativePath">The copy's path relative to a location.</param>
    public sealed record DownloadCopyTarget(DownloadTier Tier, IReadOnlyList<string> Locations, string RelativePath);
}
