using System;

namespace Multi_Desktop.PluginApi
{
    public interface IPlugin
    {
        string Name { get; }
        string Description { get; }
        string Version { get; }
        string Author { get; }

        void Initialize(IPluginHost host);
        void Shutdown();
    }
}
