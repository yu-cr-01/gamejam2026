@echo off
rem ===================================================================
rem  v3.0 card-table landing probe (copy-project run; NO -batchmode / -quit)
rem  Env vars are set only here, then Unity is launched in place.
rem  If Unity shows the "run as administrator" modal, use:
rem      Start-Process explorer.exe <this .cmd>
rem  Stage chain: 480 (runtime card dump) -> 481..484 (codex shots) -> 485/486 (finish)
rem  Evidence: docs/v3cards-evidence/ (shots + probe log lines)
rem ===================================================================
set DSH_AUTOPLAY=1
set DSH_PLAYCAPTURE=1
set DSH_CAPTURE_DIR=C:\Users\yu_cr\AppData\Local\Temp\dsh-v3cards\shots
set DSH_TURNPROBE=1
set DSH_CARDV3PROBE=1

"D:\unity\2022.3.62f3c1\Editor\Unity.exe" -projectPath "C:\Users\yu_cr\AppData\Local\Temp\dsh-v3cards\UnityProject" -executeMethod GameJam.EditorTools.AutoPlayHarness.Run -logFile "C:\Users\yu_cr\AppData\Local\Temp\dsh-v3cards\unity-v3cards-run1.log"
echo UNITY_EXIT=%ERRORLEVEL%
