.. _tut-setup:

*****************
Setting things up
*****************

Playing with mods
=================

#. Install BepInEx 6 IL2CPP, build **be.788**:
   `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788+5b766a3.zip
   <https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip>`_.
   Extract **all six** items (``winhttp.dll``, ``doorstop_config.ini``, ``.doorstop_version``,
   ``changelog.txt``, ``dotnet/``, ``BepInEx/``) directly next to ``Ironstrike.exe``.

   .. note::

      The build page lists thirteen downloads; only that one works. The Mono build has a nearly
      identical name and does nothing. If nothing happens on launch, run ``bepinex-doctor.ps1``
      from the API's repository in PowerShell.

#. Start the game once and wait for the main menu. The first start takes a minute or so while
   BepInEx generates its interop assemblies.
#. Put ``IronstrikeApi.dll`` in ``BepInEx/plugins/``, along with the mods that need it.

On Linux under Proton, add ``WINEDLLOVERRIDES="winhttp=n,b" %command%`` to the game's Steam launch
options.

The log is ``BepInEx/LogOutput.log`` (``.log``, not ``.txt``). When the API is running it contains::

   [Info   :IRONSTRIKE Mod API] IRONSTRIKE Mod API v0.1.0 loaded.
   [Info   :IRONSTRIKE Mod API] HOOK CONFIRMED: GM.Update fired.

Making mods
===========

You need the `.NET SDK <https://dotnet.microsoft.com/download>`_, 6.0 or newer, and a text editor or
IDE. You do not need Unity, and you do not need the game's files to compile: the API repository
commits the reference assemblies in ``refs/``.

A mod is a .NET 6 class library. The quickest start is to copy ``examples/HelloMod`` out of the
API's repository and rename it. Its project file references three things:

``BepInEx.Unity.IL2CPP``
   The mod loader, from the BepInEx NuGet feed (``NuGet.config`` in the repository adds it).

The game's interop assemblies
   ``GameAssembly.dll`` and friends, which BepInEx generates into ``BepInEx/interop/`` on first
   launch. They are C# views of the game's IL2CPP code: every class, field and method, with no
   method bodies.

``IronstrikeApi.dll``
   From an API release. Reference it with ``Private="false"`` so it is not copied next to your
   mod: players install it once.

.. literalinclude:: ../../examples/HelloMod/HelloMod.csproj
   :language: xml
   :lines: 1-12

Build with::

   dotnet build -c Release

and copy ``bin/Release/net6.0/YourMod.dll`` into ``BepInEx/plugins/``.
