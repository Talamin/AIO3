# AIO3 — Rework der AIO WRobot Fightclass (WotLK 3.3.5a)

Greenfield-Neubau der [Talamin/AIO-Public](https://github.com/Talamin/AIO-Public)
Fightclass. Ziel: das bewährte **Prioritätslisten-Modell (APL)** behalten, aber auf
ein sauberes, **testbares, geschichtetes** Fundament stellen. Kein Big-Bang —
Content (BossList, Talente, Spell-IDs) wird später aus dem Altprojekt **portiert**,
nicht neu erfunden.

## Schichten

```
5 · Settings & GUI      (noch nicht gebaut)
4 · Rotations           dünn: Baseline + Klassen-Filler        [AIO3.Core]
3 · Shared Step Library Interrupt/Defensive/AoE/Burst (geplant) [AIO3.Core]
2 · Engine              Prioritäts-Runner + Exclusive-Tokens    [AIO3.Core]
1 · CombatContext       EIN unveränderlicher Snapshot pro Tick  [AIO3.Core]
0 · WRobot-Adapter      einziger Code, der wManager berührt     [AIO3]
```

**Architektur-Garantie:** `AIO3.Core` referenziert WRobot **nicht**. Damit kann
keine Schicht oberhalb des Adapters versehentlich `wManager` importieren — der
Compiler erzwingt die Grenze. Testbarkeit folgt daraus: `FakeGameClient` erlaubt,
Rotationen offline ohne laufendes Spiel zu prüfen.

## Projekte

| Projekt        | Ausgabe         | WRobot-Referenz | Zweck                                  |
|----------------|-----------------|-----------------|----------------------------------------|
| `AIO3.Core`    | `AIO3.Core.dll` | nein            | Domäne, Engine, Rotationen, Fakes      |
| `AIO3`         | `AIO3.dll`      | ja (`Private=false`) | Fightclass-Einstieg + Adapter      |
| `AIO3.Tests`   | (nicht ausgeliefert) | nein       | Unit-Tests gegen `FakeGameClient`      |

Die WRobot-Libs werden mit `Private=false` referenziert → **nicht** in die
Fightclass mitkompiliert. WRobot lädt sie zur Laufzeit aus seinem `Bin`-Ordner
(`<probing privatePath="Bin;Products;FightClass" />`). Ausgeliefert werden nur
`AIO3.dll` + `AIO3.Core.dll` in den `FightClass`-Ordner.

## Bauen

```powershell
# WRobotBin ist in Directory.Build.props vorbelegt; bei Bedarf überschreiben:
dotnet build AIO3.sln -c Release -p:WRobotBin="E:\Games\Wrobot\BOT3.3.5\WRobot\Bin"
dotnet test  AIO3.sln
```

## Status

Erster vertikaler Schnitt: Engine + DSL + Context stehen und sind durch einen
Test abgesichert; der Adapter kompiliert gegen die echten WRobot-DLLs und baut
pro Tick einen `CombatContext`. Noch **keine** echte Rotation verdrahtet
(Main loggt nur einen Heartbeat) — das ist der nächste Schritt (Frost Mage).
