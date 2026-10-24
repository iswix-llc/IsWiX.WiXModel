using System;
using System.IO;
using System.Linq;
using IsWiX.WiXModel.Core;
using Xunit;

namespace IsWiX.WiXModel.Tests;

/// <summary>Services, shortcuts, registry, environment and INI files (batch 3).</summary>
public class ResourceTests
{
    private static ComponentGroup LoadGroup() =>
        WixDocument.Load(Path.Combine(AppContext.BaseDirectory, "Corpus", "services-shortcuts-registry.wxs"))
            .Root.Fragments.Single().ComponentGroups.Single();

    [Fact]
    public void Service_install_and_control_are_typed()
    {
        var component = LoadGroup().Components.First();
        var install = component.ServiceInstalls.Single();

        Assert.Equal(("AcmeService", "Acme Service", "NT AUTHORITY\\LocalService"), (install.Name, install.DisplayName, install.Account));
        Assert.Equal(ServiceInstallType.OwnProcess, install.Type!.Value.Value);
        Assert.Equal(ServiceInstallStart.Auto, install.Start!.Value.Value);
        Assert.Equal(ServiceInstallErrorControl.Normal, install.ErrorControl!.Value.Value);
        Assert.True(install.Vital!.Value.Value);
        Assert.Equal("Tcpip", install.ServiceDependencies.Single().Id);

        var config = install.ServiceConfigs.Single();
        Assert.Equal("yes", config.DelayedAutoStart);
        Assert.Equal("SeChangeNotifyPrivilege", config.RequiredPrivileges.Single().Name);

        var failureActions = install.ServiceConfigFailureActionsElements.Single();
        Assert.Equal(86400, failureActions.ResetPeriod!.Value.Value);
        Assert.Equal(["restartService", "none"], failureActions.Failures.Select(f => f.Action));

        var control = component.ServiceControls.Single();
        Assert.Equal(InstallUninstallType.Install, control.Start!.Value.Value);
        Assert.Equal(InstallUninstallType.Both, control.Stop!.Value.Value);
        Assert.Equal(InstallUninstallType.Uninstall, control.Remove!.Value.Value);
        Assert.True(control.Wait!.Value.Value);
        Assert.Equal("--quiet", control.ServiceArguments.Single().Value);
    }

    [Fact]
    public void Shortcut_and_its_properties_are_typed()
    {
        var component = LoadGroup().Components.ElementAt(1);
        var shortcut = component.Shortcuts.Single();

        Assert.Equal(("Acme", "[INSTALLFOLDER]Acme.exe", "App.ico"), (shortcut.Name, shortcut.Target, shortcut.Icon));
        Assert.Equal(ShortcutShow.Minimized, shortcut.Show!.Value.Value);
        Assert.Equal(("System.AppUserModel.ID", "Acme.App"), (shortcut.ShortcutProperties.Single().Key, shortcut.ShortcutProperties.Single().Value));

        var keyPath = component.RegistryValues.Single();
        Assert.Equal(RegistryRootType.HKCU, keyPath.Root!.Value.Value);
        Assert.Equal(RegistryValueType.Integer, keyPath.Type!.Value.Value);
        Assert.True(keyPath.KeyPath!.Value.Value);
    }

    [Fact]
    public void Registry_environment_and_ini_are_typed()
    {
        var component = LoadGroup().Components.Last();

        var key = component.RegistryKeys.Single();
        Assert.Equal(RegistryRootType.HKLM, key.Root!.Value.Value);
        Assert.True(key.ForceDeleteOnUninstall!.Value.Value);
        Assert.Equal(["Mode", "Servers"], key.RegistryValues.Select(v => v.Name));

        var servers = key.RegistryValues.Last();
        Assert.Equal(RegistryValueType.MultiString, servers.Type!.Value.Value);
        Assert.Equal(RegistryValueAction.Append, servers.Action!.Value.Value);
        Assert.Equal(["alpha", "beta"], servers.MultiStringValues.Select(v => v.Value));
        Assert.Equal("3", key.RegistryKeys.Single().RegistryValues.Single().Value);

        var environment = component.Environments.Single();
        Assert.Equal(EnvironmentAction.Set, environment.Action!.Value.Value);
        Assert.Equal(EnvironmentPart.Last, environment.Part!.Value.Value);
        Assert.True(environment.System!.Value.Value);

        var ini = component.IniFiles.Single();
        Assert.Equal(("acme.ini", "General", "Mode"), (ini.Name, ini.Section, ini.Key));
        Assert.Equal(IniFileAction.AddLine, ini.Action!.Value.Value);
    }

