using OtdHealthCheck;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
return await AnalyzerCommand.RunAsync(args, Console.Out, Console.Error, cancellation.Token);
