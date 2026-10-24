using System;
using System.Linq;
using IsWiX.WiXModel.Core;
using Xunit;
using static IsWiX.WiXModel.Tests.TestText;

namespace IsWiX.WiXModel.Tests;

/// <summary>The typed WiX Util extension elements, generated from the util schema.</summary>
public class UtilTests
{
    private const string Source = """
        <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs" xmlns:util="http://wixtoolset.org/schemas/v4/wxs/util">
          <Fragment>
            <Component Id="Config" Directory="INSTALLFOLDER">
              <File Source="app.config" />
              <util:XmlFile Id="SetLevel" File="[#app.config]" ElementPath="//log" Name="level" Value="info" Action="setValue" Permanent="yes" />
              <util:User Id="Svc" Name="svc" CreateUser="$(var.CreateUser)" />
            </Component>
          </Fragment>
        </Wix>
        """;

    [Fact]
    public void Util_elements_load_as_typed_wrappers()
    {
        var component = Parse(Source).Root.Fragments.Single().Components.Single();

        var xmlFile = component.UtilXmlFiles.Single();
        Assert.Equal("//log", xmlFile.ElementPath);
        Assert.Equal(Util.XmlFileAction.SetValue, xmlFile.Action!.Value.Value);
        Assert.True(xmlFile.Permanent!.Value.Value);

        var user = Assert.IsType<Util.User>(component.Children.Last());
        Assert.Equal("svc", user.Name);
        Assert.Equal("$(var.CreateUser)", user.CreateUser!.Value.Text);
    }

    [Fact]
    public void Adding_a_util_element_declares_the_util_prefix()
    {
        var document = Parse("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Fragment>
                <Component Id="Svc" Directory="INSTALLFOLDER" />
              </Fragment>
            </Wix>
            """);
        var component = document.Root.Fragments.Single().Components.Single();

        var user = component.Add(new Util.User { Id = "SvcUser", Name = "svc", LogonAsService = true });
        user.Add(new Util.GroupRef { Id = "Users" });

        Assert.Equal(Lf("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs" xmlns:util="http://wixtoolset.org/schemas/v4/wxs/util">
              <Fragment>
                <Component Id="Svc" Directory="INSTALLFOLDER">
                  <util:User Id="SvcUser" Name="svc" LogonAsService="yes">
                    <util:GroupRef Id="Users" />
                  </util:User>
                </Component>
              </Fragment>
            </Wix>
            """), document.ToString());
        Assert.Same(user.Xml, component.UtilUsers.Single().Xml);
    }

    [Fact]
    public void Editing_a_util_attribute_changes_only_its_text()
    {
        var document = Parse(Source);
        var xmlFile = document.Root.Fragments.Single().Components.Single().UtilXmlFiles.Single();

        xmlFile.Value = "debug";

        Assert.Equal(Lf(Source).Replace("Value=\"info\"", "Value=\"debug\"", StringComparison.Ordinal), document.ToString());
    }

    [Fact]
    public void Invalid_util_values_list_what_is_allowed()
    {
        var document = Parse(Source.Replace("Action=\"setValue\"", "Action=\"setvalue\"", StringComparison.Ordinal));

        var diagnostic = Assert.Single(document.Validate());

        Assert.Equal(WixDiagnostics.InvalidValue, diagnostic.Code);
        Assert.Equal("Action", diagnostic.Attribute!.LocalName);
        Assert.Contains("expected createElement, deleteValue, setValue", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(Util.User), nameof(Util.User.CreateUser), typeof(WixValue<bool>?))]
    [InlineData(typeof(Util.XmlFile), nameof(Util.XmlFile.Sequence), typeof(WixValue<int>?))]
    [InlineData(typeof(Util.RegistrySearch), nameof(Util.RegistrySearch.Bitness), typeof(WixValue<BitnessType>?))]
    [InlineData(typeof(Util.RegistrySearch), nameof(Util.RegistrySearch.Root), typeof(WixValue<Util.RegistrySearchRoot>?))]
    public void Core_and_util_types_map_to_csharp_types(Type element, string property, Type expected)
    {
        Assert.Equal(expected, element.GetProperty(property)!.PropertyType);
    }
}
