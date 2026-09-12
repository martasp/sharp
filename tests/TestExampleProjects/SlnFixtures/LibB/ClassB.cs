using SlnFixtures.LibA;

namespace SlnFixtures.LibB;

public class ClassB
{
    public string Greeting => new ClassA().Greeting + " + LibB";
}