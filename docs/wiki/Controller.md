# Controller

GUO plays with a gamepad on every platform, including the Windows desktop,
where upstream ClassicUO has no gamepad support at all. The controller is on
by default. To turn it off, untick Options > **Use a controller**; a pad then
does nothing.

## Hot swap

GUO follows whichever input you used last, the moment you use it, the way
console-style PC games do. There is no setting to flip.

| You use | GUO switches to | What changes |
|---|---|---|
| A pad button, or a stick pushed past halfway | Gamepad | The mouse cursor hides; the right stick moves the pointer; on-screen hints show your pad's buttons. |
| A key, a mouse button, or the mouse moved 4 px or more | Keyboard and mouse | The normal cursor and keyboard hints. |
| A finger on the screen | Touch | The touch layer ([Mobile UI](Mobile-UI.md)). |

## Default layout

Buttons are named by what's **printed** on your pad. GUO works out the layout
from the pad's name, so an Xbox A and a PlayStation Cross both confirm. Every
job except walking and the pointer can be moved to another input with
**Set controls** (below).

| Control | Action |
|---|---|
| D-pad or left stick | Walk |
| Right stick | Move the pointer |
| A (Cross) | Use: left click at the pointer (confirm, pick, use) |
| B (Circle) | Cancel: Escape (a target cursor, a text field, a menu) |
| X (Square) | Attack your last target. On mobile layouts (touch screens), the window menu (size, lock, which screen) for the topmost window instead |
| Y (Triangle) | Open or close the macro row when that row is on the touch bar; otherwise the Macros screen |
| LB | Target last (sends an open target cursor to your last target) |
| RB | Next hostile (selects the next hostile as your target) |
| LT, held | The menu wheel |
| RT, held | The interact radar |
| L3 (left stick click) | Always run, on or off |
| R3 (right stick click) | War mode, on or off |
| Start / Menu / Options | The options |
| Back / Select / View | Open or close the one-screen drawer |

While the window menu is open, the D-pad moves between its controls instead
of walking, A presses the selected control, and B closes it.

## The menu wheel (LT)

Hold LT and eight windows ring your character, drawn in the client's own
art, while the game keeps running. Push either stick toward one (the middle
is "none"): it lights gold and steps out. Let go of LT to open it.

Each window is a full-screen controller screen: the D-pad moves, A acts,
B closes, LB and RB change page. No mouse. The pack, the paperdoll, skills
and the spellbook still ask the shard for what it owns (the pack's contents,
the paperdoll, skill values, the book), but the screen is up at once from
what the client already has. Chat, the quest log, the guild and the mini map
are not on the wheel: those calls only asked the shard, and nothing opened
when it did not answer. The status bar is the same when it is already on
screen, so Status is a screen of the character's numbers instead.

| Slice | Opens |
|---|---|
| Top | Backpack |
| Top right | Paperdoll |
| Right | Journal |
| Bottom right | Skills |
| Bottom | Spellbook |
| Bottom left | World map |
| Left | Macros |
| Top left | Options |

- Let go with the stick in the middle: nothing opens.
- A quick **tap** of LT (under 0.2 s, no stick) opens the last window the
  wheel opened again.
- B while the wheel is up closes it without opening anything. B on a screen
  closes the screen.
- Which window sits in which slice is chosen at the end of Set controls.
  Status and Party are offered too.

## The interact radar (RT)

Hold RT and everything usable within 10 tiles is marked on the ground:
people, monsters and animals, doors, corpses, containers and movable items.
Hostiles (criminals, murderers, enemies) are marked red, everything else gold.

- **Right stick:** the choice jumps to the nearest thing the way you push.
- **LB / RB:** step to the previous / next thing, nearest first.
- The choice gets the client's own highlight, the target brackets of the
  "new target system", a name plate, and a card at the foot of the screen
  saying what each button does:

