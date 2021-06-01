using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Orts.Scripting.Api
{
    public class ScriptManager
    {
        private readonly Dictionary<string, Assembly> scripts = new Dictionary<string, Assembly>();

        private static readonly string runtimeDirectory = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();

        private static readonly MetadataReference[] CompilerParameters = new MetadataReference[]
        {
            MetadataReference.CreateFromFile(typeof(Trace).GetTypeInfo().Assembly.Location),
            MetadataReference.CreateFromFile("Orts.Common.dll"),
            MetadataReference.CreateFromFile("Orts.Formats.Msts.dll"),
            MetadataReference.CreateFromFile("Orts.Formats.OR.dll"),
            MetadataReference.CreateFromFile("Orts.Settings.dll"),
            MetadataReference.CreateFromFile("Orts.Scripting.Api.dll"),
            MetadataReference.CreateFromFile("Orts.Simulation.dll"),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "mscorlib.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Core.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.InteropServices.RuntimeInformation.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Collections.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Linq.dll")),

            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "netstandard.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Private.CoreLib.dll"))
        };

        private static readonly IEnumerable<string> DefaultNamespaces = new[]
        {
                "System",
                "System.IO",
                "System.Linq",
                "System.Text",
                "System.Collections.Generic"
   };

        private static readonly CSharpCompilationOptions DefaultCompilationOptions =
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).
            WithOverflowChecks(true).WithOptimizationLevel(Debugger.IsAttached ? OptimizationLevel.Debug : OptimizationLevel.Release).
            WithUsings(DefaultNamespaces);

        public object Load(string path, string name)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(name))
                return null;

            if (!Directory.Exists(path))
                return null;

            string typeName = $"Orts.Scripting.Script.{Path.GetFileNameWithoutExtension(name)}";

            try
            {
                if (scripts.TryGetValue(path, out Assembly assembly))
                {
                    return assembly?.CreateInstance(typeName, true);
                }
            }
            catch (Exception exception) when (exception is MissingMethodException)
            {
                Trace.TraceWarning($"Error when trying to load type {name}.", exception.Message);
                return null;
            }
            catch (Exception exception) when (exception is BadImageFormatException || exception is FileLoadException || exception is FileNotFoundException)
            {
                Trace.TraceWarning($"Error when trying to load type {name}. Regenerating assembly from script file {path}.", exception.Message);
            }

            List<SyntaxTree> sourceFiles = new List<SyntaxTree>();

            foreach (string fileName in Directory.EnumerateFiles(path, "*.cs"))
            {
                using (FileStream file = new FileStream(fileName, FileMode.Open))
                {
                    try
                    {
                        SourceText sourceText = SourceText.From(file, Encoding.UTF8);
                        sourceFiles.Add(SyntaxFactory.ParseSyntaxTree(sourceText, CSharpParseOptions.Default, file.Name));
                    }
                    catch (Exception exception) when (exception is InvalidDataException || exception is IOException)
                    {
                        Trace.TraceError($"Error loading script source from file {path}.", exception.Message);
                    }
                }
            }

            EmitResult emitResult = null;
            CSharpCompilation compilation = CSharpCompilation.Create(name, sourceFiles, CompilerParameters, DefaultCompilationOptions);

            using (MemoryStream assemblyBytes = new MemoryStream(), symbolBytes = Debugger.IsAttached ? new MemoryStream() : null)
            {
                emitResult = compilation.Emit(assemblyBytes, symbolBytes);

                if (!emitResult.Success)
                {
                    Trace.TraceWarning(string.Join(Environment.NewLine,
                        new string[] { $"Skipped scripts in path {path} with errors:" }.Concat(
                        emitResult.Diagnostics.Select((x, i) => $"  {i + 1}. {x}"))));
                    scripts.Add(path, null);
                    return null;
                }

                try
                {
                    Assembly scriptAssembly = Assembly.Load(assemblyBytes.ToArray(), symbolBytes?.ToArray());
                    scripts.Add(path, scriptAssembly);
                    object scriptType = scriptAssembly.CreateInstance(typeName, true);
                    if (null != scriptType)
                        return scriptType;
                }
                catch (Exception exception) when (exception is BadImageFormatException || exception is FileLoadException || exception is FileNotFoundException || exception is MissingMethodException)
                {
                    Trace.TraceWarning($"Error when trying to load type {name}.", exception.Message);
                }
            }
            return null;
        }

    }
}
