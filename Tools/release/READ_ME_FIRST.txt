ENCReload @@VER@@  -  installation
=====================================================================

ENCReload is a Humankind overhaul. Its custom 3D units are rendered by
the Humankind Asset Framework (HAF), which is a BepInEx plugin - so the
download has TWO parts that go to TWO DIFFERENT PLACES.

Do both. Either one alone will not work.

---------------------------------------------------------------------
BEFORE YOU START:  install BepInEx 5.4.x  (once)
---------------------------------------------------------------------
Get it from   https://github.com/BepInEx/BepInEx/releases
(the x64 build for Windows), and unpack it into your Humankind install
folder - the one containing Humankind.exe. Run the game once so BepInEx
creates its folders, then quit.

---------------------------------------------------------------------
PART 1 of 2:  folder "1_Humankind_game_folder"
---------------------------------------------------------------------
Copy the BepInEx folder found inside it INTO your Humankind install
folder, merging with the BepInEx folder already there.

  Typically:
  ...\Steam\steamapps\common\Humankind\

  Afterwards you should have:
    ...\Humankind\BepInEx\plugins\HumankindAssetFramework.dll
    ...\Humankind\BepInEx\plugins\Haf.Schema.dll
    ...\Humankind\BepInEx\config\haf_packs\ENCReload\pack.json

---------------------------------------------------------------------
PART 2 of 2:  folder "2_Community_mods_folder"
---------------------------------------------------------------------
Copy the "ENCReload.*" folder found inside it INTO your Humankind
Community folder - the SAME folder your other Humankind mods are in.

Its location varies (Humankind computes it, and some installs move it),
so the reliable way to find it is: open any mod you already have
installed and use that folder. If you have none, the game's mod menu
will create it the first time you browse mods.

  Afterwards you should have:
    ...\Humankind\Community\ENCReload.@@GUID@@.@@VER@@\

---------------------------------------------------------------------
CHECK IT WORKED
---------------------------------------------------------------------
Start Humankind, enable ENCReload in the mod menu, and load a game.
Press F8 to open the HAF panel: it names the build it is running and
runs a smoke test. A green smoke test with ENCReload entries listed
means both parts landed correctly.

If F8 does nothing, BepInEx or the plugin is not installed (Part 1).
If F8 works but units look vanilla, the mod is not enabled (Part 2).

Logs worth attaching to a bug report, from the game folder:
  BepInEx\LogOutput.log
  BepInEx\config\haf_load_report.txt
  BepInEx\config\haf_smoke_report.txt
