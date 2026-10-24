using System.IO;
using System.Linq;
using System.Xml.Linq;
using IsWiX.WiXModel.Core;

namespace IsWiX.WiXModel
{
    /// <summary>
    /// IsWiX's canonical element order: the conventions IsWiX has always written packages and fragments in, so
    /// files it authored stay where they are and new elements always land in the same place.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>
    /// <c>Package</c> and <c>Fragment</c> children follow IsWiX's kind order (summary information and upgrade
    /// first, sequences last). The lists are IsWiX's existing ones with the <c>Majorupgrade</c> typo fixed,
    /// entries that are not valid WiX v4+ children removed, and v4-only children placed next to their closest
    /// relatives. Kinds not listed (Bundle elements, extension elements) come last, in their existing order.
    /// </description></item>
    /// <item><description><c>Property</c> and top-level <c>Feature</c> elements are sorted by <c>Id</c>.</description></item>
    /// <item><description>
    /// <c>Component</c> elements in a <c>ComponentGroup</c> are sorted by their file's <c>Source</c>, then their
    /// directory, and <c>File</c> elements in a <c>Component</c> by <c>Source</c>, as IsWiX's Files and Folders
    /// designer sorts them.
    /// </description></item>
    /// </list>
    /// </remarks>
    public static class CanonicalOrder
    {
        /// <summary>Creates IsWiX's canonical ordering. Each call returns a new instance, which can be extended.</summary>
        /// <returns>The ordering.</returns>
        public static WixOrdering Create()
        {
            var ordering = new WixOrdering()
                .OrderChildren(Package.ElementName, Names(
                    "SummaryInformation", "SoftwareTag", "MediaTemplate", "MajorUpgrade", "Launch",
                    "AppId", "Binary", "ComplianceCheck",
                    "Component", "ComponentGroup", "ComponentGroupRef", "ComponentRef", "File", "Files",
                    "CustomAction", "CustomActionRef", "CustomTable", "CustomTableRef",
                    "StandardDirectory", "Directory", "DirectoryRef",
                    "EmbeddedChainer", "EmbeddedChainerRef", "EnsureTable",
                    "Feature", "FeatureGroupRef", "FeatureRef",
                    "Icon", "InstanceTransforms", "Media", "PackageCertificates", "PatchCertificates",
                    "SetDirectory", "Property", "PropertyRef", "SetProperty", "SFPCatalog", "SymbolPath",
                    "UI", "UIRef", "Upgrade", "Requires", "WixVariable",
                    "InstallExecuteSequence", "InstallUISequence", "AdminExecuteSequence", "AdminUISequence", "AdvertiseExecuteSequence"))
                .OrderChildren(Fragment.ElementName, Names(
                    "MediaTemplate", "Launch",
                    "AppId", "Binary", "ComplianceCheck",
                    "Component", "ComponentGroup", "File", "Files",
                    "CustomAction", "CustomActionRef", "CustomTable", "CustomTableRef",
                    "StandardDirectory", "Directory", "DirectoryRef",
                    "EmbeddedChainer", "EmbeddedChainerRef", "EnsureTable",
                    "Feature", "FeatureGroup", "FeatureRef",
                    "Icon", "Media", "PackageCertificates", "PatchCertificates", "PatchFamily",
                    "SetDirectory", "Property", "PropertyRef", "SetProperty", "SFPCatalog",
                    "UI", "UIRef", "Upgrade", "Requires", "WixVariable",
                    "InstallExecuteSequence", "InstallUISequence", "AdminExecuteSequence", "AdminUISequence", "AdvertiseExecuteSequence"));

            foreach (var root in new[] { Package.ElementName, Fragment.ElementName })
            {
                ordering
                    .SortBy(root, Property.ElementName, e => new[] { Attribute(e, "Id") })
                    .SortBy(root, Feature.ElementName, e => new[] { Attribute(e, "Id") });
            }

            return ordering
                .SortBy(ComponentGroup.ElementName, Component.ElementName, e => new[]
                {
                    Attribute(e.Element(File.ElementName), "Source"),
                    Path.Combine(Attribute(e, "Directory"), Attribute(e, "Subdirectory")),
                })
                .SortBy(Component.ElementName, File.ElementName, e => new[] { Attribute(e, "Source") });
        }

        private static XName[] Names(params string[] localNames) => localNames.Select(n => WixNamespaces.Wxs + n).ToArray();

        private static string Attribute(XElement? element, string name) => (string?)element?.Attribute(name) ?? string.Empty;
    }
}
