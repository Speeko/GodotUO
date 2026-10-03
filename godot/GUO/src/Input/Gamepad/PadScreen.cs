// SPDX-License-Identifier: BSD-2-Clause
// GUO addition, not a port: upstream ClassicUO has no gamepad.

using System;
using System.Collections.Generic;
using Godot;
using GUO.Compat;
using GUO.Configuration;
using GUO.Game;
using GUO.Game.Data;
using GUO.Game.GameObjects;
using GUO.Game.Managers;
using GUO.Game.UI.Gumps;
using GUO.Input.Touch;
using GUO.Pregame3D;
using GUO.Utility.Collections;

namespace GUO.Input.Gamepad
{
    /// <summary>
    /// A menu-wheel window as a controller screen (Steam Deck, 1280x800, no
    /// mouse). D-pad / left stick moves, A acts, B closes, LB/RB change page.
    /// The backpack and paperdoll are the client's own gumps upscaled: a big
    /// open pack with an item grid, and a big paperdoll with worn slots. Other
    /// windows stay a list. While any screen is up the world camera is cut to
    /// the left half (Diablo-style) so the player stays visible beside the UI.
    /// </summary>
    internal static class PadScreen
    {
        private sealed class Line
        {
            public string Label;
            public string Detail;
            public ushort Art;
            public uint Serial;
            public bool Wearable;
            public Action Act;
            public Action Drop;
            public Action Equip;
            public Action Context;
        }

        private static readonly string[] Maps =
        {
            "Felucca", "Trammel", "Ilshenar", "Malas", "Tokuno", "Ter Mur",
        };

        private static readonly Layer[] Worn =
        {
            Layer.OneHanded, Layer.TwoHanded, Layer.Helmet, Layer.Earrings, Layer.Necklace,
            Layer.Ring, Layer.Bracelet, Layer.Torso, Layer.Arms, Layer.Gloves, Layer.Tunic,
            Layer.Shirt, Layer.Robe, Layer.Cloak, Layer.Waist, Layer.Pants, Layer.Skirt,
            Layer.Legs, Layer.Shoes, Layer.Talisman, Layer.Mount,
        };

        private const ushort PackGump = 0x003C;   // open backpack container art
        private const ushort DollGump = 0x07d0;   // player paperdoll base
        private const int PackCols = 5;
        private const int DollCols = 3;
        private const float StickRepeat = 0.22f;

        private static Control _root;
        private static PanelContainer _frame;
        private static Control _stage;
        private static TextureRect _hero;
        private static GridContainer _grid;
        private static VBoxContainer _list;
        private static HBoxContainer _hints;
        private static Label _title, _pageLabel, _detail;
        private static TextureRect _icon;
        private static int _focus, _page, _pages = 1, _top;
        private static int _sig = int.MinValue;
        private static int _cols = 1;
        private static float _stickWait;
        private static int _stickDx, _stickDy;
        private static readonly List<Line> _lines = new();

        /// <summary>Whole-number scale of the menu panel's content (1–3). Frame stays edge-anchored.</summary>
        public static int PanelScale { get; private set; } = 1;

        /// <summary>Font scale for menu panels (backpack, paperdoll, skills, …), 1–3.</summary>
        public static int MenuFontScale { get; private set; } = 1;

        /// <summary>Font scale for journal/chat text only, 1–3 — independent of menu fonts.</summary>
        public static int ChatFontScale { get; private set; } = 1;

        private static Control _content;
        private static Vector2 _frameSize;

        private static bool _cut;
        private static Point _savedPos, _savedSize;
        private static bool _savedFull;

        public static bool IsOpen { get; private set; }

        public static WheelWindow? Current { get; private set; }

        public static int RowCount => _lines.Count;

        public static int Focus => _focus;

        public static int Columns => _cols;

        /// <summary>The screen covers the client's area (world left, menu right).</summary>
        public static bool FillsClient
        {
            get
            {
                if (!IsOpen || _root == null || !GodotObject.IsInstanceValid(_root) || !_root.Visible)
                {
                    return false;
                }

                Rect2 area = PadOverlay.ClientRect;

                return _root.Size.X >= area.Size.X - 2f && _root.Size.Y >= area.Size.Y - 2f && _root.Size.X > 64f;
            }
        }

        /// <summary>True while the world camera is cut to the left half.</summary>
        public static bool HalfCut => _cut;

