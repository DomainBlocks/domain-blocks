#!/usr/bin/env bash
# SPIKE (plan step 13): sanity check of PostgreSQL's AppendBatchingDelay on another machine. Not for merging.
# Run from anywhere with Docker running:  bash spike/run-pg-delay.sh [rounds]   (default 3 rounds, about 30 minutes)
# Builds once in Release, then runs each case once per variant per round, the variants rotated one place per round.
# Results go to spike/results-pg/ (ignored). Summarize with:  python3 spike/sumpg.py spike/results-pg d0 d1 d5
set -u
cd "$(dirname "$0")/.."
out=spike/results-pg
proj=benchmarks/DomainBlocks.EventStore.PostgreSQL.Benchmarks
rounds=${1:-3}
mkdir -p "$out"

{
  date
  uname -a
  sysctl -n hw.ncpu 2>/dev/null || nproc 2>/dev/null
  dotnet --version
  docker version --format 'docker {{.Server.Version}} ({{.Server.Os}}/{{.Server.Arch}})' 2>/dev/null
  git rev-parse HEAD
} > "$out/env.txt"

dotnet build "$proj" -c Release > "$out/build.log" 2>&1 || { echo "build failed: see $out/build.log"; exit 1; }

run() {
  local label=$1 filter=$2 spec=$3 round=$4
  IFS=':' read -r variant delay <<< "$spec"
  local file="$out/$label-$round-$variant.txt"
  DBX_SPIKE_TIMER1=1 DBX_SPIKE_PG_DELAY_US=$delay DBX_SPIKE_PG_MIN_COUNT=0 \
    dotnet test "$proj" -c Release --no-build \
    --filter "FullyQualifiedName~Benchmarks&$filter" --logger "console;verbosity=detailed" > "$file" 2>&1
  echo "$(date +%T) $label r$round $variant: $(grep -m1 -E '^throughput:' "$file" | tr -s ' ')" \
    "p50 $(grep -m1 -E '^p50:' "$file" | awk '{print $2}')"
  grep -qE "^\s*Failed " "$file" && echo "  FAILED: see $file"
}

# Delay in microseconds, with the default minimum count of 0.
variants=(d0:0 d1:1000 d5:5000)

cases=(
  "pg-1x10|Name=AppendAsync_MeasureThroughput\(1,10\)"
  "pg-4x100|Name=AppendAsync_MeasureThroughput\(4,100\)"
  "pg-4x1000|Name=AppendAsync_MeasureThroughput\(4,1000\)"
  "pg-batch1000-1x1000|Name=AppendAsync_MeasureThroughput_BatchSizeSweep\(1000,1000\)"
  "pg-open-1x1000|Name=AppendAsync_SpikeOpenLoop\(1,1000\)"
  "pg-open-1x10000|Name=AppendAsync_SpikeOpenLoop\(1,10000\)"
  "pg-open-4x10000|Name=AppendAsync_SpikeOpenLoop\(4,10000\)"
)

for ((r = 1; r <= rounds; r++)); do
  for c in "${cases[@]}"; do
    IFS='|' read -r label filter <<< "$c"
    n=${#variants[@]}
    for ((i = 0; i < n; i++)); do
      run "$label" "$filter" "${variants[$(((i + r - 1) % n))]}" "$r"
    done
  done
done
echo "RUN-PG-DELAY DONE"
