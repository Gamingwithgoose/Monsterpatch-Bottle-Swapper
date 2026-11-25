# Monsterpatch Bottle Swapper v0.1.0 (beta test)

BepInEx 5 Mono.Cecil preloader patcher, separate from Bottle House Modifiers.

## Build
Run build.bat. Requires .NET SDK and Mono.Cecil.dll (sibling lib or default Steam installation).
Optional: build.bat "path to lib" "path to Monsterpatch".
Drag Release/BepInEx into the game folder. DLL belongs in patchers, not plugins.

## Test
Back up your save first; test in single-player. Open the party menu and select a mon.
Expected order: Summary, Switch, Item, Send To, Swap Bottle, Cancel.
Swap Bottle opens the native typewriter dialogue and native Yes/No choices.
No returns to the party submenu; Yes opens the native BOTTLES inventory page with owned items.
B/cancel returns to the party submenu without changing anything. Choosing a different bottle exchanges
one inventory bottle for the old bottle, updates Mon.bottleId, and refreshes the party menu.
Same bottle is a no-op. A full old-bottle stack (999) rejects the exchange. Legacy bottle ID zero
is treated as the basic bottle (108), consistent with beta save serialization.

The native layout is measured from the existing rows at runtime, and explicit controller navigation is wired.
The actual Unity scene hierarchy, runtime reflection, navigation, compilation and gameplay still need local testing.
Check BepInEx console/preloader logs for [BottleSwapper] messages, particularly six hooks installed,
native party menu expanded, native inventory opened, and swap transaction details. Send logs and screenshots if needed.
