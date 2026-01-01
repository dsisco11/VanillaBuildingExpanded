using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

using VanillaBuildingExpanded.Tests.Benchmarks;

// Check for --manual flag to run manual benchmarks (avoids BenchmarkDotNet issues with external refs)
if (args.Contains("--manual"))
{
    ManualBenchmarkRunner.RunAllBenchmarks();
}
else
{
    Console.WriteLine("BenchmarkDotNet has issues with external (non-NuGet) references like Vintagestory.");
    Console.WriteLine("Run with --manual flag to use the manual benchmark runner instead.");
    Console.WriteLine();
    Console.WriteLine("Example: dotnet run -c Release --project VanillaBuildingExpanded.Tests -- --manual");
    Console.WriteLine();
    
    // Try BenchmarkDotNet anyway
    var config = ManualConfig.Create(DefaultConfig.Instance)
        .WithOptions(ConfigOptions.DisableOptimizationsValidator);

    BenchmarkSwitcher.FromTypes([
        typeof(VecExtensionsBenchmarks),
        typeof(FastVecExtensionsBenchmarks)
    ]).Run(args, config);
}
