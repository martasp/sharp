using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;

namespace FrameworkRefFixture;

public class AspNetCoreConsumer
{
    public ILogger CreateLogger(ILoggerFactory factory) => factory.CreateLogger("fixture");

    public string Describe()
    {
        var builder = WebApplication.CreateBuilder();
        return builder.Environment.ApplicationName;
    }
}