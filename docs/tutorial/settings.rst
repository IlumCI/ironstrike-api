.. _tut-settings:

****************************
Settings and the Mods window
****************************

Mods keep their settings in BepInEx config files, ``BepInEx/config/<guid>.cfg``. The API reads
every loaded mod's config and shows it in the **Mods window**, so players can change settings in the
game, in VR, without editing a file. Open it with the **MODS** button on the main menu's Credits
card, or :kbd:`F8`.

You get this for free. Bind your settings as usual:

.. code-block:: csharp

   speed = Config.Bind("General", "MoveSpeed", 1.25f,
       new ConfigDescription("Your move speed multiplier.",
                             new AcceptableValueRange<float>(0.5f, 3f)));
   greet = Config.Bind("General", "Greet", true, "Say hello to other players who have this mod.");

and they appear on your mod's page, under a **General** heading, as a stepper from 0.5 to 3 and a
check box. The first line of each description is shown under the setting's name.

How entries become controls
===========================

.. list-table::
   :header-rows: 1
   :widths: 40 60

   * - Setting type
     - Control
   * - ``bool``
     - check box
   * - ``int``, ``float``, ``double``
     - ``[<] value [>]`` stepper; about 20 steps across an ``AcceptableValueRange``
   * - any ``enum``
     - picker cycling through the names
   * - ``string`` with ``AcceptableValueList<string>``
     - picker cycling through the list
   * - other ``string``
     - text field, edited with the game's keyboard
   * - anything else
     - shown, not editable

Section names like ``"03 Host"`` are shown without the number, which only keeps the file in order.

Hiding and protecting entries
=============================

Some entries are not for players to touch, and some should not appear on a streamer's screen.

* Tag an entry :cs:field:`~IronstrikeApi.ModSettings.Hidden` to leave it out:

  .. code-block:: csharp

     Config.Bind("Debug", "Trace", false,
         new ConfigDescription("Log every frame.", null, ModSettings.Hidden));

* Tag it :cs:field:`~IronstrikeApi.ModSettings.Secret` to show dots instead of the text. Keys that
  look private (password, code, token, id) get dots anyway.
* Entries whose description starts with "Managed by the mod" are hidden.
* No ``SafeMode`` switch is ever shown: public play stays locked.

Your own rows
=============

:cs:meth:`~IronstrikeApi.ModSettings.AddSection` puts anything a :cs:type:`~IronstrikeApi.Ui.Page`
can draw at the top of your page, above the config entries:

.. code-block:: csharp

   ModSettings.AddSection("com.example.hellomod", page =>
   {
       page.Header("This run");
       page.Info("Kills", kills.ToString());
       page.Button("Reset counter", () => kills = 0);
   });

The page is rebuilt every time something on it is used, so it always shows current values.
