using System;
using System.Windows;
using System.Windows.Controls;

namespace Multi_Desktop.PluginApi
{
    public interface IPluginHost
    {
        /// <summary>
        /// Adds a menu item to the main MenuBar context menu.
        /// </summary>
        /// <param name="header">The text displayed on the menu item.</param>
        /// <param name="onClick">The action to execute when clicked.</param>
        void AddMenuItem(string header, Action onClick);

        /// <summary>
        /// Adds a UI element to the Control Center (tray popup) view.
        /// </summary>
        /// <param name="view">The WPF UI element to embed.</param>
        void AddTrayPopupView(UIElement view);
        
        /// <summary>
        /// Gets the main synchronization context if the plugin needs to jump back to the UI thread.
        /// </summary>
        void InvokeOnUIThread(Action action);
    }
}
