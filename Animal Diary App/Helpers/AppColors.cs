namespace Animal_Diary_App.Helpers;

/// <summary>
/// The one way code resolves a colour token from <c>Resources/Styles/Colors.xaml</c>.
///
/// <para>XAML uses <c>{StaticResource X}</c>; drawables, ViewModel presentation hints
/// and hand-built controls can't, so they come through here. Three near-identical
/// copies of this lookup used to exist and one of them used the raw indexer
/// (<c>Resources["MGreat"]</c>), which throws <see cref="KeyNotFoundException"/> when a
/// token is renamed, so the same rename degraded gracefully in two places and crashed
/// in the third. Always returns; never throws.</para>
/// </summary>
public static class AppColors
{
    /// <summary>The token's colour, or <paramref name="fallback"/> when the key is
    /// missing, isn't a <see cref="Color"/>, or there is no application (unit tests,
    /// early startup).</summary>
    public static Color Resolve(string key, Color? fallback = null) =>
        TryResolve(key, out var color) ? color : (fallback ?? Colors.Transparent);

    public static bool TryResolve(string key, out Color color)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color c)
        {
            color = c;
            return true;
        }

        color = Colors.Transparent;
        return false;
    }
}