        public static void Open(WheelWindow w)
        {
            World world = Client.Game?.UO?.World;

            if (world == null || !world.InGame || world.Player == null)
            {
                return;
            }

            if (PadWizard.IsOpen)
            {
                return;
            }

            IsOpen = true;
            Current = w;
            _page = 0;
            _focus = 0;
            _top = 0;
            _sig = int.MinValue;
            _stickWait = 0f;
            _stickDx = _stickDy = 0;
            Ask(world, w);
            ApplyHalfCut();
            Show();
            Tick();
            GD.Print($"[GUO] pad screen: {w}");
        }

        public static void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;
            Current = null;
            _lines.Clear();
            RestoreCamera();

            if (_root != null && GodotObject.IsInstanceValid(_root))
            {
                _root.Visible = false;
            }
        }

        /// <summary>D-pad / stick: move in the list or the grid.</summary>
        public static void Move(int dx, int dy)
        {
            if (!IsOpen || _lines.Count == 0)
            {
                return;
            }

            if (dx == 0 && dy == 0)
            {
                return;
            }

            if (_cols > 1)
            {
                int cols = _cols;
                int rows = (_lines.Count + cols - 1) / cols;
                int x = _focus % cols;
                int y = _focus / cols;
                x = (x + dx + cols) % cols;
                y = (y + dy + rows) % rows;
                int next = y * cols + x;

                if (next >= _lines.Count)
                {
                    next = Math.Min(_lines.Count - 1, y * cols + (_lines.Count - 1) % cols);
                }

                _focus = next;
            }
            else
            {
                int d = dy != 0 ? dy : dx;
                _focus = (_focus + d + _lines.Count) % _lines.Count;
            }

            Paint();
        }

        /// <summary>Legacy 1D step used by older call sites.</summary>
        public static void Move(int d) => Move(0, d);

        /// <summary>Left stick while the screen is up: walk the focus with a repeat.</summary>
        public static void Steer(float lx, float ly, float dt)
        {
            if (!IsOpen || _lines.Count == 0)
            {
                return;
            }

            const float dead = 0.55f;
            int dx = lx < -dead ? -1 : lx > dead ? 1 : 0;
            int dy = ly < -dead ? -1 : ly > dead ? 1 : 0;

            if (dx == 0 && dy == 0)
            {
                _stickWait = 0f;
                _stickDx = _stickDy = 0;

                return;
            }

            if (dx != _stickDx || dy != _stickDy)
            {
                _stickDx = dx;
                _stickDy = dy;
                _stickWait = 0f;
                Move(dx, dy);

                return;
            }

            _stickWait += dt;

            if (_stickWait >= StickRepeat)
            {
                _stickWait = 0f;
                Move(dx, dy);
            }
        }

        /// <summary>LB / RB.</summary>
        public static void Page(int d)
        {
            if (!IsOpen || _pages < 2)
            {
                return;
            }

            _page = (_page + d + _pages) % _pages;
            _focus = 0;
            _top = 0;
            _sig = int.MinValue;
            Tick();
        }

        public static void Activate()
        {
            if (!IsOpen || _focus < 0 || _focus >= _lines.Count)
            {
                return;
            }

            _lines[_focus].Act?.Invoke();
            _sig = int.MinValue;
        }

        /// <summary>X: drop the focused item (or put a worn one in the pack).</summary>
        public static void Drop()
        {
            if (!IsOpen || _focus < 0 || _focus >= _lines.Count)
            {
                return;
            }

            _lines[_focus].Drop?.Invoke();
            _sig = int.MinValue;
        }

        /// <summary>Y: equip the focused pack item, or use a worn one.</summary>
        public static void Equip()
        {
            if (!IsOpen || _focus < 0 || _focus >= _lines.Count)
            {
                return;
            }

            Line line = _lines[_focus];
            (line.Equip ?? line.Act)?.Invoke();
            _sig = int.MinValue;
        }

        /// <summary>Back: the client's context menu for the focused thing.</summary>
        public static void Context()
        {
            if (!IsOpen || _focus < 0 || _focus >= _lines.Count)
            {
                return;
            }

            _lines[_focus].Context?.Invoke();
        }

        /// <summary>True when this screen shows item quick-actions (pack / doll).</summary>
        public static bool HasItemActions =>
            Current is WheelWindow.Backpack or WheelWindow.Paperdoll;

        public static void CyclePanelScale() => PanelScale = PanelScale >= 3 ? 1 : PanelScale + 1;

        public static void CycleMenuFont() => MenuFontScale = MenuFontScale >= 3 ? 1 : MenuFontScale + 1;

        public static void CycleChatFont() => ChatFontScale = ChatFontScale >= 3 ? 1 : ChatFontScale + 1;