| Button (default) | With a choice |
|---|---|
| A | Use (a double click; in war mode on a mobile, an attack, as the client does) |
| X | Look (a single click: its name) |
| Y | Its context menu |
| B | Close the radar |
| Let go of RT | Use, if you chose something and pressed nothing; otherwise nothing |

**Target cursors.** When a spell, skill or item puts up a target cursor, the
radar is how a pad picks the target: hold RT, choose, and A sends the target
to the choice; B cancels the cursor (and leaves the radar up).

The reach is `radarRange` in `padbindings.json` (1 to 24 tiles, 10 by default).

## Set controls

Options > Video > Controller buttons > **Set controls...**, and offered once
after your first login with a pad (B on that card skips it; it is not offered
again).

1. Every job is a row: the action, the glyph for what it is set to, and
   that input's name. Nothing else. The order is Use, Cancel, Attack last,
   Target last, Toggle war mode, Next hostile, Always run, Macro row, Menu
   wheel, Interact radar, Options, Drawer. The row being set is highlighted.
2. **Press and hold** the button, trigger or stick direction you want for
   the highlighted row. The meter fills while exactly one input is held
   (0.7 s); then it is taken and the next row lights. Let go before the
   next one counts.
3. Then the wheel's eight slices: choose each one's window with the D-pad and A.
4. Everything is applied together at the end. Giving one input to a job takes
   it away from any other job.

**Cancel:** hold three or more inputs at once, at any point. Nothing changes.

Triggers and stick directions count as inputs (on past 60%, off under 30%).
A stick direction bound to a job no longer walks or moves the pointer that way.

The choices are saved in `padbindings.json` beside `settings.json`, so they
belong to this install, not to one character.

## Button glyphs

On-screen hints use glyphs for your pad's family (Xbox, PlayStation, Nintendo,
Steam Deck or generic), from the CC0 Kenney Input Prompts set. If GUO can't
tell the layout, it shows no glyph, and the face buttons wait until you pick
the layout.

## Handhelds

- **AYN Thor** (dual screen): the built-in pad works in both Standard and Xbox
  mode. In Xbox mode GUO undoes the pad's A/B and X/Y swap.
- **Steam Deck:** see [Steam Deck](Steam-Deck.md). The right trackpad
  works as the mouse at all times, alongside the pad: Steam Input sends it
  as mouse motion and its click as a left click. On the Deck (and in any run
  Steam launched, or Game Mode) with a pad connected, moving or clicking the
  mouse moves and shows the one shared pointer but does not switch the hints
  away from the pad; only a real key does. The right stick and the trackpad
  move the same pointer, whichever moved last wins. While LT or RT is held,
  the trackpad does not change the wheel's slice or the radar's choice.
- **Android:** see [Android Build](Android-Build.md) and [Dual Screen](Dual-Screen.md).

## Full-screen screens and the half-cut camera

When a menu-wheel window is open it is a controller screen on the **right**
half of the client. The world camera is cut to the **left** half so the player
sprite stays visible (Diablo-style). Closing the screen restores the normal
camera.

The **backpack** screen upscales the client's open-pack gump (`0x003C`) and
lays the pack's items in a grid you move with the D-pad or left stick. Quick
actions: **A** use, **X** drop, **Y** equip, **Back** context menu, **B** close.

The **paperdoll** screen upscales the client's paperdoll base (`0x07d0`) the
same way: grid of worn slots, **A** use, **X** unequip into the pack, **Y**
use again, **Back** context menu, **B** close.

### Scale

Each fullscreen controller panel can change its **menu scale** (the content
inside the stone frame) without moving the frame: the frame stays glued to
the right half of the client; only the content scales from the top-left
inside it, so nothing drifts or clips the border. **Menu font** is per panel
family (backpack, paperdoll, skills, …). **Journal font** (chat text) is a
separate scale and does not follow the menu font. Options on the Options
screen cycles each for a quick check: Menu scale, Menu font, Journal font.


