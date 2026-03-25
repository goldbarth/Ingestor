using BenchmarkDotNet.Running;

#if DEBUG
Console.Error.WriteLine("Benchmarks require Release mode: dotnet run -c Release -- [options]");
return 1;
#endif

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
return 0;