        /// <summary>Once a frame while it is up: refill when the world changed, keep the half-cut and layout.</summary>
        public static void Tick()
        {
            if (!IsOpen)
            {
                return;
            }

            if (_root == null || !GodotObject.IsInstanceValid(_root))
            {
                Show();
            }

            if (_root == null)
            {
                return;
            }

            if (!_cut)
            {
                ApplyHalfCut();
            }

            int sig = Signature();

            if (sig != _sig)
            {
                _sig = sig;
                Rebuild();
                Paint();
            }

            Layout();
        }

        // --- what the client already knows -------------------------------------------------------

        private static void Ask(World world, WheelWindow w)
        {
            switch (w)
            {
                case WheelWindow.Backpack:
                    GameActions.OpenBackpack(world);
                    break;
                case WheelWindow.Paperdoll:
                    GameActions.OpenPaperdoll(world, world.Player);
                    break;
                case WheelWindow.Skills:
                    GameActions.OpenSkills(world);
                    break;
                case WheelWindow.Spellbook:
                    PadWheel.RunMacro(world, MacroType.Open, MacroSubType.MageSpellbook);
                    break;
            }
        }

        private static void Rebuild()
        {
            _lines.Clear();
            _pages = 1;
            _cols = 1;
            World world = Client.Game?.UO?.World;
            PlayerMobile player = world?.Player;

            if (player == null || Current is not WheelWindow w)
            {
                return;
            }

            switch (w)
            {
                case WheelWindow.Backpack: Pack(player); break;
                case WheelWindow.Paperdoll: Doll(player); break;
                case WheelWindow.Journal: Journal(); break;
                case WheelWindow.Skills: Skills(player); break;
                case WheelWindow.Spellbook: Spells(); break;
                case WheelWindow.WorldMap: Map(world, player); break;
                case WheelWindow.Macros: Macros(world); break;
                case WheelWindow.Options: Options(world); break;
                case WheelWindow.Status: Status(world, player); break;
                case WheelWindow.Party: Party(world); break;
            }

            if (_lines.Count == 0)
            {
                _lines.Add(new Line { Label = "Empty" });
            }

            _focus = Math.Clamp(_focus, 0, _lines.Count - 1);
        }

        private static void Pack(PlayerMobile player)
        {
            _cols = PackCols;
            Item pack = player.FindItemByLayer(Layer.Backpack);

            if (pack == null)
            {
                return;
            }

            for (LinkedObject o = pack.Items; o != null; o = o.Next)
            {
                if (o is not Item item || item.IsDestroyed)
                {
                    continue;
                }

                Item held = item;
                string name = string.IsNullOrEmpty(item.Name) ? item.ItemData.Name : item.Name;
                bool wear = item.ItemData.IsWearable;
                _lines.Add(new Line
                {
                    Label = string.IsNullOrEmpty(name) ? "Item" : name,
                    Detail = item.Amount > 1 ? item.Amount.ToString() : (wear ? "equip" : ""),
                    Art = item.Graphic,
                    Serial = item.Serial,
                    Wearable = wear,
                    Act = () => GameActions.DoubleClick(Client.Game.UO.World, held.Serial),
                    Drop = () => DropHeld(held),
                    Equip = wear ? () => EquipHeld(held) : null,
                    Context = () => GameActions.OpenPopupMenu(held.Serial, true),
                });
            }

            _lines.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
        }

        private static void Doll(PlayerMobile player)
        {
            _cols = DollCols;

            foreach (Layer layer in Worn)
            {
                Item item = player.FindItemByLayer(layer);

                if (item == null || item.IsDestroyed)
                {
                    _lines.Add(new Line { Label = layer.ToString(), Detail = "-" });
                    continue;
                }

                Item held = item;
                string name = string.IsNullOrEmpty(item.Name) ? item.ItemData.Name : item.Name;
                _lines.Add(new Line
                {
                    Label = string.IsNullOrEmpty(name) ? layer.ToString() : name,
                    Detail = layer.ToString(),
                    Art = item.Graphic,
                    Serial = item.Serial,
                    Wearable = true,
                    Act = () => GameActions.DoubleClick(Client.Game.UO.World, held.Serial),
                    Drop = () => UnequipToPack(held),
                    Equip = () => GameActions.DoubleClick(Client.Game.UO.World, held.Serial),
                    Context = () => GameActions.OpenPopupMenu(held.Serial, true),
                });
            }
        }

