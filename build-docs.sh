#!/usr/bin/env bash
# Build the documentation site into docs/_build/html.
#
#   ./build-docs.sh
#
# Needs the .NET SDK and Python 3 with the packages in docs/requirements.txt
# (pip install -r docs/requirements.txt). Warnings fail the build, as they do in CI.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
cd "$HERE"

dotnet build IronstrikeApi/IronstrikeApi.csproj -c Release --nologo -v q
NUGET="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
dotnet run --project docs/tools/RefGen -c Release -- \
  IronstrikeApi/bin/Release/net6.0/IronstrikeApi.dll docs/reference/api refs "$NUGET"
"${PYTHON:-python3}" -m sphinx -W --keep-going -b html docs docs/_build/html
echo "==> docs/_build/html/index.html"