## Gamepad mode hides the PC UI

While the input mode is **Gamepad**, classic mouse gumps (the pack window,
paperdoll gump, top bar, and the rest of the ClassicUO UI stack) are not
drawn. The touch command bar is hidden too. The world viewport still draws.
The pad's own screens (wheel, radar, PadScreen, Set controls) are Godot
layers and stay. Switching back to keyboard/mouse or touch shows the PC UI
again; nothing was disposed.

## What the controller covers

Honest list of player-facing UO systems. "Bound" means a pad path exists
today (wheel screen, radar, or a default button). "Not yet" means a player
still needs mouse/touch or it is unfinished. Second-player systems are
marked separately: the client can open them, but a probe cannot pass them
against one shard character.

| System | Controller | Notes |
|---|---|---|
| Walk / run | Bound | D-pad / left stick; L3 always-run |
| Use / double-click | Bound | A at pointer; radar A; backpack/paperdoll A |
| Cancel / close | Bound | B |
| Attack last | Bound | X (default); radar A in war mode |
| War mode | Bound | R3 |
| Target last / next hostile | Bound | LB / RB |
| Interact (look, use, context) | Bound | RT radar: X look, A use, Y context |
| Backpack | Bound | LT wheel → backpack screen (grid, drop/equip/context) |
| Paperdoll / equipment | Bound | LT wheel → paperdoll screen |
| Journal | Bound | LT wheel → journal screen |
| Skills (use skill) | Bound | LT wheel → skills screen (A uses a clickable skill) |
| Spells / spellbook | Bound | LT wheel → spellbook screen (A casts) |
| Macros | Bound | Y / wheel Macros screen |
| Options / Set controls | Bound | Start; Options screen; Set controls wizard |
| Status (vitals) | Bound | Wheel Status screen (numbers) |
| Party list | Bound | Wheel Party screen (roster only) |
| World map (nearby) | Bound | Wheel World map screen |
| Loot corpse / open container | Bound | Radar use / double-click; loot dummy probe |
| Talk / speech | Bound | Not a dedicated pad keyboard in-world yet; probe types speech; OSK is pregame |
| Context menus | Bound | Radar Y; backpack/paperdoll Back |
| Vendors (buy/sell) | Partial | Radar can use a vendor; no dedicated vendor pad screen |
| Banking | Partial | Radar can use a banker/bank box; no dedicated bank pad screen |
| Stealing / pickpocket | Partial | Skills screen can fire Stealing; target via radar/target cursor |
| Trade | Client only | Needs a second player; client can open trade, not a world pass alone |
| Party invite | Client only | Client opens a target; needs a second player to accept |
| Guild | Not yet | No pad screen; classic guild gump is mouse UI |
| Quests log | Not yet | Removed from the wheel (shard packet only) |
| Chat / messenger | Not yet | No pad screen |
| Housing / customization | Not yet | Mouse UI |
| Character creation / login | Bound | Pregame 3D pad path (`--pregame3d-probe`) |


## D-pad and chord map

What the pad does **today**, by context. A cell marked **not bound** has no
action. Holds that are not listed do not open a menu. This is the map; it is
not a promise of later chords.

### World (no screen, no wheel, no radar)

| Input | Tap | Hold | Right stick while held |
|---|---|---|---|
| D-pad up | Walk north | Keep walking north. Does **not** open a menu (**not bound**) | Pointer, as usual (the hold is not a mode) |
| D-pad down | Walk south | Keep walking south (**not bound** as a menu) | Pointer |
| D-pad left | Walk west | Keep walking west (**not bound** as a menu) | Pointer |
| D-pad right | Walk east | Keep walking east (**not bound** as a menu) | Pointer |
| Left stick | Same as the D-pad | Same | Pointer |

### LT — menu wheel

