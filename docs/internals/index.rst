.. _internals-index:

################
 Game internals
################

What is known about IRONSTRIKE's own code, for when the API does not cover something yet. Facts
marked **verified** were observed in the running game, in disassembly, or in a network capture;
everything else is inference.

The game's classes are in the global namespace of the interop assembly ``GameAssembly.dll`` (not
``Assembly-CSharp.dll``, which holds third-party code).

.. toctree::
   :maxdepth: 2

   binary.rst
   gm.rst
   combat.rst
   network.rst
   menus.rst
