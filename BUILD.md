# KOSpopupStop — Build & Deploy

## Overview

Suppresses kOS terminal popup windows that steal focus during KSP gameplay. Uses reflection to interact with kOS types (no compile-time kOS dependency).

## Prerequisites

- .NET SDK 9.0+ (configured via `global.json`, rollForward: latestFeature)
- KSP install path configured in `.csproj.user` (see below)
- kOS mod installed in KSP GameData (runtime dependency only)

## Setup

Copy the example user file and set your KSP path:

```powershell
cd KOSpopupStop
Copy-Item KOSpopupStop.example.user KOSpopupStop.csproj.user
```

Edit `KOSpopupStop.csproj.user` and set `KSPBT_GameRoot` to your KSP install:

```xml
<PropertyGroup>
  <KSPBT_GameRoot>G:\Steam\steamapps\common\Kerbal RSS</KSPBT_GameRoot>
</PropertyGroup>
```

## Build

```powershell
cd KOSpopupStop
dotnet restore
dotnet build -c Release
```

Output: `bin\Release\KOSpopupStop.dll`

## Deploy

KSPBuildTools (NuGet package v1.1.1) handles post-build deployment automatically to:

```
<KSP>\GameData\KOSpopupStop\Plugins\KOSpopupStop.dll
```

The build also populates the local `GameData\KOSpopupStop\Plugins\` directory.

To deploy manually if needed:

```powershell
$KSP = "G:\Steam\steamapps\common\Kerbal RSS"
Copy-Item "GameData\KOSpopupStop" "$KSP\GameData\KOSpopupStop" -Recurse -Force
```

## Deployed Files

| File | Location |
|------|----------|
| `KOSpopupStop.dll` | `GameData\KOSpopupStop\Plugins\` |

## Notes

- Uses `KSPBuildTools` NuGet package for build integration — handles KSP assembly references and post-build copy.
- `.csproj.user` is gitignored — each developer sets their own KSP path.
- CKAN metadata is in `Properties\KOSpopupStop.netkan`.
