using System;
using System.Linq;

// One-call versions of common authoring patterns, on the generated element classes. Each builds its elements
// completely before adding them, so with an ordering set they land at their canonical position.
namespace IsWiX.WiXModel
{
    public sealed partial class ComponentGroup
    {
        /// <summary>
        /// Adds a component that installs one file as its key path:
        /// <c>&lt;Component Directory Subdirectory&gt;&lt;File Source KeyPath="yes" /&gt;&lt;/Component&gt;</c>.
        /// </summary>
        /// <param name="source">The file's source path, such as <c>$(var.SourceDir)\app.exe</c>.</param>
        /// <param name="directory">The directory Id; <see langword="null"/> uses the group's <c>Directory</c>.</param>
        /// <param name="subdirectory">A path below <paramref name="directory"/>, or <see langword="null"/>.</param>
        /// <returns>The file; its component is <see cref="Core.WixElement.Parent"/>.</returns>
        public File AddFile(string source, string? directory = null, string? subdirectory = null)
        {
            if (string.IsNullOrEmpty(source))
            {
                throw new ArgumentException("A file needs a source.", nameof(source));
            }

            var component = new Component { Directory = directory, Subdirectory = NullIfEmpty(subdirectory) };
            var file = component.Add(new File { Source = source, KeyPath = true });
            Add(component);
            return file;
        }

        /// <summary>
        /// Adds a component that creates an empty folder:
        /// <c>&lt;Component Directory Subdirectory Guid KeyPath="yes"&gt;&lt;CreateFolder /&gt;&lt;/Component&gt;</c>.
        /// A folder has no file to derive a component GUID from, so it gets one.
        /// </summary>
        /// <param name="directory">The directory Id.</param>
        /// <param name="subdirectory">A path below <paramref name="directory"/>, or <see langword="null"/>.</param>
        /// <param name="componentGuid">The component GUID; <see langword="null"/> generates a new one.</param>
        /// <returns>The component.</returns>
        public Component AddFolder(string directory, string? subdirectory = null, Guid? componentGuid = null)
        {
            if (string.IsNullOrEmpty(directory))
            {
                throw new ArgumentException("A folder needs a directory.", nameof(directory));
            }

            var component = new Component
            {
                Directory = directory,
                Subdirectory = NullIfEmpty(subdirectory),
                Guid = (componentGuid ?? System.Guid.NewGuid()).ToString(),
                KeyPath = true,
            };
            component.Add(new CreateFolder());
            return Add(component);
        }

        private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
    }

    public sealed partial class File
    {
        /// <summary>Adds a shortcut to this file, such as a Start menu entry.</summary>
        /// <param name="name">The shortcut's name.</param>
        /// <param name="directory">Where it goes, such as <c>ProgramMenuFolder</c> or <c>DesktopFolder</c>.</param>
        /// <param name="subdirectory">A folder below <paramref name="directory"/>, or <see langword="null"/>.</param>
        /// <param name="advertise">Whether the shortcut is advertised (the default, as IsWiX writes them).</param>
        /// <returns>The shortcut.</returns>
        public Shortcut AddShortcut(string name, string directory, string? subdirectory = null, bool advertise = true)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A shortcut needs a name.", nameof(name));
            }

            if (string.IsNullOrEmpty(directory))
            {
                throw new ArgumentException("A shortcut needs a directory.", nameof(directory));
            }

            return Add(new Shortcut
            {
                Name = name,
                Directory = directory,
                Subdirectory = string.IsNullOrEmpty(subdirectory) ? null : subdirectory,
                Advertise = advertise,
            });
        }
    }

    public sealed partial class Component
    {
        /// <summary>
        /// Installs the component's executable as a Windows service: a <c>ServiceInstall</c> (own process, starts
        /// automatically, normal error control) and a <c>ServiceControl</c> that starts it on install, and stops
        /// and removes it on install and uninstall, waiting for each.
        /// </summary>
        /// <param name="name">The service name.</param>
        /// <param name="displayName">The name shown in Services; <see langword="null"/> leaves it unset.</param>
        /// <param name="description">The service description, or <see langword="null"/>.</param>
        /// <returns>The <c>ServiceInstall</c>; the <c>ServiceControl</c> follows it.</returns>
        public ServiceInstall AddService(string name, string? displayName = null, string? description = null)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A service needs a name.", nameof(name));
            }

            var install = Add(new ServiceInstall
            {
                Name = name,
                DisplayName = displayName,
                Description = description,
                ErrorControl = ServiceInstallErrorControl.Normal,
                Start = ServiceInstallStart.Auto,
                Type = ServiceInstallType.OwnProcess,
            });
            Add(new ServiceControl
            {
                Name = name,
                Start = InstallUninstallType.Install,
                Stop = InstallUninstallType.Both,
                Remove = InstallUninstallType.Both,
                Wait = true,
            });
            return install;
        }
    }

    public sealed partial class Feature
    {
        /// <summary>Makes the feature install a component group, unless it already does.</summary>
        /// <param name="id">The component group's Id.</param>
        /// <returns>The reference, new or existing.</returns>
        public ComponentGroupRef AddComponentGroupRef(string id) =>
            ComponentGroupRefs.FirstOrDefault(r => r.Id == id) ?? Add(new ComponentGroupRef { Id = RequireId(id) });

        /// <summary>Makes the feature install a merge module, unless it already does.</summary>
        /// <param name="id">The <c>Merge</c> element's Id.</param>
        /// <returns>The reference, new or existing.</returns>
        public MergeRef AddMergeRef(string id) =>
            MergeRefs.FirstOrDefault(r => r.Id == id) ?? Add(new MergeRef { Id = RequireId(id) });

        private static string RequireId(string id) =>
            string.IsNullOrEmpty(id) ? throw new ArgumentException("A reference needs an Id.", nameof(id)) : id;
    }

    public sealed partial class Package
    {
        /// <summary>
        /// Sets a property's value, adding the property if the package doesn't define it. Only the
        /// <c>Value</c> attribute changes on an existing property. (Not to be confused with the
        /// <c>&lt;SetProperty&gt;</c> element, a custom action.)
        /// </summary>
        /// <param name="id">The property Id.</param>
        /// <param name="value">The value; <see langword="null"/> removes the <c>Value</c> attribute.</param>
        /// <returns>The property.</returns>
        public Property SetPropertyValue(string id, string? value) => Authoring.SetPropertyValue(Properties, Add, id, value);
    }

    public sealed partial class Fragment
    {
        /// <summary>
        /// Sets a property's value, adding the property if the fragment doesn't define it. Only the
        /// <c>Value</c> attribute changes on an existing property.
        /// </summary>
        /// <param name="id">The property Id.</param>
        /// <param name="value">The value; <see langword="null"/> removes the <c>Value</c> attribute.</param>
        /// <returns>The property.</returns>
        public Property SetPropertyValue(string id, string? value) => Authoring.SetPropertyValue(Properties, Add, id, value);
    }

    internal static class Authoring
    {
        public static Property SetPropertyValue(System.Collections.Generic.IEnumerable<Property> properties, Func<Property, Property> add, string id, string? value)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("A property needs an Id.", nameof(id));
            }

            var property = properties.FirstOrDefault(p => p.Id == id);
            if (property is null)
            {
                return add(new Property { Id = id, Value = value });
            }

            if (property.Value != value)
            {
                property.Value = value;
            }

            return property;
        }
    }
}
