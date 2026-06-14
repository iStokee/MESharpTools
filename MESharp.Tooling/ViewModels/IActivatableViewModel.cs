namespace MESharp.ViewModels
{
    /// <summary>
    /// Optional lifecycle hooks for view models that should react to navigation.
    /// </summary>
    public interface IActivatableViewModel
    {
        /// <summary>
        /// Called when the view becomes the active tab/content.
        /// </summary>
        void OnActivated();

        /// <summary>
        /// Called when the view is being hidden or navigated away from.
        /// </summary>
        void OnDeactivated();
    }
}
