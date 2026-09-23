namespace MediaBrowser.Model.Configuration
{
    /// <summary>
    /// What the plain Download does when optimised downloads are on. Only one behaviour is offered
    /// at a time, so users are never shown the same file under two names.
    /// </summary>
    public enum DownloadBehaviour
    {
        /// <summary>
        /// The plain Download serves the item's own file, and the optimised copy is a separate
        /// action. The default, and the zero value, so turning the feature on adds an action rather
        /// than changing an existing one.
        /// </summary>
        /// <remarks>
        /// A client that builds its own download URL from the item id should hide the action rather
        /// than offer one that would quietly serve the original.
        /// </remarks>
        SeparateAction = 0,

        /// <summary>
        /// The plain Download serves the optimised copy when there is one and the original
        /// otherwise, and no separate action is offered. Works on every client unchanged.
        /// </summary>
        Substitute = 1,
    }
}
