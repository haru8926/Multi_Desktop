using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows;
using Multi_Desktop.PluginApi;

namespace Multi_Desktop.Services
{
    public class PluginLoadContext : AssemblyLoadContext
    {
        private AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string pluginPath) : base(isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            if (assemblyPath != null)
            {
                return LoadFromAssemblyPath(assemblyPath);
            }

            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (libraryPath != null)
            {
                return LoadUnmanagedDllFromPath(libraryPath);
            }

            return IntPtr.Zero;
        }
    }

    public class PluginManager
    {
        private List<IPlugin> _plugins = new List<IPlugin>();
        private readonly IPluginHost _pluginHost;

        public PluginManager(IPluginHost pluginHost)
        {
            _pluginHost = pluginHost;
        }

        public IReadOnlyList<IPlugin> Plugins => _plugins;

        public void LoadPlugins()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string pluginsDir = Path.Combine(appDir, "Plugins");

            // 開発環境用のフォールバック (bin/Release/net8.0-windows... などの外側の Plugins フォルダ)
            if (!Directory.Exists(pluginsDir))
            {
                string devPluginsDir = Path.Combine(appDir, "..", "..", "..", "Plugins");
                if (Directory.Exists(devPluginsDir))
                {
                    pluginsDir = Path.GetFullPath(devPluginsDir);
                }
                else
                {
                    Directory.CreateDirectory(pluginsDir);
                    return;
                }
            }

            // Each plugin should ideally be in its own subfolder to isolate dependencies
            var pluginFolders = Directory.GetDirectories(pluginsDir);

            foreach (var folder in pluginFolders)
            {
                var dllFiles = Directory.GetFiles(folder, "*.dll");
                
                foreach (var dll in dllFiles)
                {
                    LoadPlugin(dll);
                }
            }
        }

        private void LoadPlugin(string pluginPath)
        {
            try
            {
                var loadContext = new PluginLoadContext(pluginPath);
                
                // Try to load the assembly
                AssemblyName assemblyName = new AssemblyName(Path.GetFileNameWithoutExtension(pluginPath));
                Assembly assembly = loadContext.LoadFromAssemblyName(assemblyName);

                foreach (Type type in assembly.GetTypes())
                {
                    if (typeof(IPlugin).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
                    {
                        if (Activator.CreateInstance(type) is IPlugin pluginInstance)
                        {
                            try
                            {
                                pluginInstance.Initialize(_pluginHost);
                                _plugins.Add(pluginInstance);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Failed to initialize plugin '{type.Name}' from '{Path.GetFileName(pluginPath)}':\n{ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // This might not be a .NET assembly, or just failed to load.
                // We gracefully ignore it and log to debug.
                System.Diagnostics.Debug.WriteLine($"Failed to load assembly {pluginPath}: {ex.Message}");
            }
        }
        
        public void ShutdownPlugins()
        {
            foreach (var plugin in _plugins)
            {
                try
                {
                    plugin.Shutdown();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to shutdown plugin {plugin.Name}: {ex.Message}");
                }
            }
            _plugins.Clear();
        }
    }
}
