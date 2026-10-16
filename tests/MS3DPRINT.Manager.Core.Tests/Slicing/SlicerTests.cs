using MS3DPRINT.Manager.Core.Slicing;

namespace MS3DPRINT.Manager.Core.Tests.Slicing;

public sealed class SlicerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ms3d-slicer-" + Guid.NewGuid().ToString("N"));

    public SlicerTests()
    {
        foreach (var kind in new[] { "machine", "process", "filament" }) Directory.CreateDirectory(Path.Combine(_root, kind));
        Write("machine", "Bambu Lab A1 0.4 nozzle", """
            { "type": "machine", "name": "Bambu Lab A1 0.4 nozzle", "instantiation": "true", "printer_model": "Bambu Lab A1",
              "default_print_profile": "0.20mm Standard @BBL A1", "default_filament_profile": ["Bambu PLA Basic @BBL A1"] }
            """);
        Write("machine", "Bambu Lab A1 0.6 nozzle", """
            { "type": "machine", "name": "Bambu Lab A1 0.6 nozzle", "instantiation": "true", "printer_model": "Bambu Lab A1" }
            """);
        Write("machine", "Bambu Lab A1 mini", """
            { "type": "machine", "name": "Bambu Lab A1 mini", "instantiation": "false", "printer_model": "Bambu Lab A1 mini" }
            """);
        Write("machine", "Bambu Lab H2D 0.4 nozzle", """
            { "type": "machine", "name": "Bambu Lab H2D 0.4 nozzle", "instantiation": "true", "printer_model": "Bambu Lab H2D", "nozzle_diameter": ["0.4", "0.4"] }
            """);
        Write("machine", "Bambu Lab H2S 0.4 nozzle", """
            { "type": "machine", "name": "Bambu Lab H2S 0.4 nozzle", "instantiation": "true", "printer_model": "Bambu Lab H2S", "nozzle_diameter": ["0.4"] }
            """);
        Write("machine", "fragment", """{ "name": "Bambu Lab A1 0.4 nozzle template", "instantiation": "true" }""");
        Write("process", "0.20mm Standard @BBL A1", """
            { "type": "process", "name": "0.20mm Standard @BBL A1", "instantiation": "true", "compatible_printers": ["Bambu Lab A1 0.4 nozzle"] }
            """);
        Write("process", "0.12mm Fine @BBL A1", """
            { "type": "process", "name": "0.12mm Fine @BBL A1", "instantiation": "true", "compatible_printers": ["Bambu Lab A1 0.4 nozzle"] }
            """);
        Write("process", "0.20mm Standard @BBL H2D", """
            { "type": "process", "name": "0.20mm Standard @BBL H2D", "instantiation": "true", "compatible_printers": ["Bambu Lab H2D 0.4 nozzle"] }
            """);
        Write("filament", "Generic PETG @BBL A1", """
            { "type": "filament", "name": "Generic PETG @BBL A1", "instantiation": "true", "compatible_printers": ["Bambu Lab A1 0.4 nozzle"] }
            """);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string kind, string name, string json) => File.WriteAllText(Path.Combine(_root, kind, name + ".json"), json);

    private SlicerProfiles Profiles() => new(new SlicerInstall("Bambu Studio", "bambu-studio.exe", _root));

    [Fact]
    public void Machines_ListsOnlyRealMachineProfiles()
    {
        var machines = Profiles().Machines();

        Assert.Equal(["Bambu Lab A1 0.4 nozzle", "Bambu Lab H2D 0.4 nozzle", "Bambu Lab H2S 0.4 nozzle", "Bambu Lab A1 0.6 nozzle"], machines.Select(machine => machine.Name));
        Assert.Equal("0.20mm Standard @BBL A1", machines[0].DefaultProcess);
        Assert.Equal("Bambu PLA Basic @BBL A1", machines[0].DefaultFilament);
    }

    [Fact]
    public void Processes_AreTheOnesMadeForTheMachine()
    {
        var profiles = Profiles();
        var machine = profiles.Machines()[0];

        Assert.Equal(["0.12mm Fine @BBL A1", "0.20mm Standard @BBL A1"], profiles.Processes(machine));
    }

    [Fact]
    public void FilamentFor_PrefersTheGenericProfileOfTheMaterialThenTheMachineDefault()
    {
        var profiles = Profiles();
        var machine = profiles.Machines()[0];

        Assert.Equal("Generic PETG @BBL A1", profiles.FilamentFor(machine, "PETG"));
        Assert.Equal("Bambu PLA Basic @BBL A1", profiles.FilamentFor(machine, "TPU"));
        Assert.Equal("Bambu PLA Basic @BBL A1", profiles.FilamentFor(machine, null));
    }

    [Fact]
    public void Detect_PrefersBambuStudioThenOrcaSlicer()
    {
        var exe = Path.Combine(_root, "slicer.exe");
        File.WriteAllText(exe, "");

        Assert.Equal("Bambu Studio", SlicerLocator.Detect(exe, _root, "missing.exe", "missing")?.Name);
        Assert.Equal("OrcaSlicer", SlicerLocator.Detect("missing.exe", "missing", exe, _root)?.Name);
        Assert.Null(SlicerLocator.Detect("missing.exe", "missing", "missing.exe", "missing"));
    }

    [Fact]
    public void BuildArguments_PassesProfilesOverridesAndModelAsSeparateArguments()
    {
        var install = new SlicerInstall("Bambu Studio", "bambu-studio.exe", _root);
        var profiles = new SlicerProfiles(install);
        var machine = profiles.Machines()[0];

        var arguments = SliceRunner.BuildArguments(install, profiles, new SliceRequest(@"C:\models\a b.stl", machine, "0.20mm Standard @BBL A1", "Generic PETG @BBL A1", 15, 3), @"C:\out");

        Assert.Equal("--slice", arguments[0]);
        Assert.Contains(arguments, argument => argument.EndsWith("0.20mm Standard @BBL A1.json") && argument.Contains(';'));
        Assert.Equal("15%", arguments[arguments.ToList().IndexOf("--sparse-infill-density") + 1]);
        Assert.Equal("3", arguments[arguments.ToList().IndexOf("--wall-loops") + 1]);
        Assert.Equal(@"C:\models\a b.stl", arguments[^1]);
    }

    [Fact]
    public void BuildArguments_ScalesAModelDrawnInInches()
    {
        var install = new SlicerInstall("Bambu Studio", "bambu-studio.exe", _root);
        var profiles = new SlicerProfiles(install);
        var machine = profiles.Machines()[0];

        var inches = SliceRunner.BuildArguments(install, profiles, new SliceRequest("m.stl", machine, "0.20mm Standard @BBL A1", null, MillimetresPerUnit: 25.4), "out");
        var millimetres = SliceRunner.BuildArguments(install, profiles, new SliceRequest("m.stl", machine, "0.20mm Standard @BBL A1", null), "out");

        Assert.Equal("25.4", inches[inches.ToList().IndexOf("--scale") + 1]);
        Assert.DoesNotContain("--scale", millimetres);
    }

    [Fact]
    public void BuildArguments_DoesNotScaleAnOrientedCopyAlreadyInMillimetres()
    {
        var install = new SlicerInstall("Bambu Studio", "bambu-studio.exe", _root);
        var profiles = new SlicerProfiles(install);
        var machine = profiles.Machines()[0];

        var arguments = SliceRunner.BuildArguments(install, profiles, new SliceRequest(@"C:\temp\oriented.stl", machine, "0.20mm Standard @BBL A1", null, MillimetresPerUnit: 1), "out");

        Assert.DoesNotContain("--scale", arguments);
        Assert.Equal(@"C:\temp\oriented.stl", arguments[^1]);
    }

    [Fact]
    public void BuildArguments_SwitchesSupportsWithAnEqualsSignAndLeavesThemAloneWhenUnasked()
    {
        var install = new SlicerInstall("Bambu Studio", "bambu-studio.exe", _root);
        var profiles = new SlicerProfiles(install);
        var machine = profiles.Machines()[0];

        var on = SliceRunner.BuildArguments(install, profiles, new SliceRequest("m.stl", machine, "0.20mm Standard @BBL A1", null, Supports: true), "out");
        var off = SliceRunner.BuildArguments(install, profiles, new SliceRequest("m.stl", machine, "0.20mm Standard @BBL A1", null, Supports: false), "out");
        var unset = SliceRunner.BuildArguments(install, profiles, new SliceRequest("m.stl", machine, "0.20mm Standard @BBL A1", null), "out");

        Assert.Contains("--enable-support=1", on);
        Assert.Contains("--enable-support=0", off);
        Assert.DoesNotContain(unset, argument => argument.StartsWith("--enable-support", StringComparison.Ordinal));
        Assert.Equal("m.stl", on[^1]);
    }

    [Fact]
    public void ParseResult_ReadsLengthDiameterAndLayersFromTheHeader()
    {
        const string header = "; model printing time: 1h 7m 9s; total estimated time: 1h 7m 10s\n; total layer number: 70\n; total filament length [mm] : 3005.10\n; filament_diameter: 1.75\n";

        var result = SliceRunner.ParseResult(4030, 0.2, 20, 2, header);

        Assert.Equal(4030, result.TimeSeconds);
        Assert.Equal(3005.10, result.FilamentLengthMm, 2);
        Assert.Equal(1.75, result.FilamentDiameterMm);
        Assert.Equal(70, result.Layers);
        Assert.Equal(7.228, result.FilamentVolumeCm3, 2);
        Assert.Equal(7.228 * 1.24, result.WeightGrams(1.24), 2);
    }

    [Fact]
    public void ParseResult_FallsBackToTheTimeWrittenInTheHeader()
    {
        var result = SliceRunner.ParseResult(0, 0.2, 15, 2, "; total estimated time: 1d 2h 3m 4s\n");

        Assert.Equal(86400 + 2 * 3600 + 3 * 60 + 4, result.TimeSeconds);
    }

    [Fact]
    public void Read_ReportsTheSlicerErrorWhenTheSliceFailed()
    {
        File.WriteAllText(Path.Combine(_root, "result.json"), """{ "return_code": -3, "error_string": "The input files to the slicer are not found." }""");

        var error = Assert.Throws<InvalidOperationException>(() => SliceRunner.Read(_root));

        Assert.Contains("not found", error.Message);
    }

    [Fact]
    public void Read_CombinesTheResultFileAndTheGcodeHeader()
    {
        File.WriteAllText(Path.Combine(_root, "result.json"), """
            { "return_code": 0, "layer_height": 0.2, "sparse_infill_density": 15.0, "wall_loops": 2,
              "sliced_plates": [ { "total_predication": 3600.5 } ] }
            """);
        File.WriteAllText(Path.Combine(_root, "plate_1.gcode"), "; total filament length [mm] : 1000.0\n; filament_diameter: 1.75\n; total layer number: 10\n");

        var result = SliceRunner.Read(_root);

        Assert.Equal(3600.5, result.TimeSeconds);
        Assert.Equal(1000, result.FilamentLengthMm);
        Assert.Equal(15, result.InfillPercent);
        Assert.Equal(10, result.Layers);
    }

    [Fact]
    public void Machines_know_how_many_nozzles_they_have()
    {
        var machines = Profiles().Machines();

        Assert.Equal(2, machines.Single(machine => machine.PrinterModel == "Bambu Lab H2D").ExtruderCount);
        Assert.False(machines.Single(machine => machine.PrinterModel == "Bambu Lab H2D").CanBeSlicedAutomatically);
        Assert.True(machines.First(machine => machine.PrinterModel == "Bambu Lab A1").CanBeSlicedAutomatically);
    }

    [Fact]
    public void MachineFor_slices_a_double_nozzle_printer_as_its_single_nozzle_sibling()
    {
        var machines = Profiles().Machines();

        Assert.Equal("Bambu Lab A1 0.4 nozzle", SlicerProfiles.MachineFor(machines, "bambu lab a1")?.Name);
        Assert.Equal("Bambu Lab H2S 0.4 nozzle", SlicerProfiles.MachineFor(machines, "Bambu Lab H2D")?.Name);
        Assert.Null(SlicerProfiles.MachineFor(machines, "Prusa MK4"));
    }
}
