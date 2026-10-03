// SPDX-License-Identifier: BSD-2-Clause
// GUO addition, not a port: upstream ClassicUO has no gamepad.

using System;
using System.Collections.Generic;
using Godot;
using GUO.Configuration;
using GUO.Game;
using GUO.Game.Data;
using GUO.Game.GameObjects;
using GUO.Game.Managers;
using GUO.Input.Touch;
using GUO.Pregame3D;
using GUO.Utility.Collections;

namespace GUO.Input.Gamepad
{
    /// <summary>
    /// A menu-wheel window as a full-screen controller screen (Steam Deck,
    /// 1280x800, no mouse). D-pad moves, A acts, B closes, LB/RB change page.
    /// The wheel used to call the mouse windows, and several of those do
    /// nothing a player can see: the spellbook only asks the shard, skills
    /// wait for a packet, the status bar is skipped when it is already up,
    /// and chat, quests and the guild never draw a gump here. This draws the
    /// window itself from what the client already knows.
    /// </summary>
    internal static class PadScreen
    {
        private sealed class Line
        {
            public string Label;
            public string Detail;
            public ushort Art;
            public Action Act;
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

        private static Control _root;
        private static PanelContainer _frame;
        private static VBoxContainer _list;
        private static Label _title, _pageLabel;
        private static TextureRect _icon;
        private static int _focus, _page, _pages = 1, _top;
        private static int _sig = int.MinValue;
        private static readonly List<Line> _lines = new();

        public static bool IsOpen { get; private set; }

        public static WheelWindow? Current { get; private set; }

        public static int RowCount => _lines.Count;

        public static int Focus => _focus;

        /// <summary>The screen covers the client's area, not a floating card.</summary>
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
            Ask(world, w);
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

            if (_root != null && GodotObject.IsInstanceValid(_root))
            {
                _root.Visible = false;
            }
        }

        /// <summary>D-pad: -1 up, +1 down.</summary>
        public static void Move(int d)
        {
            if (!IsOpen || _lines.Count == 0)
            {
                return;
            }

            _focus = (_focus + d + _lines.Count) % _lines.Count;
            Paint();
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

        /// <summary>Once a frame while it is up: refill when the world changed, and keep the frame full-screen.</summary>
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

        /// <summary>Ask the shard for anything this window cannot invent (the pack's contents, skill values).</summary>
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
                    // The packet opens nothing until the shard answers. The screen does not wait for it.
                    PadWheel.RunMacro(world, MacroType.Open, MacroSubType.MageSpellbook);
                    break;
            }
        }

        private static void Rebuild()
        {
            _lines.Clear();
            _pages = 1;
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
                _lines.Add(new Line
                {
                    Label = string.IsNullOrEmpty(name) ? "Item" : name,
                    Detail = item.Amount > 1 ? item.Amount.ToString() : "",
                    Art = item.Graphic,
                    Act = () => GameActions.DoubleClick(Client.Game.UO.World, held.Serial),
                });
            }

            _lines.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
        }

        private static void Doll(PlayerMobile player)
        {
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
                    Act = () => GameActions.DoubleClick(Client.Game.UO.World, held.Serial),
                });
            }
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
            int h = ((int)(Current ?? 0) * 397) ^ (_page * 17) ^ _lines.Count;
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

            h ^= JournalManager.Entries.Count << 3;
            h ^= world.Macros.GetAllMacros().Count << 5;
            h ^= PadBindings.RadarRange << 7;

            return h;
        }

        // --- the view ---------------------------------------------------------------------------------

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
            _root.AddChild(_frame);
            VBoxContainer col = Overlay.Column(2);
            _frame.AddChild(col);

            HBoxContainer header = Overlay.Row(6);
            _icon = PadArt.Pic(null);
            _icon.CustomMinimumSize = new Vector2(28, 22);
            _icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            header.AddChild(_icon);
            _title = Overlay.Text("", UoTheme.Heading, 2);
            _title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            header.AddChild(_title);
            _pageLabel = Overlay.Text("", UoTheme.Muted);
            header.AddChild(_pageLabel);
            col.AddChild(header);

            _list = Overlay.Column(1);
            _list.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            col.AddChild(_list);
        }

        private static int VisibleRows()
        {
            float h = _root?.Size.Y ?? PadOverlay.ClientRect.Size.Y;

            return Math.Max(6, (int)((h - 36f) / 16f));
        }

        private static void Paint()
        {
            if (_list == null || !GodotObject.IsInstanceValid(_list) || Current is not WheelWindow w)
            {
                return;
            }

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
            _icon.Texture = PadArt.Art(PadArt.Icon(w));
            _title.Text = PadBindings.Name(w);
            _pageLabel.Text = _pages > 1 ? $"{_page + 1}/{_pages}" : $"{_focus + 1}/{_lines.Count}";

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
                Label label = Overlay.Text(line.Label ?? "", lit ? UoTheme.Danger : UoTheme.Ink);
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

        private static void Layout()
        {
            if (_root == null || !GodotObject.IsInstanceValid(_root))
            {
                return;
            }

            Rect2 area = PadOverlay.ClientRect;
            _root.Position = area.Position.Round();
            _root.Size = area.Size;
            _frame.Position = Vector2.Zero;
            _frame.Size = area.Size;
        }
    }
}
