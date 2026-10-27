namespace MESharp.Services
{
    /// <summary>Formats navigation preparation and terminal results.</summary>
    internal static class MapTravelStatus
    {
        internal static string? WithPreparation(string? status, bool running, bool preparing, string readinessMessage)
            => running && preparing ? "Preparing navigation data: " + readinessMessage : status;

        internal static string FromResult(string description, MESharp.API.NavigationResult result, bool cancellationRequested)
        {
            if (result.Succeeded || cancellationRequested)
                return FromResult(description, result.Succeeded, cancellationRequested);
            var message = string.IsNullOrWhiteSpace(result.FailMessage) ? result.PlanResult?.Message : result.FailMessage;
            return string.IsNullOrWhiteSpace(message)
                ? FromResult(description, false, false)
                : description + " failed: " + message;
        }

        internal static string FromResult(string description, bool succeeded, bool cancellationRequested)
        {
            if (succeeded) return description + " finished.";
            return cancellationRequested
                ? description + " cancelled."
                : description + " failed: destination was not reached.";
        }
    }
}
