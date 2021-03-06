using ApplicationUpdateUtility.Shared;
using System;
using System.CommandLine;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.CommandLine.Invocation;
using System.Text;
using System.Net.Http;

namespace ApplicationUpdateUtility
{
    class Program
    {
        const string ManifestFileName = "manifest.auu.json";

        static class ErrorCodes
        {
            public const int Ok = 0;
            public const int NoManifest = -1;
            public const int NoServer = -2;
        }

        static string SerializeManifest(Manifest manifest)
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.General);
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            options.Converters.Add(new JsonConverterByteArray());
            options.Converters.Add(new JsonConverterVersion());
            options.WriteIndented = true;
            return JsonSerializer.Serialize(manifest, options);
        }

        static Manifest? DeserializeManifest(string json)
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.General);
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            options.Converters.Add(new JsonConverterByteArray());
            options.Converters.Add(new JsonConverterVersion());
            options.WriteIndented = true;
            return JsonSerializer.Deserialize<Manifest>(json, options);
        }

        static async Task<int> Generate(string output, string dir, string remote, Version version, UpdateMode updateMode, HashAlgorithm hash, CancellationToken cancellationToken)
        {
            var absoluteDir = Path.GetFullPath(dir);
            var absoluteOutput = Path.GetFullPath(output);

            Console.WriteLine($"Generating manifest for directory {absoluteDir}");

            var groupDescription = await CreateGroupDescriptionFromDirectory(dir, remote, hash, updateMode, cancellationToken);

            if (groupDescription is null)
                return -1;

            var manifest = new Manifest(Path.GetDirectoryName(dir) ?? "Unknown", version, version.ToString(), new[] { groupDescription });
            var json = SerializeManifest(manifest);
            await File.WriteAllTextAsync(absoluteOutput, json, Encoding.UTF8, cancellationToken);

            Console.WriteLine($"Manifest written to {absoluteOutput}");

            return 0;
        }

        static async Task<int> Verify(string manifest, CancellationToken cancellationToken)
        {
            var absoluteManifest = Path.GetFullPath(manifest);
            var errors = new List<string>();

            if (!File.Exists(absoluteManifest))
                return -1;

            var json = await File.ReadAllTextAsync(absoluteManifest, cancellationToken);
            var m = DeserializeManifest(json);
            if (m is null)
                return -2;

            var manifestDirectory = Path.GetDirectoryName(absoluteManifest);
            if (manifestDirectory is null)
                return -3;

            Console.WriteLine(manifestDirectory);

            try
            {
                foreach (var group in m.Groups)
                {
                    Console.WriteLine($"Verifying group '{group.Name}'");

                    var groupPath = Path.Combine(manifestDirectory, group.Path);
                    if (!Directory.Exists(groupPath))
                    {
                        errors.Add($"Group directory '{group.Path}' can not be found at '{groupPath}'");
                        continue;
                    }

                    foreach (var file in group.Files)
                    {
                        Console.WriteLine($"Verifying file '{file.Path}'");
                        var filePath = Path.Combine(groupPath, file.Path);
                        if (!File.Exists(filePath))
                        {
                            errors.Add($"File '{file.Path}' in group '{group.Name}' can not be found at '{filePath}'");
                            continue;
                        }

                        using var stream = File.OpenRead(filePath);
                        var calculatedHashBytes = await ComputeFileHash(file.Hash.Algorithm, stream, cancellationToken);
                        if (!calculatedHashBytes.SequenceEqual(file.Hash.Hash))
                        {
                            errors.Add($"File '{file.Path}' hash is '{calculatedHashBytes.ToByteString()}' but expected '{file.Hash.Hash.ToByteString()}'");
                            continue;
                        }
                    }
                }
            }
            catch(Exception e)
            {
                errors.Add($"Exception during verifying: {e}");
            }

            foreach (var error in errors)
            {
                Console.WriteLine(error);
            }

            Console.WriteLine($"Verification finished with {errors.Count} errors");

            return errors.Count;
        }

        static async Task<int> Download(string directory, string manifestUrl, CancellationToken cancellationToken)
        {
            var fullDirectory = Path.GetFullPath(directory);
            Directory.CreateDirectory(fullDirectory);

            if (File.Exists(Path.Combine(fullDirectory, ManifestFileName)))
            {
                Console.WriteLine("Directory already contains a manifest file, use update instead");
                return -1;
            }

            using var client = new HttpClient();

            var manifestResponse = await client.GetAsync(manifestUrl, cancellationToken);
            if (manifestResponse.StatusCode != System.Net.HttpStatusCode.OK)
            {
                Console.WriteLine($"Error {manifestResponse.StatusCode} when fetching manifest '{manifestUrl}'");
                return -2;
            }

            var manifest = DeserializeManifest(await manifestResponse.Content.ReadAsStringAsync(cancellationToken));
            if (manifest is null)
            {
                Console.WriteLine($"Url '{manifestUrl}' does not contain valid manifest");
                return -3;
            }

            Console.WriteLine($"Got manifest '{manifest.Name}'");
            Console.WriteLine($"Version: {manifest.Version} ({manifest.SemVersion})");

            var groups = 0;
            var files = 0;
            var errors = 0;

            // Fetch all files
            foreach (var group in manifest.Groups)
            {
                groups++;

                var groupDirectory = Path.Combine(fullDirectory, group.Path);
                var groupBaseUri = new Uri(group.Remote);

                Console.WriteLine($"Downloading group '{group.Name}' from '{groupBaseUri}' into directory '{groupDirectory}'");

                foreach (var file in group.Files)
                {
                    files++;

                    var filePath = Path.Combine(groupDirectory, file.Path);
                    var uri = new Uri(groupBaseUri, file.Path);

                    Console.WriteLine($"Downloading file '{uri}' to '{filePath}'");

                    var fileResponse = await client.GetAsync(uri, cancellationToken);
                    if (!fileResponse.IsSuccessStatusCode)
                    {
                        // File error
                        Console.WriteLine($"Error code {fileResponse.StatusCode} downloading file '{uri}'");
                        errors++;
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? groupDirectory);

                    {
                        using var writeStream = File.OpenWrite(filePath);
                        await fileResponse.Content.CopyToAsync(writeStream, cancellationToken);
                    }
                    {
                        using var readStream = File.OpenRead(filePath);
                        var calculatedHashBytes = await ComputeFileHash(file.Hash.Algorithm, readStream, cancellationToken);
                        if (!calculatedHashBytes.SequenceEqual(file.Hash.Hash))
                        {
                            Console.WriteLine($"File '{file.Path}' hash is '{calculatedHashBytes.ToByteString()}' but expected '{file.Hash.Hash.ToByteString()}'");
                            errors++;
                            continue;
                        }
                    }
                }
            }

            // Store the manifest as well
            await File.WriteAllTextAsync(Path.Combine(fullDirectory, ManifestFileName), SerializeManifest(manifest), cancellationToken);

            Console.WriteLine($"Finished download of {groups} groups with {files} files with {errors} errors");

            return errors;
        }

        static async Task<int> Main(string[] args)
        {
            var rootCommand = new RootCommand();

            var generateCommand = new Command("generate", "Generate a manifest file") {
                new Option<string>(new [] { "--output", "-o" }, "The path of the manifest file to output") { IsRequired = true },
                new Option<string>(new [] { "--dir", "-d" }, "The directory to generate a manifest for") { IsRequired = true },
                new Option<string>(new string[] { "--remote", "-r" }, "The remote URL that the files can be downloaded from") { IsRequired = true },
                new Option<Version>(new string[] { "--version", "-v" }, "The version of the manifest") { IsRequired = true },
                new Option<UpdateMode>(new [] { "--update-mode", "-u" }, "The update mode to use for files in the directory") { IsRequired = true },
                new Option<HashAlgorithm>(new [] { "--hash", "-h" }, "The hashing algorithm to use for files in the directory") { IsRequired = true },
            };
            generateCommand.Handler = CommandHandler.Create<string, string, string, Version, UpdateMode, HashAlgorithm, CancellationToken>(Generate);
            rootCommand.AddCommand(generateCommand);

            var verifyCommand = new Command("verify", "Verifies the integrity of a manifest's files") {
                new Option<string>(new string[] { "--manifest", "-m" }, "The path of the manifest file to verify") { IsRequired = true },
            };
            verifyCommand.Handler = CommandHandler.Create<string, CancellationToken>(Verify);
            rootCommand.Add(verifyCommand);

            var downloadCommand = new Command("download", "Downloads a manifest and its files into a directory") { 
                new Option<string>(new string[] { "--directory", "-d" }, "The directory to download into. Will be created if it doesn't exist") { IsRequired = true },
                new Option<string>(new string[] { "--manifest-url", "-m" }, "The remote URL where the manifest can be found") { IsRequired = true },
            };
            downloadCommand.Handler = CommandHandler.Create<string, string, CancellationToken>(Download);
            rootCommand.Add(downloadCommand);

            return await rootCommand.InvokeAsync(args);
        }

        static async Task<GroupDescription?> CreateGroupDescriptionFromDirectory(string directoryPath, string remote, HashAlgorithm hashAlgorithm, UpdateMode updateMode, CancellationToken cancellationToken)
        {
            var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories).Select(filePath => Path.GetRelativePath(directoryPath, filePath));

            return await CreateGroupDescriptionAsync(Path.GetFileName(directoryPath), directoryPath, remote, files, hashAlgorithm, updateMode, cancellationToken);
        }

        static async Task<GroupDescription?> CreateGroupDescriptionAsync(string name, string groupPath, string remote, IEnumerable<string> filePaths, HashAlgorithm hashAlgorithm, UpdateMode updateMode, CancellationToken cancellationToken)
        {
            var fileDescriptions = (await Task.WhenAll(filePaths.Select(async filePath => await CreateFileDescriptionAsync(groupPath, filePath, hashAlgorithm, updateMode, cancellationToken)))).Where(fd => fd != null);
            if (fileDescriptions is null)
                return null;

            return new GroupDescription(name, groupPath, remote, fileDescriptions!);
        }

        /// <summary>
        /// Creates a file description for a given file.
        /// </summary>
        /// <param name="groupPath">The group path that is prepended before the filePath</param>
        /// <param name="filePath">Path of the file in the group</param>
        /// <param name="hashAlgorithm">Hashing algorithm to use for computing the file hash</param>
        /// <returns></returns>
        static async Task<FileDescription?> CreateFileDescriptionAsync(string groupPath, string filePath, HashAlgorithm hashAlgorithm, UpdateMode updateMode, CancellationToken cancellationToken)
        {
            var fullPath = Path.Join(groupPath, filePath);

            if (!File.Exists(fullPath))
                return null;

            using var fileStream = File.OpenRead(fullPath);
            var hashBytes = await ComputeFileHash(hashAlgorithm, fileStream, cancellationToken);
            return new FileDescription(filePath, (ulong) fileStream.Length, updateMode, new HashDescription(hashAlgorithm, hashBytes));
        }

        static async Task<byte[]> ComputeFileHash(HashAlgorithm hashAlgorithm, Stream fileStream, CancellationToken cancellationToken)
        {
            return hashAlgorithm switch
            {
                HashAlgorithm.None => Array.Empty<byte>(),
                HashAlgorithm.Sha256 => await ComputeSha256Hash(fileStream, cancellationToken),
                _ => throw new ArgumentException($"Algorithm {hashAlgorithm} is not supported", nameof(hashAlgorithm)),
            };
        }

        static async Task<byte[]> ComputeSha256Hash(Stream stream, CancellationToken cancellationToken)
        {
            using var hash = System.Security.Cryptography.SHA256.Create();
            return await hash.ComputeHashAsync(stream, cancellationToken);
        }

        static async Task<Manifest?> FindLocalManifest(string folder)
        {
            const string fileName = "auu-manifest.xml";
            var filePath = Path.Join(folder, fileName);

            if (File.Exists(filePath))
            {
                try
                {
                    return await Manifest.LoadFromXmlFileAsync(filePath);
                }
                catch (ManifestParseException e)
                {
                    Console.WriteLine(e.ToString());
                    return null;
                }
            }

            return null;
        }
    }
}
