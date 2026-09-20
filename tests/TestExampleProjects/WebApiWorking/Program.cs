using System;

Console.WriteLine("--------captured-webapi-logs-test--------");

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/", () => "Hello Sharp WebApi");
app.MapGet("/captured-logs-test", () => "--------captured-webapi-logs-test--------");

app.Run();