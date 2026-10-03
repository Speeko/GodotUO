// SPDX-License-Identifier: BSD-2-Clause
// GUO addition, not a port: upstream ClassicUO has no gamepad.

using System;
using System.Collections.Generic;
using Godot;
using GUO.Input.Touch;
using GUO.Pregame3D;

namespace GUO.Input.Gamepad
{
    /// <summary>
    /// The pad's own layer over the world: the menu wheel (<see cref="PadWheel"/>),
    /// the interact radar's marks and card (<see cref="PadRadar"/>) and "Set
    /// controls" (<see cref="PadWizard"/>). One CanvasLayer under the client's
    /// GameController, as the window menu has; nothing here takes a Godot
    /// event (GameController marks every one handled, ADR-0006), the pad
    /// drives it all through GamepadInput.
    /// </summary>
    /// <remarks>
    /// Drawn in the client's own art (docs/ui/uo_godot_style.md): the stone and
    /// parchment ResizePics, font 1 at a whole-number scale (<see cref="UoTheme.PixelScale"/>),
    /// item and gump art, all sampled nearest-neighbour (AGENTS.md rule 7). The
    /// art is asked for when the world is entered and nothing is shown until
    /// all of it is in (<see cref="PadArt.Warm"/>), the pregame's rule: never
    /// a flat stand-in.
    /// </remarks>
    internal sealed partial class PadOverlay : Node
    {
        private static PadOverlay _instance;

        private CanvasLayer _layer;

        /// <summary>Window pixels, unscaled: the radar's marks over the world.</summary>
        public Control Screen { get; private set; }

        /// <summary>Art pixels: everything built from the client's art, scaled by <see cref="UiScale"/>.</summary>
        public Control Ui { get; private set; }

        public static int UiScale => Math.Max(1, UoTheme.PixelScale);

        /// <summary>The layer, made on first use (null before the client exists).</summary>
        public static PadOverlay Get()
        {
            if (_instance != null && GodotObject.IsInstanceValid(_instance))
            {
                return _instance;
            }

            if (Client.Game == null)
            {
                return null;
            }

            _instance = new PadOverlay { Name = "PadOverlay" };
            Client.Game.AddChild(_instance);
            return _instance;
        }

        public override void _Ready()
        {
            // Above the command bar (10) and the pregame (70), under the window menu (100).
            _layer = new CanvasLayer { Layer = 90 };
            AddChild(_layer);

            Screen = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
            _layer.AddChild(Screen);

            Ui = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
            _layer.AddChild(Ui);
            Fit();
        }

        public override void _Process(double delta) => Fit();

        private void Fit()
        {
            Vector2 size = GetViewport().GetVisibleRect().Size;
            int s = UiScale;
            Screen.Position = Vector2.Zero;
            Screen.Size = size;
            Ui.Scale = new Vector2(s, s);
            Ui.Position = Vector2.Zero;
            Ui.Size = size / s;

            if (Ui.Theme == null && UoTheme.Ready)
            {
                Ui.Theme = UoTheme.Theme;
            }
        }

        /// <summary>The window size in art pixels (what <see cref="Ui"/> lays out in).</summary>
        public Vector2 UiSize => Ui.Size;

        /// <summary>Client pixels to <see cref="Ui"/>'s art pixels.</summary>
        private static float ClientToUi => (Client.Game?.DpiScale ?? 1f) / UiScale;

        /// <summary>The client's area (its ClientBounds) in art pixels: what the cards centre in.</summary>
        public static Rect2 ClientRect
        {
            get
            {
                Compat.Rectangle b = Client.Game.ClientBounds;
                float k = ClientToUi;
                return new Rect2(0, 0, b.Width * k, b.Height * k);
            }
        }

        /// <summary>The world view (the camera's bounds) in art pixels; the client's area outside the world.</summary>
        public static Rect2 WorldRect
        {
            get
            {
                Renderer.Camera camera = Client.Game?.Scene?.Camera;

                if (camera == null || camera.Bounds.Width <= 0)
                {
                    return ClientRect;
                }

                float k = ClientToUi;
                return new Rect2(camera.Bounds.X * k, camera.Bounds.Y * k, camera.Bounds.Width * k, camera.Bounds.Height * k);
            }
        }
    }

    /// <summary>
    /// The art the pad's layer draws, read once from the client: gumps (the
    /// frames, the target brackets, the bar lines) and item art (the wheel's
    /// icons). Images are copied out of the client's atlas on the main thread,
    /// as UoTheme.GumpImage does; one that reads back blank is asked for again.
    /// </summary>
    internal static class PadArt
    {
        // The new target system's brackets (HealthLinesManager): over and under a mobile.
        public const ushort BracketTop = 0x756D, BracketBottom = 0x756A;

        // The status bar's lines (HealthBarGump): a meter's track and fill.
        public const ushort LineTrack = 0x0805, LineFill = 0x0809;

        private static readonly Dictionary<ushort, Texture2D> _art = new();
        private static bool _ready;

