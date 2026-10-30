# Prompt Train Script – Kitsune Trains converter

Turns **your own download** of [Kitsune Trains 7](https://www.gta5-mods.com/) (by **Foxunitone**) into train packs for **Prompt Train Script 2.x** on FiveM.

No OpenIV, CodeWalker or GTA install needed. This repository contains only the converter – no models or files from Kitsune Trains.

## Use it

1. Download **Kitsune Trains 7** from gta5-mods.com. You get a `.zip` – don't unzip it.
2. Download `PromptTrainConverter_KitsuneTrains.zip` from [Releases](../../releases) and unzip it.
3. Put the Kitsune Trains `.zip` next to `PromptTrainConverter_KitsuneTrains.exe` (or leave it in your Downloads folder – the converter looks there too, or drag the zip onto the exe).
4. Double-click the exe, type the numbers of the trains you want (e.g. `22, 25`) and press Enter.
5. A `[kt_trains]` folder appears next to the zip. Follow `HOW_TO_INSTALL.txt` inside it.

Windows may show *"Windows protected your PC"* because the exe isn't signed: click **More info → Run anyway**.

## What you get

- `kt_models` – the models of every train you picked, plus their vehicle data.
- One small `kt_<train>` folder per train – a `trains.xml` and a `train_pack.lua`. Prompt Train Script finds these by itself.
- `HOW_TO_INSTALL.txt` – where to put the folder and how to put the trains on a line (`fleet` in `config/trains.lua`).

Notes:
- Trains marked `*` in the list had their wheels animated by the mod's singleplayer script; in FiveM their wheels may not turn.
- Trains longer than 12 vehicles are shortened (the end car is kept).
- The trains have no doors, seats or interior.

**Please don't re-upload converted trains.** Point people to the mod on gta5-mods.com and to this converter instead.

## Build

```
git clone --recurse-submodules https://github.com/Prompt-Coder/prompt_train_kitsune_converter
cd prompt_train_kitsune_converter
dotnet publish -c Release -o release
```

Needs the .NET 8 SDK. [CodeWalker](https://github.com/dexyfex/CodeWalker) (by dexyfex) is included as a submodule and reads the mod's archive – see `THIRD_PARTY_NOTICES.txt`.
