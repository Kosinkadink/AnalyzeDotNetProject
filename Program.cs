using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NuGet.ProjectModel;

namespace AnalyzeDotNetProject
{
    class Program
    {
        /* USAGE:
         * To perform a scan, change scanFile var to path of .sln or .csproj that should be scanned.
         * NOTE: Changes in scan will only be reflected after .sln or .csproj is built - if not built, will not be able to find any dependencies, or will not show current dependencies.
         * To cache scan in a 'latestscan.json' file, set outputPath to some real directory.
         * To save an additional permanent scan, set save to 'true'; 'false' will only save in 'latestscan.json' and be replaced on next scan.
         * 
         * To open a previous scan, comment out code related to GenerateDependenciesJson and use:
         *      FindInJson("path/to/somejsonfile.json", "");
         *      
         * Generating dependency graph may take a while depending on project size (including more than a minute).
         * Once generated, scan is explorable within console.
         * Type in the package name to be searched.
         * Special commands:
         *      'exit': exits program
         *      '!short': toggles whether full dependency path should be shown, or if only the project name and package name/version should be shown.
         *      '!starts': toggles whether package query should match if starts with the searched package name string, or if the query can be anywhere within the package name.
         */
        static void Main(string[] args)
        {
            string outputPath = @""; // some existing directory where latest scan should be cached, or empty string (optional)
            bool save = true; // flags whether or not additional copy of scan should be saved in separate file from 'latestscan.json' (optional)
            string scanFile = @"LETTER:\some\csproj_or_sln.sln"; // .sln/.csproj to be scanned
            var jObject = GenerateDependenciesJson(scanFile, outputPath, save);
            FindInJson(jObject, "");


            //FindInJson(@$"{outputPath}\latestscan.json", "");
        }

        static void FindInJson(string file, string searchString)
        {
            var fullsw = new Stopwatch();
            fullsw.Start();
            string jsonString = "";
            try
            {
                jsonString = File.ReadAllText(file);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception {e.GetType()}: {e.Message}");
            }
            var jObject = JObject.Parse(jsonString);
            fullsw.Stop();
            Console.WriteLine($"Json load time: {fullsw.Elapsed}");
            FindInJson(jObject, searchString);
        }

        static void FindInJson(JObject jObject, string searchString)
        {
            var sw = new Stopwatch();

            bool shouldRun = true;
            bool shortenPaths = true;
            bool startsWith = false;            

            while (shouldRun)
            {

                var found = new List<string>();
                Console.WriteLine($"Searching for: {searchString}...");
                sw.Restart();
                FindStringInJObjectParallel(jObject, searchString.ToLower(), startsWith, ref found);
                sw.Stop();

                while (shouldRun)
                {
                    Console.WriteLine(found.Count);
                    foreach (var name in found)
                    {
                        string toPrint = name;
                        if (shortenPaths)
                        {
                            var split = name.Split("[");
                            if (split.Length < 2)
                                toPrint = split[0];
                            else
                                toPrint = $"{split[0]}[{split[1]} ... [{split[split.Length - 1]}";
                        }
                        Console.WriteLine(toPrint);
                    }

                    Console.WriteLine($"Elapsed time: {sw.Elapsed}");
                    // see if should stop or search for another string
                    Console.Write("New query (or exit or !short or !starts): ");
                    var input = Console.ReadLine();
                    if (input == "exit")
                    {
                        shouldRun = false;
                        break;
                    }
                    else if (input == "!short")
                    {
                        shortenPaths = !shortenPaths;
                        Console.WriteLine($"Shorten paths? -> {startsWith}");
                    }
                    else if (input == "!starts")
                    {
                        startsWith = !startsWith;
                        Console.WriteLine($"Must start with string? -> {startsWith}");
                        break;
                    }
                    else
                    {
                        searchString = input;
                        break;
                    }
                }
            }
        }

        static void FindStringInJObjectParallel(JObject jObject, string searchString, bool startsWith, ref List<string> stringList)
        {
            if (jObject.Count == 0)
                return;
            
            var tasks = new Task<List<string>>[jObject.Count];
            int index = 0;
            foreach (var valuePair in jObject)
            {
                tasks[index] = Task.Factory.StartNew(() => {
                    var localStringList = new List<string>();
                    FindStringInJObject((JObject)valuePair.Value, searchString, startsWith, ref localStringList);
                    return localStringList;
                    });
                index++;
            }
            for (int i = 0; i < jObject.Count; i++)
            {
                stringList.AddRange(tasks[i].Result);
            }
        }

        static List<string> FindStringInJObject(JObject jObject, string searchString, bool startsWith, ref List<string> stringList)
        {
            if (jObject.Count == 0)
                return stringList;
            foreach (var valuePair in jObject)
            {
                var matches = startsWith ?
                    valuePair.Key.ToLower().StartsWith(searchString) :
                    valuePair.Key.ToLower().Contains(searchString);

                if (matches)
                {
                    //Console.WriteLine("Found Search String at: " + valuePair.Value.Path);
                    stringList.Add(valuePair.Value.Path);
                    continue;
                }
                FindStringInJObject((JObject)valuePair.Value, searchString, startsWith, ref stringList);
                //stringList.AddRange(valueReturned);
            }
            return stringList;
        }

