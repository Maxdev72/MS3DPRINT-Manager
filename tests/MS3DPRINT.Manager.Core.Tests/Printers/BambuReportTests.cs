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

        var delta = report.Apply("""{"print":{"command":"push_status","mc_percent":46,"hms":[{"attr":1,"code":2}]}}""")!;
        Assert.Equal(46, delta.Percent);
        Assert.Equal(12, delta.Layer);
        Assert.Equal("Support casque", delta.JobName);
        Assert.Equal(1, delta.ErrorCount);
        Assert.Single(delta.Trays);
    }

    [Theory]
    [InlineData("""{"info":{"command":"get_version"}}""")]
    [InlineData("not json")]
    public void Messages_without_print_state_are_ignored(string json) => Assert.Null(new BambuReport().Apply(json));
}
