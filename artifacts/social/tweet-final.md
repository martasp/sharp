Want to learn how a C# compiler works? sharp CLI is 19k C# lines vs 722k in the MSBuild repo. The whole sharp SDK is just one Native AOT executable: no installed SDK or MSBuild needed. Great for small and medium-sized solutions as it is faster. Compiling our own code results in these timings: dotnet build 4.2s, sharp build 2.0s. Both `dotnet run` and `sharp run` print "Hello from Sharp!".

https://github.com/martasp/sharp/releases/tag/v0.3.1
