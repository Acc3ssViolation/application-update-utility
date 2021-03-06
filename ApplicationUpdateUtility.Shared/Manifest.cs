using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace ApplicationUpdateUtility.Shared
{
    public class Manifest
    {
        public string Name { get; set; }

        public Version Version { get; set; }

        public string SemVersion { get; set; }

        public IReadOnlyList<GroupDescription> Groups { get; set; }

        public Manifest()
        {
            Name = string.Empty;
            Version = new Version();
            SemVersion = string.Empty;
            Groups = Array.Empty<GroupDescription>();
        }

        public Manifest(string name, Version version, string semVersion, IEnumerable<GroupDescription> groups)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Version = version ?? throw new ArgumentNullException(nameof(version));
            SemVersion = semVersion ?? throw new ArgumentNullException(nameof(semVersion));
            Groups = groups?.ToArray() ?? throw new ArgumentNullException(nameof(groups));
        }

        public static async Task<Manifest> LoadFromXmlFileAsync(string filePath)
        {
            Manifest? manifest;
            try
            {
                var text = await File.ReadAllTextAsync(filePath);
                var document = new XmlDocument();
                
                document.LoadXml(text);


                //                manifest = serializer.Deserialize(stream) as Manifest;
                Console.WriteLine("AAA");
                manifest = null;
            }
            catch (Exception e)
            {
                throw new ManifestParseException($"Error parsing manifest file {filePath}", e);
            }

            if (manifest is null)
                throw new ManifestParseException($"Failed parsing manigest file {filePath}");

            return manifest;
        }
    }
}
