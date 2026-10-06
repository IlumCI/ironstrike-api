.. _license:

*******************
History and license
*******************

History
=======

The API grew out of two mods: `Ironstrike Trainer <https://github.com/IlumCI/ironstrike-trainer>`_,
a cheat menu for solo and private games, and `Ironstrike Servers
<https://github.com/IlumCI/ironstrike-servers>`_, a community server browser for modded play. Each
had to solve the same problems: hooking the game reliably, drawing a window that works in VR, keeping
mods out of public games. Version 0.1 collects those solutions in one library.

License
=======

The IRONSTRIKE Mod API is free software, licensed under the **GNU Affero General Public License
version 3** (AGPL-3.0). The full text is in ``LICENSE`` in the repository.

IRONSTRIKE is a game by E McNeill. This project is not affiliated with or endorsed by its
developer. It contains none of the game's assets. The repository's ``refs/`` folder holds reference
assemblies generated from the game's metadata (class and method signatures, no method bodies), so
mods can be compiled without the game installed; at runtime, mods use the interop assemblies BepInEx
generates on the player's own machine.