        private static void DropHeld(Item item)
        {
            World world = Client.Game?.UO?.World;
            PlayerMobile player = world?.Player;

            if (world == null || player == null || item == null || item.IsDestroyed)
            {
                return;
            }

            if (!GameActions.PickUp(world, item.Serial, 0, 0, item.Amount))
            {
                return;
            }

            GameActions.DropItem(item.Serial, player.X, player.Y, player.Z, 0);
        }

        private static void EquipHeld(Item item)
        {
            World world = Client.Game?.UO?.World;

            if (world == null || item == null || item.IsDestroyed || !item.ItemData.IsWearable)
            {
                return;
            }

            if (!GameActions.PickUp(world, item.Serial, 0, 0, item.Amount))
            {
                return;
            }

            GameActions.Equip(world);
        }

        private static void UnequipToPack(Item item)
        {
            World world = Client.Game?.UO?.World;
            PlayerMobile player = world?.Player;
            Item pack = player?.FindItemByLayer(Layer.Backpack);

            if (world == null || pack == null || item == null || item.IsDestroyed)
            {
                return;
            }

            if (!GameActions.PickUp(world, item.Serial, 0, 0, item.Amount))
            {
                return;
            }

            GameActions.DropItem(item.Serial, 0xFFFF, 0xFFFF, 0, pack.Serial);
        }

        private static void Journal()
        {
            Deque<JournalEntry> entries = JournalManager.Entries;
            int start = Math.Max(0, entries.Count - 80);

            for (int i = entries.Count - 1; i >= start; i--)
            {
                JournalEntry e = entries[i];
                _lines.Add(new Line
                {
                    Label = string.IsNullOrEmpty(e.Name) ? e.Text : e.Name + ": " + e.Text,
                    Detail = e.Time.ToString("HH:mm"),
                });
            }
        }

        private static void Skills(PlayerMobile player)
        {
            _pages = 2;
            bool known = _page == 0;

            foreach (Skill skill in player.Skills)
            {
                if (skill == null || string.IsNullOrEmpty(skill.Name))
                {
                    continue;
                }

                if (known && skill.ValueFixed <= 0 && skill.BaseFixed <= 0)
                {
                    continue;
                }

                Skill held = skill;
                _lines.Add(new Line
                {
                    Label = skill.Name,
                    Detail = $"{skill.Value:0.0}",
                    Act = skill.IsClickable ? () => GameActions.UseSkill(held.Index) : null,
                });
            }
        }

        private static void Spells()
        {
            var pages = new List<(string name, IEnumerable<SpellDefinition> spells)>();

            for (int circle = 0; circle < 8; circle++)
            {
                int first = circle * 8 + 1;
                var list = new List<SpellDefinition>();

                for (int id = first; id < first + 8; id++)
                {
                    if (SpellsMagery.GetAllSpells.TryGetValue(id, out SpellDefinition spell))
                    {
                        list.Add(spell);
                    }
                }

                pages.Add(($"Circle {circle + 1}", list));
            }

            pages.Add(("Necromancy", SpellsNecromancy.GetAllSpells.Values));
            pages.Add(("Chivalry", SpellsChivalry.GetAllSpells.Values));
            pages.Add(("Bushido", SpellsBushido.GetAllSpells.Values));
            pages.Add(("Ninjitsu", SpellsNinjitsu.GetAllSpells.Values));
            pages.Add(("Spellweaving", SpellsSpellweaving.GetAllSpells.Values));
            pages.Add(("Mysticism", SpellsMysticism.GetAllSpells.Values));
            _pages = pages.Count;
            _page = Math.Clamp(_page, 0, _pages - 1);

            foreach (SpellDefinition spell in pages[_page].spells)
            {
                if (spell == null || string.IsNullOrEmpty(spell.Name))
                {
                    continue;
                }

                SpellDefinition held = spell;
                _lines.Add(new Line
                {
                    Label = spell.Name,
                    Detail = spell.ManaCost > 0 ? spell.ManaCost.ToString() : "",
                    Act = () => GameActions.CastSpell(held.ID),
                });
            }
        }

        private static void Map(World world, PlayerMobile player)
        {
            string facet = world.MapIndex >= 0 && world.MapIndex < Maps.Length ? Maps[world.MapIndex] : world.MapIndex.ToString();
            _lines.Add(new Line { Label = player.Name ?? "You", Detail = $"{facet}  {player.X}, {player.Y}, {player.Z}" });

            foreach (Mobile mobile in world.Mobiles.Values)
            {
                if (mobile == null || mobile == player || mobile.IsDestroyed || mobile.Distance > 24)
                {
                    continue;
                }

                Mobile held = mobile;
                _lines.Add(new Line
                {
                    Label = string.IsNullOrEmpty(mobile.Name) ? "Someone" : mobile.Name,
                    Detail = $"{mobile.Distance} {Dir(player, mobile)}",
                    Act = () => GameActions.DoubleClick(world, held.Serial),
                    Context = () => GameActions.OpenPopupMenu(held.Serial, true),
                });
            }

            _lines.Sort((a, b) =>
            {
                if (a.Act == null) return -1;
                if (b.Act == null) return 1;
                return string.Compare(a.Detail, b.Detail, StringComparison.Ordinal);
            });
        }

