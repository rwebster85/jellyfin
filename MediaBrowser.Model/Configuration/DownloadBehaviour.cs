namespace MediaBrowser.Model.Configuration
{
    /// <summary>
    /// Which download behaviour the server offers its users.
    /// </summary>
    /// <remarks>
    /// This is a question about what the admin wants users to be able to do: whether downloading is
    /// a single action whose meaning the admin decides, or a choice the user makes for themselves
    /// each time. Both behaviours exist in the code, and offering both at once is what this setting
    /// exists to prevent - a plain download that quietly substitutes while a second entry offers the
    /// same file explicitly tells two stories about one button.
    ///
    /// The values are ordered so that the safer one is zero, which is what an existing
    /// <c>downloads.xml</c> with no element for this setting reads back as. That mattered less once
    /// <see cref="DownloadOptions.Enabled"/> arrived - a server with the feature off is unaffected
    /// either way - but it still decides what an admin gets the moment they switch it on.
    /// </remarks>
    public enum DownloadBehaviour
    {
        /// <summary>
        /// Both files are reachable and the user picks. The plain download serves the item's own
        /// file, exactly as an unmodified server would, and the optimised copy is a separate action
        /// asked for explicitly.
        /// </summary>
        /// <remarks>
        /// For an admin who wants users to be able to take either - the original when they want the
        /// full-quality file, the optimised copy when size matters more. Honest about which file is
        /// being served, at the cost of asking the user to know the difference.
        ///
        /// The default, because it does not redefine what an existing button means. Turning the
        /// feature on adds an action rather than silently changing one, so an admin who wants
        /// substitution opts into it deliberately.
        ///
        /// A client can only offer the explicit action if it can actually request it. One that
        /// rebuilds its own download URL from the item id is expected to hide the action rather than
        /// offer one that would quietly serve the original.
        /// </remarks>
        SeparateAction = 0,

        /// <summary>
        /// The optimised workflow replaces the normal one. The plain download serves the optimised
        /// copy in place of the item's own file, and no separate action is offered, so a user takes
        /// the optimised copy whenever one exists and the original otherwise.
        /// </summary>
        /// <remarks>
        /// For an admin who wants the optimised copy to be what downloading means - to keep
        /// transfers small, say - without asking users to understand the distinction or choose
        /// correctly. It is also the only behaviour a client that rebuilds its own download URL can
        /// benefit from without being taught the new request.
        /// </remarks>
        Substitute = 1,
    }
}
