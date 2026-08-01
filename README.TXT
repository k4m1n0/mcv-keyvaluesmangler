 /##   /##                                        /##
| ##  /##/                                       | ##
| ## /##/   /######  /##   /## /##    /## /##### | ## /##  /##  /#####   /######
| #####/   /##__  ##| ##  | ##|  ##  /##/|___  ##| ##| ## | ## /##_  ## /##____/
| ##  ##  | ########| ##  | ## \  ##/##/  /######| ##| ## | ##| #######|  #####
| ##\  ## | ##_____/| ##  | ##  \  ###/  /##_  ##| ##| ## | ##| ##____/ \___  ##
| ## \  ##|  #######|  #######   \  #/  |  ######| ##|  #####/|  ###### /######/
|__/  \__/ \_______/ \____  ##    \_/    \______/|__/ \_____/  \______/|______/
                     /##  | ##
                    |  ######/
                     \______/
         /##      /##                               /##
        | ###    /###                              | ##
        | ####  /####  /######  /#######   /###### | ##  /######   /######
        | ## ##/## ## |____  ##| ##__  ## /##__  ##| ## /##__  ## /##__  ##
        | ##  ###| ##  /#######| ##  \ ##| ##  \ ##| ##| ########| ##  \__/
        | ##\  # | ## /##__  ##| ##  | ##| ##  | ##| ##| ##_____/| ##
        | ## \/  | ##|  #######| ##  | ##|  #######| ##|  #######| ##
        |__/     |__/ \_______/|__/  |__/ \____  ##|__/ \_______/|__/
                                          /##  | ##
                                         |  ######/
                                          \______/
                 /#######   /######   /######   /######  TM
                | ##____/  /###_  ## /###_  ## /###_  ##
                | ##      | ####\ ##| ####\ ##| ####\ ##
                | ####### | ## ## ##| ## ## ##| ## ## ##
                |_____  ##| ##\ ####| ##\ ####| ##\ ####
                /##  \  ##| ## \ ###| ## \ ###| ## \ ###
                |  ######/|  ######/|  ######/|  ######/
                 \______/  \______/  \______/  \______/

================================================================================
  1. WHAT IS THIS
================================================================================

  Keyvalues Mangler (TM) 5000 is a weapon data editor and wiki publishing
  tool for Military Conflict: Vietnam. Browse, compare, edit, and export
  weapon statistics

  FEATURE:
    - Dual panel weapon comparison with live TTK calculation
    - 100 levels of Undo / Redo
    - Day of Victory && Zombie Mode alternate statistics
    - One click export to game script files
    - Wiki integration: convert, generate, and upload weapon pages
    - Run from command line if you hate GUIs for some reason
    - Detect dark mode so your eyeballs donot bleed at 3 AM
      (Win/GTK/KDE/Xfce)

================================================================================
  2. SYSTEM REQUIREMENTS
================================================================================

  MINIMUM:
    - Windows 7 SP1 with KB3063858 (use the self contained one)
    - Wine/Proton 9.0+ for the OS of evil
    - Pentium 4 630 with SVGA graphics
    - 1.44 MB floppy (Desktop Runtime) or 64 MB drive space available
    - A keyboard. A mouse. Fingers optional but recommended

  RECOMMENDED:
    - Windows 10 x64 1607 (20H1 or later for dark mode title bar via DWM)
    - 1366x768 display resolution
    - MCV game instead of the scripts folder only
    - Internet connection for wiki stuff

================================================================================
  3. QUICK START
================================================================================

  GUI MODE:
    1. Double click the EXE
    2. Import from weapon scripts
    3. Pick weapons from the dropdowns
    4. Change numbers until they look good
    5. Ctrl+S to save or Ctrl+Shift+S to save && export to scripts

  WINLATOR:
    set DOTNET_GCHeapHardLimit=0x10000000
    set DOTNET_EnableWriteXorExecute=0

  CLI MODE:
    Too long, see 7. or just use this:
    keyvaluesmangler_x.x.x.x_win_amd64[-selfcontained].exe --help

================================================================================
  4. KEYBOARD SHORTCUTS
