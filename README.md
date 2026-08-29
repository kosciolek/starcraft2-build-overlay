# StarCraft II Build Overlay

A lightweight, always-on-top Windows overlay for following StarCraft II build
orders from plain-text files.

The overlay is transparent and click-through, starts in the upper-left corner,
and automatically reloads the selected text file when it is saved.

## Run

Pass the build-order text file as the first argument:

```powershell
& ".\Text Overlay.exe" ".\StarCraft Build Order.txt"
```

You can also drag a `.txt` file onto `Text Overlay.exe`. If no argument is
provided, the program opens `overlay.txt` from its own directory.

## Controls

- **F1** — go back one line
- **F2** — complete the current line and move forward
- **F3** — exit

F1 and F2 support normal Windows held-key repeat. Completed lines become much
more transparent. The notification-area icon can open the text file, reset
progress, or exit the program.

## Colored build items

Text enclosed in double asterisks is rendered bold and assigned a bright,
deterministic color based on its content. Matching is case-insensitive, so the
same item always receives the same color.

```text
13  0:17 **Supply Depot**  Build it at your ramp with your first SCV produced
```

The asterisks are not displayed in the overlay.

## Build from source

No third-party dependencies are required. Compile `Program.cs` with the C#
compiler included in the 64-bit .NET Framework installation:

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" `
  /nologo /target:winexe /optimize+ /platform:anycpu `
  /reference:System.dll /reference:System.Drawing.dll `
  /reference:System.Windows.Forms.dll `
  '/out:Text Overlay.exe' Program.cs
```

## Compatibility

The overlay works over ordinary windows and borderless-windowed games. Windows
may prevent normal overlays from appearing over exclusive-fullscreen games,
protected video, or some anti-cheat-protected applications.
