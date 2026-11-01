using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Linework.App.Views;

/// <summary>A view that draws itself again when what it reads changes, at most once a frame, as a SwiftUI view does.</summary>
public abstract class StoreView : UserControl
{
    readonly INotifyPropertyChanged[] _sources;
    bool _pending;

    protected StoreView(params INotifyPropertyChanged?[] sources)
    {
        _sources = sources.OfType<INotifyPropertyChanged>().ToArray();
        Loaded += (_, _) =>
        {
            Theme.Tokens.Changed += Invalidate;
            foreach (var source in _sources) source.PropertyChanged += OnChanged;
            Invalidate();
        };
        Unloaded += (_, _) =>
        {
            Theme.Tokens.Changed -= Invalidate;
            foreach (var source in _sources) source.PropertyChanged -= OnChanged;
        };
    }

    void OnChanged(object? sender, PropertyChangedEventArgs e) => Invalidate();

    public void Invalidate()
    {
        if (_pending) return;
        _pending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _pending = false;
            Refresh();
        });
    }

    protected void Refresh()
    {
        if (ShouldRender()) Content = Render();
    }

    /// <summary>False keeps what's showing, for example while a field in it has focus.</summary>
    protected virtual bool ShouldRender() => true;

    protected abstract UIElement Render();
}