        /// <summary>The item art for each wheel window: what it is in the world.</summary>
        public static ushort Icon(WheelWindow w) => w switch
        {
            WheelWindow.Backpack => 0x0E75,  // a backpack
            WheelWindow.Paperdoll => 0x1517, // a shirt
            WheelWindow.Journal => 0x0FF2,   // a book
            WheelWindow.Skills => 0x0E34,    // a blank scroll
            WheelWindow.Spellbook => 0x0EFA, // a spellbook
            WheelWindow.WorldMap => 0x14EB,  // a map
            WheelWindow.Macros => 0x1F14,    // a recall rune
            WheelWindow.Options => 0x1EB8,   // a tool kit
            WheelWindow.Status => 0x0E21,    // a bandage
            WheelWindow.Party => 0x0E79,     // a pouch
            _ => 0x0E75,
        };

        private static IEnumerable<ushort> Gumps()
        {
            foreach (ushort first in new[] { Overlay.Stone, Overlay.Parchment })
            {
                for (int i = 0; i < 9; i++)
                {
                    yield return (ushort) (first + i);
                }
            }

            yield return UoTheme.ButtonPlate;
            yield return BracketTop;
            yield return BracketBottom;
            yield return LineTrack;
            yield return LineFill;
        }

        /// <summary>Asks for every piece; true once all of them are in. Cheap once true.</summary>
        public static bool Warm()
        {
            if (_ready)
            {
                return true;
            }

            if (Client.Game?.UO?.Gumps == null || Client.Game.UO.Arts == null || !UoTheme.Ready)
            {
                return false;
            }

            bool all = true;

            foreach (ushort id in Gumps())
            {
                all &= PregameAssets.Gump(id) != null;
            }

            foreach (WheelWindow w in Enum.GetValues<WheelWindow>())
            {
                all &= Art(Icon(w)) != null;
            }

            _ = UoTheme.Font;
            _ready = all;

            if (_ready)
            {
                GD.Print("[GUO] pad: wheel and radar art ready");
            }

            return _ready;
        }

        public static bool Ready => _ready;

        public static Texture2D Gump(ushort id) => PregameAssets.Texture(id);

        /// <summary>An item's art as a texture of its own, or null while it is not readable yet.</summary>
        public static Texture2D Art(ushort graphic)
        {
            if (_art.TryGetValue(graphic, out Texture2D t))
            {
                return t;
            }

            ref readonly Renderer.SpriteInfo info = ref Client.Game.UO.Arts.GetArt(graphic);

            if (info.Texture == null)
            {
                return null;
            }

            Image atlas = info.Texture.GetImage();

            if (atlas == null)
            {
                return null;
            }

            if (atlas.GetFormat() != Image.Format.Rgba8)
            {
                atlas.Convert(Image.Format.Rgba8);
            }

            Image image = atlas.GetRegion(new Rect2I(info.UV.X, info.UV.Y, info.UV.Width, info.UV.Height));

            // As UoTheme.GumpImage: art just packed can read back blank until its upload lands.
            if (image.IsInvisible())
            {
                return null;
            }

            t = ImageTexture.CreateFromImage(image);
            _art[graphic] = t;
            return t;
        }

        /// <summary>
        /// The input that does a job, as the player sees it: the face buttons
        /// and Back as their glyphs (Input.Glyphs, the pad's family), anything
        /// else (a shoulder, a trigger, a stick click) as its name on a small
        /// parchment plate.
        /// </summary>
        public static Control Cap(PadInput? input)
        {
            if (input is PadInput i && i.AsButton is JoyButton b)
            {
                string name = b == JoyButton.Back
                    ? "pad_back"
                    : Glyphs.InputGlyphs.NameFor(PadAction.Confirm, InputKind.Gamepad, InputMode.PadFamily, b);
                Texture2D glyph = Glyphs.InputGlyphs.Load(name);

                if (glyph != null)
                {
                    TextureRect pic = Pic(glyph);
                    pic.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                    return pic;
                }
            }

            // The top bar's marble plate (UoTheme.Plate), captioned in ink.
            var plate = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            StyleBox box = (StyleBox) UoTheme.Plate().Duplicate();

            if (box is StyleBoxTexture t)
            {
                t.ContentMarginLeft = t.ContentMarginRight = 7;
            }

            plate.AddThemeStyleboxOverride("panel", box);
            Label caption = Overlay.Text(input is PadInput j ? Short(j) : "-", UoTheme.Ink);
            caption.AddThemeFontOverride("font", UoTheme.PlateFont);
            plate.AddChild(caption);
            return plate;
        }

        /// <summary>A cap's words: "LB", "RT", "L3", "LS left".</summary>
        public static string Short(PadInput i) => i.IsAxis && i.Index <= (int) JoyAxis.RightY
            ? (i.Index <= (int) JoyAxis.LeftY ? "LS " : "RS ") + i.Label.Substring(i.Label.LastIndexOf(' ') + 1)
            : i.Label;

        /// <summary>A texture drawn nearest at its own pixels.</summary>
        public static TextureRect Pic(Texture2D t, Color? tint = null)
        {
            var r = new TextureRect
            {
                Texture = t,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                StretchMode = TextureRect.StretchModeEnum.KeepCentered,
            };

            if (tint is Color c)
            {
                r.Modulate = c;
            }

            return r;
        }
    }
}
