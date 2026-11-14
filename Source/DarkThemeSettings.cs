namespace Celeste.Mod.DarkTheme;

public class DarkThemeSettings : EverestModuleSettings
{
    [SettingIgnore]
    public SpriteTheme Theme { get; set; } = SpriteTheme.Light;
}