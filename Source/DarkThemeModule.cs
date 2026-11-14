using Celeste.Mod;
using Iced.Intel;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Celeste.Mod.DarkTheme;

public class DarkThemeModule : EverestModule
{
    public static DarkThemeModule Instance { get; private set; } = null!;
    public DarkThemeModule() { Instance = this; }

    public override Type SettingsType => typeof(DarkThemeSettings);
    public static DarkThemeSettings Settings => (DarkThemeSettings)Instance._Settings;

    static SpriteTheme CurrentTheme => Settings?.Theme ?? SpriteTheme.Light;

    static List<Hook> PageRedrawHooks;
    static int HookDepth = 0;

    #region Hooks 
    static void TrackRender(ILContext il)
    {
        ILCursor cursor = new(il);
        cursor.EmitDelegate(SetTargetUI);

        while (cursor.TryGotoNext(MoveType.Before, instr => instr.MatchRet()))
        {
            cursor.MoveAfterLabels();
            cursor.EmitDelegate(UnsetTargetUI);
            cursor.Index++;
        }

        static void SetTargetUI() => HookDepth++;
        static void UnsetTargetUI() => HookDepth--;
    }

    static void ForceRecolorCPUIlabel(ILContext il)
    {
        ILCursor cursor = new(il);

        if(cursor.TryGotoNext(MoveType.After,
            instr => instr.MatchCall<Color>("get_Black"),
            instr => instr.MatchLdcR4(0.8f),
            instr => instr.MatchCall<Color>("op_Multiply")))
        {
            cursor.EmitLdcI4(1);
            cursor.EmitDelegate<Func<Color, bool, Color>>(Fix);
        }
    }

    static void DrawSV2C(On.Celeste.ActiveFont.orig_Draw_string_Vector2_Color orig,
        string text, Vector2 pos, Color col) => orig(text, pos, Fix(col));

    static void DrawSV2V2V2C(On.Celeste.ActiveFont.orig_Draw_string_Vector2_Vector2_Vector2_Color orig,
        string text, Vector2 pos, Vector2 justify, Vector2 scale, Color col) => orig(text, pos, justify, scale, Fix(col));
        
    static void DrawSV2V2V2CFCFC(On.Celeste.ActiveFont.orig_Draw_string_Vector2_Vector2_Vector2_Color_float_Color_float_Color orig,
        string text, Vector2 pos, Vector2 justify, Vector2 scale, Color col, float edge, Color edgeCol, float stroke, Color strokeCol)
            => orig(text, pos, justify, scale, Fix(col), edge, edgeCol, stroke, strokeCol);

    static void CharDraw(On.Celeste.FancyText.Char.orig_Draw orig,
        FancyText.Char self, PixelFont font, float baseSize, Vector2 position, Vector2 scale, float alpha)
    {
        Color preserve = self.Color;
        self.Color = Fix(self.Color);

        orig(self, font, baseSize, position, scale, alpha);

        self.Color = preserve;
    }

    #endregion

    static Color Fix(Color c, bool evenLowBrightness = false)
    {
        if (CurrentTheme == SpriteTheme.Light
            || (!evenLowBrightness && CurrentTheme == SpriteTheme.LowBrightness)
            || HookDepth == 0)

            return c;

        int max = Math.Max(c.R, Math.Max(c.G, c.B));
        int min = Math.Min(c.R, Math.Min(c.G, c.B));

        if (c.A == 0 || max > c.A || max + min >= c.A)
            return c;

        int delta = c.A - max - min;
        return new Color(c.R + delta, c.G + delta, c.B + delta, c.A);
    }


