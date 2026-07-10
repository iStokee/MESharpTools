namespace MESharp.Services
{
    /// <summary>Formats the terminal state of a map-dispatched boolean travel operation.</summary>
    internal static class MapTravelStatus
    {
        internal static string FromResult(string description, bool succeeded, bool cancellationRequested)
        {
            if (succeeded) return description + " finished.";
            return cancellationRequested
                ? description + " cancelled."
                : description + " failed: destination was not reached.";
        }
    }
}
