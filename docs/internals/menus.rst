.. _internals-menus:

*****
Menus
*****

Menus are world-space canvases deriving from ``MenuBase`` (``Show(PositionType)``, ``Hide()``,
``currentlyOpenMenus``). ``PositionType`` is ``Static``, ``PlaceAtPlayer`` or ``FollowPlayer``.

Main menu
=========

``MainMenuUI``'s buttons, by the method each is wired to (**verified**):

.. list-table::
   :header-rows: 1
   :widths: 50 20 30

   * - Path under the menu
     - Label
     - Method
   * - ``Canvas/MainPanel/Options``
     - Options
     - ``PressOptions``
   * - ``Canvas/MainPanel/Credits``
     - Credits
     - ``PressCredits``
   * - ``.../PlayModes/Play``
     - Play
     - ``PressPlay`` (public quick match)
   * - ``.../PlayModes/Play/HostButton``
     - HOST
     - ``PressHost`` (public host)
   * - ``.../PlayModes/Private Match``
     - Private Match
     - ``PressPrivateMatch``
   * - ``.../PlayModes/Solo``
     - Solo
     - ``PressSolo``

``MainPanel`` has no layout group; anything added must be placed by hand. Read a button's wiring with
``onClick.GetPersistentEventCount()`` and ``GetPersistentMethodName(i)``.

Options menu
============

``OptionsMenuUI`` holds a ``Canvas`` with a ``MenuBox``: background art, a title, a back button, and a
``Scroll View`` whose content rows are instantiated when the menu opens. Its check box, stepper and
section-title rows are good templates. ``OptionsMenuUI.Show(null)`` throws: it needs the previous
menu.

:cs:type:`~IronstrikeApi.Ui.Window` copies this canvas, removes the menu logic and the art, and draws
its own.

Keyboard and notices
====================

``VRKeyboard.instance.Show(title, Action<string> done, Action back)`` is the game's text entry
(:cs:type:`~IronstrikeApi.Ui.TextInput`). ``Modal.instance`` queues notices; ``Confirm()`` is what a
notice's OK button calls.
