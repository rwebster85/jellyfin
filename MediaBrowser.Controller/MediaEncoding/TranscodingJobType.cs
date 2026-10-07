namespace MediaBrowser.Controller.MediaEncoding
{
    /// <summary>
    /// Enum TranscodingJobType.
    /// </summary>
    public enum TranscodingJobType
    {
        /// <summary>
        /// The progressive.
        /// </summary>
        Progressive,

        /// <summary>
        /// The HLS.
        /// </summary>
        Hls,

        /// <summary>
        /// The dash.
        /// </summary>
        Dash,

        /// <summary>
        /// A copy made for download and kept: it runs to the end whether or not anyone is reading
        /// it, and is never throttled. Encoded as <see cref="Progressive"/>, which the stream state
        /// keeps as its own type; only the job is a download.
        /// </summary>
        Download
    }
}
