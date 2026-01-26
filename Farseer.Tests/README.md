# Farseer Performance Tests

This project contains unit tests and benchmarks for validating the PopulateRegionFromChunk optimization.

## Test Types

### 1. Correctness Tests (xUnit)
Verify the optimized code produces identical results to the baseline.

**Run tests:**
```bash
dotnet test Farseer.Tests/Farseer.Tests.csproj
```

**Tests included:**
- ✅ Identical output verification
- ✅ Iteration count validation (16,384 → 64)
- ✅ All chunk positions (0,0 to 15,15)
- ✅ Different grid sizes (32, 64, 128, 256)

### 2. Performance Benchmarks (BenchmarkDotNet)
Measure actual performance improvement.

**Run benchmarks:**
```bash
cd Farseer.Tests
dotnet run -c Release
```

**Metrics measured:**
- **Mean execution time** (baseline vs optimized)
- **Memory allocation**
- **Speedup ratio**

## Expected Results

### Correctness Tests
```
✅ All tests should PASS
✅ Outputs should be identical
✅ Iterations: 16,384 (baseline) → 64 (optimized)
```

### Performance Benchmarks
```
| Method                      | Mean      | Allocated |
|---------------------------- |----------:| ---------:|
| PopulateRegion_Baseline     | 850.0 μs  | -         |
| PopulateRegion_Optimized    | 3.5 μs    | -         |

Speedup: ~240x faster
```

## Quick Start

```bash
# Restore dependencies
dotnet restore Farseer.Tests/Farseer.Tests.csproj

# Run correctness tests
dotnet test Farseer.Tests/Farseer.Tests.csproj -v normal

# Run benchmarks
cd Farseer.Tests && dotnet run -c Release
```

## Interpreting Benchmark Results

**Good results:**
- Mean time ratio: > 100x improvement
- Baseline: 500-2000 μs per call
- Optimized: 2-20 μs per call
- No additional allocations

**Concerning results:**
- Speedup < 50x (something wrong with optimization)
- Optimized still > 50 μs (unexpected overhead)
- Different memory allocation (shouldn't change)

## Troubleshooting

**Tests fail with "outputs don't match":**
- Check edge cases in grid bounds calculation
- Verify rounding behavior with fractional cellSize

**Benchmarks show minimal improvement:**
- Ensure running in Release mode (`-c Release`)
- Check baseline code is actually the old implementation
- Verify JIT isn't optimizing away the work

**Build errors:**
- Run `dotnet restore Farseer.Tests/Farseer.Tests.csproj`
- Ensure .NET 8 SDK installed

## CI Integration

Add to CI pipeline:
```yaml
- name: Run unit tests
  run: dotnet test Farseer.Tests/Farseer.Tests.csproj

- name: Run benchmarks
  run: cd Farseer.Tests && dotnet run -c Release
```
