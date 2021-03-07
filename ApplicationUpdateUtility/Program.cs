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
    partial class Program
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
            options.WriteIndented = false;
            return JsonSerializer.Serialize(manifest, options);
        }

        static Manifest? DeserializeManifest(string json)
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.General);
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            options.Converters.Add(new JsonConverterByteArray());
            options.Converters.Add(new JsonConverterVersion());
            options.WriteIndented = false;
            return JsonSerializer.Deserialize<Manifest>(json, options);
        }

        static async Task<int> Generate(string? output, string path, string directory, string remote, Version version, UpdateMode updateMode, CancellationToken cancellationToken)
        {
            output ??= Environment.CurrentDirectory;
            var absoluteDir = Path.GetFullPath(directory);
            var absoluteOutput = Path.Combine(Path.GetFullPath(output), ManifestFileName);

            Console.WriteLine($"Generating manifest for directory {absoluteDir}");

            var groupDescription = await CreateGroupDescriptionFromDirectory(directory, path, remote, HashAlgorithm.Sha256, updateMode, cancellationToken);

            if (groupDescription is null)
                return -1;

            var manifest = new Manifest(Path.GetDirectoryName(directory) ?? "Unknown", version, version.ToString(), new[] { groupDescription });
            var json = SerializeManifest(manifest);
            Directory.CreateDirectory(Path.GetFullPath(output));
            await File.WriteAllTextAsync(absoluteOutput, json, Encoding.UTF8, cancellationToken);

            Console.WriteLine($"Manifest written to {absoluteOutput}");

            return 0;
        }

        static async Task<int> Verify(string directory, CancellationToken cancellationToken)
        {
            var absoluteManifest = Path.Combine(Path.GetFullPath(directory), ManifestFileName);
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


        static async Task<bool> DownloadFileAsync(FileDescription file, string groupDirectory, Uri groupBaseUri, HttpClient client, CancellationToken cancellationToken)
        {
            var filePath = Path.Combine(groupDirectory, file.Path);
            var uri = new Uri(groupBaseUri, file.Path);

            Console.WriteLine($"Downloading file '{uri}' to '{filePath}'");

            var fileResponse = await client.GetAsync(uri, cancellationToken);
            if (!fileResponse.IsSuccessStatusCode)
            {
                // File error
                Console.WriteLine($"Error code {fileResponse.StatusCode} downloading file '{uri}'");
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? groupDirectory);

            {
                if (File.Exists(filePath))
                    File.Delete(filePath);

                using var writeStream = File.OpenWrite(filePath);
                await fileResponse.Content.CopyToAsync(writeStream, cancellationToken);
            }
            {
                using var readStream = File.OpenRead(filePath);
                var calculatedHashBytes = await ComputeFileHash(file.Hash.Algorithm, readStream, cancellationToken);
                if (!calculatedHashBytes.SequenceEqual(file.Hash.Hash))
                {
                    Console.WriteLine($"File '{file.Path}' hash is '{calculatedHashBytes.ToByteString()}' but expected '{file.Hash.Hash.ToByteString()}'");
                    return false;
                }
            }

            return true;
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
                var groupBaseUri = new Uri(group.Remote.EndsWith('/') ? group.Remote : (group.Remote + "/"));

                Console.WriteLine($"Downloading group '{group.Name}' from '{groupBaseUri}' into directory '{groupDirectory}'");

                foreach (var file in group.Files)
                {
                    files++;
                    if (!await DownloadFileAsync(file, groupDirectory, groupBaseUri, client, cancellationToken))
                    {
                        errors++;
                    }
                }
            }

            // Store the manifest as well
            await File.WriteAllTextAsync(Path.Combine(fullDirectory, ManifestFileName), SerializeManifest(manifest), cancellationToken);

            Console.WriteLine($"Finished download of {groups} groups with {files} files with {errors} errors");

            return errors;
        }

        static async Task<int> Update(string directory, string manifestUrl, bool allowDowngrade, bool force, bool quick, CancellationToken cancellationToken)
        {
            var fullDirectory = Path.GetFullPath(directory);
            Directory.CreateDirectory(fullDirectory);
            var manifestPath = Path.Combine(fullDirectory, ManifestFileName);

            if (!File.Exists(manifestPath))
            {
                Console.WriteLine("Directory does not contain a manifest file");
                return -1;
            }

            var oldManifest = DeserializeManifest(await File.ReadAllTextAsync(manifestPath));
            if (oldManifest is null)
            {
                Console.WriteLine($"Path '{manifestPath}' does not contain valid manifest");
                return -3;
            }

            Console.WriteLine($"Got old manifest '{oldManifest.Name}'");
            Console.WriteLine($"Old Version: {oldManifest.Version} ({oldManifest.SemVersion})");

            using var client = new HttpClient();

            var manifestResponse = await client.GetAsync(manifestUrl, cancellationToken);
            if (manifestResponse.StatusCode != System.Net.HttpStatusCode.OK)
            {
                Console.WriteLine($"Error {manifestResponse.StatusCode} when fetching manifest '{manifestUrl}'");
                return -2;
            }

            var newManifest = DeserializeManifest(await manifestResponse.Content.ReadAsStringAsync(cancellationToken));
            if (newManifest is null)
            {
                Console.WriteLine($"Url '{manifestUrl}' does not contain valid manifest");
                return -3;
            }

            Console.WriteLine($"Got new manifest '{newManifest.Name}'");
            Console.WriteLine($"New Version: {newManifest.Version} ({newManifest.SemVersion})");

            if (!force)
            {
                if (newManifest.Name != oldManifest.Name)
                {
                    Console.WriteLine($"Remote manifest name '{newManifest.Name}' does not match local manifest name '{oldManifest.Name}'");
                    return -4;
                }
                if (newManifest.Version == oldManifest.Version)
                {
                    Console.WriteLine("Remote version is the same as local version, not updating");
                    return 0;
                }
                if (newManifest.Version < oldManifest.Version && !allowDowngrade)
                {
                    Console.WriteLine("Remote version is older than the local version, not updating");
                    return 0;
                }
            }

            // Determine differences between groups
            var newGroups = new List<GroupDescription>();
            var sameGroups = new List<GroupDescriptionDiff>();

            foreach (var group in newManifest.Groups)
            {
                var oldGroup = oldManifest.Groups.FirstOrDefault(g => g.Name == group.Name);
                if (oldGroup is not null)
                {
                    var diff = new GroupDescriptionDiff(oldGroup, group);
                    sameGroups.Add(diff);
                }
                else
                {
                    newGroups.Add(group);
                }
            }

            var removedFiles = new List<string>();
            var upToDateFiles = new List<string>();
            var addedFiles = new List<string>();
            var updatedFiles = new List<string>();
            var failedFiles = new List<string>();

            // Download new files
            foreach (var group in newGroups)
            {
                var groupDirectory = Path.Combine(fullDirectory, group.Path);
                var groupBaseUri = new Uri(group.Remote.EndsWith('/') ? group.Remote : (group.Remote + "/"));

                foreach (var file in group.Files)
                {
                    if (await DownloadFileAsync(file, groupDirectory, groupBaseUri, client, cancellationToken))
                    {
                        addedFiles.Add(Path.Combine(groupDirectory, file.Path));
                    }
                    else
                    {
                        failedFiles.Add(Path.Combine(groupDirectory, file.Path));
                    }
                }
            }

            // Update existing groups
            foreach (var groupDiff in sameGroups)
            {
                var groupDirectory = Path.Combine(fullDirectory, groupDiff.New.Path);
                var groupBaseUri = new Uri(groupDiff.New.Remote.EndsWith('/') ? groupDiff.New.Remote : (groupDiff.New.Remote + "/"));

                foreach (var file in groupDiff.RemovedFiles)
                {
                    var filePath = Path.Combine(groupDirectory, file.Path);
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                    removedFiles.Add(filePath);
                }

                foreach (var file in groupDiff.AddedFiles)
                {
                    if (await DownloadFileAsync(file, groupDirectory, groupBaseUri, client, cancellationToken))
                    {
                        addedFiles.Add(Path.Combine(groupDirectory, file.Path));
                    }
                    else
                    {
                        failedFiles.Add(Path.Combine(groupDirectory, file.Path));
                    }
                }

                foreach (var fileDiff in groupDiff.UpdatedFiles)
                {
                    var filePath = Path.Combine(groupDirectory, fileDiff.Old.Path);
                    var shouldUpdate = fileDiff.NeedsUpdate() || (!File.Exists(filePath));
                    
                    if (!quick && !shouldUpdate)
                    {
                        // Run a full hash check
                        using var existingFile = File.OpenRead(filePath);
                        var oldHash = await ComputeFileHash(fileDiff.New.Hash.Algorithm, existingFile, cancellationToken);
                        if (!oldHash.SequenceEqual(fileDiff.New.Hash.Hash))
                            shouldUpdate = true;
                    }

                    if (!shouldUpdate)
                    {
                        upToDateFiles.Add(filePath);
                        continue;
                    }
                    
                    if (await DownloadFileAsync(fileDiff.New, groupDirectory, groupBaseUri, client, cancellationToken))
                    {
                        updatedFiles.Add(filePath);
                    }
                    else
                    {
                        failedFiles.Add(filePath);
                    }
                }
            }


            Console.WriteLine($"Added {addedFiles.Count} files:");
            foreach (var file in addedFiles)
            {
                Console.WriteLine("  " + file);
            }
            Console.WriteLine($"Removed {removedFiles.Count} files:");
            foreach (var file in removedFiles)
            {
                Console.WriteLine("  " + file);
            }
            Console.WriteLine($"Updated {updatedFiles.Count} files:");
            foreach (var file in updatedFiles)
            {
                Console.WriteLine("  " + file);
            }
            Console.WriteLine($"Skipped {upToDateFiles.Count} up-to-date files:");
            foreach (var file in upToDateFiles)
            {
                Console.WriteLine("  " + file);
            }

            if (failedFiles.Count == 0)
            {
                // Update manifest
                Console.WriteLine("Updating local manifest");
                await File.WriteAllTextAsync(Path.Combine(fullDirectory, ManifestFileName), SerializeManifest(newManifest), cancellationToken);

                Console.WriteLine($"Finished update to version {newManifest.Version} ({newManifest.SemVersion})");
            }
            else
            {
                // Results
                Console.WriteLine($"Errors for {failedFiles.Count} files:");
                foreach (var file in failedFiles)
                {
                    Console.WriteLine("  " + file);
                }

                return failedFiles.Count;
            }

            return 0;
        }

        static async Task<int> Main(string[] args)
        {
            var rootCommand = new RootCommand();

            var generateCommand = new Command("generate", "Generate a manifest file") {
                new Option<string?>(new [] { "--output", "-o" }, "The directory to output the manifest to") { IsRequired = false },
                new Option<string>(new [] { "--directory", "-d" }, "The directory to generate a manifest for") { IsRequired = true },
                new Option<string>(new [] { "--path", "-p" }, "The relative path of the directory in the manifest") { IsRequired = true },
                new Option<string>(new string[] { "--remote", "-r" }, "The remote URL that the files can be downloaded from") { IsRequired = true },
                new Option<Version>(new string[] { "--version", "-v" }, "The version of the manifest") { IsRequired = true },
                new Option<UpdateMode>(new [] { "--update-mode", "-u" }, "The update mode to use for files in the directory") { IsRequired = true },
            };
            generateCommand.Handler = CommandHandler.Create<string?, string, string, string, Version, UpdateMode, CancellationToken>(Generate);
            rootCommand.AddCommand(generateCommand);

            var verifyCommand = new Command("verify", "Verifies the integrity of a directory") {
                new Option<string>(new string[] { "--directory", "-d" }, "The path of the directory to verify") { IsRequired = true },
            };
            verifyCommand.Handler = CommandHandler.Create<string, CancellationToken>(Verify);
            rootCommand.Add(verifyCommand);

            var downloadCommand = new Command("download", "Downloads a manifest and its files into a directory") { 
                new Option<string>(new string[] { "--directory", "-d" }, "The directory to download into. Will be created if it doesn't exist") { IsRequired = true },
                new Option<string>(new string[] { "--manifest-url", "-m" }, "The remote URL where the manifest can be found") { IsRequired = true },
            };
            downloadCommand.Handler = CommandHandler.Create<string, string, CancellationToken>(Download);
            rootCommand.Add(downloadCommand);

            var updateCommand = new Command("update", "Updates a directory containing a manifest") {
                new Option<string>(new string[] { "--directory", "-d" }, "The directory to update, must contain a manifest file") { IsRequired = true },
                new Option<string>(new string[] { "--manifest-url", "-m" }, "The remote URL where the new manifest can be found") { IsRequired = true },
                new Option<bool>(new string[] { "--quick", "-q" }, "Quick update, skips hash checks on existing files") { IsRequired = false },
                new Option<bool>(new string[] { "--force", "-f" }, "Forces an update by skipping version checks") { IsRequired = false },
                new Option<bool>(new string[] { "--allow-downgrade", "-a" }, "Allows downgrades, by default the version of the new manifest must be higher") { IsRequired = false },
            };
            updateCommand.Handler = CommandHandler.Create<string, string, bool, bool, bool, CancellationToken>(Update);
            rootCommand.Add(updateCommand);

            return await rootCommand.InvokeAsync(args);
        }

        static async Task<GroupDescription?> CreateGroupDescriptionFromDirectory(string directoryPath, string groupPath, string remote, HashAlgorithm hashAlgorithm, UpdateMode updateMode, CancellationToken cancellationToken)
        {
            var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories).Where(path => !path.EndsWith(ManifestFileName)).Select(filePath => Path.GetRelativePath(directoryPath, filePath));

            var group = await CreateGroupDescriptionAsync("directory-group", directoryPath, remote, files, hashAlgorithm, updateMode, cancellationToken);
            if (group is not null)
                group.Path = groupPath;
            return group;
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
    }
}
