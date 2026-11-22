namespace BundleUltimateTool.Rendering
{
    // Owned by a single viewer input surface on its UI thread.
    internal sealed class ViewerDragState
    {
        internal bool Orbiting { get; set; }
        internal bool Panning { get; set; }
        internal void LostCapture() { Orbiting = false; Panning = false; }
    }
}