        private static void Macros(World world)
        {
            foreach (Macro macro in world.Macros.GetAllMacros())
            {
                if (macro?.Items is not MacroObject first)
                {
                    continue;
                }

                MacroObject held = first;
                _lines.Add(new Line
                {
                    Label = string.IsNullOrEmpty(macro.Name) ? "Macro" : macro.Name,
                    Act = () =>
                    {
                        world.Macros.SetMacroToExecute(held);
                        Close();
                    },
                });
            }
        }

        private static void Options(World world)
        {
            _lines.Add(new Line
            {
                Label = "Set controls",
                Detail = "A",
                Act = () =>
                {
                    Close();
                    PadWizard.Open();
                },
            });
            _lines.Add(new Line
            {
                Label = "Radar",
                Detail = PadBindings.RadarRange.ToString(),
                Act = () =>
                {
                    int n = PadBindings.RadarRange;
                    PadBindings.RadarRange = n >= 24 ? 6 : n + 2;
                },
            });
            _lines.Add(new Line
            {
                Label = "Always run",
                Detail = ProfileManager.CurrentProfile != null && ProfileManager.CurrentProfile.AlwaysRun ? "On" : "Off",
                Act = () => PadWheel.RunMacro(world, MacroType.AlwaysRun),
            });
            _lines.Add(new Line
            {
                Label = "Options window",
                Act = () =>
                {
                    Close();
                    GameActions.OpenSettings(world);
                },
            });
            _lines.Add(new Line
            {
                Label = "Menu scale",
                Detail = PanelScale.ToString(),
                Act = () => { CyclePanelScale(); },
            });
            _lines.Add(new Line
            {
                Label = "Menu font",
                Detail = MenuFontScale.ToString(),
                Act = () => { CycleMenuFont(); },
            });
            _lines.Add(new Line
            {
                Label = "Journal font",
                Detail = ChatFontScale.ToString(),
                Act = () => { CycleChatFont(); },
            });
        }

        private static void Status(World world, PlayerMobile player)
        {
            Add("Hits", $"{player.Hits}/{player.HitsMax}");
            Add("Mana", $"{player.Mana}/{player.ManaMax}");
            Add("Stamina", $"{player.Stamina}/{player.StaminaMax}");
            Add("Strength", player.Strength.ToString());
            Add("Dexterity", player.Dexterity.ToString());
            Add("Intelligence", player.Intelligence.ToString());
            Add("Weight", $"{player.Weight}/{player.WeightMax}");
            Add("Gold", player.Gold.ToString());
            string facet = world.MapIndex >= 0 && world.MapIndex < Maps.Length ? Maps[world.MapIndex] : "";
            Add(string.IsNullOrEmpty(player.Name) ? "You" : player.Name, $"{facet} {player.X}, {player.Y}".Trim());
        }

        private static void Party(World world)
        {
            PartyMember[] members = world.Party?.Members;

            if (members == null)
            {
                return;
            }

            foreach (PartyMember member in members)
            {
                if (member == null || !SerialHelper.IsValid(member.Serial))
                {
                    continue;
                }

                Mobile mobile = world.Mobiles.Get(member.Serial);
                _lines.Add(new Line
                {
                    Label = member.Name ?? "Member",
                    Detail = mobile != null ? $"{mobile.Hits}/{mobile.HitsMax}" : "",
                });
            }
        }

        private static void Add(string label, string detail) => _lines.Add(new Line { Label = label, Detail = detail });

        private static string Dir(Mobile from, Mobile to)
        {
            int dx = to.X - from.X, dy = to.Y - from.Y;
            string x = dx > 1 ? "E" : dx < -1 ? "W" : "";
            string y = dy > 1 ? "S" : dy < -1 ? "N" : "";

            return (y + x).Length == 0 ? "here" : y + x;
        }

