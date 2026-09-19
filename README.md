# Beastmaster Assistant

> **Note:** This is a fork of [anmili2022/Beastmaster](https://github.com/anmili2022/Beastmaster) maintained at [lexxxgo/Masterbeast](https://github.com/lexxxgo/Masterbeast) that only translates the original plugin's user interface and documentation from Chinese into English. All credit for the plugin's design and implementation goes to the original author, Anmi. This fork does not add or change any functionality.

A Dalamud plugin for Final Fantasy XIV that tracks the Beastmaster quest chain and Beast Catalog collection progress.

## Features

- Displays the Beastmaster quest chain and its status based on client-side quest data.
- Automatically records catalog progress on the current character when you receive a "You have successfully befriended a ... beast!" message.
- Supports manually editing or hiding captured beasts, and sorting catalog targets by map.
- Supports navigation to quest pickup points and field catalog targets.
- Can work with vnavmesh and Lifestream to handle same-map movement and cross-map teleporting.
- The Beast Catalog shows each beast's attribute, ultimate, and Release skill info.
- The Auto Rotation overlay shows the current beast and Beastmaster gauge status.
- Advanced skills support independent toggles and read-only availability checks.

## Installation

Add the following to your custom plugin repositories in the Dalamud settings:

```text
https://raw.githubusercontent.com/lexxxgo/Masterbeast/main/repo.json
```

## Commands

- `/beastmaster`: Opens the Beastmaster Assistant.
- `/驯兽师`: Opens the Beastmaster Assistant (Chinese alias).
- `/驯兽师 输出`: Toggles Auto Rotation between on and paused; turns it on when off, resumes when paused, pauses when running.
- `/驯兽师 暂停`: Pauses Auto Rotation while keeping the toggle on.
- `/驯兽师 恢复`: Resumes a paused Auto Rotation.
- `/驯兽师 关闭`: Turns off Auto Rotation.

The Auto Rotation subcommands above also support the English form: `/beastmaster output|pause|resume|off`.

## Building

```powershell
dotnet build
```

The build output is located at `output\Beastmaster.dll`.

Gauge reference: [docs/BST_GAUGE.md](https://github.com/lexxxgo/Masterbeast/blob/main/docs/BST_GAUGE.md); ACR design: [docs/BST_ACR_DESIGN.md](https://github.com/lexxxgo/Masterbeast/blob/main/docs/BST_ACR_DESIGN.md); Beast Arena Round 1 reference: [docs/BEAST_ARENA_ROUND_1.md](https://github.com/lexxxgo/Masterbeast/blob/main/docs/BEAST_ARENA_ROUND_1.md); development roadmap: [docs/ROADMAP.md](https://github.com/lexxxgo/Masterbeast/blob/main/docs/ROADMAP.md).

Release process: [docs/release.md](docs/release.md).
