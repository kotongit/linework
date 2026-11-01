using System.ComponentModel;

namespace Linework.App.Library;

/// <summary>Views re-render when this says something changed. No finer grain, as SwiftUI's views read what they like.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void NotifyChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
}