================================================================================

  Ctrl+S            Save changes to weapons.csv (do this often)
  Ctrl+Shift+S      Quick Export (save CSV + export to scripts)
  Ctrl+Z            Undo
  Ctrl+Y            Redo
  Ctrl+1            Focus left weapon dropdown
  Ctrl+2            Focus right weapon dropdown
  Ctrl+R            Refresh weapon list from CSV
  Ctrl+T            Toggle always on top / minimize

================================================================================
  5. FILE REFERENCE
================================================================================

  weapons.csv       Master weapon database
  mangler.log       Diagnostic log File
  preset_file.txt   Optional script template (overrides embedded)
  generated\        Wiki page output directory
  wiki\             Wiki conversion cache directory

================================================================================
  6. WIKI INTEGRATION
================================================================================

  The Wiki Stats Updater (press the Wiki button) provides three functions:

  DRY RUN:  Fetch a wiki page, convert using script data, preview changes
            Click again to upload

  BATCH:    Process ALL weapons linked from a summary page
            Supports dryrun (convert and cache) then batch upload

  GENERATE: Create brand new wiki pages from game scripts + resource
            files. Automatically checks for existing pages on the wiki
            Generated files are saved to .\generated\ for review

  Wiki credentials are stored in memory only and cleared on exit

================================================================================
  7. PARAMETERS USAGE
================================================================================

  GUI MODE:
  WeaponDamageCalc.exe [--darkmode|--lightmode] [--log-level <level>]

  CLI MODE:
  WeaponDamageCalc.exe [command] [options]

  --log-level <debug|info|warn|error>
    Minimum log level written to mangler.log
    (default: GUI=debug, CLI=warn)
      DEBUG  Log everything including control value changes and hotkeys
      INFO   Log operations (save, export, import, wiki actions)
      WARN   Log warnings and errors only (missing files, failed ops)
      ERROR  Log errors and fatal events only
      FATAL  No need to specify, this program gets fucked
    Log file rotates at 5 MiB so your disk does not die

  COMMANDS:
    --csv-to-scripts <csv> <dir>
    --scripts-to-csv <dir> [csv]
    --convert-templates <dir> [--simple]
    --wiki-dryrun <page> <dir> [--single] [--verbose]
    --wiki-upload <page> <dir> --user <u> --pw <p> [--single]
    --batch-dryrun <page> <dir> [--skip-cached] [--verbose]
    --batch-upload <page> <dir> --user <u> --pw <p>
    --generate <dir> [out] [--include-existing] [--check-wiki]

  RETURN CODES: 0=OK  1=Usage  2=Login  3=Not Found  4=Unknown  5=Error

================================================================================
  8. LIMITATIONS
================================================================================

  - Self contained build is ~60 MB and blame Microsoft not me
  - Some wiki operations require login to
    wiki.militaryconflictvietnam.com
  - Two copies from the same folder will not open, this is intentional

================================================================================
  9. BUILD FROM SOURCE
================================================================================

  NEEDS: .NET 8.0 SDK, CsvHelper (NuGet handles it)

   BUILD WITH:
      dotnet publish -c Release -r win-x64
      --no-self-contained -p:PublishSingleFile=true
      -p:DebugType=none -p:DebugSymbols=false -o ./publish
    or
      dotnet publish -c Release -r win-x64
      --self-contained true -p:PublishSingleFile=true
      -p:DebugType=none -p:DebugSymbols=false
      -p:EnableCompressionInSingleFile=true
      -p:InvariantGlobalization=true -o ./publish

  Then grab the EXE from .\publish\ and do whatever you want with it

================================================================================
  A. CREDITS
================================================================================

  Written for the MCV community by
        John "k0u74ku" Doe
   who clearly has too much time

  Powered by .NET, CsvHelper, caffeine, and poor life choices

  Keyvalues Mangler is a trademark of someone
  I guess, so please just steal it responsibly
  All rights reversed. Some assembly required
  Batteries not included. Use only as directed
  If symptoms persist, consult your physician

  (C) Juche 115. Made with Visual Studio and spite

================================================================================
