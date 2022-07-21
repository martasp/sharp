using System;
using TestBNamespace;

namespace TestANamespace;

public class A
{
    private int value = 10;
    public B BObject { get; set; } = new B();

    public void Print(){
        Console.WriteLine(value);
        Console.WriteLine(BObject.valueFromB);
    }
}