        private static int Signature()
        {
            int h = ((int)(Current ?? 0) * 397) ^ (_page * 17) ^ _lines.Count ^ (_cols << 9) ^ (PanelScale << 11) ^ (MenuFontScale << 13) ^ (ChatFontScale << 15);
            World world = Client.Game?.UO?.World;
            PlayerMobile player = world?.Player;

            if (player == null)
            {
                return h;
            }

            h = (h * 397) ^ player.Hits ^ (player.Mana << 8) ^ player.X ^ (player.Y << 4);
            Item pack = player.FindItemByLayer(Layer.Backpack);

            if (pack != null)
            {
                for (LinkedObject o = pack.Items; o != null; o = o.Next)
                {
                    if (o is Item item)
                    {
                        h = (h * 31) ^ (int)item.Serial ^ item.Amount;
                    }
                }
            }

            foreach (Layer layer in Worn)
            {
                Item item = player.FindItemByLayer(layer);
                h = (h * 17) ^ (item == null ? (int)layer : (int)item.Serial);
            }

            h ^= JournalManager.Entries.Count << 3;
            h ^= world.Macros.GetAllMacros().Count << 5;
            h ^= PadBindings.RadarRange << 7;

            return h;
        }

        // --- half-cut camera ---------------------------------------------------------------------

        private static void ApplyHalfCut()
        {
            Profile profile = ProfileManager.CurrentProfile;
            WorldViewportGump viewport = UIManager.GetGump<WorldViewportGump>();

            if (profile == null || viewport == null || Client.Game == null)
            {
                return;
            }

            if (!_cut)
            {
                _savedPos = profile.GameWindowPosition;
                _savedSize = profile.GameWindowSize;
                _savedFull = profile.GameWindowFullSize;
                _cut = true;
            }

            int clientW = Math.Max(1, Client.Game.ClientBounds.Width);
            int clientH = Math.Max(1, Client.Game.ClientBounds.Height);
            // Left half for the world (player visible); menu fills the right.
            int worldW = Math.Max(640, clientW / 2);
            int worldH = Math.Max(480, clientH);
            profile.GameWindowFullSize = false;
            profile.GameWindowPosition = new Point(0, 0);
            profile.GameWindowSize = new Point(worldW, worldH);
            viewport.SetGameWindowPosition(new Point(-WorldViewportGump.BORDER_WIDTH, -WorldViewportGump.BORDER_WIDTH));
            viewport.ResizeGameWindow(new Point(worldW, worldH));
        }

        private static void RestoreCamera()
        {
            if (!_cut)
            {
                return;
            }

            Profile profile = ProfileManager.CurrentProfile;
            WorldViewportGump viewport = UIManager.GetGump<WorldViewportGump>();
            _cut = false;

            if (profile == null)
            {
                return;
            }

            profile.GameWindowFullSize = _savedFull;
            profile.GameWindowPosition = _savedPos;
            profile.GameWindowSize = _savedSize;

            if (viewport == null || Client.Game == null)
            {
                return;
            }

            if (_savedFull)
            {
                viewport.ResizeGameWindow(new Point(Client.Game.ClientBounds.Width, Client.Game.ClientBounds.Height));
                viewport.SetGameWindowPosition(new Point(-WorldViewportGump.BORDER_WIDTH, -WorldViewportGump.BORDER_WIDTH));
            }
            else
            {
                viewport.SetGameWindowPosition(_savedPos);
                viewport.ResizeGameWindow(_savedSize);
            }
        }

        // --- the view ----------------------------------------------------------------------------

        private static void Show()
        {
            PadOverlay layer = PadOverlay.Get();

            if (layer == null || !PadArt.Warm())
            {
                return;
            }

            if (_root == null || !GodotObject.IsInstanceValid(_root))
            {
                Build(layer);
            }

            _root.Visible = true;
            Layout();
        }

