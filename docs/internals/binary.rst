.. _internals-binary:

**************
The game build
**************

.. list-table::
   :widths: 30 70

   * - Engine
     - Unity 2021.3.28f1, IL2CPP backend (no ``Managed/`` folder)
   * - Metadata
     - ``global-metadata.dat`` version 29, not encrypted or obfuscated: stock tools work
   * - Gameplay code
     - the IL2CPP image ``GameAssembly`` (about 711 types)
   * - VR
     - OpenXR; the game also runs flat when no runtime is present
   * - Netcode
     - Photon Fusion 2 in Host mode, Photon Voice
   * - Content
     - classic serialized scenes ``level0`` to ``level14``; no AssetBundles or Addressables
   * - Platform
     - Steam app 3233230. A paid game; the Gem purchase code belongs to the Quest build

Tools
=====

* **Cpp2IL** (``--output-as diffable-cs``) gives every type with fields, field offsets, method
  signatures and the original source paths. It does not recover method bodies for the game's
  types.
* **Il2CppDumper** gives a ``dump.cs`` with each method's address, for disassembly.
* **Il2CppInterop** generates the assemblies mods compile against. Pass ``--unity`` with the Unity
  base libraries, or the result differs from what BepInEx generates at runtime. The authoritative
  set is the one in the game's ``BepInEx/interop/``.
* **ilspycmd** on the interop assemblies shows exact signatures. Use ``-t <TypeName>``; whole-assembly
  decompiles are slow.

Loose data
==========

``Ironstrike_Data/StreamingAssets`` holds ``skillvalues.csv`` and ``spellvalues.csv`` (per-level
values for every skill and spell, read at startup), ``CosmeticsData.csv`` (off limits: see
:ref:`howto-safety`), and the characters' dialogue graphs as JSON. Editing the CSVs is untested.
