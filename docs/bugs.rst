.. _bugs:

****************
Reporting issues
****************

Open an issue at `github.com/IlumCI/ironstrike-api/issues
<https://github.com/IlumCI/ironstrike-api/issues>`_ for the API or these docs. For a bug in a
mod built on the API, report it to that mod first.

Include:

* the API version and your other mods (the Mods window lists them, or the first lines of the log);
* ``BepInEx/LogOutput.log`` from a session where it happened;
* what you did, what you expected, and what happened.

The log is safe to attach. The API never writes Steam names, account ids or join codes to it, and
mods built on it are asked not to (:cs:meth:`~IronstrikeApi.ApiLog.Redact`). Config files are a
different matter: Ironstrike Servers keeps join codes and passwords in its config, so do not attach
those.

If nothing happens on launch at all, run ``bepinex-doctor.ps1`` from the repository first; most
"broken" installs are BepInEx extracted into a subfolder.
