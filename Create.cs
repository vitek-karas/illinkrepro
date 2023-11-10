using Microsoft.Build.Logging.StructuredLogger;
using System.CommandLine;
using System.CommandLine.Invocation;
using Task = Microsoft.Build.Logging.StructuredLogger.Task;

namespace illinkrepro
{
    internal class Create
    {
        public static Command Command
        {
            get
            {
                var command = new Command("create")
                {
                    new Argument<FileInfo>("binlog"),
                    new Option<DirectoryInfo>(new[] { "--out", "-o" }),
                    new Option<bool>(new [] { "--force", "-f" }),
                    new Option<string>(new[] { "--target" }),
                    new Option<string>(new[] { "--project" })
                };

                command.Handler = CommandHandler.Create(Run);
                return command;
            }
        }

        private static int Run(FileInfo binlog, DirectoryInfo? @out, bool force, string? target, string? project)
        {
            if (!binlog.Exists)
                throw new ArgumentException($"Binlog {binlog} doesn't exist.", nameof(binlog));

            if (@out == null)
            {
                @out = new DirectoryInfo(Path.Combine(Environment.CurrentDirectory, "repro"));
            }

            if (@out.Exists)
            {
                if (!force)
                {
                    throw new ApplicationException($"Output path {@out} already exists. Use --force to overwrite.");
                }

                @out.Delete(true);
            }

            @out.Create();

            var build = BinaryLog.ReadBuild(binlog.FullName);
            List<Task> ilLinkTasks = new();
            build.VisitAllChildren<Task>(task => { if (task.Name == "ILLink") ilLinkTasks.Add(task); });

            if (ilLinkTasks.Count == 0)
            {
                Console.Error.WriteLine($"No ILLink task found in the log, make sure that trimming runs (and is not skipped by incremental build).");
                return -1;
            }

            if (!string.IsNullOrEmpty (project))
            {
                var candidateTasks = ilLinkTasks.Where(t =>
                {
                    var n = (t.Parent as Target)?.Project.Name;
                    if (n == project)
                    {
                        return true;
                    }
                    else if (Path.GetFileNameWithoutExtension(n) == project)
                    {
                        return true;
                    }

                    return false;
                });

                if (!candidateTasks.Any())
                {
                    Console.Error.WriteLine($"No ILLink task in '{project}' project found.");
                    return -1;
                }

                ilLinkTasks = candidateTasks.ToList();
            }

            if (!string.IsNullOrEmpty(target))
            {
                var candidateTasks = ilLinkTasks.Where(t => ((Target)t.Parent).Name == target);
                if (!candidateTasks.Any())
                {
                    Console.Error.WriteLine($"No ILLink task in '{target}' target found.");
                    return -1;
                }

                ilLinkTasks = candidateTasks.ToList();
            }

            Task? ilLinkTask = null;
            if (ilLinkTasks.Count > 1)
            {
                // Find the first failing one
                var ilLinkTasksWhichFailed = ilLinkTasks.Where(t => t.FindChild<NamedNode>("Errors") != null);
                ilLinkTask = ilLinkTasksWhichFailed.FirstOrDefault();
                if (ilLinkTask != null)
                {
                    if (ilLinkTasksWhichFailed.Count() > 1)
                    {
                        Console.WriteLine($"Found more that one failing ILLink task. Picking the first failing one.");
                    }
                    else
                    {
                        Console.WriteLine($"Found failing ILLink task, picking the failing one over any other.");
                    }
                }
                else
                {
                    ilLinkTask = ilLinkTasks[0];
                    Console.WriteLine($"Found more than one ILLink task and no failing one. Pickign the first one.");
                }
            }
            else
            {
                ilLinkTask = ilLinkTasks[0];
            }

            var projectNode = (ilLinkTask.Parent as Target)?.Project!;
            string projectName = projectNode.Name;
            Console.WriteLine($"Creating repro for ILLink task from project {projectName}");

            var ilLink = new ILLink(projectNode.ProjectDirectory, ilLinkTask.CommandLineArguments);
            DirectoryInfo input = new(Path.Combine(@out.FullName, "input"));
            input.Create();

            Dictionary<string, string> inputFiles = new Dictionary<string, string>();

            List<ILLink.Argument> reproArgs = new();
            foreach (var arg in ilLink.Arguments)
            {
                switch (arg)
                {
                    case ILLink.Reference reference:
                        reproArgs.Add(reference with { AssemblyPath = CopyFileToInput(reference.AssemblyPath) });
                        break;

                    case ILLink.Root root:
                        if (File.Exists(root.AssemblyPath))
                            reproArgs.Add(root with { AssemblyPath = CopyFileToInput(root.AssemblyPath) });
                        else
                            reproArgs.Add(root with { AssemblyPath = Path.GetFileName(root.AssemblyPath) });
                        break;

                    case ILLink.Out outArg:
                        reproArgs.Add(new ILLink.Out("out"));
                        break;

                    case ILLink.Descriptor descriptor:
                        reproArgs.Add(descriptor with { Path = CopyFileToInput(descriptor.Path) });
                        break;

                    case ILLink.LinkAttributes linkAttributes:
                        reproArgs.Add(linkAttributes with { Path = CopyFileToInput(linkAttributes.Path) });
                        break;

                    case ILLink.SearchDirectory searchDirectory:
                        reproArgs.Add(searchDirectory with { Path = CopyDirectoryToInput(searchDirectory.Path) });
                        break;

                    default:
                        reproArgs.Add(arg);
                        break;
                }
            }

            File.WriteAllLines(
                Path.Combine(@out.FullName, "linker.rsp"),
                reproArgs.Select(a => a.ToString()));

            Console.WriteLine(@out.FullName);

            return 0;

            string CopyFileToInput(string path)
            {
                path = Path.GetFullPath(path);
                string fileName = Path.GetFileName(path);
                if (inputFiles.TryGetValue(path, out var inputRelativePath))
                {
                    return inputRelativePath;
                }
                else
                {
                    string inputPath = Path.Combine(input.FullName, fileName);
                    string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
                    string fileExtension = Path.GetExtension(fileName);
                    int index = 1;
                    while (File.Exists(inputPath))
                    {
                        fileName = fileNameWithoutExtension + $".{index++}" + fileExtension;
                        inputPath = Path.Combine(input.FullName, fileName);
                    }

                    File.Copy(path, inputPath);
                    inputRelativePath = Path.Combine(input.Name, fileName);
                    inputFiles.Add(path, inputRelativePath);
                    return inputRelativePath;
                }
            }

            string CopyDirectoryToInput(string path)
            {
                path = Path.GetFullPath(path);
                string directoryName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
                if (inputFiles.TryGetValue(path, out var inputRelativePath))
                {
                    return inputRelativePath;
                }
                else
                {
                    string inputPath = Path.Combine(input.FullName, directoryName);
                    string directoryBaseName = directoryName;
                    int index = 1;
                    while (Directory.Exists(inputPath))
                    {
                        directoryName = directoryBaseName + $".{index++}";
                        inputPath = Path.Combine(input.FullName, directoryName);
                    }

                    CopyDirectory(path, inputPath, true);
                    inputRelativePath = Path.Combine(input.Name, directoryName);
                    inputFiles.Add(path, inputRelativePath);
                    return inputRelativePath;
                }
            }
        }

        static void CopyDirectory(string sourceDir, string destinationDir, bool recursive)
        {
            // Get information about the source directory
            var dir = new DirectoryInfo(sourceDir);

            // Check if the source directory exists
            if (!dir.Exists)
                throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

            // Cache directories before we start copying
            DirectoryInfo[] dirs = dir.GetDirectories();

            // Create the destination directory
            Directory.CreateDirectory(destinationDir);

            // Get the files in the source directory and copy to the destination directory
            foreach (FileInfo file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath);
            }

            // If recursive and copying subdirectories, recursively call this method
            if (recursive)
            {
                foreach (DirectoryInfo subDir in dirs)
                {
                    string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                    CopyDirectory(subDir.FullName, newDestinationDir, true);
                }
            }
        }
    }
}
