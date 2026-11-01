using Linework.App.Library;

namespace Linework.App.Views;

/// <summary>What views read, handed down from the app.</summary>
public sealed record LineworkContext(AppModel App, SessionStore Session)
{
    public Settings Settings => App.Settings;
}
