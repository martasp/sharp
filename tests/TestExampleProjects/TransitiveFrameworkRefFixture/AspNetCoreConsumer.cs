using Microsoft.AspNetCore.Mvc.ModelBinding;
using TransitiveRefChild;

namespace TransitiveFrameworkRefFixture;

public class AspNetCoreConsumer
{
    [BindNever]
    public string Secret { get; set; } = "";

    public ChildModel BuildChild() => new ChildModel();

    public string Describe() => "parent consuming shared-framework types via transitive FrameworkReference";
}