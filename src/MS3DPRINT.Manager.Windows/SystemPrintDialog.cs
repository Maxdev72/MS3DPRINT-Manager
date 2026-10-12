using System.Runtime.InteropServices;

namespace MS3DPRINT.Manager.Windows;

/// <summary>
/// The standard Windows print dialog (comdlg32 PrintDlgEx): printer chooser, preferences, copies and page range,
/// exactly as in any other Windows application. It only collects the choices; nothing is printed here.
/// </summary>
public static class SystemPrintDialog
{
    private const uint PdPageNums = 0x00000002;
    private const uint PdNoSelection = 0x00000004;
    private const uint PdCurrentPage = 0x00400000;
    private const uint PdResultCancel = 0;
    private const uint StartPageGeneral = 0xFFFFFFFF;

    [StructLayout(LayoutKind.Sequential)]
    private struct PrintPageRange
    {
        public uint FromPage;
        public uint ToPage;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PrintDlgEx
    {
        public uint StructSize;
        public IntPtr Owner;
        public IntPtr DevMode;
        public IntPtr DevNames;
        public IntPtr Dc;
        public uint Flags;
        public uint Flags2;
        public uint ExclusionFlags;
        public uint PageRangeCount;
        public uint MaxPageRanges;
        public IntPtr PageRanges;
        public uint MinPage;
        public uint MaxPage;
        public uint Copies;
        public IntPtr Instance;
        public IntPtr PrintTemplateName;
        public IntPtr Callback;
        public uint PropertyPages;
        public IntPtr PropertyPageHandles;
        public uint StartPage;
        public uint ResultAction;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "PrintDlgExW")]
    private static extern int ShowPrintDialogEx(ref PrintDlgEx dialog);

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr handle);

    /// <summary>Shows the dialog on the calling (UI) thread. Returns null when the user cancels.</summary>
    public static PdfPrintRequest? Show(IntPtr owner, int pageCount, int currentPage)
    {
        if (pageCount <= 0) throw new ArgumentOutOfRangeException(nameof(pageCount));
        // PrintDlgEx refuses a null owner (E_HANDLE); fall back to the foreground window.
        if (owner == IntPtr.Zero) owner = GetForegroundWindow();
        const int maxRanges = 16;
        var ranges = Marshal.AllocHGlobal(Marshal.SizeOf<PrintPageRange>() * maxRanges);
        try
        {
            Marshal.StructureToPtr(new PrintPageRange { FromPage = 1, ToPage = (uint)pageCount }, ranges, false);
            var dialog = new PrintDlgEx
            {
                StructSize = (uint)Marshal.SizeOf<PrintDlgEx>(),
                Owner = owner,
                Flags = PdPageNums | PdNoSelection,
                PageRangeCount = 1,
                MaxPageRanges = maxRanges,
                PageRanges = ranges,
                MinPage = 1,
                MaxPage = (uint)pageCount,
                Copies = 1,
                StartPage = StartPageGeneral
            };
            var result = ShowPrintDialogEx(ref dialog);
            try
            {
                if (result != 0) throw new InvalidOperationException($"La boîte d’impression Windows n’a pas pu s’ouvrir (erreur 0x{result:X8}).");
                if (dialog.ResultAction == PdResultCancel || dialog.ResultAction == 2 /* PD_RESULT_APPLY */) return null;

                var printer = ReadPrinterName(dialog.DevNames);
                if (string.IsNullOrWhiteSpace(printer)) throw new InvalidOperationException("Aucune imprimante n’a été sélectionnée.");
                return new PdfPrintRequest(printer, (int)Math.Clamp(dialog.Copies, 1, 99), SelectedPages(dialog, pageCount, currentPage));
            }
            finally
            {
                if (dialog.DevMode != IntPtr.Zero) GlobalFree(dialog.DevMode);
                if (dialog.DevNames != IntPtr.Zero) GlobalFree(dialog.DevNames);
            }
        }
        finally { Marshal.FreeHGlobal(ranges); }
    }

    private static IReadOnlyList<int> SelectedPages(PrintDlgEx dialog, int pageCount, int currentPage)
    {
        if ((dialog.Flags & PdCurrentPage) != 0) return [Math.Clamp(currentPage, 0, pageCount - 1)];
        if ((dialog.Flags & PdPageNums) == 0) return Enumerable.Range(0, pageCount).ToArray();

        var pages = new List<int>();
        var size = Marshal.SizeOf<PrintPageRange>();
        for (var index = 0; index < Math.Min(dialog.PageRangeCount, dialog.MaxPageRanges); index++)
        {
            var range = Marshal.PtrToStructure<PrintPageRange>(dialog.PageRanges + index * size);
            for (var page = Math.Max(1, range.FromPage); page <= Math.Min((uint)pageCount, range.ToPage); page++)
                if (!pages.Contains((int)page - 1)) pages.Add((int)page - 1);
        }
        return pages.Count == 0 ? Enumerable.Range(0, pageCount).ToArray() : pages;
    }

    /// <summary>DEVNAMES holds offsets, in characters, to the driver, device (printer) and port strings.</summary>
    private static string? ReadPrinterName(IntPtr devNames)
    {
        if (devNames == IntPtr.Zero) return null;
        var pointer = GlobalLock(devNames);
        if (pointer == IntPtr.Zero) return null;
        try
        {
            var deviceOffset = (ushort)Marshal.ReadInt16(pointer, 2);
            return Marshal.PtrToStringUni(pointer + deviceOffset * 2);
        }
        finally { GlobalUnlock(devNames); }
    }
}
