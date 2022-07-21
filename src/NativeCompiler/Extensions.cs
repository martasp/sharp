using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;
using System.Xml.Serialization;

namespace NativeCompiler
{
    public static class Extensions
    {
        public static XML.Project DeserializeObject(this XML.Project project, string path)
        {
            string xml = File.ReadAllText(path);

            XmlSerializer serializer = new XmlSerializer(typeof(XML.Project));
            using TextReader reader = new StringReader(xml);
            XML.Project result = (XML.Project?)serializer.Deserialize(reader) ?? throw new Exception("cannot Deserialize");

            return result;
        }

    }
}
