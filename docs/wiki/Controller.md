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
