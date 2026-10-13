Animated Workshop cover for Vanity Studio
=========================================

workshop_preview.gif         the animated cover (320x320, 40 frames, under Steam's 1 MB limit)
workshop_preview_still.png   first frame, if you ever need a still
original_logo.png            the logo icon.png had before it was replaced by the animated one
publish_preview.ps1          does the upload through SteamCMD
Publish Workshop Preview.bat double-click this

How to use
1. Publish or update the mod from tModLoader as usual (Workshop > Develop Mods > Publish).
   tModLoader always uploads icon.png as the cover, so this step resets it to the still image.
2. Double-click "Publish Workshop Preview.bat".
   - SteamCMD is downloaded into a "steamcmd" folder here the first time, if it isn't installed.
   - The Workshop item id is found automatically from your Steam folder; if not, it asks
     (it's the number after ?id= in the mod's Steam page address).
   - It asks for your Steam login name once and remembers it in publish_settings.txt
     (the password is never saved, SteamCMD asks for it and the Steam Guard code itself).
3. Only the cover changes. Files, description, tags and visibility are left alone.

Run step 2 again after every publish from tModLoader.

This folder is excluded from the built .tmod (buildIgnore in build.txt), so the GIF and a
downloaded SteamCMD never end up inside the mod.
