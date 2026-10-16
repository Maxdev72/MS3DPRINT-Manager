namespace MS3DPRINT.Manager.Desktop.Viewer;

/// <summary>An active printer of the workshop as the viewer needs it: its name and its build volume as typed on the Printers page.</summary>
public sealed record ViewerPrinter(string Name, string BuildVolume);
