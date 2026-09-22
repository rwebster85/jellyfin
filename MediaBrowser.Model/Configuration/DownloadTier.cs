namespace MediaBrowser.Model.Configuration
{
    /// <summary>
    /// A download quality tier, as an administrator defined it.
    /// </summary>
    /// <remarks>
    /// Tiers are admin-defined rather than a fixed list because the mapping from a name to a size is
    /// one server's own convention. <c>High</c> and <c>Standard</c> meaning 1080p and 720p is a
    /// personal choice, and a server wanting three tiers, or 480p for phones, or Small/Medium/Large,
    /// is equally valid.
    ///
    /// The server does not produce these files, so it cannot check a tier against anything that
    /// exists. Naming what goes into a download location to match the <see cref="Suffix"/> defined
    /// here is the administrator's responsibility, and a tier nothing is named for simply never
    /// matches.
    /// </remarks>
    public class DownloadTier
    {
        /// <summary>
        /// Gets or sets this tier's identifier, which is what a user's stored choice and
        /// <see cref="DownloadOptions.DefaultTierId"/> refer to.
        /// </summary>
        /// <remarks>
        /// The identity is here rather than on <see cref="Suffix"/> so that the suffix stays
        /// editable. Renaming a tier is a normal thing to want - the file naming changed, or the
        /// label was wrong - and it would otherwise silently drop every user who had chosen that
        /// tier back onto the default.
        ///
        /// Assigned when the tier is saved, so a tier written by hand without one gets an id the
        /// first time the settings are stored.
        /// </remarks>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the file name suffix this tier is matched by: a rendition of it is named
        /// <c>&lt;source stem&gt; - &lt;Suffix&gt;.&lt;ext&gt;</c>.
        /// </summary>
        public string Suffix { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the short label users see, such as <c>1080p</c>.
        /// </summary>
        /// <remarks>
        /// Administrator-supplied and therefore untranslated, the same as a library's name. An empty
        /// name falls back to <see cref="Suffix"/>, so a tier is never shown unlabelled.
        /// </remarks>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets an optional longer explanation, shown beneath the name where a user picks a
        /// tier.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether users may choose this tier.
        /// </summary>
        /// <remarks>
        /// A disabled tier is still searched, last. Enabling decides what a user may <em>pick</em>,
        /// not which files may be served, because the only alternative to serving a rendition is
        /// serving the item's own file, which is larger than any of them.
        /// </remarks>
        public bool Enabled { get; set; } = true;
    }
}
