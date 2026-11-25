using MS3DPRINT.Manager.Core.Printers;

namespace MS3DPRINT.Manager.Core.Tests.Printers;

public sealed class BambuReportTests
{
    [Fact]
    public void Partial_reports_are_merged_into_the_full_state()
    {
        var report = new BambuReport();
        var full = report.Apply("""
            {"print":{"command":"push_status","gcode_state":"RUNNING","mc_percent":45,"mc_remaining_time":83,"layer_num":12,"total_layer_num":200,
             "subtask_name":"Support casque","nozzle_temper":219.6,"nozzle_target_temper":220,"bed_temper":"60.0","bed_target_temper":60,"hms":[],
             "ams":{"ams":[{"id":"0","tray":[{"id":"0","tray_type":"PLA","tray_color":"FF0000FF"},{"id":"1"}]}]}}}
            """)!;
        Assert.Equal("En impression", full.StateLabel);
        Assert.Equal(45, full.Percent);
        Assert.Equal(60, full.Bed);
        Assert.Equal([new BambuTray("PLA", "FF0000")], full.Trays);

        var delta = report.Apply("""{"print":{"command":"push_status","mc_percent":46,"hms":[{"attr":83886336,"code":196612,"ts_unix":"20261125153645"}]}}""")!;
        Assert.Equal(46, delta.Percent);
        Assert.Equal(12, delta.Layer);
        Assert.Equal("Support casque", delta.JobName);
        Assert.Equal(1, delta.ErrorCount);
        Assert.Single(delta.Trays);
        var alert = Assert.Single(delta.Alerts);
        Assert.Equal("0500010000030004", alert.Ecode);
        Assert.Equal("HMS_0500_0100_0003_0004", alert.DisplayCode);
        Assert.Equal("Courante", alert.Severity);
        Assert.Equal(new DateTime(2026, 11, 25, 15, 36, 45), alert.Since);
    }

    [Fact]
    public void Catalogue_gives_the_french_text_or_the_code()
    {
        var texts = BambuHmsCatalog.Parse("""{"result":0,"data":{"device_hms":{"ver":1,"fr":[{"ecode":"0500010000030004","intro":"Espace insuffisant sur la carte MicroSD."},{"ecode":"0500040000030021","intro":""}]}}}""");
        Assert.Equal("Espace insuffisant sur la carte MicroSD.", BambuHmsCatalog.Describe(new BambuAlert(0x05000100, 0x00030004), texts));
        Assert.Contains("HMS_0500_0400_0003_0021", BambuHmsCatalog.Describe(new BambuAlert(0x05000400, 0x00030021), texts));
        Assert.Empty(BambuHmsCatalog.Parse("not json"));
    }

    [Fact]
    public void H2_temperatures_are_read_from_the_packed_device_values()
    {
        var status = new BambuReport().Apply("""
            {"print":{"gcode_state":"RUNNING","nozzle_temper":245.0,"chamber_temper":5,"device":{"bed":{"info":{"temp":4587590}},"ctc":{"info":{"temp":34}},
             "extruder":{"info":[{"id":0,"temp":16056565},{"id":1,"temp":47}]}}}}
            """)!;
        Assert.Equal((245d, 245d), (status.Nozzle, status.NozzleTarget));
        Assert.Equal(47, status.SecondNozzle);
        Assert.Equal((70d, 70d), (status.Bed, status.BedTarget));
        Assert.Equal(34, status.Chamber);
        Assert.True(status.HasTwoNozzles);
    }

    [Theory]
    [InlineData("""{"info":{"command":"get_version"}}""")]
    [InlineData("not json")]
    public void Messages_without_print_state_are_ignored(string json) => Assert.Null(new BambuReport().Apply(json));
}
