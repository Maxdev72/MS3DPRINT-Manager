using System.Windows;
using System.Windows.Markup;
using System.Xml.Linq;

namespace MS3DPRINT.Manager.Core.Tests;

internal static class ThemeTestResources
{
    public static ResourceDictionary Load()
    {
        System.Reflection.Assembly.Load("MaterialDesignThemes.Wpf");
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "MS3DPRINT.Manager.App", "App.xaml")))
            directory = directory.Parent;
        var source = XDocument.Load(Path.Combine(directory!.FullName, "src", "MS3DPRINT.Manager.App", "App.xaml"));
        var ns = source.Root!.Name.Namespace;
        var contents = source.Root.Element(ns + "Application.Resources")!;
        var dictionary = contents.Element(ns + "ResourceDictionary") is { } existing
            ? new XElement(existing)
            : new XElement(ns + "ResourceDictionary", contents.Elements());
        foreach (var attribute in source.Root.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
            dictionary.SetAttributeValue(attribute.Name, attribute.Value);
        return (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
    }

    public static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Le test du thème n’a pas terminé.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
