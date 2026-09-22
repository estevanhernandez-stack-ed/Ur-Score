# Runs the score-book benchmark at a season's size and prints its numbers (Sweep D: S1-F.2, S1-F.3).
#
# The benchmark is an ordinary test, BookBenchmarks.LoadingAndChartingASeasonsBook, which runs on a day's book in
# every ordinary test run so the harness cannot rot. With URSCORE_BENCH=1 it writes five clan sources and a clans
# list, five weeks, a reading every three minutes (about 100,000 lines, 60 MB) into a temp folder, loads it the way
# AppServices does at startup, runs one board's worth of chart queries ten times, and prints the timings. Nothing
# real is read: the book is synthetic, and it is deleted when the test ends.
#
# Numbers on the owner's machine, 2026-09-22 — before Sweep D: startup 2,246 ms (two passes), one board's charts
# 68 ms. After: startup 1,540 ms (one pass, cold), charts 11 ms. The reader holds 326 MB for that book either way.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$env:URSCORE_BENCH = '1'
try {
    dotnet test (Join-Path $root 'tests/Ur-Score.Tests.csproj') --filter 'FullyQualifiedName~BookBenchmarks' --logger 'console;verbosity=detailed' 2>&1 |
        Select-String -Pattern 'book:|startup:|charts:|Failed|error' |
        ForEach-Object { $_.Line.Trim() }
    if ($LASTEXITCODE -ne 0) { throw "the benchmark run failed (exit $LASTEXITCODE)" }
}
finally {
    Remove-Item Env:URSCORE_BENCH -ErrorAction SilentlyContinue
}