        private static void Build(PadOverlay layer)
        {
            _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Name = "PadScreen" };
            layer.Ui.AddChild(_root);
            _frame = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            _frame.AddThemeStyleboxOverride("panel", Overlay.Frame(Overlay.Stone, 8));
            _frame.ClipContents = true;
            _root.AddChild(_frame);
            // Content scales inside the frame; the stone frame stays edge-anchored (no drift).
            _content = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Name = "PadScreenContent" };
            _frame.AddChild(_content);
            VBoxContainer col = Overlay.Column(4);
            col.Name = "PadScreenCol";
            _content.AddChild(col);

            HBoxContainer header = Overlay.Row(6);
            _icon = PadArt.Pic(null);
            _icon.CustomMinimumSize = new Vector2(36, 28);
            _icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            header.AddChild(_icon);
            _title = Overlay.Text("", UoTheme.Heading, 2); // scale refreshed in Paint
            _title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            header.AddChild(_title);
            _pageLabel = Overlay.Text("", UoTheme.Muted);
            header.AddChild(_pageLabel);
            col.AddChild(header);

            _stage = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
            _stage.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            col.AddChild(_stage);

            _hero = PadArt.Pic(null);
            _hero.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            _hero.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            _hero.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
            _stage.AddChild(_hero);

            _grid = new GridContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Columns = PackCols };
            _grid.AddThemeConstantOverride("h_separation", 4);
            _grid.AddThemeConstantOverride("v_separation", 4);
            _stage.AddChild(_grid);

            _list = Overlay.Column(1);
            _list.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            _stage.AddChild(_list);

            _detail = Overlay.Text("", UoTheme.Ink);
            _detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            col.AddChild(_detail);

            _hints = Overlay.Row(10);
            _hints.Alignment = BoxContainer.AlignmentMode.Center;
            col.AddChild(_hints);
        }

        private static int VisibleRows()
        {
            float h = _list?.Size.Y > 1f ? _list.Size.Y : (_root?.Size.Y ?? PadOverlay.ClientRect.Size.Y) * 0.5f;

            return Math.Max(6, (int)((h - 8f) / 16f));
        }

        private static void Paint()
        {
            if (_stage == null || !GodotObject.IsInstanceValid(_stage) || Current is not WheelWindow w)
            {
                return;
            }

            bool grid = w is WheelWindow.Backpack or WheelWindow.Paperdoll;
            _hero.Visible = grid;
            _grid.Visible = grid;
            _list.Visible = !grid;
            _icon.Texture = PadArt.Art(PadArt.Icon(w));
            _title.Text = PadBindings.Name(w);
            int titleScale = Math.Max(1, (Current == WheelWindow.Journal ? ChatFontScale : MenuFontScale) + 1);
            _title.AddThemeFontSizeOverride("font_size", Math.Max(1, UoTheme.FontSize * titleScale));
            _pageLabel.Text = _pages > 1 ? $"{_page + 1}/{_pages}" : $"{_focus + 1}/{_lines.Count}";

            if (_focus >= 0 && _focus < _lines.Count)
            {
                Line cur = _lines[_focus];
                _detail.Text = string.IsNullOrEmpty(cur.Detail) ? (cur.Label ?? "") : $"{cur.Label}  ·  {cur.Detail}";
            }
            else
            {
                _detail.Text = "";
            }

            PaintHints();

            if (grid)
            {
                PaintGrid(w);
            }
            else
            {
                PaintList();
            }
        }

        private static void PaintGrid(WheelWindow w)
        {
            foreach (Node n in _grid.GetChildren())
            {
                _grid.RemoveChild(n);
                n.QueueFree();
            }

            _grid.Columns = _cols;
            ushort gump = w == WheelWindow.Backpack ? PackGump : DollGump;
            Texture2D hero = PadArt.Gump(gump) ?? PadArt.Art(PadArt.Icon(w));
            _hero.Texture = hero;
            // Upscale the client's own art; nearest keeps the UO pixels sharp.
            float scale = w == WheelWindow.Backpack ? 2.5f : 2f;

            if (hero != null)
            {
                _hero.CustomMinimumSize = hero.GetSize() * scale;
            }

            for (int i = 0; i < _lines.Count; i++)
            {
                Line line = _lines[i];
                bool lit = i == _focus;
                var cell = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(48, 48) };
                cell.AddThemeStyleboxOverride("panel", lit
                    ? Overlay.Frame(Overlay.Parchment, 2, 1)
                    : Overlay.Frame(Overlay.Parchment, 2));
                VBoxContainer box = Overlay.Column(1);
                TextureRect art = PadArt.Pic(line.Art == 0 ? null : PadArt.Art(line.Art));
                art.CustomMinimumSize = new Vector2(40, 32);
                art.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
                art.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
                box.AddChild(art);
                Label label = Overlay.Text(Short(line.Label), lit ? UoTheme.Danger : UoTheme.Ink, FontScaleFor());
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.ClipText = true;
                label.CustomMinimumSize = new Vector2(44, 0);
                box.AddChild(label);
                cell.AddChild(box);
                _grid.AddChild(cell);
            }
        }

        private static void PaintList()
        {
            foreach (Node n in _list.GetChildren())
            {
                _list.RemoveChild(n);
                n.QueueFree();
            }

            int vis = VisibleRows();

            if (_focus < _top)
            {
                _top = _focus;
            }

            if (_focus >= _top + vis)
            {
                _top = _focus - vis + 1;
            }

            _top = Math.Max(0, _top);
            int end = Math.Min(_lines.Count, _top + vis);

            for (int i = _top; i < end; i++)
            {
                Line line = _lines[i];
                bool lit = i == _focus;
                var row = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(0, 15) };
                row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                row.AddThemeStyleboxOverride("panel", lit
                    ? Overlay.Frame(Overlay.Parchment, 2, 1)
                    : new StyleBoxEmpty { ContentMarginLeft = 2, ContentMarginRight = 2, ContentMarginTop = 1, ContentMarginBottom = 1 });
                HBoxContainer box = Overlay.Row(4);
                TextureRect art = PadArt.Pic(line.Art == 0 ? null : PadArt.Art(line.Art));
                art.CustomMinimumSize = new Vector2(22, 14);
                art.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
                box.AddChild(art);
                Label label = Overlay.Text(line.Label ?? "", lit ? UoTheme.Danger : UoTheme.Ink, FontScaleFor());
                label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                label.ClipText = true;
                box.AddChild(label);

                if (!string.IsNullOrEmpty(line.Detail))
                {
                    box.AddChild(Overlay.Text(line.Detail, UoTheme.Muted));
                }

                row.AddChild(box);
                _list.AddChild(row);
            }
        }

        private static void PaintHints()
        {
            if (_hints == null || !GodotObject.IsInstanceValid(_hints))
            {
                return;
            }

            foreach (Node n in _hints.GetChildren())
            {
                _hints.RemoveChild(n);
                n.QueueFree();
            }

            void Hint(PadCommand command, string caption)
            {
                HBoxContainer pair = Overlay.Row(4);
                pair.AddChild(PadArt.Cap(PadBindings.For(command)));
                pair.AddChild(Overlay.Text(caption, UoTheme.Muted));
                _hints.AddChild(pair);
            }

            Hint(PadCommand.Use, HasItemActions ? "Use" : "Select");
            Hint(PadCommand.Cancel, "Close");

            if (HasItemActions)
            {
                Hint(PadCommand.AttackLast, Current == WheelWindow.Paperdoll ? "Unequip" : "Drop");
                Hint(PadCommand.MacroRow, "Equip");
                Hint(PadCommand.Drawer, "More");
            }

            if (_pages > 1)
            {
                Hint(PadCommand.TargetLast, "Page");
                Hint(PadCommand.NextHostile, "Page");
            }
        }

        private static int FontScaleFor() =>
            Current == WheelWindow.Journal ? ChatFontScale : MenuFontScale;

        private static string Short(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return "";
            }

            return s.Length <= 8 ? s : s.Substring(0, 7) + "…";
        }

        private static void Layout()
        {
            if (_root == null || !GodotObject.IsInstanceValid(_root))
            {
                return;
            }

            Rect2 area = PadOverlay.ClientRect;
            _root.Position = area.Position.Round();
            _root.Size = area.Size;

            // Right half: the menu. Frame is edge-anchored; scale never moves it.
            float left = MathF.Floor(area.Size.X * 0.5f);
            Vector2 framePos = new Vector2(left, 0f);
            Vector2 frameSize = new Vector2(area.Size.X - left, area.Size.Y);
            _frame.Position = framePos;
            _frame.Size = frameSize;
            _frameSize = frameSize;

            if (_content != null && GodotObject.IsInstanceValid(_content))
            {
                float ps = Math.Clamp(PanelScale, 1, 3);
                _content.Scale = new Vector2(ps, ps);
                _content.Position = Vector2.Zero;
                // Lay out in unscaled art pixels so the frame still fits (no clipped border).
                _content.Size = new Vector2(frameSize.X / ps, frameSize.Y / ps);
            }

            if (_hero.Visible && _hero.Texture != null)
            {
                Vector2 want = _hero.CustomMinimumSize;
                float maxW = Math.Max(48f, _frame.Size.X - 24f);
                float maxH = Math.Max(48f, _frame.Size.Y * 0.42f);
                float s = Math.Min(1f, Math.Min(maxW / Math.Max(1f, want.X), maxH / Math.Max(1f, want.Y)));
                Vector2 size = want * s;
                _hero.Size = size;
                _hero.Position = new Vector2((_stage.Size.X - size.X) * 0.5f, 0f);
                float gridTop = size.Y + 6f;
                _grid.Position = new Vector2(4f, gridTop);
                _grid.Size = new Vector2(Math.Max(8f, _stage.Size.X - 8f), Math.Max(8f, _stage.Size.Y - gridTop));
            }
            else if (_list.Visible)
            {
                _list.Position = Vector2.Zero;
                _list.Size = _stage.Size;
            }
        }
    }
}
