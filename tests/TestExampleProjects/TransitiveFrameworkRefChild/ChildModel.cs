using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace TransitiveRefChild;

public class ChildModel
{
    [BindNever]
    public string Secret { get; set; } = "";

    public string Describe() => "child with shared-framework types";
}