# Benchmark trajectory

Timings parsed from the committed captures in `benchmarks/`.

Regenerate with:

```bash
node tools/benchmark-graph.mjs --write
```

## In-process JIT

110 runs on 15 days (2026-09-20 to 2026-10-17); each point is the daily median, all times in seconds.

### Ours

```text
In-process JIT — Ours (seconds)
   9s|
   8s|                  *
   7s|        *
   6s|            * *
   5s|          *           *     *
   4s|* *                     *
   3s|      *         *   *     *
   2s|    *
   1s|
   0s|
     +------------------------------
     09-20        10-02       10-16
     * ours (run1)
```

### dotnet build (cold)

```text
In-process JIT — dotnet build (cold) (seconds)
  18s|
  16s|                    + +
  14s|                        +
  12s|                  +       + +
  10s|            +
   8s|          +   + +
   6s|+       +
   4s|  + + +
   2s|
   0s|
     +------------------------------
     09-20        10-02       10-16
     + dotnet build (cold)
```

| Date | Ours (s) | dotnet cold (s) | Speed-up |
|---|---:|---:|---:|
| 2026-09-20 | 4.09 | 5.36 | 1.00x |
| 2026-09-26 | 3.90 | 4.46 | 1.18x |
| 2026-09-27 | 2.30 | 3.68 | 1.68x |
| 2026-09-28 | 3.40 | 4.36 | 1.55x |
| 2026-09-29 | 6.88 | 6.45 | 0.93x |
| 2026-09-30 | 4.55 | 7.59 | 1.56x |
| 2026-10-01 | 6.02 | 9.69 | 1.48x |
| 2026-10-02 | 6.25 | 7.68 | 1.19x |
| 2026-10-03 | 3.26 | 8.31 | 2.47x |
| 2026-10-04 | 7.64 | 11.34 | 1.51x |
| 2026-10-13 | 2.78 | 15.31 | 5.52x |
| 2026-10-14 | 5.02 | 15.83 | 2.98x |
| 2026-10-15 | 3.64 | 13.41 | 3.62x |
| 2026-10-16 | 3.17 | 12.57 | 4.02x |
| 2026-10-17 | 5.09 | 12.81 | 2.76x |

## Published AOT

96 runs on 14 days (2026-09-20 to 2026-10-17); each point is the daily median, all times in seconds.

### Ours

```text
Published AOT — Ours (seconds)
 4.5s|
 4.0s|
 3.5s|          *         *
 3.0s|*     *           *   * * *
 2.5s|            * *
 2.0s|  * *   *       *
 1.5s|
 1.0s|
 0.5s|
 0.0s|
     +----------------------------
     09-20       10-02      10-16
     * ours (run1)
```

### dotnet build (cold)

```text
Published AOT — dotnet build (cold) (seconds)
  18s|
  16s|                  + +
  14s|                      +
  12s|                +       + +
  10s|          +
   8s|        +   + +
   6s|      +
   4s|+ + +
   2s|
   0s|
     +----------------------------
     09-20       10-02      10-16
     + dotnet build (cold)
```

| Date | Ours (s) | dotnet cold (s) | Speed-up |
|---|---:|---:|---:|
| 2026-09-20 | 3.07 | 3.37 | 1.24x |
| 2026-09-27 | 1.86 | 3.52 | 1.90x |
| 2026-09-28 | 1.85 | 3.70 | 2.02x |
| 2026-09-29 | 2.99 | 6.82 | 2.29x |
| 2026-09-30 | 1.87 | 7.25 | 3.62x |
| 2026-10-01 | 3.25 | 9.54 | 3.04x |
| 2026-10-02 | 2.33 | 7.68 | 3.31x |
| 2026-10-03 | 2.45 | 8.31 | 3.39x |
| 2026-10-04 | 2.15 | 11.34 | 5.56x |
| 2026-10-13 | 2.79 | 15.31 | 5.48x |
| 2026-10-14 | 3.54 | 15.83 | 4.55x |
| 2026-10-15 | 2.79 | 13.41 | 4.84x |
| 2026-10-16 | 3.13 | 12.57 | 4.04x |
| 2026-10-17 | 2.92 | 12.81 | 4.38x |
