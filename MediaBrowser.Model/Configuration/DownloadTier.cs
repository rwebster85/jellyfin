namespace MediaBrowser.Model.Configuration
{
    /// <summary>
    /// A download tier, as an administrator defined it.
    /// </summary>
    /// <remarks>
    /// The server does not produce these files, so a tier is never checked against what exists: a
    /// suffix no file is named for simply never matches.
    /// </remarks>
    public class DownloadTier
    {
        /// <summary>
        /// Gets or sets this tier's id, which a user's stored choice and
        /// <see cref="DownloadOptions.DefaultTierId"/> refer to. Assigned on save if missing.
        /// </summary>
        /// <remarks>
        /// Kept apart from <see cref="Suffix"/> so the suffix can be renamed without dropping
        /// everyone who chose the tier back onto the default.
        /// </remarks>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the file name suffix this tier is matched by: a file of this tier is named
        /// <c>&lt;source stem&gt; - &lt;Suffix&gt;.&lt;ext&gt;</c>.
        /// </summary>
        public string Suffix { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the label users see, such as <c>1080p</c>. Not translated; an empty name
        /// falls back to <see cref="Suffix"/>.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets an optional explanation shown beneath the name.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether users may choose this tier.
        /// </summary>
        /// <remarks>
        /// A disabled tier is still searched, last: the only alternative is the item's own file,
        /// which is larger than any optimised copy.
        /// </remarks>
        public bool Enabled { get; set; } = true;
    }
}
