.. _tut-windows:

***************
Your own window
***************

For more than a settings page, make a :cs:type:`~IronstrikeApi.Ui.Window`. It looks like the classic
Steam server browser: olive panels, beveled buttons, dense rows. It is built from a copy of the
game's own Options window, so it is placed, pointed at and clicked exactly like the game's menus,
in VR and flat.

.. code-block:: csharp

   using IronstrikeApi.Ui;

   var window = new Window("Arena");
   window.AddTab("Rules", page =>
   {
       page.Header("Arena rules");
       page.Toggle("Bots fight each other", chaos, v => chaos = v,
                   help: "Half the bots switch sides.");
       page.Stepper("Wave size", waveSize, 1, 1, 20, v => waveSize = (int)v, "0");
       page.Choice("Arena", arenas, arenaIndex, i => arenaIndex = i);
       page.TextField("Banner", banner, 24, s => banner = s);
       page.Buttons(("Start", StartArena), ("Stop", StopArena));
   });
   window.AddTab("Scores", page =>
   {
       page.Table(new[] { new TableColumn("Player", 3), new TableColumn("Kills"), new TableColumn("Deaths") },
                  scores.Select(s => new[] { s.Name, s.Kills.ToString(), s.Deaths.ToString() }).ToList());
   });

   MainMenu.AddButton("ARENA", () => window.Toggle(MainMenu.MenuAnchor));

How drawing works
=================

You never update a control. You describe the whole page in a render callback, and the window calls
it again whenever something changes: after any control is used, when you call
:cs:meth:`~IronstrikeApi.Ui.Window.Refresh`, and every
:cs:prop:`~IronstrikeApi.Ui.Window.AutoRefreshSeconds` if you set it. Keep your state in your own
fields, read it in the callback, and change it in the control callbacks.

This is the same idea as an "immediate mode" UI, and it means the window cannot show stale values.
It also means the ``Page`` you are given is only valid inside the callback.

Opening it
==========

* :cs:meth:`~IronstrikeApi.Ui.Window.Open` with :cs:prop:`~IronstrikeApi.Ui.MainMenu.MenuAnchor`
  opens it over the main menu; with no argument, in front of the player wherever they are.
* :cs:meth:`~IronstrikeApi.Ui.MainMenu.AddButton` puts a small pill on one of the main menu's cards.
  Call it once from ``Load()``; the API recreates the pill whenever the menu is rebuilt.
* One mod window is open at a time. Opening yours closes whatever was open.

Text entry
==========

:cs:meth:`~IronstrikeApi.Ui.Page.TextField` uses the game's own VR keyboard, the one Private Match
uses for codes. You can call it yourself with :cs:meth:`~IronstrikeApi.Ui.TextInput.Ask`. The window
steps aside while the keyboard is up.

Drawing something else
======================

:cs:meth:`~IronstrikeApi.Ui.Page.Custom` gives you a full-width strip of the page to draw in with
:cs:type:`~IronstrikeApi.Ui.Kit`, the same drawing kit the controls use: rects, labels, bevel frames,
pixel-art icons.

.. code-block:: csharp

   var strip = page.Custom(120);
   Kit.Frame(strip, Kit.Sunken);
   var bar = Kit.Rect(strip, "Bar", 0, 0, health / maxHealth, 1, 8, 8, 8, 8);
   Kit.Fill(bar, Kit.Good);
   Kit.Label(strip, $"{health:0} / {maxHealth:0}", 40, Kit.Text, TMPro.TextAlignmentOptions.Center);
