using System.Xml.Serialization;

namespace NativeCompiler;

public static class Extensions
{
    public static XML.Project DeserializeObject(this XML.Project project, string path)
    {
        var xml = File.ReadAllText(path);

        var serializer = new XmlSerializer(typeof(XML.Project));
        using TextReader reader = new StringReader(xml);
        var result = (XML.Project?)serializer.Deserialize(reader) ?? throw new Exception("cannot Deserialize");

        return result;
    }
}