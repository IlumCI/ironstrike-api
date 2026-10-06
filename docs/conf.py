# Sphinx configuration for the IRONSTRIKE Mod API docs.
#
#   pip install -r docs/requirements.txt
#   dotnet build IronstrikeApi/IronstrikeApi.csproj -c Release
#   dotnet run --project docs/tools/RefGen -- IronstrikeApi/bin/Release/net6.0/IronstrikeApi.dll docs/reference/api refs
#   sphinx-build -W -b html docs docs/_build/html
#
# build-docs.sh / build-docs.ps1 at the repo root do all of that.
import os
import re
import sys

sys.path.insert(0, os.path.abspath("_ext"))

HERE = os.path.dirname(os.path.abspath(__file__))
with open(os.path.join(HERE, "..", "IronstrikeApi", "IronstrikeApi.csproj"), encoding="utf-8") as f:
    release = re.search(r"<Version>([^<]+)</Version>", f.read()).group(1)
version = ".".join(release.split(".")[:2])

project = "IRONSTRIKE Mod API"
author = "IlumCI"
copyright = "2026, IlumCI. Licensed under the AGPL-3.0"

extensions = ["csdomain"]
root_doc = "contents"
primary_domain = "cs"
default_role = "cs:obj"
highlight_language = "none"
templates_path = ["_templates"]
exclude_patterns = ["_build", "tools", "Thumbs.db", ".DS_Store"]

# docs.python.org's own theme: classic layout, sidebar, breadcrumbs, prev/next, search, light/dark.
html_theme = "python_docs_theme"
html_title = f"{project} {release} documentation"
html_short_title = f"{project} {release}"
html_static_path = ["_static"]
html_css_files = ["ironstrike.css"]
html_theme_options = {
    "collapsiblesidebar": True,
    "root_name": "IRONSTRIKE",
    "root_url": "https://www.ironstrikegame.com/",
    "root_icon": "ironstrike-32.png",
    "root_icon_alt_text": "IRONSTRIKE Mod API",
    "root_include_title": True,
    "issues_url": "https://github.com/IlumCI/ironstrike-api/issues",
    "license_url": "license.html",
    "hosted_on": "",
}
html_sidebars = {
    "**": ["localtoc.html", "relations.html", "sourcelink.html"],
    "index": ["indexsidebar.html"],
}
# The landing page is a template, as on docs.python.org.
html_additional_pages = {"index": "indexcontent.html"}
html_last_updated_fmt = "%b %d, %Y"
html_copy_source = True
html_show_sourcelink = True
