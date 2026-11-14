# DualSense Companion

A basic companion app for DualSense and DualShock 4 controllers for Windows. It emulates an Xbox 360 controller so you can play almost all PC games with your PlayStation controller!

What This App Can Do:
1. Xbox 360 controller emulation for a single DualSense (PS5) or DualShock 4 (PS4) controller.
2. USB and BT connectivity support.
3. Rumble support.
4. Automatic hiding of original controller to prevent double input.
5. Dependency check and install on startup.
6. Desktop controls to enable or disable controller hiding and Xbox 360 emulation.
7. Optional Xbox rumble mirrored as adaptive-trigger vibration on DualSense controllers.

The adaptive-trigger vibration toggle is only available when a DualSense is detected.
DualShock 4 controllers do not support adaptive triggers and are not sent trigger effects.

Crash and unexpected controller-disconnect reports are written as timestamped `.txt` files
to the `crash` folder beside the running application. After a controller disconnects, the app
now retries the HID connection; output reports use the length advertised by the connected device.


The window uses `DualSenseCompanion\img\background.jpg` and
`DualSenseCompanion\img\logo.jpg`; replace these files to customize its artwork.

## credits to Sar1911 : https://github.com/Sar1911/DualSenseCompanion
A huge thanks goes to:
1. ViGEm (Virtual Gamepad Emulation Driver) – For providing the essential Xbox 360 controller emulation.
2. HidHide – For allowing HID device filtering and hiding.
3. HidSharp – For handling HID device communication in C#.
4. Linux Kernel (hid-playstation.c) – For documentation on DualSense Controller.
5. The awesome Open-Source Developers & Community Discussions – For shared knowledge on DualSense input/output handling.

Build with: 'dotnet build "C:\path-to-file\DualSenseCompanion-master\DualSenseCompanion.sln" -c Release'
The executable should be under DualSenseCompanion\bin\Release\net8.0-windows10.0.26100.0
You’ll need the .NET 8 SDK.
