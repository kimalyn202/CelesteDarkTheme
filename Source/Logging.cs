namespace Celeste.Mod.DarkTheme;

static class Logging
{
    static string ModName => DarkThemeModule.Instance.Metadata.Name;
    public static void Log(string message) => Logger.Info(ModName, message);
    public static void Warn(string message) => Logger.Warn(ModName, message);
    public static void Error(string message) => Logger.Error(ModName, message);
}