        static JObject GenerateDependenciesJson(string scanFile, string outputPath="", bool save=true)
        {
            // Setup project name/path
            var projectName = Path.GetFileName(scanFile);
            var projectPath = scanFile;

            // Setup output Json files (if path provided)
            StreamWriter streamwriter = null; // not used, but could be used later
            StreamWriter streamwriterJson = null; // logged json file
            StreamWriter streamwriterLatest = null;
            string outputFileName = DateTime.Now.ToString("yyyyMMddHHmmss") + $".{projectName}";
            string outputJson = @$"{outputPath}\{outputFileName}.json";
            string outputLatest = @$"{outputPath}\latestscan.json";
            if (!string.IsNullOrEmpty(outputPath))
            {
                var filestreamLatest = new FileStream(outputLatest, FileMode.Create);
                streamwriterLatest = new StreamWriter(filestreamLatest);
                streamwriterLatest.AutoFlush = true;


                if (save)
                {
                    var filestreamJson = new FileStream(outputJson, FileMode.Create);
                    streamwriterJson = new StreamWriter(filestreamJson);
                    streamwriterJson.AutoFlush = true;
                }
            }

            Console.WriteLine("Generating dependency graph...");
            var dependencyGraphService = new DependencyGraphService();
            var dependencyGraph = dependencyGraphService.GenerateDependencyGraph(projectPath);
            Console.WriteLine("Done generating dependency graph!");
            Console.WriteLine("Parsing dependency graph...");

            var rootObj = new JObject();

            foreach(var project in dependencyGraph.Projects.Where(p => p.RestoreMetadata.ProjectStyle == ProjectStyle.PackageReference))
            {
                // Generate lock file
                var lockFileService = new LockFileService();
                var lockFile = lockFileService.GetLockFile(project.FilePath, project.RestoreMetadata.OutputPath);

                var projectObj = new JObject();
                DoWriteLine(project.Name, streamwriter);
                
                foreach(var targetFramework in project.TargetFrameworks)
                {
                    var frameworkObj = new JObject();
                    DoWriteLine($"  [{targetFramework.FrameworkName}]", streamwriter);

                    var lockFileTargetFramework = lockFile.Targets.FirstOrDefault(t => t.TargetFramework.Equals(targetFramework.FrameworkName));
                    if (lockFileTargetFramework != null)
                    {
                        foreach(var dependency in targetFramework.Dependencies)
                        {
                            var projectLibrary = lockFileTargetFramework.Libraries.FirstOrDefault(library => library.Name == dependency.Name);
                            ReportDependency(projectLibrary, lockFileTargetFramework, 1, frameworkObj, streamwriter);
                        }
                    }
                    projectObj.Add($"[{targetFramework.FrameworkName}]", frameworkObj);
                }
                rootObj.Add(project.Name, projectObj);

            }
            Console.WriteLine("Done parsing dependency graph!");

            var jsonString = rootObj.ToString();
            if (!string.IsNullOrEmpty(outputPath))
            {
                streamwriterJson?.Write(jsonString);
                streamwriterLatest?.Write(jsonString);

                Console.WriteLine($"Wrote latest results in file: {outputLatest}");
                if (save)
                {
                    Console.WriteLine($"And: {outputJson}");
                }

                streamwriterJson?.Close();
                streamwriterLatest?.Close();
            }
            return rootObj;
        }

        private static void ReportDependency(LockFileTargetLibrary projectLibrary, LockFileTarget lockFileTargetFramework, int indentLevel, JObject parentObj, StreamWriter streamwriter=null)
        {
            // if null, do nothing
            if (projectLibrary == null) return;
            var dependencyObj = new JObject();
            //DoWrite(new String(' ', indentLevel * 2), streamwriter);
            //DoWriteLine($"{projectLibrary.Name}, v{projectLibrary.Version}", streamwriter);

            foreach (var childDependency in projectLibrary.Dependencies)
            {
                var childLibrary = lockFileTargetFramework.Libraries.FirstOrDefault(library => library.Name == childDependency.Id);

                ReportDependency(childLibrary, lockFileTargetFramework, indentLevel + 1, dependencyObj, streamwriter);
            }
            parentObj.Add($"{projectLibrary.Name}, v{projectLibrary.Version}", dependencyObj);
        }

        private static void DoWrite(String value, StreamWriter writer)
        {
            //Console.Write(value);
            writer?.Write(value);
        }

        private static void DoWriteLine(String value, StreamWriter writer)
        {
            //Console.WriteLine(value);
            writer?.WriteLine(value);
        }
    }
}