| Input | Tap | Hold | Right stick while held |
|---|---|---|---|
| LT | Reopen the last wheel window (under 0.2 s, stick centred) | The eight-slice wheel. Release opens the lit slice. Stick in the middle: nothing. B closes without opening | Points at a slice, same as the left stick. D-pad does **not** steer the wheel (**not bound**) |

Slices (default): up Backpack, up-right Paperdoll, right Journal, down-right Skills, down Spellbook, down-left World map, left Macros, up-left Options. Status and Party are offered in Set controls.

### A wheel window (backpack, paperdoll, journal, …)

The world camera is the left half. The screen is the right half.

| Input | Tap | Hold | Right stick while held |
|---|---|---|---|
| D-pad up / down / left / right | Move the focus (a grid on backpack and paperdoll; a list elsewhere) | Repeat is **not bound** (one step per press). Left stick repeats | **Not bound** (pointer is off while the screen is up) |
| A | Use / select the focus | — | — |
| B | Close the screen (camera returns) | — | — |
| X | Backpack: drop. Paperdoll: unequip to the pack. Other screens: **not bound** | — | — |
| Y | Backpack / paperdoll: equip / use. Other screens: **not bound** (does not open the macro row) | — | — |
| Back | Backpack / paperdoll: context menu. Other screens: **not bound** | — | — |
| LB / RB | Previous / next page, where the screen has pages | — | — |

### RT — interact radar

| Input | Tap | Hold | Right stick while held |
|---|---|---|---|
| RT | — | Radar of things in range. Release with a choice and no button: use it. Release with nothing chosen: nothing | Snaps the choice toward the push. LB / RB step. A use, X look, Y context menu, B close. Left stick still walks |

### Other chords (not D-pad)

| Input | What it does | Not bound |
|---|---|---|
| L3 | Always run | — |
| R3 | War mode. After Set controls moves war mode, R3 is the macro row | — |
| Start | Options | — |
| X (world) | Attack last | — |
| Y (world) | Macro row, or the Macros screen | — |
| LB / RB (world) | Target last / next hostile | — |
| A / B (world) | Use at the pointer / cancel | — |

The character-swapper flyout (below) is **not bound** in any of these contexts.

## Note for Moshu — multibox / character swapper

One UO connection is one character. Multibox means **several clients**, not a zoomed-out view of one world. Do not build a fake camera pull-back for this.

The engine (not this client pass) would need, for the player's other characters:

- a list, each with position
- what they are doing now
- auto-behavior toggles
- assume control
- spectate

On the controller we **reserve** these jobs for that flyout. They are names only. They are not in Set controls, they do nothing in game, and they are **not** a world-test pass:

| Reserved job | Meant to do | Status |
|---|---|---|
| Open swapper | Open the flyout of your other characters | **Not bound.** No engine list |
| Move across paperdolls | D-pad / stick moves the highlight | **Not bound** |
| Change auto behavior | Cycle that character's auto-behavior | **Not bound.** No engine toggle |
| Control | Assume control of the highlighted character | **Not bound.** Needs another client/connection |
| Spectate | Watch the highlighted character | **Not bound.** Needs the engine |

## For developers


- The design record is ADR-0025 (`docs/architecture/ADR-0025-gamepad-on-by-default.md`).
- The code is `src/Input/Gamepad/GamepadInput.cs` and `src/Input/InputMode.cs`.
- The menu wheel, radar and Set controls are `src/Input/Gamepad/PadWheel.cs`,
  `PadRadar.cs`, `PadWizard.cs`; the action map is `PadBindings.cs`; their
  layer and art are `PadOverlay.cs`. Set controls ports the behaviour of
  Ghostroads' own "Set controls" (`game/ui/set_controls.gd`).
- `--gamepad-probe` checks the bindings with scripted pad events, and
  `--pad-wheels-probe` the wheel, the radar, the new buttons, Set controls and
  the trackpad, with a picture at each step (see
  [Scripted Runs and Probes](Scripted-Runs-and-Probes.md)).