    [Fact]
    public void Building_a_service_component_in_code()
    {
        var document = WixDocument.Create(new IsWiX.WiXModel.Core.Xml.XmlTextFormat(new System.Text.UTF8Encoding(false), "\n", "  "));
        var component = document.Root.Add(new Fragment()).Add(new Component { Id = "Svc" });
        component.Add(new File { Source = "Svc.exe", KeyPath = true });
        var install = component.Add(new ServiceInstall
        {
            Name = "Svc",
            Type = ServiceInstallType.OwnProcess,
            Start = ServiceInstallStart.Demand,
            ErrorControl = ServiceInstallErrorControl.Normal,
        });
        install.Add(new ServiceDependency { Id = "Tcpip" });
        component.Add(new ServiceControl { Name = "Svc", Stop = InstallUninstallType.Both, Remove = InstallUninstallType.Uninstall });

        Assert.Equal(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<Wix xmlns=\"http://wixtoolset.org/schemas/v4/wxs\">\n" +
            "  <Fragment>\n" +
            "    <Component Id=\"Svc\">\n" +
            "      <File Source=\"Svc.exe\" KeyPath=\"yes\" />\n" +
            "      <ServiceInstall Name=\"Svc\" Type=\"ownProcess\" Start=\"demand\" ErrorControl=\"normal\">\n" +
            "        <ServiceDependency Id=\"Tcpip\" />\n" +
            "      </ServiceInstall>\n" +
            "      <ServiceControl Name=\"Svc\" Stop=\"both\" Remove=\"uninstall\" />\n" +
            "    </Component>\n" +
            "  </Fragment>\n" +
            "</Wix>\n",
            document.ToString());
    }
}

/// <summary>Custom actions, sequences, standard actions and searches (batch 4).</summary>
public class SequenceTests
{
    private static Package LoadPackage() =>
        WixDocument.Load(Path.Combine(AppContext.BaseDirectory, "Corpus", "custom-actions-and-searches.wxs")).Root.Packages.Single();

    [Fact]
    public void Custom_actions_are_typed()
    {
        var package = LoadPackage();

        var configure = package.CustomActions.First();
        Assert.Equal(("ConfigureService", "CustomActionsDll", "ConfigureService"), (configure.Id, configure.BinaryRef, configure.DllEntry));
        Assert.Equal(CustomActionExecute.Deferred, configure.Execute!.Value.Value);
        Assert.Equal(CustomActionReturn.Check, configure.Return!.Value.Value);
        Assert.False(configure.Impersonate!.Value.Value);
        Assert.Equal("WixFailWhenDeferred", package.CustomActionRefs.Single().Id);
    }

    [Fact]
    public void Sequences_hold_typed_custom_and_standard_actions()
    {
        var package = LoadPackage();
        var execute = package.InstallExecuteSequences.Single();

        Assert.Equal(
            [("SetConfigureServiceData", null, "ConfigureService", null), ("ConfigureService", "InstallFiles", null, "NOT Installed")],
            execute.CustomElements.Select(c => (c.Action, c.After, c.Before, c.Condition)));
        Assert.Equal("InstallInitialize", execute.RemoveExistingProductsElements.Single().After);
        Assert.True(execute.LaunchConditionsElements.Single().Suppress!.Value.Value);
        Assert.Equal(4000, execute.InstallFilesElements.Single().Sequence!.Value.Value);
        Assert.DoesNotContain(execute.Children, e => e is WixUnknownElement);

        var ui = package.InstallUISequences.Single();
        Assert.Equal("LaunchConditions", ui.FindRelatedProductsElements.Single().Before);
        Assert.Empty(package.AdminExecuteSequences.Single().Children);
    }

    [Fact]
    public void Searches_are_typed()
    {
        var properties = LoadPackage().Properties.ToDictionary(p => p.Id!);

        var directory = properties["EXISTINGDIR"].DirectorySearches.Single();
        Assert.Equal(1, directory.Depth!.Value.Value);
        Assert.Equal(("Acme.exe", "1.0.0.0"), (directory.FileSearches.Single().Name, directory.FileSearches.Single().MinVersion));

        Assert.Equal(ComponentSearchType.File, properties["RUNTIMEFOUND"].ComponentSearches.Single().Type!.Value.Value);
        Assert.Equal(IniFileSearchType.Directory, properties["LEGACYINI"].IniFileSearches.Single().Type!.Value.Value);
        Assert.Equal("2.0.0", properties["OLDPRODUCT"].ProductSearches.Single().Maximum);
        Assert.Equal("SharedRegistrySearch", properties["SHAREDSEARCH"].RegistrySearchRefs.Single().Id);
    }

    [Fact]
    public void Scheduling_a_custom_action_adds_it_after_the_existing_ones()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Corpus", "custom-actions-and-searches.wxs");
        var document = WixDocument.Load(path);

        document.Root.Packages.Single().InstallExecuteSequences.Single()
            .Add(new Custom { Action = "Cleanup", Before = "InstallFinalize", Condition = "REMOVE=\"ALL\"" });

        var expected = System.IO.File.ReadAllText(path).Replace(
            "Condition=\"NOT Installed\" />\n",
            "Condition=\"NOT Installed\" />\n      <Custom Action=\"Cleanup\" Before=\"InstallFinalize\" Condition=\"REMOVE=&quot;ALL&quot;\" />\n",
            StringComparison.Ordinal);
        Assert.Equal(expected, document.ToString());
    }
}