    public override void Load()
    {
        On.Celeste.ActiveFont.Draw_string_Vector2_Color += DrawSV2C;
        On.Celeste.ActiveFont.Draw_string_Vector2_Vector2_Vector2_Color += DrawSV2V2V2C;
        On.Celeste.ActiveFont.Draw_string_Vector2_Vector2_Vector2_Color_float_Color_float_Color += DrawSV2V2V2CFCFC;

        IL.Celeste.OuiFileSelectSlot.Render += TrackRender;
        IL.Celeste.OuiChapterPanel.Render += TrackRender;
        IL.Celeste.OuiChapterPanel.Render += ForceRecolorCPUIlabel;

        On.Celeste.FancyText.Char.Draw += CharDraw;
    }

    public override void Unload()
    {
        On.Celeste.ActiveFont.Draw_string_Vector2_Color -= DrawSV2C;
        On.Celeste.ActiveFont.Draw_string_Vector2_Vector2_Vector2_Color -= DrawSV2V2V2C;
        On.Celeste.ActiveFont.Draw_string_Vector2_Vector2_Vector2_Color_float_Color_float_Color -= DrawSV2V2V2CFCFC;

        IL.Celeste.OuiFileSelectSlot.Render -= TrackRender;
        IL.Celeste.OuiChapterPanel.Render -= TrackRender;
        IL.Celeste.OuiChapterPanel.Render -= ForceRecolorCPUIlabel;

        On.Celeste.FancyText.Char.Draw -= CharDraw;

        PageRedrawHooks?.ForEach(hook => hook.Dispose());
    }

    public override void Initialize()
    {
        PageRedrawHooks = new();

        HashSet<MethodInfo> cached = new();
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray()!; }

            foreach (Type t in types)
            {
                if (t.IsAbstract || !typeof(OuiJournalPage).IsAssignableFrom(t))
                    continue;

                MethodInfo m = t.GetMethod(nameof(OuiJournalPage.Redraw),
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (m != null && m.DeclaringType != typeof(OuiJournalPage) && cached.Add(m))
                    PageRedrawHooks.Add(new Hook(m, TrackPageRedraw));
            }
        }

        static void TrackPageRedraw(Action<OuiJournalPage, VirtualRenderTarget> orig,
            OuiJournalPage self, VirtualRenderTarget buffer)
        {
            HookDepth++;
            try { orig(self, buffer); }
            finally { HookDepth--; }
        }
    }


    public override void LoadContent(bool _)
    {
        SpriteSwapper.Reset();
        SpriteSwapper.Apply(CurrentTheme);
    }

    public override void CreateModMenuSection(TextMenu menu, bool inGame, FMOD.Studio.EventInstance snapshot)
    {
        CreateModMenuSectionHeader(menu, inGame, snapshot);

        TextMenu.Option<SpriteTheme> option = new("Theme");
        option.Add("Light theme", SpriteTheme.Light, Settings.Theme == SpriteTheme.Light);
        option.Add("Low brightness", SpriteTheme.LowBrightness, Settings.Theme == SpriteTheme.LowBrightness);
        option.Add("Inverted theme", SpriteTheme.Inverted, Settings.Theme == SpriteTheme.Inverted);
        option.Add("Aurafarm", SpriteTheme.Aurafarm, Settings.Theme == SpriteTheme.Aurafarm);
        
        option.Change(value =>
        {
            Settings.Theme = value;
            SpriteSwapper.Apply(value);
        });

        menu.Add(option);
    }


#if DEBUG

    [Command("ren", "")]
    public static void LogEnts()
    {
        Logger.Error("", "SDKFJDKSLFS");
        foreach (Entity e in Engine.Scene.Entities)
        {
            if (e.Visible) Logger.Warn("", e.GetType().FullName ?? "null");
        }
    }
    [Command("col", "")]
    public static void CheckColor(float r, float g, float b, float a)
    {
        HookDepth++;
        Celeste.Commands.Log(Fix(new(r, g, b, a)).ToString(), Color.White);
        HookDepth--;
    }
    [Command("t", "")]
    public static void LogTheme()
    {
        Celeste.Commands.Log(CurrentTheme, Color.White);
    }

#endif
}