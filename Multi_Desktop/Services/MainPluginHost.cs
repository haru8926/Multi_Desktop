using System;
using System.Collections.Generic;
using System.Windows;
using Multi_Desktop.PluginApi;

namespace Multi_Desktop.Services
{
    public class MainPluginHost : IPluginHost
    {
        private List<Tuple<string, Action>> _bufferedMenuItems = new();
        private List<UIElement> _bufferedTrayViews = new();

        public event Action<string, Action>? OnMenuItemAdded;
        public event Action<UIElement>? OnTrayPopupViewAdded;

        public void AddMenuItem(string header, Action onClick)
        {
            _bufferedMenuItems.Add(new Tuple<string, Action>(header, onClick));
            OnMenuItemAdded?.Invoke(header, onClick);
        }

        public void AddTrayPopupView(UIElement view)
        {
            _bufferedTrayViews.Add(view);
            OnTrayPopupViewAdded?.Invoke(view);
        }

        public void InjectBufferedUI(MenuBarWindow window)
        {
            foreach (var item in _bufferedMenuItems)
                window.InjectMenuItem(item.Item1, item.Item2);
            
            foreach (var view in _bufferedTrayViews)
                window.InjectTrayPopupView(view);
        }

        public void InvokeOnUIThread(Action action)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(action);
        }
    }
}
