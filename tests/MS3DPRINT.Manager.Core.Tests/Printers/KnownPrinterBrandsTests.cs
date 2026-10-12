using MS3DPRINT.Manager.Desktop;

namespace MS3DPRINT.Manager.Core.Tests.Printers;

public sealed class KnownPrinterBrandsTests
{
    [Theory]
    [InlineData("Bambu Lab")]
    [InlineData("Creality")]
    [InlineData("Prusa Research")]
    [InlineData("Anycubic")]
    [InlineData("Elegoo")]
    [InlineData("Raise3D")]
    [InlineData("UltiMaker")]
    [InlineData("Formlabs")]
    public void Printer_dialog_offers_common_manufacturer_brands(string brand)
    {
        Assert.Contains(brand, CreatePrinterWindow.KnownBrands);
    }
}
