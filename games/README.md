# Pino games

Three text games written in Pino, ported to the new compiler (0.4 and later).

| Game | File | What it is |
| :--- | :--- | :--- |
| Jade Temple: Path of the Spirit Fist | `jade_temple.pino` | A martial-arts adventure with turn-based combat and two moral paths |
| Sable: Protocolo Semilla | `sable.pino` | A seven-cycle evacuation sim in Spanish, with resource allocation and a fragmenting AI |
| StarPino Odyssey | `star_pino.pino` | A space trading, mining and pirate game: earn 2000 CR for the Hyper-Warp Core |

## Run

```
pino-re run --vm games/jade_temple.pino
pino-re run --vm games/sable.pino
pino-re run --vm games/star_pino.pino
```

Use `--c` instead of `--vm` to compile through C99 and run the native program. Check a game without running it with `pino-re check games/<name>.pino`.

## Legacy copy

`pino.games/` holds the original versions, written for the legacy compiler (0.3.x). `pino play update` on 0.3 downloads that folder from the `main` branch, so it stays as it is until the legacy compiler is removed in 0.5. Don't edit it. The games here are the ones for the new Pino.
